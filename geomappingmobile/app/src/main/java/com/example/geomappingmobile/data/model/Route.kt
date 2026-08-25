package com.example.geomappingmobile.data.model

import java.util.Date
import java.util.UUID

enum class RouteStatus {
    PLANNED, RECORDING, PAUSED, COMPLETED
}

data class Route(
    val id: String = UUID.randomUUID().toString().take(8).uppercase(),
    val name: String = "",
    val description: String = "",
    val status: RouteStatus = RouteStatus.PLANNED,
    val trackPoints: MutableList<TrackPoint> = mutableListOf(),
    val pointIds: MutableList<String> = mutableListOf(),
    val journal: String = "",
    val createdAt: Date = Date(),
    val updatedAt: Date = Date(),
    val totalDistance: Double = 0.0,
    val totalDuration: Long = 0L
) {
    val pointCount: Int get() = pointIds.size
    val trackPointCount: Int get() = trackPoints.size

    fun getStartTime(): Date = if (trackPoints.isNotEmpty()) trackPoints.first().timestamp else createdAt
    fun getEndTime(): Date = if (trackPoints.isNotEmpty()) trackPoints.last().timestamp else createdAt
}
