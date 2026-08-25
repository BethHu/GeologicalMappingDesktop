package com.example.geomappingmobile.service

import android.content.Context
import android.content.SharedPreferences
import com.example.geomappingmobile.util.GeoJsonExporter
import com.example.geomappingmobile.util.GeoJsonImporter
import com.example.geomappingmobile.data.model.GeologicalPoint
import com.example.geomappingmobile.data.model.Route

import com.google.gson.Gson
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.net.HttpURLConnection
import java.net.URL

data class CloudAuthResult(
    val success: Boolean,
    val token: String = "",
    val message: String = "",
    val username: String = ""
)

data class CloudSyncResult(
    val success: Boolean,
    val message: String = "",
    val pointsPushed: Int = 0,
    val routesPushed: Int = 0,
    val pointsPulled: Int = 0,
    val routesPulled: Int = 0,
    val pulledPoints: List<GeologicalPoint> = emptyList(),
    val pulledRoutes: List<Route> = emptyList()
)

object CloudSyncService {

    private const val PREFS_NAME = "cloud_sync"
    private const val KEY_TOKEN = "token"
    private const val KEY_USERNAME = "username"
    private const val KEY_SERVER_URL = "server_url"
    private val gson = Gson()

    fun getSavedToken(context: Context): String {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .getString(KEY_TOKEN, "") ?: ""
    }

    fun getSavedUsername(context: Context): String {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .getString(KEY_USERNAME, "") ?: ""
    }

    fun getSavedServerUrl(context: Context): String {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .getString(KEY_SERVER_URL, "") ?: ""
    }

    fun saveAuth(context: Context, token: String, username: String, serverUrl: String) {
        var url = serverUrl.trimEnd('/')
        if (!url.startsWith("http://") && !url.startsWith("https://"))
            url = "http://$url"
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit().apply {
            putString(KEY_TOKEN, token)
            putString(KEY_USERNAME, username)
            putString(KEY_SERVER_URL, url)
            apply()
        }
    }

    fun clearAuth(context: Context) {
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit().apply {
            remove(KEY_TOKEN)
            remove(KEY_USERNAME)
            apply()
        }
    }

    suspend fun auth(
        serverUrl: String,
        username: String,
        password: String,
        isRegister: Boolean = false
    ): CloudAuthResult = withContext(Dispatchers.IO) {
        var baseUrl = serverUrl.trimEnd('/')
        if (!baseUrl.startsWith("http://") && !baseUrl.startsWith("https://"))
            baseUrl = "http://$baseUrl"
        val fullUrl = "$baseUrl/api/auth/${if (isRegister) "register" else "login"}"
        try {
            val url = URL(fullUrl)
            val conn = url.openConnection() as HttpURLConnection
            conn.requestMethod = "POST"
            conn.setRequestProperty("Content-Type", "application/json")
            conn.connectTimeout = 15000
            conn.readTimeout = 15000
            conn.doOutput = true

            val body = gson.toJson(mapOf("username" to username, "password" to password))
            conn.outputStream.write(body.toByteArray(Charsets.UTF_8))

            val code = conn.responseCode
            val responseStr = if (code in 200..299) {
                conn.inputStream.bufferedReader(Charsets.UTF_8).readText()
            } else {
                conn.errorStream?.bufferedReader(Charsets.UTF_8)?.readText() ?: "{\"message\":\"连接失败\"}"
            }
            conn.disconnect()

            val json = gson.fromJson(responseStr, Map::class.java)
            val ok = json["ok"] as? Boolean ?: false
            val msg = json["message"] as? String ?: ""

            if (ok) {
                val token = json["token"] as? String ?: ""
                val user = json["user"] as? Map<*, *>
                val name = user?.get("username") as? String ?: username
                CloudAuthResult(true, token, msg, name)
            } else {
                CloudAuthResult(false, "", msg, "")
            }
        } catch (e: Exception) {
            CloudAuthResult(false, "", "连接失败 [${e.javaClass.simpleName}]: ${e.message}\n\n请求地址: ${fullUrl}", "")
        }
    }

