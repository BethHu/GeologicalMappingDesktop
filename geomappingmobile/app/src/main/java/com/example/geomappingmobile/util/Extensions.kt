package com.example.geomappingmobile.util

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.pm.PackageManager
import android.location.LocationManager
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import com.example.geomappingmobile.data.model.PointType
import com.example.geomappingmobile.data.model.RouteStatus
import com.example.geomappingmobile.R
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

fun Context.hasLocationPermission(): Boolean {
    return ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED
}

fun Context.hasCameraPermission(): Boolean {
    return ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
}

fun Context.isGpsEnabled(): Boolean {
    val lm = getSystemService(Context.LOCATION_SERVICE) as LocationManager
    return lm.isProviderEnabled(LocationManager.GPS_PROVIDER)
}

fun Activity.requestLocationPermission(requestCode: Int = 100) {
    ActivityCompat.requestPermissions(
        this,
        arrayOf(
            Manifest.permission.ACCESS_FINE_LOCATION,
            Manifest.permission.ACCESS_COARSE_LOCATION
        ),
        requestCode
    )
}

fun Activity.requestCameraPermission(requestCode: Int = 101) {
    ActivityCompat.requestPermissions(
        this,
        arrayOf(Manifest.permission.CAMERA),
        requestCode
    )
}

fun Date.formatDateTime(): String {
    val sdf = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.getDefault())
    return sdf.format(this)
}

fun Date.formatDate(): String {
    val sdf = SimpleDateFormat("yyyy-MM-dd", Locale.getDefault())
    return sdf.format(this)
}

fun Date.formatTime(): String {
    val sdf = SimpleDateFormat("HH:mm:ss", Locale.getDefault())
    return sdf.format(this)
}

fun Double.formatCoord(): String {
    return String.format("%.6f", this)
}

fun Double.formatElevation(): String {
    return String.format("%.1f m", this)
}

fun Double.formatDistance(): String {
    return when {
        this >= 1000 -> String.format("%.2f km", this / 1000)
        else -> String.format("%.0f m", this)
    }
}

fun Float.formatAccuracy(): String {
    return String.format("±%.0f m", this)
}

fun Long.formatDuration(): String {
    val hours = this / 3600000
    val minutes = (this % 3600000) / 60000
    val seconds = (this % 60000) / 1000
    return if (hours > 0) {
        String.format("%d:%02d:%02d", hours, minutes, seconds)
    } else {
        String.format("%02d:%02d", minutes, seconds)
    }
}

fun PointType.toColorRes(): Int {
    return when (this) {
        PointType.GEOLOGICAL -> R.color.point_geological
        PointType.STRUCTURAL -> R.color.point_structural
        PointType.SAMPLE -> R.color.point_sample
        PointType.PHOTO -> R.color.point_photo
        PointType.LANDFORM -> R.color.point_landform
        PointType.HYDROLOGICAL -> R.color.point_hydrological
        PointType.ENGINEERING -> R.color.point_engineering
    }
}

fun RouteStatus.toStatusColorRes(): Int {
    return when (this) {
        RouteStatus.RECORDING -> R.color.status_recording
        RouteStatus.PAUSED -> R.color.status_paused
        RouteStatus.PLANNED -> R.color.status_idle
        RouteStatus.COMPLETED -> R.color.status_synced
    }
}
