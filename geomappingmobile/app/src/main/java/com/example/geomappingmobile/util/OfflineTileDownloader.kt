package com.example.geomappingmobile.util

import android.database.sqlite.SQLiteDatabase
import com.google.gson.JsonParser
import java.io.ByteArrayOutputStream
import java.net.HttpURLConnection
import java.net.URL

/**
 * 从 GeoJSON 解析数据范围，并从 OpenStreetMap 瓦片服务器下载对应范围内的
 * XYZ 瓦片，写入 MBTiles（SQLite）文件，供 ArcGIS Runtime 的 TileCache 离线加载。
 */
object OfflineTileDownloader {

    private const val OSM_TILE_URL = "https://tile.openstreetmap.org/%d/%d/%d.png"
    private const val MAX_LAT = 85.05112878
    private const val USER_AGENT = "GeologicalMappingMobile/1.0 (offline map downloader)"

    /**
     * 解析 GeoJSON 中所有要素坐标的并集范围，返回 [west, south, east, north]（WGS84）。
     * 支持 Point / LineString / Polygon / MultiPoint / MultiLineString / MultiPolygon / GeometryCollection。
     */
    fun parseExtentFromGeoJson(content: String): DoubleArray? {
        return try {
            val root = JsonParser.parseString(content)
            if (!root.isJsonObject) return null
            val features = root.asJsonObject.getAsJsonArray("features") ?: return null
            var w = Double.MAX_VALUE
            var s = Double.MAX_VALUE
            var e = -Double.MAX_VALUE
            var n = -Double.MAX_VALUE
            var found = false

            fun walkCoords(coords: com.google.gson.JsonElement) {
                if (coords.isJsonArray) {
                    val arr = coords.asJsonArray
                    if (arr.size() >= 2 &&
                        arr[0].isJsonPrimitive && arr[0].asJsonPrimitive.isNumber &&
                        arr[1].isJsonPrimitive && arr[1].asJsonPrimitive.isNumber
                    ) {
                        val lon = arr[0].asDouble
                        val lat = arr[1].asDouble
                        if (lon in -180.0..180.0 && lat in -90.0..90.0) {
                            if (lon < w) w = lon
                            if (lon > e) e = lon
                            if (lat < s) s = lat
                            if (lat > n) n = lat
                            found = true
                        }
                    } else {
                        for (i in 0 until arr.size()) walkCoords(arr[i])
                    }
                }
            }

            for (i in 0 until features.size()) {
                val feature = features[i].asJsonObject
                val geometry = feature.getAsJsonObject("geometry") ?: continue
                if (geometry.get("type")?.asString == "GeometryCollection") {
                    geometry.getAsJsonArray("geometries")?.forEach { walkCoords(it.asJsonObject.get("coordinates") ?: return@forEach) }
                } else {
                    walkCoords(geometry.get("coordinates") ?: continue)
                }
            }

            if (!found) null else doubleArrayOf(w, s, e, n)
        } catch (t: Throwable) {
            null
        }
    }

    /**
     * 估算指定缩放级别范围内需要下载的瓦片总数。
     */
    fun estimateTileCount(bbox: DoubleArray, minZoom: Int, maxZoom: Int): Long {
        var total = 0L
        for (z in minZoom..maxZoom) {
            val (x0, x1, y0, y1) = tileRange(bbox, z)
            total += (x1 - x0 + 1).toLong() * (y1 - y0 + 1)
        }
        return total
    }

    /**
     * 计算某缩放级别下的瓦片行列范围，返回 intArrayOf(xMin, xMax, yMin, yMax)（XYZ 顺序）。
     */
    private fun tileRange(bbox: DoubleArray, zoom: Int): IntArray {
        val n = 1 shl zoom
        fun lonToX(lon: Double): Int = ((lon + 180.0) / 360.0 * n).toInt().coerceIn(0, n - 1)
        fun latToY(lat: Double): Int {
            val latRad = (lat.coerceIn(-MAX_LAT, MAX_LAT)) * Math.PI / 180.0
            val y = (1.0 - Math.log(Math.tan(latRad) + 1.0 / Math.cos(latRad)) / Math.PI) / 2.0 * n
            return y.toInt().coerceIn(0, n - 1)
        }
        val x0 = lonToX(bbox[0])
        val x1 = lonToX(bbox[2])
        val y0 = latToY(bbox[3])
        val y1 = latToY(bbox[1])
        return intArrayOf(Math.min(x0, x1), Math.max(x0, x1), Math.min(y0, y1), Math.max(y0, y1))
    }

