package com.example.geomappingmobile.data.model

import com.google.gson.Gson
import java.io.Serializable
import java.util.Date

enum class PointType(val displayName: String, val description: String) {
    GEOLOGICAL("地质观察点", "岩性、地层、接触关系、地貌特征等"),
    STRUCTURAL("构造测量点", "产状、断层、节理、褶皱等"),
    SAMPLE("样品点", "岩石样、土样、水样、矿样、化石样等"),
    PHOTO("照片点", "现场照片、全景照片、剖面照片"),
    LANDFORM("地貌点", "滑坡、崩塌、泥石流、侵蚀地貌等"),
    HYDROLOGICAL("水文点", "泉点、河流、地下水露头等"),
    ENGINEERING("工程地质点", "边坡、基岩露头、不良地质体等")
}

data class GeologicalPoint(
    val id: String = generateId(),
    val pointType: PointType = PointType.GEOLOGICAL,
    val customId: String = "",
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val elevation: Double = 0.0,
    val accuracy: Float = 0f,
    val timestamp: Date = Date(),
    val description: String = "",
    val photoPaths: MutableList<String> = mutableListOf(),
    val attachmentPaths: MutableList<String> = mutableListOf(),
    val sketchPaths: MutableList<String> = mutableListOf(),
    val videoPaths: MutableList<String> = mutableListOf(),
    val routeId: String = "",
    val isSynced: Boolean = false
) : Serializable {
    fun toJson(): String = Gson().toJson(this)

    companion object {
        private var nextId = 1
        fun generateId(): String = "GP${String.format("%04d", nextId++)}"
        fun fromJson(json: String): GeologicalPoint = Gson().fromJson(json, GeologicalPoint::class.java)
    }
}