    suspend fun sync(
        context: Context,
        serverUrl: String,
        token: String,
        points: List<GeologicalPoint>,
        routes: List<Route>
    ): CloudSyncResult = withContext(Dispatchers.IO) {
        var baseUrl = serverUrl.trimEnd('/')
        if (!baseUrl.startsWith("http://") && !baseUrl.startsWith("https://"))
            baseUrl = "http://$baseUrl"
        try {

            // Push local data
            val geojson = GeoJsonExporter.export(points, routes)
            val pushUrl = URL("$baseUrl/api/sync/push")
            val pushConn = pushUrl.openConnection() as HttpURLConnection
            pushConn.requestMethod = "POST"
            pushConn.setRequestProperty("Content-Type", "application/json")
            pushConn.setRequestProperty("Authorization", "Bearer $token")
            pushConn.connectTimeout = 30000
            pushConn.readTimeout = 30000
            pushConn.doOutput = true

            val pushBody = gson.toJson(mapOf("content" to geojson, "dataType" to "geojson"))
            pushConn.outputStream.write(pushBody.toByteArray(Charsets.UTF_8))

            val pushCode = pushConn.responseCode
            pushConn.disconnect()

            if (pushCode !in 200..299) {
                return@withContext CloudSyncResult(false, "上传失败 (HTTP $pushCode)")
            }

            // Upload attachment files (photos / sketches / videos / attachments)
            val filesToUpload = mutableMapOf<String, String>()
            for (pt in points) {
                val allPaths = pt.photoPaths + pt.sketchPaths + pt.videoPaths + pt.attachmentPaths
                for (filePath in allPaths) {
                    try {
                        val srcFile = java.io.File(filePath)
                        if (srcFile.exists()) {
                            val serverName = srcFile.name
                            val bytes = srcFile.readBytes()
                            filesToUpload[serverName] = android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP)
                        }
                    } catch (_: Exception) { }
                }
            }
            if (filesToUpload.isNotEmpty()) {
                try {
                    android.util.Log.d("CloudSync", "uploading ${filesToUpload.size} files: ${filesToUpload.keys}")
                    val uploadUrl = URL("$baseUrl/api/sync/upload")
                    val uploadConn = uploadUrl.openConnection() as HttpURLConnection
                    uploadConn.requestMethod = "POST"
                    uploadConn.setRequestProperty("Content-Type", "application/json")
                    uploadConn.setRequestProperty("Authorization", "Bearer $token")
                    uploadConn.connectTimeout = 120000
                    uploadConn.readTimeout = 120000
                    uploadConn.doOutput = true
                    val uploadBody = gson.toJson(mapOf("files" to filesToUpload))
                    uploadConn.outputStream.write(uploadBody.toByteArray(Charsets.UTF_8))
                    val code = uploadConn.responseCode
                    android.util.Log.d("CloudSync", "upload response code: $code")
                    uploadConn.disconnect()
                } catch (e: Exception) {
                    android.util.Log.e("CloudSync", "upload failed", e)
                }
            }

            // Pull remote data
            val pullUrl = URL("$baseUrl/api/sync/pull?type=geojson")
            val pullConn = pullUrl.openConnection() as HttpURLConnection
            pullConn.requestMethod = "GET"
            pullConn.setRequestProperty("Authorization", "Bearer $token")
            pullConn.connectTimeout = 30000
            pullConn.readTimeout = 30000

            val pullCode = pullConn.responseCode
            val pullResponse = if (pullCode in 200..299) {
                pullConn.inputStream.bufferedReader(Charsets.UTF_8).readText()
            } else null
            pullConn.disconnect()

            var pulledPointList = emptyList<GeologicalPoint>()
            var pulledRouteList = emptyList<Route>()

            if (pullResponse != null) {
                val json = gson.fromJson(pullResponse, Map::class.java)
                val dataStr = json["data"] as? String
                if (!dataStr.isNullOrBlank()) {
                    val result = GeoJsonImporter.import(dataStr)
                    if (result.error == null) {
                        pulledPointList = result.points
                        pulledRouteList = result.routes
                    }
                }
            }

            // Download attachment files for pulled points
            if (pulledPointList.isNotEmpty()) {
                val updatedPoints = mutableListOf<GeologicalPoint>()
                val fileList = getServerFileList(baseUrl, token)
                for (pt in pulledPointList) {
                    val savedPhotos = mutableListOf<String>()
                    val savedSketches = mutableListOf<String>()
                    val savedVideos = mutableListOf<String>()
                    val savedAttachments = mutableListOf<String>()

                    if (fileList != null) {
                        val matchedFiles = fileList.filter {
                            it.equals(pt.id, ignoreCase = true) ||
                                it.startsWith("${pt.id}_", ignoreCase = true)
                        }
                        for (serverFile in matchedFiles) {
                            try {
                                val dlUrl = URL("$baseUrl/api/sync/download/${java.net.URLEncoder.encode(serverFile, "UTF-8")}")
                                val dlConn = dlUrl.openConnection() as HttpURLConnection
                                dlConn.requestMethod = "GET"
                                dlConn.setRequestProperty("Authorization", "Bearer $token")
                                dlConn.connectTimeout = 60000
                                dlConn.readTimeout = 120000
                                if (dlConn.responseCode in 200..299) {
                                    val respStr = dlConn.inputStream.bufferedReader(Charsets.UTF_8).readText()
                                    val dlJson = gson.fromJson(respStr, Map::class.java)
                                    if (dlJson["ok"] == true) {
                                        val base64Data = dlJson["data"] as? String ?: continue
                                        val fileBytes = android.util.Base64.decode(base64Data, android.util.Base64.DEFAULT)
                                        val lower = serverFile.lowercase()
                                        val subDir = when {
                                            lower.contains("sketch") -> "sketches"
                                            lower.endsWith(".mp4") || lower.endsWith(".mov") ||
                                                lower.endsWith(".3gp") || lower.endsWith(".avi") ||
                                                lower.endsWith(".mkv") || lower.contains("video") -> "videos"
                                            lower.endsWith(".png") && !lower.contains("photo") -> "sketches"
                                            else -> "photos"
                                        }
                                        val dir = java.io.File(context.filesDir, subDir)
                                        if (!dir.exists()) dir.mkdirs()
                                        val localFile = java.io.File(dir, serverFile)
                                        localFile.writeBytes(fileBytes)
                                        when (subDir) {
                                            "photos" -> savedPhotos.add(localFile.absolutePath)
                                            "sketches" -> savedSketches.add(localFile.absolutePath)
                                            "videos" -> savedVideos.add(localFile.absolutePath)
                                            else -> savedAttachments.add(localFile.absolutePath)
                                        }
                                    }
                                }
                                dlConn.disconnect()
                            } catch (_: Exception) { }
                        }
                    }

                    updatedPoints.add(pt.copy(
                        photoPaths = (pt.photoPaths + savedPhotos).distinct().toMutableList(),
                        sketchPaths = (pt.sketchPaths + savedSketches).distinct().toMutableList(),
                        videoPaths = (pt.videoPaths + savedVideos).distinct().toMutableList(),
                        attachmentPaths = (pt.attachmentPaths + savedAttachments).distinct().toMutableList()
                    ))
                }
                pulledPointList = updatedPoints
            }

            CloudSyncResult(
                success = true,
                message = "同步完成",
                pointsPushed = points.size,
                routesPushed = routes.size,
                pointsPulled = pulledPointList.size,
                routesPulled = pulledRouteList.size,
                pulledPoints = pulledPointList,
                pulledRoutes = pulledRouteList
            )

        } catch (e: Exception) {
            val fullUrl = "$baseUrl/api/sync/push"
            CloudSyncResult(false, "同步失败 [${e.javaClass.simpleName}]: ${e.message}\n请求地址: $fullUrl")
        }
    }

    private fun getServerFileList(baseUrl: String, token: String): List<String>? {
        return try {
            val url = URL("$baseUrl/api/sync/files")
            val conn = url.openConnection() as HttpURLConnection
            conn.requestMethod = "GET"
            conn.setRequestProperty("Authorization", "Bearer $token")
            conn.connectTimeout = 15000
            conn.readTimeout = 15000
            val resp = if (conn.responseCode in 200..299) {
                conn.inputStream.bufferedReader(Charsets.UTF_8).readText()
            } else null
            conn.disconnect()
            if (resp != null) {
                val json = gson.fromJson(resp, Map::class.java)
                if (json["ok"] == true) {
                    @Suppress("UNCHECKED_CAST")
                    (json["files"] as? List<*>)?.map { it.toString() }
                } else null
            } else null
        } catch (_: Exception) { null }
    }
}