    private fun fetchTile(z: Int, x: Int, y: Int): ByteArray? {
        return try {
            val url = URL(String.format(OSM_TILE_URL, z, x, y))
            val conn = url.openConnection() as HttpURLConnection
            conn.connectTimeout = 15000
            conn.readTimeout = 30000
            conn.setRequestProperty("User-Agent", USER_AGENT)
            try {
                if (conn.responseCode != 200) return null
                val bos = ByteArrayOutputStream()
                conn.inputStream.use { input ->
                    val buf = ByteArray(8192)
                    while (true) {
                        val read = input.read(buf)
                        if (read < 0) break
                        bos.write(buf, 0, read)
                    }
                }
                bos.toByteArray().takeIf { it.isNotEmpty() }
            } finally {
                conn.disconnect()
            }
        } catch (t: Throwable) {
            null
        }
    }

    /**
     * 下载范围内所有瓦片并写入 MBTiles 文件。progress 回调参数为 (已下载, 总数)。
     */
    fun downloadToMbtiles(
        bbox: DoubleArray,
        minZoom: Int,
        maxZoom: Int,
        name: String,
        outputFile: java.io.File,
        progress: (Int, Int) -> Unit
    ): Int {
        if (outputFile.exists()) outputFile.delete()

        val db = SQLiteDatabase.openOrCreateDatabase(outputFile, null)
        try {
            db.execSQL("CREATE TABLE metadata (name TEXT, value TEXT);")
            db.execSQL("CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);")
            db.execSQL("CREATE UNIQUE INDEX tile_index ON tiles (zoom_level, tile_column, tile_row);")

            var total = 0L
            val ranges = ArrayList<IntArray>()
            for (z in minZoom..maxZoom) {
                val r = tileRange(bbox, z)
                ranges.add(r)
                total += (r[1] - r[0] + 1).toLong() * (r[3] - r[2] + 1)
            }

            val insert = db.compileStatement(
                "INSERT OR REPLACE INTO tiles (zoom_level, tile_column, tile_row, tile_data) VALUES (?, ?, ?, ?)"
            )
            var done = 0L
            db.beginTransaction()
            try {
                for (zi in ranges.indices) {
                    val z = minZoom + zi
                    val r = ranges[zi]
                    val n = 1 shl z
                    for (x in r[0]..r[1]) {
                        for (y in r[2]..r[3]) {
                            // MBTiles 使用 TMS 顺序，需翻转 Y
                            val tmsRow = n - 1 - y
                            val data = fetchTile(z, x, y)
                            if (data != null) {
                                insert.clearBindings()
                                insert.bindLong(1, z.toLong())
                                insert.bindLong(2, x.toLong())
                                insert.bindLong(3, tmsRow.toLong())
                                insert.bindBlob(4, data)
                                insert.executeInsert()
                            }
                            done++
                            if (done % 10 == 0L || done == total) {
                                val safeDone = done.coerceAtMost(Int.MAX_VALUE.toLong()).toInt()
                                val safeTotal = total.coerceAtMost(Int.MAX_VALUE.toLong()).toInt()
                                progress(safeDone, safeTotal)
                            }
                            Thread.sleep(150) // 遵守 OSM 瓦片服务器使用策略，降低请求频率
                        }
                    }
                }
                db.setTransactionSuccessful()
            } finally {
                db.endTransaction()
                insert.close()
            }

            db.execSQL("INSERT INTO metadata (name, value) VALUES ('name', ?)", arrayOf(name))
            db.execSQL("INSERT INTO metadata (name, value) VALUES ('format', 'png')")
            db.execSQL("INSERT INTO metadata (name, value) VALUES ('minzoom', ?)", arrayOf(minZoom.toString()))
            db.execSQL("INSERT INTO metadata (name, value) VALUES ('maxzoom', ?)", arrayOf(maxZoom.toString()))
            db.execSQL(
                "INSERT INTO metadata (name, value) VALUES ('bounds', ?)",
                arrayOf("${bbox[0]},${bbox[1]},${bbox[2]},${bbox[3]}")
            )
            db.execSQL("INSERT INTO metadata (name, value) VALUES ('scheme', 'tms')")
            return done.toInt()
        } finally {
            db.close()
        }
    }
}
