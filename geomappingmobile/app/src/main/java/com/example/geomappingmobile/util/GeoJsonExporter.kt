package com.example.geomappingmobile.util

import com.example.geomappingmobile.data.model.GeologicalPoint
import com.example.geomappingmobile.data.model.Route
import com.google.gson.GsonBuilder
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

object GeoJsonExporter {

    private val dateFormat = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US)
    private val gson = GsonBuilder().setPrettyPrinting().create()

    fun export(points: List<GeologicalPoint>, routes: List<Route>): String {
        val features = mutableListOf<Map<String, Any?>>()

        for (pt in points) {
            val coords = listOf(pt.longitude, pt.latitude, pt.elevation)
            val props = mutableMapOf<String, Any?>(
                "_entityType" to "GeologicalPoint",
                "id" to pt.id,
                "customId" to pt.customId,
                "pointType" to pt.pointType.name,
                "typeCode" to pt.pointType.name,
                "pointTypeDisplay" to pt.pointType.displayName,
                "latitude" to pt.latitude,
                "longitude" to pt.longitude,
                "description" to pt.description,
                "timestamp" to formatDate(pt.timestamp),
                "accuracy" to pt.accuracy.toDouble(),
                "routeId" to pt.routeId,
                "isSynced" to pt.isSynced,
                "photoPaths" to pt.photoPaths,
                "sketchPaths" to pt.sketchPaths,
                "videoPaths" to pt.videoPaths,
                "attachmentPaths" to pt.attachmentPaths
            )
            features.add(mapOf(
                "type" to "Feature",
                "geometry" to mapOf("type" to "Point", "coordinates" to coords),
                "properties" to props
            ))
        }

        for (route in routes) {
            val coords = route.trackPoints.map { tp ->
                listOf(tp.longitude, tp.latitude, tp.elevation)
            }
            val trackPointsData = route.trackPoints.map { tp ->
                mapOf(
                    "id" to tp.id,
                    "latitude" to tp.latitude,
                    "longitude" to tp.longitude,
                    "elevation" to tp.elevation,
                    "accuracy" to tp.accuracy.toDouble(),
                    "speed" to tp.speed.toDouble(),
                    "bearing" to tp.bearing.toDouble(),
                    "timestamp" to formatDate(tp.timestamp)
                )
            }
            val props = mutableMapOf<String, Any?>(
                "_entityType" to "Route",
                "id" to route.id,
                "name" to route.name,
                "description" to route.description,
                "status" to route.status.name,
                "totalDistance" to route.totalDistance,
                "totalDuration" to route.totalDuration,
                "trackPointCount" to route.trackPointCount,
                "pointCount" to route.pointCount,
                "pointIds" to route.pointIds,
                "journal" to route.journal,
                "createdAt" to formatDate(route.createdAt),
                "updatedAt" to formatDate(route.updatedAt),
                "_trackPoints" to trackPointsData
            )
            features.add(mapOf(
                "type" to "Feature",
                "geometry" to mapOf("type" to "LineString", "coordinates" to coords),
                "properties" to props
            ))
        }

        val root = mapOf(
            "type" to "FeatureCollection",
            "features" to features,
            "metadata" to mapOf(
                "generatedAt" to formatDate(Date()),
                "generator" to "GeoMappingMobile",
                "version" to "1.0.0",
                "totalPoints" to points.size,
                "totalRoutes" to routes.size
            )
        )

        return gson.toJson(root)
    }

    private fun formatDate(date: Date): String = dateFormat.format(date)
}
