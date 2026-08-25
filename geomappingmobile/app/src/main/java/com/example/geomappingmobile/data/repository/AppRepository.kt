package com.example.geomappingmobile.data.repository

import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import com.example.geomappingmobile.data.model.GeologicalPoint
import com.example.geomappingmobile.data.model.PointType
import com.example.geomappingmobile.data.model.Route
import com.example.geomappingmobile.data.model.RouteStatus
import com.example.geomappingmobile.data.model.TrackPoint
import java.util.Date

object AppRepository {

    private val _routes = MutableLiveData<List<Route>>(emptyList())
    val routes: LiveData<List<Route>> = _routes

    private val _points = MutableLiveData<List<GeologicalPoint>>(emptyList())
    val points: LiveData<List<GeologicalPoint>> = _points

    private val _currentRoute = MutableLiveData<Route?>(null)
    val currentRoute: LiveData<Route?> = _currentRoute

    private val _currentLocation = MutableLiveData<TrackPoint?>(null)
    val currentLocation: LiveData<TrackPoint?> = _currentLocation

    private val _gpsStatus = MutableLiveData<GpsStatus>(GpsStatus.DISABLED)
    val gpsStatus: LiveData<GpsStatus> = _gpsStatus

    private val _displayRoute = MutableLiveData<Route?>(null)
    val displayRoute: LiveData<Route?> = _displayRoute

    private val _replayRoute = MutableLiveData<Route?>(null)
    val replayRoute: LiveData<Route?> = _replayRoute

    private val _planningPoints = MutableLiveData<List<TrackPoint>>(emptyList())
    val planningPoints: LiveData<List<TrackPoint>> = _planningPoints

    private val _isPlanning = MutableLiveData(false)
    val isPlanning: LiveData<Boolean> = _isPlanning

    private val _routeToEdit = MutableLiveData<Route?>(null)
    val routeToEdit: LiveData<Route?> = _routeToEdit

    private val _pointTypeFilter = MutableLiveData<PointType?>(null)
    val pointTypeFilter: LiveData<PointType?> = _pointTypeFilter

    fun setPointTypeFilter(type: PointType?) {
        _pointTypeFilter.value = type
    }

    fun setRouteToEdit(route: Route?) {
        _routeToEdit.value = route
    }

    fun setDisplayRoute(route: Route?) {
        _replayRoute.value = null  // clear first
        _displayRoute.value = route // then show
    }

    fun setReplayRoute(route: Route?) {
        _displayRoute.value = null  // clear first
        _replayRoute.value = route  // then show
    }

    fun startPlanning() {
        _planningPoints.value = emptyList()
        _isPlanning.value = true
    }

    fun addPlanningPoint(tp: TrackPoint) {
        _planningPoints.value = (_planningPoints.value ?: emptyList()) + tp
    }

    fun undoLastPlanningPoint() {
        val pts = _planningPoints.value ?: return
        if (pts.isNotEmpty()) _planningPoints.value = pts.dropLast(1)
    }

    fun cancelPlanning() {
        _planningPoints.value = emptyList()
        _isPlanning.value = false
    }

    fun savePlanningRoute(name: String, notes: String = ""): Route {
        val pts = _planningPoints.value ?: emptyList()
        val distance = recalculateDistance(pts)
        val route = Route(
            name = name,
            status = RouteStatus.PLANNED,
            trackPoints = pts.toMutableList(),
            journal = notes,
            totalDistance = distance,
            createdAt = Date()
        )
        _routes.value = listOf(route) + (_routes.value ?: emptyList())
        _planningPoints.value = emptyList()
        _isPlanning.value = false
        return route
    }

    fun addPoint(point: GeologicalPoint) {
        _points.value = listOf(point) + (_points.value ?: emptyList())
        val route = _currentRoute.value
        if (route != null && route.status == RouteStatus.RECORDING) {
            val updatedRoute = route.copy(
                pointIds = (listOf(point.id) + route.pointIds).toMutableList(),
                updatedAt = Date()
            )
            _currentRoute.value = updatedRoute
            updateRouteInList(updatedRoute)
        }
    }

    fun updatePoint(point: GeologicalPoint) {
        _points.value = (_points.value ?: emptyList()).map {
            if (it.id == point.id) point else it
        }
    }

