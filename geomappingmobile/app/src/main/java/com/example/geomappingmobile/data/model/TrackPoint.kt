package com.example.geomappingmobile.data.model

import java.util.Date
import java.util.UUID

data class TrackPoint(
    val id: String = UUID.randomUUID().toString().take(8).uppercase(),
    val latitude: Double,
    val longitude: Double,
    val elevation: Double = 0.0,
    val accuracy: Float = 0f,
    val speed: Float = 0f,
    val bearing: Float = 0f,
    val timestamp: Date = Date()
)
