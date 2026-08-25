package com.example.geomappingmobile.util

import com.example.geomappingmobile.data.model.GeologicalPoint
import com.example.geomappingmobile.data.model.PointType
import com.example.geomappingmobile.data.model.Route
import com.example.geomappingmobile.data.model.RouteStatus
import com.example.geomappingmobile.data.model.TrackPoint
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import com.google.gson.JsonSyntaxException
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

object GeoJsonImporter {

    private val dateFormat = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US)
    private val altDateFormat = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.getDefault())

    data class ImportResult(
        val points: List<GeologicalPoint> = emptyList(),
        val routes: List<Route> = emptyList(),
        val error: String? = null
    )

    fun import(geoJsonContent: String): ImportResult {
        return try {
            val root = JsonParser.parseString(geoJsonContent).asJsonObject
            if (root.get("type")?.asString != "FeatureCollection") {
                return ImportResult(error = "无效的 GeoJSON: 缺少 FeatureCollection")
            }
            val features = root.getAsJsonArray("features")
                ?: return ImportResult(error = "GeoJSON 中没有要素")

            val points = mutableListOf<GeologicalPoint>()
            val routes = mutableListOf<Route>()
            var polygonCount = 0

            for (element in features) {
                val feature = element.asJsonObject
                val geometry = feature.getAsJsonObject("geometry") ?: continue
                val props = feature.getAsJsonObject("properties") ?: continue

                val geomType = geometry.get("type")?.asString ?: ""
                if (geomType == "Polygon" || geomType == "MultiPolygon") {
                    polygonCount++
                    continue
                }

                val entityType = guessEntityType(geometry, props)
                when (entityType) {
                    "point" -> {
                        parseGeoJsonPoint(geometry, props)?.let { points.add(it) }
                    }
                    "route" -> {
                        parseGeoJsonRoute(feature, geometry, props)?.let { routes.add(it) }
                    }
                }
            }
            if (points.isEmpty() && routes.isEmpty() && polygonCount > 0) {
                return ImportResult(
                    error = "该文件仅包含面（Polygon/MultiPolygon）边界要素，移动端不支持导入面图层。\n请使用桌面端「导入 GeoJSON」功能导入并显示。"
                )
            }
            ImportResult(points, routes)
        } catch (e: JsonSyntaxException) {
            ImportResult(error = "JSON 语法错误: ${e.message}")
        } catch (e: Exception) {
            ImportResult(error = "导入失败: ${e.message}")
        }
    }

    private fun guessEntityType(geom: JsonObject, props: JsonObject): String {
        if (props.has("_entityType")) {
            val et = props.get("_entityType").asString
            if (et.contains("Route", ignoreCase = true) || et.contains("route", ignoreCase = true)) return "route"
            return "point"
        }
        if (props.has("totalDistance") || props.has("trackPointCount")) return "route"
        if (props.has("name") && !props.has("customId") && geom.get("type")?.asString == "LineString") return "route"
        return "point"
    }

    private fun parseGeoJsonPoint(geometry: JsonObject, props: JsonObject): GeologicalPoint? {
        val coords = geometry.getAsJsonArray("coordinates") ?: return null
        if (coords.size() < 2) return null

        val id = optString(props, "id") ?: GeologicalPoint.generateId()
        val typeStr = optString(props, "pointType")
            ?: optString(props, "typeCode")
            ?: "GEOLOGICAL"
        val pointType = try { PointType.valueOf(typeStr) } catch (_: Exception) { PointType.GEOLOGICAL }

        return GeologicalPoint(
            id = id,
            pointType = pointType,
            customId = optString(props, "customId") ?: "",
            latitude = coords[1].asDouble,
            longitude = coords[0].asDouble,
            elevation = if (coords.size() > 2) coords[2].asDouble else 0.0,
            accuracy = optDouble(props, "accuracy")?.toFloat() ?: 0f,
            timestamp = parseDate(optString(props, "timestamp")) ?: Date(),
            description = optString(props, "description") ?: "",
            photoPaths = optStringList(props, "photoPaths"),
            attachmentPaths = optStringList(props, "attachmentPaths"),
            sketchPaths = optStringList(props, "sketchPaths"),
            videoPaths = optStringList(props, "videoPaths"),
            routeId = optString(props, "routeId") ?: "",
            isSynced = optBoolean(props, "isSynced")
        )
    }

    private fun parseGeoJsonRoute(
        feature: JsonObject,
        geometry: JsonObject,
        props: JsonObject
    ): Route? {
        val id = optString(props, "id") ?: return null

        val trackPoints = mutableListOf<TrackPoint>()
        val embedded = props.getAsJsonArray("_trackPoints")
        if (embedded != null && embedded.size() > 0) {
            for (el in embedded) {
                val tp = el.asJsonObject
                trackPoints.add(TrackPoint(
                    id = optString(tp, "id") ?: "",
                    latitude = tp.get("latitude")?.asDouble ?: 0.0,
                    longitude = tp.get("longitude")?.asDouble ?: 0.0,
                    elevation = optDouble(tp, "elevation") ?: 0.0,
                    accuracy = optDouble(tp, "accuracy")?.toFloat() ?: 0f,
                    speed = optDouble(tp, "speed")?.toFloat() ?: 0f,
                    bearing = optDouble(tp, "bearing")?.toFloat() ?: 0f,
                    timestamp = parseDate(optString(tp, "timestamp")) ?: Date()
                ))
            }
        } else {
            val coords = geometry.getAsJsonArray("coordinates")
            if (coords != null) {
                for (ce in coords) {
                    val c = ce.asJsonArray
                    if (c.size() >= 2) {
                        trackPoints.add(TrackPoint(
                            latitude = c[1].asDouble,
                            longitude = c[0].asDouble,
                            elevation = if (c.size() > 2) c[2].asDouble else 0.0
                        ))
                    }
                }
            }
        }

        val pointIds = mutableListOf<String>()
        props.getAsJsonArray("pointIds")?.forEach { pointIds.add(it.asString) }

        val status = try {
            RouteStatus.valueOf(optString(props, "status") ?: "COMPLETED")
        } catch (_: Exception) { RouteStatus.COMPLETED }

        return Route(
            id = id,
            name = optString(props, "name") ?: "",
            description = optString(props, "description") ?: "",
            status = status,
            trackPoints = trackPoints.toMutableList(),
            pointIds = pointIds,
            journal = optString(props, "journal") ?: "",
            createdAt = parseDate(optString(props, "createdAt")) ?: Date(),
            updatedAt = parseDate(optString(props, "updatedAt")) ?: Date(),
            totalDistance = optDouble(props, "totalDistance") ?: 0.0,
            totalDuration = optLong(props, "totalDuration") ?: 0L
        )
    }

    private fun optString(obj: JsonObject, key: String): String? {
        val el = obj.get(key)
        return if (el != null && !el.isJsonNull) el.asString else null
    }

    private fun optDouble(obj: JsonObject, key: String): Double? {
        val el = obj.get(key)
        return if (el != null && !el.isJsonNull) el.asDouble else null
    }

    private fun optLong(obj: JsonObject, key: String): Long? {
        val el = obj.get(key)
        return if (el != null && !el.isJsonNull) el.asLong else null
    }

    private fun optBoolean(obj: JsonObject, key: String): Boolean {
        val el = obj.get(key)
        return el != null && !el.isJsonNull && el.asBoolean
    }

    private fun optStringList(obj: JsonObject, key: String): MutableList<String> {
        val arr = obj.getAsJsonArray(key) ?: return mutableListOf()
        return arr.mapNotNull { if (it.isJsonNull) null else it.asString }.toMutableList()
    }

    private fun parseDate(dateStr: String?): Date? {
        if (dateStr == null) return null
        return try { dateFormat.parse(dateStr) }
            catch (_: Exception) {
                try { altDateFormat.parse(dateStr) }
                catch (_: Exception) { null }
            }
    }
}