    fun deletePoint(pointId: String) {
        _points.value = (_points.value ?: emptyList()).filter { it.id != pointId }
    }

    fun getPointById(pointId: String): GeologicalPoint? {
        return _points.value?.find { it.id == pointId }
    }

    fun getPointsByType(type: PointType): List<GeologicalPoint> {
        return _points.value?.filter { it.pointType == type } ?: emptyList()
    }

    fun getPointsByRoute(routeId: String): List<GeologicalPoint> {
        return _points.value?.filter { it.routeId == routeId } ?: emptyList()
    }

    fun addRoute(route: Route) {
        _routes.value = listOf(route) + (_routes.value ?: emptyList())
    }

    fun updateRoute(route: Route) {
        updateRouteInList(route)
    }

    private fun updateRouteInList(route: Route) {
        _routes.value = (_routes.value ?: emptyList()).map {
            if (it.id == route.id) route else it
        }
    }

    fun deleteRoute(routeId: String) {
        _routes.value = (_routes.value ?: emptyList()).filter { it.id != routeId }
        if (_currentRoute.value?.id == routeId) {
            _currentRoute.value = null
        }
    }

    fun getRouteById(routeId: String): Route? {
        return _routes.value?.find { it.id == routeId }
    }

    fun startNewRoute(name: String, description: String = ""): Route {
        val route = Route(
            name = name,
            description = description,
            status = RouteStatus.RECORDING,
            createdAt = Date()
        )
        _currentRoute.value = route
        addRoute(route)
        return route
    }

    fun addTrackPointToCurrentRoute(trackPoint: TrackPoint) {
        val route = _currentRoute.value ?: return
        if (route.status != RouteStatus.RECORDING) return

        val newPoints = route.trackPoints + trackPoint
        var newDistance = route.totalDistance
        if (newPoints.size >= 2) {
            val prev = newPoints[newPoints.size - 2]
            newDistance += calculateDistance(
                prev.latitude, prev.longitude,
                trackPoint.latitude, trackPoint.longitude
            )
        }
        val updatedRoute = route.copy(
            trackPoints = newPoints.toMutableList(),
            totalDistance = newDistance,
            updatedAt = Date()
        )
        _currentRoute.value = updatedRoute
        updateRouteInList(updatedRoute)
    }

    fun pauseCurrentRoute() {
        _currentRoute.value?.let {
            val updated = it.copy(status = RouteStatus.PAUSED)
            _currentRoute.value = updated
            updateRouteInList(updated)
        }
    }

    fun resumeCurrentRoute() {
        _currentRoute.value?.let {
            val updated = it.copy(status = RouteStatus.RECORDING)
            _currentRoute.value = updated
            updateRouteInList(updated)
        }
    }

    fun completeCurrentRoute() {
        _currentRoute.value?.let {
            val newDuration = if (it.trackPoints.isNotEmpty()) {
                it.trackPoints.last().timestamp.time - it.trackPoints.first().timestamp.time
            } else 0L
            val updated = it.copy(
                status = RouteStatus.COMPLETED,
                totalDuration = newDuration
            )
            _currentRoute.value = updated
            updateRouteInList(updated)
        }
    }

    /**
     * Recalculate totalDistance from a list of TrackPoints using Haversine.
     */
    fun recalculateDistance(points: List<TrackPoint>): Double {
        var total = 0.0
        for (i in 1 until points.size) {
            total += calculateDistance(
                points[i - 1].latitude, points[i - 1].longitude,
                points[i].latitude, points[i].longitude
            )
        }
        return total
    }

    fun updateCurrentLocation(trackPoint: TrackPoint) {
        _currentLocation.value = trackPoint
    }

    fun updateGpsStatus(status: GpsStatus) {
        _gpsStatus.value = status
    }

    private fun calculateDistance(lat1: Double, lon1: Double, lat2: Double, lon2: Double): Double {
        val R = 6371000.0
        val dLat = Math.toRadians(lat2 - lat1)
        val dLon = Math.toRadians(lon2 - lon1)
        val a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
                Math.cos(Math.toRadians(lat1)) * Math.cos(Math.toRadians(lat2)) *
                Math.sin(dLon / 2) * Math.sin(dLon / 2)
        val c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
        return R * c
    }
}

enum class GpsStatus {
    DISABLED, SEARCHING, FIXED, LOST
}
