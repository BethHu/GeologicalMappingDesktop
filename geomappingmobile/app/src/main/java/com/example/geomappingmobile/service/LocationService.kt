package com.example.geomappingmobile.service

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.content.pm.PackageManager
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.os.IBinder
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.example.geomappingmobile.R
import com.example.geomappingmobile.data.model.TrackPoint
import com.example.geomappingmobile.data.repository.AppRepository
import com.example.geomappingmobile.data.repository.GpsStatus
import java.util.Date

class LocationService : Service(), LocationListener {

    private lateinit var locationManager: LocationManager
    private var isTracking = false

    override fun onCreate() {
        super.onCreate()
        locationManager = getSystemService(LOCATION_SERVICE) as LocationManager
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_START -> {
                startForeground(NOTIFICATION_ID, createNotification())
                startLocationUpdates()
            }
            ACTION_STOP -> {
                stopLocationUpdates()
                stopForeground(STOP_FOREGROUND_REMOVE)
                stopSelf()
            }
            ACTION_START_TRACKING -> {
                isTracking = true
            }
            ACTION_STOP_TRACKING -> {
                isTracking = false
            }
        }
        return START_STICKY
    }

    private fun startLocationUpdates() {
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_FINE_LOCATION)
            != PackageManager.PERMISSION_GRANTED
        ) return

        val gpsEnabled = locationManager.isProviderEnabled(LocationManager.GPS_PROVIDER)
        if (!gpsEnabled) {
            AppRepository.updateGpsStatus(GpsStatus.DISABLED)
            return
        }

        AppRepository.updateGpsStatus(GpsStatus.SEARCHING)

        locationManager.requestLocationUpdates(
            LocationManager.GPS_PROVIDER,
            UPDATE_INTERVAL_MS,
            MIN_DISTANCE_M,
            this
        )

        locationManager.requestLocationUpdates(
            LocationManager.NETWORK_PROVIDER,
            UPDATE_INTERVAL_MS,
            MIN_DISTANCE_M,
            this
        )
    }

    private fun stopLocationUpdates() {
        locationManager.removeUpdates(this)
        AppRepository.updateGpsStatus(GpsStatus.DISABLED)
    }

    override fun onLocationChanged(location: Location) {
        val trackPoint = TrackPoint(
            latitude = location.latitude,
            longitude = location.longitude,
            elevation = location.altitude,
            accuracy = location.accuracy,
            speed = location.speed,
            bearing = location.bearing,
            timestamp = Date(location.time)
        )

        AppRepository.updateCurrentLocation(trackPoint)

        val status = when {
            location.accuracy < 200f -> GpsStatus.FIXED
            else -> GpsStatus.SEARCHING
        }
        AppRepository.updateGpsStatus(status)

        if (isTracking) {
            AppRepository.addTrackPointToCurrentRoute(trackPoint)
        }
    }

    override fun onProviderDisabled(provider: String) {
        if (provider == LocationManager.GPS_PROVIDER) {
            AppRepository.updateGpsStatus(GpsStatus.DISABLED)
        }
    }

    override fun onProviderEnabled(provider: String) {
        if (provider == LocationManager.GPS_PROVIDER) {
            AppRepository.updateGpsStatus(GpsStatus.SEARCHING)
        }
    }

    override fun onStatusChanged(provider: String?, status: Int, extras: android.os.Bundle?) {
        if (provider == LocationManager.GPS_PROVIDER) {
            when (status) {
                android.location.LocationProvider.AVAILABLE -> AppRepository.updateGpsStatus(GpsStatus.FIXED)
                android.location.LocationProvider.TEMPORARILY_UNAVAILABLE -> AppRepository.updateGpsStatus(GpsStatus.LOST)
                android.location.LocationProvider.OUT_OF_SERVICE -> AppRepository.updateGpsStatus(GpsStatus.LOST)
            }
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun createNotificationChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            "GPS定位服务",
            NotificationManager.IMPORTANCE_LOW
        ).apply {
            description = "用于后台GPS定位和轨迹记录"
            setShowBadge(false)
        }
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(channel)
    }

    private fun createNotification(): Notification {
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("地质填图助手")
            .setContentText("GPS定位服务运行中")
            .setSmallIcon(R.drawable.ic_gps_fixed)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }

    companion object {
        const val ACTION_START = "com.example.geomappingmobile.START"
        const val ACTION_STOP = "com.example.geomappingmobile.STOP"
        const val ACTION_START_TRACKING = "com.example.geomappingmobile.START_TRACKING"
        const val ACTION_STOP_TRACKING = "com.example.geomappingmobile.STOP_TRACKING"

        private const val CHANNEL_ID = "gps_location_channel"
        private const val NOTIFICATION_ID = 1001
        private const val UPDATE_INTERVAL_MS = 2000L
        private const val MIN_DISTANCE_M = 2f
    }
}
