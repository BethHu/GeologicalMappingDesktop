package com.example.geomappingmobile.service

import com.example.geomappingmobile.data.model.GeologicalPoint
import com.example.geomappingmobile.data.model.Route
import com.example.geomappingmobile.data.repository.AppRepository
import com.example.geomappingmobile.util.GeoJsonExporter
import com.example.geomappingmobile.util.GeoJsonImporter
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.BufferedReader
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.HttpURLConnection
import java.net.URL

object SyncService {

    data class SyncResult(
        val success: Boolean,
        val pointsPulled: Int = 0,
        val routesPulled: Int = 0,
        val pointsPushed: Int = 0,
        val routesPushed: Int = 0,
        val message: String = ""
    )

    suspend fun syncWithServer(serverUrl: String): SyncResult = withContext(Dispatchers.IO) {
        try {
            val baseUrl = serverUrl.trimEnd('/')

            // Step 1: Ping to check server
            val pingOk = pingServer(baseUrl)
            if (!pingOk) {
                return@withContext SyncResult(false, message = "无法连接到服务器: $baseUrl")
            }

            // Step 2: Push local data to server
            val localPoints = AppRepository.points.value ?: emptyList()
            val localRoutes = AppRepository.routes.value ?: emptyList()
            val localGeoJson = GeoJsonExporter.export(localPoints, localRoutes)

            val pushResult = httpPost("$baseUrl/api/push", localGeoJson)
            if (!pushResult.startsWith("{\"status\":\"ok\"")) {
                return@withContext SyncResult(false, message = "推送数据失败: $pushResult")
            }

            // Step 3: Pull remote data from server
            val remoteGeoJson = httpGet("$baseUrl/api/pull")

            // Step 4: Parse and import
            val importResult = GeoJsonImporter.import(remoteGeoJson)
            if (importResult.error != null) {
                return@withContext SyncResult(
                    success = true,
                    pointsPushed = localPoints.size,
                    routesPushed = localRoutes.size,
                    message = "拉取数据解析失败: ${importResult.error}，但本地数据已推送"
                )
            }

            // Add pulled points and routes to AppRepository
            for (point in importResult.points) {
                val existing = AppRepository.getPointById(point.id)
                if (existing == null) {
                    AppRepository.addPoint(point)
                } else {
                    AppRepository.updatePoint(point)
                }
            }
            for (route in importResult.routes) {
                val existing = AppRepository.getRouteById(route.id)
                if (existing == null) {
                    AppRepository.addRoute(route)
                } else {
                    AppRepository.updateRoute(route)
                }
            }

            SyncResult(
                success = true,
                pointsPulled = importResult.points.size,
                routesPulled = importResult.routes.size,
                pointsPushed = localPoints.size,
                routesPushed = localRoutes.size,
                message = "同步完成"
            )
        } catch (e: Exception) {
            SyncResult(false, message = "同步失败: ${e.message}")
        }
    }

    private fun pingServer(baseUrl: String): Boolean {
        return try {
            val result = httpGet("$baseUrl/api/ping")
            result.contains("\"status\":\"ok\"")
        } catch (_: Exception) {
            false
        }
    }

    private fun httpGet(urlStr: String): String {
        val url = URL(urlStr)
        val conn = url.openConnection() as HttpURLConnection
        conn.requestMethod = "GET"
        conn.connectTimeout = 10000
        conn.readTimeout = 10000
        try {
            val reader = BufferedReader(InputStreamReader(conn.inputStream, Charsets.UTF_8))
            return reader.readText()
        } finally {
            conn.disconnect()
        }
    }

    private fun httpPost(urlStr: String, body: String): String {
        val url = URL(urlStr)
        val conn = url.openConnection() as HttpURLConnection
        conn.requestMethod = "POST"
        conn.doOutput = true
        conn.connectTimeout = 10000
        conn.readTimeout = 10000
        conn.setRequestProperty("Content-Type", "application/json; charset=utf-8")
        try {
            val writer = OutputStreamWriter(conn.outputStream, Charsets.UTF_8)
            writer.write(body)
            writer.flush()
            writer.close()

            val reader = BufferedReader(InputStreamReader(conn.inputStream, Charsets.UTF_8))
            return reader.readText()
        } finally {
            conn.disconnect()
        }
    }
}
