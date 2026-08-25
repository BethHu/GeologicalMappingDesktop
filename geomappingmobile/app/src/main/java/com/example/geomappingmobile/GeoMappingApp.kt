package com.example.geomappingmobile

import android.app.Application
import com.esri.arcgisruntime.ArcGISRuntimeEnvironment

class GeoMappingApp : Application() {

    override fun onCreate() {
        super.onCreate()
        android.util.Log.d("GeoMappingApp", "onCreate start")
        instance = this
        try {
            ArcGISRuntimeEnvironment.setApiKey("AAPTaHMNa0MN4o4so22wXNOf2sA..O2MK3rHy6tqM87UzMvBqvy3Bp0UFFvdnNiOBhU6RF1GGGSZeyQFabvS22MbKR0-qmvq6wjPdRiQqvgQxXVmcJ1-L2aN2IZQI-L0P_V9bQwx1XnOwdcf5-n30Y4LrLy9Wl87tJYHTzFl9is6WAUwosQi12pfPUxxTDkmm7pFU9PpVlWx4dEYrVW6o2xLsnq2YRqVLsRpPsLospvvLJPVi_XJ9GBi_HY07cGhGd_Lonf40wAqjOxQyAT1_FL6wdkLV")
            android.util.Log.d("GeoMappingApp", "API key set")
        } catch (e: Throwable) {
            android.util.Log.e("GeoMappingApp", "API key failed", e)
            try {
                val f = java.io.File(filesDir, "fatal_log.txt")
                f.writeText(android.util.Log.getStackTraceString(e))
            } catch (_: Exception) {}
        }
        Thread.setDefaultUncaughtExceptionHandler { _, e ->
            try {
                val f = java.io.File(filesDir, "fatal_log.txt")
                f.writeText(android.util.Log.getStackTraceString(e))
            } catch (_: Exception) {}
        }
    }

    companion object {
        lateinit var instance: GeoMappingApp
            private set
    }
}
