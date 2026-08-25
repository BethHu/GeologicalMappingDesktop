package com.example.geomappingmobile.ui.map

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.location.Address
import android.location.Geocoder
import android.net.Uri
import android.os.Bundle
import android.view.LayoutInflater
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.inputmethod.EditorInfo
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.fragment.app.Fragment
import androidx.lifecycle.lifecycleScope
import com.example.geomappingmobile.R
import com.example.geomappingmobile.data.model.GeologicalPoint
import com.example.geomappingmobile.data.model.PointType
import com.example.geomappingmobile.data.model.Route
import com.example.geomappingmobile.data.model.RouteStatus
import com.example.geomappingmobile.data.model.TrackPoint
import com.example.geomappingmobile.data.repository.AppRepository
import com.example.geomappingmobile.data.repository.GpsStatus
import com.example.geomappingmobile.databinding.FragmentMapBinding
import com.example.geomappingmobile.service.LocationService
import com.example.geomappingmobile.util.formatAccuracy
import com.example.geomappingmobile.util.OfflineTileDownloader
import com.esri.arcgisruntime.geometry.AngularUnit
import com.esri.arcgisruntime.geometry.AngularUnitId
import com.esri.arcgisruntime.geometry.AreaUnit
import com.esri.arcgisruntime.geometry.AreaUnitId
import com.esri.arcgisruntime.geometry.GeodeticCurveType
import com.esri.arcgisruntime.geometry.GeometryEngine
import com.esri.arcgisruntime.geometry.LinearUnit
import com.esri.arcgisruntime.geometry.LinearUnitId
import com.esri.arcgisruntime.geometry.Point
import com.esri.arcgisruntime.geometry.PointCollection
import com.esri.arcgisruntime.geometry.Polygon
import com.esri.arcgisruntime.geometry.Polyline
import com.esri.arcgisruntime.geometry.SpatialReferences
import com.esri.arcgisruntime.mapping.ArcGISMap
import com.esri.arcgisruntime.mapping.ArcGISScene
import com.esri.arcgisruntime.mapping.BasemapStyle
import com.esri.arcgisruntime.mapping.Surface
import com.esri.arcgisruntime.mapping.view.Camera
import com.esri.arcgisruntime.mapping.view.Graphic
import com.esri.arcgisruntime.mapping.view.GraphicsOverlay
import com.esri.arcgisruntime.mapping.view.LocationDisplay
import com.esri.arcgisruntime.data.TileCache
import com.esri.arcgisruntime.layers.ArcGISTiledLayer
import com.esri.arcgisruntime.mapping.Basemap
import com.esri.arcgisruntime.loadable.LoadStatus
import com.esri.arcgisruntime.mapping.ArcGISTiledElevationSource
import com.esri.arcgisruntime.symbology.SimpleFillSymbol
import com.esri.arcgisruntime.symbology.SimpleLineSymbol
import com.esri.arcgisruntime.symbology.SimpleMarkerSymbol
import com.esri.arcgisruntime.symbology.TextSymbol
import com.esri.arcgisruntime.tasks.tilecache.ExportTileCacheParameters
import com.esri.arcgisruntime.tasks.tilecache.ExportTileCacheTask
import java.io.File
import com.google.android.material.snackbar.Snackbar
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.util.Locale

enum class MeasureMode { NONE, DISTANCE, AREA, BEARING, SLOPE }
enum class NavMode { DRIVE, WALK, BIKE }

class MapFragment : Fragment() {

    private var _binding: FragmentMapBinding? = null
    private val binding get() = _binding!!

    private lateinit var map: ArcGISMap
    private var scene: ArcGISScene? = null
    private lateinit var measureOverlay: GraphicsOverlay
    private lateinit var navOverlay: GraphicsOverlay
    private lateinit var displayRouteOverlay: GraphicsOverlay
    private lateinit var planningOverlay: GraphicsOverlay
    private lateinit var pointsOverlay: GraphicsOverlay
    private lateinit var plannedRouteOverlay: GraphicsOverlay
    private lateinit var recordedRouteOverlay: GraphicsOverlay
    private var isPlanningMode = false
    private var isAddPointMode = false
    private var measureMode = MeasureMode.NONE
    private val measurePoints = mutableListOf<Point>()
    private var navMode = NavMode.DRIVE
    private var navStartPoint: Point? = null
    private var navEndPoint: Point? = null
    private var isNavPanelVisible = false
    private var mapReady = false
    private var is3dMode = false
    private var currentBasemapIndex = 0
    private var currentDisplayedRoute: Route? = null
    private val pointTypeVisibility = BooleanArray(PointType.entries.size) { true }
    private var lastElevationFetchTime = 0L

    private lateinit var editingOverlay: GraphicsOverlay
    private var isRouteEditMode = false
    private var editingRoute: Route? = null
    private var selectedTrackPointIndex: Int? = null
    private var isPointEditMode = false
    private var editingPoint: GeologicalPoint? = null

    private fun allOverlays(): List<GraphicsOverlay> = listOf(
        measureOverlay, navOverlay, planningOverlay, pointsOverlay,
        displayRouteOverlay, plannedRouteOverlay, recordedRouteOverlay, editingOverlay
    )

    private val locationPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        if (permissions.entries.all { it.value }) enableLocationDisplay()
    }

    private val geoJsonPickerLauncher = registerForActivityResult(
        ActivityResultContracts.GetContent()
    ) { uri: Uri? ->
        uri?.let { downloadGeoJsonArea(it) }
    }

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        android.util.Log.d("MapFragment", "onCreateView start")
        try {
            _binding = FragmentMapBinding.inflate(inflater, container, false)
            android.util.Log.d("MapFragment", "onCreateView done")
            return binding.root
        } catch (e: Exception) {
            android.util.Log.e("MapFragment", "onCreateView failed", e)
            val tv = android.widget.TextView(inflater.context).apply {
                text = "布局加载失败: ${e.message}"
                setTextColor(android.graphics.Color.RED)
                textSize = 16f
                gravity = android.view.Gravity.CENTER
                setPadding(32, 32, 32, 32)
            }
            return tv
        }
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        android.util.Log.d("MapFragment", "onViewCreated start")
        super.onViewCreated(view, savedInstanceState)
        try {
            setupMap()
            android.util.Log.d("MapFragment", "setupMap done")
            setupClickListeners()
            android.util.Log.d("MapFragment", "setupClickListeners done")
            observeLocation()
            observeDisplayRoute()
            observeReplayRoute()
            observePlanning()
            observePoints()
            observeRoutes()
            observeRouteToEdit()
            observePointTypeFilter()
            android.util.Log.d("MapFragment", "observers done")
            autoEnableLocation()
            android.util.Log.d("MapFragment", "onViewCreated done")
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "onViewCreated failed", e)
            try {
                val f = java.io.File(requireContext().filesDir, "crash_log.txt")
                f.writeText(android.util.Log.getStackTraceString(e))
            } catch (_: Exception) {}
            android.widget.Toast.makeText(requireContext(), "初始化失败: ${e.message}", android.widget.Toast.LENGTH_LONG).show()
        }
    }

    override fun onResume() {
        super.onResume()
        android.util.Log.d("MapFragment", "onResume")
        try {
            if (is3dMode) binding.sceneView.resume() else binding.mapView.resume()
        } catch (e: Exception) {
            android.util.Log.e("MapFragment", "onResume failed", e)
        }
    }

    override fun onPause() {
        super.onPause()
        android.util.Log.d("MapFragment", "onPause")
        try {
            if (is3dMode) binding.sceneView.pause() else binding.mapView.pause()
        } catch (e: Exception) {
            android.util.Log.e("MapFragment", "onPause failed", e)
        }
    }

    private fun setupMap() {
        android.util.Log.d("MapFragment", "setupMap: creating ArcGISMap")
        try {
            map = ArcGISMap(BasemapStyle.ARCGIS_IMAGERY)
            android.util.Log.d("MapFragment", "setupMap: ArcGISMap created")
            map.addDoneLoadingListener {
                android.util.Log.d("MapFragment", "Map load status: ${map.loadStatus}")
                if (map.loadStatus == LoadStatus.FAILED_TO_LOAD) {
                    android.util.Log.e("MapFragment", "Map failed to load", map.loadError)
                }
            }
            binding.mapView.map = map
            android.util.Log.d("MapFragment", "setupMap: map set to MapView")
        } catch (e: Exception) {
            android.util.Log.e("MapFragment", "Map init failed", e)
        }
        measureOverlay = GraphicsOverlay()
        binding.mapView.graphicsOverlays.add(measureOverlay)
        navOverlay = GraphicsOverlay()
        binding.mapView.graphicsOverlays.add(navOverlay)
        planningOverlay = GraphicsOverlay()
        binding.mapView.graphicsOverlays.add(planningOverlay)
        pointsOverlay = GraphicsOverlay()
        binding.mapView.graphicsOverlays.add(pointsOverlay)
        // displayRouteOverlay for current single-route view
        displayRouteOverlay = GraphicsOverlay()
        binding.mapView.graphicsOverlays.add(displayRouteOverlay)
        // All-routes layers (batch display)
        plannedRouteOverlay = GraphicsOverlay()
        binding.mapView.graphicsOverlays.add(plannedRouteOverlay)
        recordedRouteOverlay = GraphicsOverlay()
        binding.mapView.graphicsOverlays.add(recordedRouteOverlay)
        editingOverlay = GraphicsOverlay()
        binding.mapView.graphicsOverlays.add(editingOverlay)
        mapReady = true
        android.util.Log.d("MapFragment", "setupMap: graphics overlays added")
        // Set default tap overlay listener (always active)
        binding.tapOverlay.setOnTouchListener { _, event ->
            processTapEvent(event)
            if (is3dMode) binding.sceneView.onTouchEvent(event)
            else binding.mapView.onTouchEvent(event)
            true
        }
    }

    private fun currentBasemapStyle(): BasemapStyle = when (currentBasemapIndex) {
        0 -> BasemapStyle.ARCGIS_IMAGERY
        1 -> BasemapStyle.ARCGIS_TOPOGRAPHIC
        2 -> BasemapStyle.ARCGIS_LIGHT_GRAY
        3 -> BasemapStyle.ARCGIS_TERRAIN
        4 -> BasemapStyle.ARCGIS_STREETS
        else -> BasemapStyle.ARCGIS_IMAGERY
    }

    private fun getCurrentCenterWgs(): Point? {
        return try {
            if (is3dMode) {
                val cam = binding.sceneView.currentViewpointCamera
                Point(cam.location.x, cam.location.y, SpatialReferences.getWgs84())
            } else {
                val vp = binding.mapView.getCurrentViewpoint(
                    com.esri.arcgisruntime.mapping.Viewpoint.Type.CENTER_AND_SCALE
                )
                val center = vp?.targetGeometry as? Point ?: return null
                if (center.spatialReference != null && center.spatialReference != SpatialReferences.getWgs84()) {
                    GeometryEngine.project(center, SpatialReferences.getWgs84()) as? Point
                } else center
            }
        } catch (_: Exception) {
            null
        }
    }

    private fun toggle3dMode() {
        try {
            if (!is3dMode) {
                // 2D -> 3D
                val center = getCurrentCenterWgs()
                    ?: AppRepository.currentLocation.value?.let {
                        Point(it.longitude, it.latitude, SpatialReferences.getWgs84())
                    }
                    ?: Point(116.4, 39.9, SpatialReferences.getWgs84())

                val newScene = ArcGISScene(currentBasemapStyle())
                val elevationSource = ArcGISTiledElevationSource(
                    "https://elevation3d.arcgis.com/arcgis/rest/services/WorldElevation3D/Terrain3D/ImageServer"
                )
                newScene.baseSurface = Surface().apply {
                    elevationSources.add(elevationSource)
                    elevationExaggeration = 1.5f
                }
                scene = newScene
                binding.sceneView.scene = newScene

                // Move overlays MapView -> SceneView
                binding.mapView.graphicsOverlays.clear()
                binding.sceneView.graphicsOverlays.clear()
                for (ov in allOverlays()) {
                    binding.sceneView.graphicsOverlays.add(ov)
                }

                // pitch≈65: 倾斜俯视（0=正下视, 90=水平）
                val camera = Camera(center.y, center.x, 2500.0, 0.0, 65.0, 0.0)
                binding.sceneView.setViewpointCameraAsync(camera)

                binding.mapView.pause()
                binding.mapView.visibility = View.GONE
                binding.sceneView.visibility = View.VISIBLE
                binding.sceneView.resume()
                is3dMode = true
                binding.btn3dToggle.imageTintList =
                    ContextCompat.getColorStateList(requireContext(), R.color.primary)
                Snackbar.make(binding.root, "已切换到 3D 视图（倾斜拖动可旋转）", Snackbar.LENGTH_SHORT).show()
            } else {
                // 3D -> 2D
                val center = getCurrentCenterWgs()
                binding.sceneView.graphicsOverlays.clear()
                binding.mapView.graphicsOverlays.clear()
                for (ov in allOverlays()) {
                    binding.mapView.graphicsOverlays.add(ov)
                }
                binding.sceneView.pause()
                binding.sceneView.visibility = View.GONE
                binding.mapView.visibility = View.VISIBLE
                binding.mapView.resume()
                if (center != null) {
                    binding.mapView.setViewpointCenterAsync(center, 15000.0)
                }
                is3dMode = false
                binding.btn3dToggle.imageTintList =
                    ContextCompat.getColorStateList(requireContext(), R.color.text_primary)
                Snackbar.make(binding.root, "已切换到 2D 视图", Snackbar.LENGTH_SHORT).show()
            }
        } catch (e: Exception) {
            android.util.Log.e("MapFragment", "toggle3dMode failed", e)
            Snackbar.make(binding.root, "3D 切换失败: ${e.message}", Snackbar.LENGTH_LONG).show()
        }
    }

    private var tapHandler: ((Point) -> Unit)? = null
    private var tapPossible = false
    private var tapDownX = 0f
    private var tapDownY = 0f
    private var tapDownTime = 0L

    private fun processTapEvent(event: MotionEvent) {
        try {
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    tapDownX = event.x
                    tapDownY = event.y
                    tapDownTime = System.currentTimeMillis()
                    tapPossible = true
                }
                MotionEvent.ACTION_MOVE -> {
                    if (kotlin.math.abs(event.x - tapDownX) > 15f || kotlin.math.abs(event.y - tapDownY) > 15f)
                        tapPossible = false
                }
                MotionEvent.ACTION_POINTER_DOWN -> {
                    tapPossible = false
                }
                MotionEvent.ACTION_UP -> {
                    if (tapPossible && event.pointerCount == 1) {
                        val elapsed = System.currentTimeMillis() - tapDownTime
                        if (elapsed < 400) {
                            val screenPt = android.graphics.Point(event.x.toInt(), event.y.toInt())
                            val mapPt = if (is3dMode) {
                                binding.sceneView.screenToBaseSurface(screenPt)
                            } else {
                                binding.mapView.screenToLocation(screenPt)
                            }
                            if (mapPt != null) {
                                val host = if (is3dMode) binding.sceneView else binding.mapView
                                host.post {
                                    if (tapHandler != null) tapHandler?.invoke(mapPt)
                                    else onDefaultTap(mapPt)
                                }
                            }
                        }
                    }
                    tapPossible = false
                }
                MotionEvent.ACTION_CANCEL -> {
                    tapPossible = false
                }
            }
        } catch (_: Throwable) {}
    }

    private fun setMapTapHandler(handler: (Point) -> Unit) {
        tapHandler = handler
    }

    private fun clearMapTapHandler() {
        tapHandler = null
    }

    private fun vibrateShort() {
        val vibrator = requireContext().getSystemService(android.content.Context.VIBRATOR_SERVICE) as? android.os.Vibrator
        if (vibrator != null) {
            if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
                vibrator.vibrate(android.os.VibrationEffect.createOneShot(30, android.os.VibrationEffect.DEFAULT_AMPLITUDE))
            } else {
                @Suppress("DEPRECATION")
                vibrator.vibrate(30)
            }
        }
    }

    private fun handleMapTap(pt: Point) {
        try {
            android.util.Log.d("MapFragment", "handleMapTap: pt=(${pt.x},${pt.y}) sr=${pt.spatialReference}")
            val wgs84 = SpatialReferences.getWgs84()
            val wgsPt = if (pt.spatialReference != null && pt.spatialReference != wgs84) {
                GeometryEngine.project(pt, wgs84) as? Point ?: pt
            } else {
                pt
            }
            android.util.Log.d("MapFragment", "handleMapTap: wgsPt=(${wgsPt.x},${wgsPt.y})")
            when {
                isPlanningMode -> {
                    AppRepository.addPlanningPoint(
                        TrackPoint(latitude = wgsPt.y, longitude = wgsPt.x)
                    )
                    vibrateShort()
                }
                isAddPointMode -> addGeologicalPoint(wgsPt)
                measureMode != MeasureMode.NONE -> {
                    addMeasurePoint(wgsPt)
                    binding.mapView.setViewpointCenterAsync(wgsPt)
                }
                isRouteEditMode -> onRouteEditTap(wgsPt)
                isPointEditMode -> onPointEditTap(wgsPt)
                else -> {
                    // Check if tapping near a geological point
                    val pts = AppRepository.points.value ?: return
                    val hit = pts.minByOrNull {
                        val dlat = it.latitude - wgsPt.y
                        val dlng = it.longitude - wgsPt.x
                        dlat * dlat + dlng * dlng
                    }
                    if (hit != null) {
                        val dlat = hit.latitude - wgsPt.y
                        val dlng = hit.longitude - wgsPt.x
                        if (Math.sqrt(dlat * dlat + dlng * dlng) < 0.0005) {
                            showPointEditOptions(hit)
                            return
                        }
                    }
                }
            }
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "handleMapTap crashed", e)
        }
    }

    private fun onDefaultTap(pt: Point) {
        try {
            val wgs84 = SpatialReferences.getWgs84()
            val wgsPt = if (pt.spatialReference != null && pt.spatialReference != wgs84) {
                GeometryEngine.project(pt, wgs84) as? Point ?: pt
            } else {
                pt
            }
            if (measureMode == MeasureMode.NONE && !isPlanningMode && !isAddPointMode) {
                val latStr = String.format("%.6f", wgsPt.y)
                val lngStr = String.format("%.6f", wgsPt.x)
                binding.locationInfo.visibility = View.VISIBLE
                binding.locationInfo.setTextColor(ContextCompat.getColor(requireContext(), R.color.text_secondary))
                binding.locationInfo.text = "纬度: $latStr  经度: $lngStr"
                querySingleElevationAsync(wgsPt.y, wgsPt.x, latStr, lngStr)
                // Check for geological point
                val pts = AppRepository.points.value ?: return
                val hit = pts.minByOrNull {
                    val dlat = it.latitude - wgsPt.y
                    val dlng = it.longitude - wgsPt.x
                    dlat * dlat + dlng * dlng
                }
                if (hit != null) {
                    val dlat = hit.latitude - wgsPt.y
                    val dlng = hit.longitude - wgsPt.x
                    if (Math.sqrt(dlat * dlat + dlng * dlng) < 0.0005) {
                        showPointEditOptions(hit)
                    }
                }
            }
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "onDefaultTap failed", e)
        }
    }

    private fun setupClickListeners() {
        binding.btnLocate.setOnClickListener {
            if (checkLocationPermission()) {
                toggleLocationDisplay()
                // Also show current location coordinates
                val loc = AppRepository.currentLocation.value
                if (loc != null) updateLocationInfo(loc)
            }
        }
        binding.btnBasemap.setOnClickListener { showBasemapSwitcher() }
        binding.btnLayers.setOnClickListener { showLayerControl() }
        binding.btnMeasure.setOnClickListener { showMeasurementTool() }
        binding.btnNavigate.setOnClickListener { toggleNavPanel() }
        binding.btnAddPoint.setOnClickListener { toggleAddPointMode() }
        binding.btn3dToggle.setOnClickListener { toggle3dMode() }
        binding.navCloseBtn.setOnClickListener { hideNavPanel() }
        binding.navSwapBtn.setOnClickListener { swapNavStartEnd() }
        binding.navGoBtn.setOnClickListener { solveRoute() }
        binding.navModeDrive.setOnClickListener { selectNavMode(NavMode.DRIVE) }
        binding.navModeWalk.setOnClickListener { selectNavMode(NavMode.WALK) }
        binding.navModeBike.setOnClickListener { selectNavMode(NavMode.BIKE) }
        binding.navSearchBtn.setOnClickListener { geocodeDestination() }
        binding.btnDownload.setOnClickListener { showDownloadTool() }
        binding.navDestinationInput.setOnEditorActionListener { _, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_SEARCH) { geocodeDestination(); true } else false
        }
        binding.btnPlanUndo.setOnClickListener { AppRepository.undoLastPlanningPoint() }
        binding.btnPlanCoord.setOnClickListener { showCoordInputDialog() }
        binding.btnPlanSave.setOnClickListener { savePlan() }
        binding.btnPlanCancel.setOnClickListener { cancelPlan() }
        binding.btnRouteEditDelete.setOnClickListener { deleteSelectedTrackPoint() }
        binding.btnRouteEditCoord.setOnClickListener { showRouteEditCoordInput() }
        binding.btnRouteEditSave.setOnClickListener { saveRouteEdit() }
        binding.btnRouteEditCancel.setOnClickListener { cancelRouteEdit() }
        binding.btnPointEditCoord.setOnClickListener {
            val p = editingPoint
            if (p != null) showPointCoordInput(p)
            else android.widget.Toast.makeText(requireContext(), "未选中地质点", android.widget.Toast.LENGTH_SHORT).show()
        }
        binding.btnPointEditSave.setOnClickListener { savePointEdit() }
        binding.btnPointEditCancel.setOnClickListener { cancelPointEdit() }
    }

    private fun toggleAddPointMode() {
        isAddPointMode = !isAddPointMode
        if (isAddPointMode) {
            binding.btnAddPoint.imageTintList = ContextCompat.getColorStateList(requireContext(), R.color.primary)
            binding.locationInfo.visibility = View.VISIBLE
            binding.locationInfo.text = "添加点模式: 点击地图添加地质点，再次点击按钮退出"
            binding.locationInfo.setTextColor(ContextCompat.getColor(requireContext(), R.color.primary))
            if (isPlanningMode) {
                AppRepository.cancelPlanning()
            }
            if (measureMode != MeasureMode.NONE) {
                exitMeasureMode()
            }
            setMapTapHandler(::handleMapTap)
        } else {
            binding.btnAddPoint.imageTintList = ContextCompat.getColorStateList(requireContext(), R.color.text_primary)
            binding.locationInfo.visibility = View.GONE
            val loc = AppRepository.currentLocation.value
            if (loc != null) {
                binding.locationInfo.visibility = View.VISIBLE
                updateLocationInfo(loc)
            }
            clearMapTapHandler()
        }
    }

    private fun addGeologicalPoint(point: Point) {
        try {
            val types = PointType.entries.map { it.displayName }.toTypedArray()
            android.app.AlertDialog.Builder(requireContext())
                .setTitle("选择点类型")
                .setItems(types) { _, which ->
                    try {
                        val selectedType = PointType.entries[which]
                        val elevation = if (point.z.isNaN() || point.z.isInfinite()) 0.0 else point.z
                        val gp = GeologicalPoint(
                            pointType = selectedType,
                            latitude = point.y,
                            longitude = point.x,
                            elevation = elevation
                        )
                        android.util.Log.d("MapFragment", "Adding point: ${gp.id} at (${gp.latitude},${gp.longitude})")
                        AppRepository.addPoint(gp)
                        updatePointsOnMap()
                        vibrateShort()
                        Snackbar.make(binding.root, "已添加${selectedType.displayName}", Snackbar.LENGTH_SHORT).show()
                        editGeologicalPoint(gp)
                        toggleAddPointMode()
                    } catch (e: Throwable) {
                        android.util.Log.e("MapFragment", "Point creation failed", e)
                    }
                }
                .show()
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "addGeologicalPoint failed", e)
        }
    }

    private fun editGeologicalPoint(point: GeologicalPoint) {
        try {
            val dialog = com.example.geomappingmobile.ui.collection.PointDetailDialog.newInstance(point)
            dialog.show(childFragmentManager, "PointDetailEdit")
            dialog.setOnSaveListener { savedPoint ->
                AppRepository.updatePoint(savedPoint)
                updatePointsOnMap()
            }
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "editGeologicalPoint failed", e)
        }
    }

    // ========== Route Editing ==========

    fun enterEditRouteMode(route: Route) {
        try {
            editingRoute = route
            isRouteEditMode = true
            isPointEditMode = false
            selectedTrackPointIndex = null
            isAddPointMode = false
            if (measureMode != MeasureMode.NONE) exitMeasureMode()
            displayRouteOverlay.graphics.clear()
            navOverlay.graphics.clear()
            planningOverlay.graphics.clear()
            editingOverlay.graphics.clear()
            binding.routeEditToolbar.visibility = View.VISIBLE
            binding.pointEditToolbar.visibility = View.GONE
            binding.planningToolbar.visibility = View.GONE
            binding.actionButtons.visibility = View.INVISIBLE
            binding.locationInfo.visibility = View.VISIBLE
            binding.locationInfo.text = "编辑路线: 点击轨迹点选中，点击地图新位置移动点"
            setMapTapHandler(::handleMapTap)
            drawRouteEditPoints(route)
            // Zoom to route
            val pts = route.trackPoints.map { Point(it.longitude, it.latitude, SpatialReferences.getWgs84()) }
            if (pts.isNotEmpty()) {
                val minX = pts.minOf { it.x }
                val minY = pts.minOf { it.y }
                val maxX = pts.maxOf { it.x }
                val maxY = pts.maxOf { it.y }
                val envelope = com.esri.arcgisruntime.geometry.Envelope(minX, minY, maxX, maxY, SpatialReferences.getWgs84())
                binding.mapView.setViewpointGeometryAsync(envelope, 50.0)
            }
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "enterEditRouteMode failed", e)
        }
    }

    private fun drawRouteEditPoints(route: Route) {
        editingOverlay.graphics.clear()
        val pts = route.trackPoints.mapIndexed { i, tp ->
            Point(tp.longitude, tp.latitude, SpatialReferences.getWgs84())
        }
        if (pts.isEmpty()) return
        // Draw line
        editingOverlay.graphics.add(
            Graphic(Polyline(PointCollection(pts)),
                SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, 0xFF58A6FF.toInt(), 4f))
        )
        // Draw point markers
        pts.forEachIndexed { i, p ->
            val isSelected = i == selectedTrackPointIndex
            val color = if (isSelected) 0xFFF85149.toInt() else 0xFF3FB950.toInt()
            val size = if (isSelected) 24f else 16f
            val marker = SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, color, size).apply {
                outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, -0x1, 3f)
            }
            editingOverlay.graphics.add(Graphic(p, marker))
            val label = TextSymbol(14f, "${i + 1}", -0x1,
                TextSymbol.HorizontalAlignment.CENTER, TextSymbol.VerticalAlignment.BOTTOM).apply {
                offsetY = -20f
            }
            editingOverlay.graphics.add(Graphic(p, label))
        }
    }

    private fun onRouteEditTap(wgsPt: Point) {
        try {
            val route = editingRoute ?: return
            // First check if tapping near any existing track point (selection)
            val threshold = 0.0003
            val hitIndex = route.trackPoints.indexOfFirst { tp ->
                val dlat = tp.latitude - wgsPt.y
                val dlng = tp.longitude - wgsPt.x
                Math.sqrt(dlat * dlat + dlng * dlng) < threshold
            }
            if (hitIndex >= 0) {
                // Select this point
                selectedTrackPointIndex = hitIndex
                binding.routeEditInfo.text = "已选中 #${hitIndex + 1}，点击地图位置可移动"
                drawRouteEditPoints(route)
                vibrateShort()
                return
            }
            // If a point is already selected, move it to tapped location
            if (selectedTrackPointIndex != null) {
                val idx = selectedTrackPointIndex!!
                val newPoints = route.trackPoints.toMutableList()
                newPoints[idx] = newPoints[idx].copy(latitude = wgsPt.y, longitude = wgsPt.x)
                val newDist = AppRepository.recalculateDistance(newPoints)
                editingRoute = route.copy(trackPoints = newPoints, totalDistance = newDist, updatedAt = java.util.Date())
                binding.routeEditInfo.text = "点 #${idx + 1} 已移动，继续编辑或点完成"
                selectedTrackPointIndex = null
                drawRouteEditPoints(editingRoute!!)
                vibrateShort()
            }
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "onRouteEditTap failed", e)
        }
    }

    private fun deleteSelectedTrackPoint() {
        try {
            val route = editingRoute ?: return
            if (selectedTrackPointIndex == null) {
                android.widget.Toast.makeText(requireContext(), "请先在图中点击选中要删除的点", android.widget.Toast.LENGTH_SHORT).show()
                return
            }
            val idx = selectedTrackPointIndex!!
            if (route.trackPoints.size <= 2) {
                android.widget.Toast.makeText(requireContext(), "轨迹点太少，无法继续删除", android.widget.Toast.LENGTH_SHORT).show()
                return
            }
            val newPoints = route.trackPoints.toMutableList().apply { removeAt(idx) }
            val newDist = AppRepository.recalculateDistance(newPoints)
            editingRoute = route.copy(trackPoints = newPoints, totalDistance = newDist, updatedAt = java.util.Date())
            selectedTrackPointIndex = null
            binding.routeEditInfo.text = "点 #${idx + 1} 已删除"
            drawRouteEditPoints(editingRoute!!)
            vibrateShort()
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "deleteSelectedTrackPoint failed", e)
        }
    }

    private fun showRouteEditCoordInput() {
        try {
            val route = editingRoute ?: return
            val idx = selectedTrackPointIndex
            if (idx == null) {
                android.widget.Toast.makeText(requireContext(), "请先在图中点击选中要编辑的点", android.widget.Toast.LENGTH_SHORT).show()
                return
            }
            val tp = route.trackPoints[idx]
            showCoordEditDialog(
                title = "编辑轨迹点 #${idx + 1} 坐标",
                initialLat = tp.latitude,
                initialLon = tp.longitude
            ) { newLat, newLon ->
                val newPoints = route.trackPoints.toMutableList()
                newPoints[idx] = tp.copy(latitude = newLat, longitude = newLon)
                val newDist = AppRepository.recalculateDistance(newPoints)
                editingRoute = route.copy(trackPoints = newPoints, totalDistance = newDist, updatedAt = java.util.Date())
                selectedTrackPointIndex = null
                binding.routeEditInfo.text = "点 #${idx + 1} 坐标已更新"
                drawRouteEditPoints(editingRoute!!)
            }
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "showRouteEditCoordInput failed", e)
        }
    }

    private fun saveRouteEdit() {
        try {
            val route = editingRoute ?: return
            AppRepository.updateRoute(route)
            selectedTrackPointIndex = null
            exitEditRouteMode()
            android.widget.Toast.makeText(requireContext(), "路线已更新", android.widget.Toast.LENGTH_SHORT).show()
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "saveRouteEdit failed", e)
        }
    }

    private fun cancelRouteEdit() {
        exitEditRouteMode()
        // Redraw original route
        val route = AppRepository.displayRoute.value
        if (route != null) showRouteWithGeoPoints(route, displayRouteOverlay, zoomToFit = false)
        else displayRouteOverlay.graphics.clear()
    }

    private fun exitEditRouteMode() {
        isRouteEditMode = false
        editingRoute = null
        selectedTrackPointIndex = null
        editingOverlay.graphics.clear()
        binding.routeEditToolbar.visibility = View.GONE
        binding.actionButtons.visibility = View.VISIBLE
        clearMapTapHandler()
    }

    // ========== Point Editing ==========

    private fun showPointEditOptions(point: GeologicalPoint) {
        try {
            val options = arrayOf("拖动位置", "坐标输入", "编辑详情")
            com.google.android.material.dialog.MaterialAlertDialogBuilder(requireContext())
                .setTitle("编辑地质点 - ${point.customId.ifEmpty { point.pointType.displayName }}")
                .setItems(options) { _, which ->
                    when (which) {
                        0 -> enterEditPointMode(point)
                        1 -> showPointCoordInput(point)
                        2 -> editGeologicalPoint(point)
                    }
                }
                .setNegativeButton("取消", null)
                .show()
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "showPointEditOptions failed", e)
        }
    }

    private fun enterEditPointMode(point: GeologicalPoint) {
        try {
            editingPoint = point
            isPointEditMode = true
            isRouteEditMode = false
            if (measureMode != MeasureMode.NONE) exitMeasureMode()
            binding.pointEditToolbar.visibility = View.VISIBLE
            binding.routeEditToolbar.visibility = View.GONE
            binding.actionButtons.visibility = View.INVISIBLE
            binding.locationInfo.visibility = View.VISIBLE
            binding.locationInfo.text = "编辑地质点: 点击新位置移动点"
            setMapTapHandler(::handleMapTap)
            // Highlight the point on the editing overlay
            editingOverlay.graphics.clear()
            val mp = Point(point.longitude, point.latitude, SpatialReferences.getWgs84())
            val highlight = SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, 0xFFF85149.toInt(), 28f).apply {
                outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, -0x1, 4f)
            }
            editingOverlay.graphics.add(Graphic(mp, highlight))
            binding.mapView.setViewpointCenterAsync(mp)
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "enterEditPointMode failed", e)
        }
    }

    private fun onPointEditTap(wgsPt: Point) {
        try {
            val point = editingPoint ?: return
            // Move the point to tapped location
            editingPoint = point.copy(latitude = wgsPt.y, longitude = wgsPt.x)
            binding.locationInfo.text = "已移动至: ${String.format("%.6f", wgsPt.y)}, ${String.format("%.6f", wgsPt.x)}"
            // Update overlay
            editingOverlay.graphics.clear()
            val mp = Point(wgsPt.x, wgsPt.y, SpatialReferences.getWgs84())
            val highlight = SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, 0xFFF85149.toInt(), 28f).apply {
                outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, -0x1, 4f)
            }
            editingOverlay.graphics.add(Graphic(mp, highlight))
            vibrateShort()
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "onPointEditTap failed", e)
        }
    }

    private fun showPointCoordInput(point: GeologicalPoint) {
        try {
            showCoordEditDialog(
                title = "编辑坐标 - ${point.customId.ifEmpty { point.pointType.displayName }}",
                initialLat = point.latitude,
                initialLon = point.longitude
            ) { newLat, newLon ->
                val updated = point.copy(latitude = newLat, longitude = newLon)
                AppRepository.updatePoint(updated)
                updatePointsOnMap()
                // Zoom to updated point so it doesn't appear to "disappear"
                val mp = com.esri.arcgisruntime.geometry.Point(newLon, newLat, com.esri.arcgisruntime.geometry.SpatialReferences.getWgs84())
                binding.mapView.setViewpointCenterAsync(mp, 5000.0)
                android.widget.Toast.makeText(requireContext(), "坐标已更新", android.widget.Toast.LENGTH_SHORT).show()
            }
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "showPointCoordInput failed", e)
        }
    }

    private fun savePointEdit() {
        try {
            val point = editingPoint ?: return
            AppRepository.updatePoint(point)
            updatePointsOnMap()
            exitEditPointMode()
            android.widget.Toast.makeText(requireContext(), "地质点位置已更新", android.widget.Toast.LENGTH_SHORT).show()
        } catch (e: Throwable) {
            android.util.Log.e("MapFragment", "savePointEdit failed", e)
        }
    }

    private fun cancelPointEdit() {
        editingPoint = null
        exitEditPointMode()
    }

    private fun exitEditPointMode() {
        isPointEditMode = false
        editingPoint = null
        editingOverlay.graphics.clear()
        binding.pointEditToolbar.visibility = View.GONE
        binding.actionButtons.visibility = View.VISIBLE
        clearMapTapHandler()
    }

    private fun updatePointsOnMap() {
        pointsOverlay.graphics.clear()
        val points = AppRepository.points.value ?: return
        points.forEachIndexed { _, gp ->
            val idx = PointType.entries.indexOf(gp.pointType)
            if (idx < 0 || idx >= pointTypeVisibility.size || !pointTypeVisibility[idx]) return@forEachIndexed
            val mapPoint = Point(gp.longitude, gp.latitude, SpatialReferences.getWgs84())
            val color = when (gp.pointType) {
                PointType.GEOLOGICAL -> 0xFF3FB950.toInt()
                PointType.STRUCTURAL -> 0xFFD29922.toInt()
                PointType.SAMPLE -> 0xFF58A6FF.toInt()
                PointType.PHOTO -> 0xFFA371F7.toInt()
                PointType.LANDFORM -> 0xFFF85149.toInt()
                PointType.HYDROLOGICAL -> 0xFF2188FF.toInt()
                PointType.ENGINEERING -> 0xFFDB6D28.toInt()
                else -> 0xFF58A6FF.toInt()
            }
            val symbol = SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, color, 18f).apply {
                outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, -0x1, 3f)
            }
            pointsOverlay.graphics.add(Graphic(mapPoint, symbol))
            val label = TextSymbol(12f, gp.customId.ifEmpty { gp.pointType.displayName.take(2) },
                -0x1, TextSymbol.HorizontalAlignment.CENTER, TextSymbol.VerticalAlignment.BOTTOM).apply {
                offsetY = -22f
            }
            pointsOverlay.graphics.add(Graphic(mapPoint, label))
        }
    }

    private fun showRouteWithGeoPoints(route: Route, overlay: GraphicsOverlay = displayRouteOverlay, isPlanned: Boolean = route.status == RouteStatus.PLANNED, zoomToFit: Boolean = true) {
        overlay.graphics.clear()
        val points = route.trackPoints.map { Point(it.longitude, it.latitude, SpatialReferences.getWgs84()) }
        android.util.Log.d("MapFragment", "showRouteWithGeoPoints: trackPts=${route.trackPoints.size} mapPts=${points.size}")
        if (points.isEmpty()) {
            android.util.Log.w("MapFragment", "No track points to draw")
            return
        }

        val lineColor = if (isPlanned) 0xFF42A5F5.toInt() else 0xFFEF5350.toInt()
        val lineStyle = if (isPlanned) SimpleLineSymbol.Style.DASH else SimpleLineSymbol.Style.SOLID
        val startColor = if (isPlanned) 0xFF42A5F5.toInt() else 0xFFEF5350.toInt()
        val endFillColor = if (isPlanned) 0xFF1E88E5.toInt() else 0xFFC62828.toInt()
        val endStrokeColor = if (isPlanned) 0xFF42A5F5.toInt() else 0xFFEF5350.toInt()

        // Route line
        overlay.graphics.add(
            Graphic(Polyline(PointCollection(points)),
                SimpleLineSymbol(lineStyle, lineColor, 4f))
        )
        // Start/end markers
        if (points.size >= 2) {
            overlay.graphics.add(
                Graphic(points.first(),
                    SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, startColor, 12f).apply {
                        outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, -0x1, 2f)
                    })
            )
            overlay.graphics.add(
                Graphic(points.last(),
                    SimpleMarkerSymbol(SimpleMarkerSymbol.Style.DIAMOND, 0xFF30363D.toInt(), 16f).apply {
                        outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, endStrokeColor, 2f)
                    })
            )
        }
        // Geological points along the route
        route.pointIds.forEach { ptId ->
            val gp = AppRepository.getPointById(ptId) ?: return@forEach
            val gpPoint = Point(gp.longitude, gp.latitude, SpatialReferences.getWgs84())
            val color = when (gp.pointType) {
                PointType.GEOLOGICAL -> 0xFF3FB950.toInt()
                PointType.STRUCTURAL -> 0xFFD29922.toInt()
                PointType.SAMPLE -> 0xFF58A6FF.toInt()
                PointType.PHOTO -> 0xFFA371F7.toInt()
                PointType.LANDFORM -> 0xFFF85149.toInt()
                PointType.HYDROLOGICAL -> 0xFF2188FF.toInt()
                PointType.ENGINEERING -> 0xFFDB6D28.toInt()
                else -> 0xFF58A6FF.toInt()
            }
            overlay.graphics.add(
                Graphic(gpPoint, SimpleMarkerSymbol(SimpleMarkerSymbol.Style.TRIANGLE, color, 10f))
            )
            overlay.graphics.add(
                Graphic(gpPoint, TextSymbol(10f, gp.customId.ifEmpty { gp.pointType.displayName.take(2) },
                    -0x1, TextSymbol.HorizontalAlignment.LEFT, TextSymbol.VerticalAlignment.BOTTOM))
            )
        }
        if (zoomToFit) {
            val extent = GeometryEngine.combineExtents(points)
            if (extent != null) binding.mapView.setViewpointGeometryAsync(extent, 60.0)
        }
    }

    // Draw just the route track line (no points, no zoom) for batch route layer display
    private fun drawRouteTrackLine(route: Route, overlay: GraphicsOverlay, isPlanned: Boolean) {
        val pts = route.trackPoints.map { Point(it.longitude, it.latitude, SpatialReferences.getWgs84()) }
        if (pts.size < 2) return
        val lineColor = if (isPlanned) 0xFF42A5F5.toInt() else 0xFFEF5350.toInt()
        val lineStyle = if (isPlanned) SimpleLineSymbol.Style.DASH else SimpleLineSymbol.Style.SOLID
        overlay.graphics.add(
            Graphic(Polyline(PointCollection(pts)),
                SimpleLineSymbol(lineStyle, lineColor, 3f))
        )
        overlay.graphics.add(
            Graphic(pts.first(),
                SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, lineColor, 8f))
        )
        overlay.graphics.add(
            Graphic(pts.last(),
                SimpleMarkerSymbol(SimpleMarkerSymbol.Style.DIAMOND, lineColor, 10f))
        )
    }

    private fun refreshRouteLayers() {
        plannedRouteOverlay.graphics.clear()
        recordedRouteOverlay.graphics.clear()
        val routes = AppRepository.routes.value ?: return
        routes.filter { it.status == RouteStatus.PLANNED && plannedRouteOverlay.isVisible }
            .forEach { drawRouteTrackLine(it, plannedRouteOverlay, isPlanned = true) }
        routes.filter { it.status != RouteStatus.PLANNED && recordedRouteOverlay.isVisible }
            .forEach { drawRouteTrackLine(it, recordedRouteOverlay, isPlanned = false) }
    }

    private fun observeDisplayRoute() {
        AppRepository.displayRoute.observe(viewLifecycleOwner) { route ->
            if (route == null) { displayRouteOverlay.graphics.clear(); return@observe }
            android.util.Log.d("MapFragment", "observeDisplayRoute: ${route.name} status=${route.status}")
            if (!mapReady) {
                android.util.Log.w("MapFragment", "map not ready yet, deferring")
                return@observe
            }
            currentDisplayedRoute = route
            // Only draw if the corresponding layer is visible
            val routeLayerVisible = if (route.status == RouteStatus.PLANNED) plannedRouteOverlay.isVisible else recordedRouteOverlay.isVisible
            if (!routeLayerVisible) {
                android.util.Log.d("MapFragment", "layer hidden, skipping display")
                displayRouteOverlay.graphics.clear()
                return@observe
            }
            showRouteWithGeoPoints(route, displayRouteOverlay, zoomToFit = true)
        }
    }

    private fun observeReplayRoute() {
        AppRepository.replayRoute.observe(viewLifecycleOwner) { route ->
            if (route == null) return@observe
            showRouteWithGeoPoints(route, displayRouteOverlay, zoomToFit = true)
            // Animate a marker along the route
            if (route.trackPoints.size >= 2) {
                val animPoints = route.trackPoints.map { Point(it.longitude, it.latitude, SpatialReferences.getWgs84()) }
                val replayMarker = SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, 0xFFF85149.toInt(), 14f).apply {
                    outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, -0x1, 3f)
                }
                val markerGraphic = Graphic(animPoints.first(), replayMarker)
                displayRouteOverlay.graphics.add(markerGraphic)

                val ticker = object : Runnable {
                    var index = 0
                    override fun run() {
                        if (index < animPoints.size) {
                            markerGraphic.geometry = animPoints[index]
                            index++
                            binding.mapView.postDelayed(this, 200)
                        }
                    }
                }
                binding.mapView.post(ticker)
            }
        }
    }

    private fun observePlanning() {
        AppRepository.isPlanning.observe(viewLifecycleOwner) { planning ->
            isPlanningMode = planning
            binding.planningToolbar.visibility = if (planning) View.VISIBLE else View.GONE
            if (planning) {
                isAddPointMode = false
                binding.btnAddPoint.imageTintList = ContextCompat.getColorStateList(requireContext(), R.color.text_primary)
                if (measureMode != MeasureMode.NONE) exitMeasureMode()
                displayRouteOverlay.graphics.clear()
                navOverlay.graphics.clear()
                binding.actionButtons.visibility = View.INVISIBLE
                binding.locationInfo.visibility = View.GONE
                hideNavPanel()
                setMapTapHandler(::handleMapTap)
            } else {
                binding.actionButtons.visibility = View.VISIBLE
                clearMapTapHandler()
            }
        }
        AppRepository.planningPoints.observe(viewLifecycleOwner) { pts ->
            try {
                planningOverlay.graphics.clear()
                if (pts.isEmpty()) {
                    binding.planningInfo.text = "规划模式: 点击地图添加点"
                    return@observe
                }
                val mapPts = pts.map { Point(it.longitude, it.latitude, SpatialReferences.getWgs84()) }
                planningOverlay.graphics.add(
                    Graphic(Polyline(PointCollection(mapPts)),
                        SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, 0xFF3FB950.toInt(), 4f))
                )
                mapPts.forEachIndexed { i, p ->
                    val marker = SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, 0xFF3FB950.toInt(), 16f).apply {
                        outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, -0x1, 3f)
                    }
                    planningOverlay.graphics.add(Graphic(p, marker))
                    val label = TextSymbol(16f, "${i + 1}", 0xFF3FB950.toInt(),
                        TextSymbol.HorizontalAlignment.CENTER, TextSymbol.VerticalAlignment.MIDDLE).apply {
                        outlineColor = -0x1
                        outlineWidth = 2f
                        offsetY = -18f
                    }
                    planningOverlay.graphics.add(Graphic(p, label))
                }
                val startMarker = SimpleMarkerSymbol(SimpleMarkerSymbol.Style.DIAMOND, 0xFF58A6FF.toInt(), 18f).apply {
                    outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, -0x1, 3f)
                }
                planningOverlay.graphics.add(Graphic(mapPts.first(), startMarker))
                val dist = AppRepository.recalculateDistance(pts)
                val distStr = if (dist >= 1000) String.format("%.2f km", dist / 1000) else String.format("%.0f m", dist)
                binding.planningInfo.text = "${pts.size}个点 | 总长 $distStr | 点击继续加点"
            } catch (e: Throwable) {
                android.util.Log.e("MapFragment", "planningPoints observer crashed", e)
            }
        }
    }

    private fun observePoints() {
        AppRepository.points.observe(viewLifecycleOwner) {
            updatePointsOnMap()
        }
    }

    private fun observeRoutes() {
        AppRepository.routes.observe(viewLifecycleOwner) {
            refreshRouteLayers()
        }
    }

    private fun observeRouteToEdit() {
        AppRepository.routeToEdit.observe(viewLifecycleOwner) { route ->
            if (route != null && isAdded && mapReady) {
                enterEditRouteMode(route)
                AppRepository.setRouteToEdit(null)
            }
        }
    }

    private fun observePointTypeFilter() {
        AppRepository.pointTypeFilter.observe(viewLifecycleOwner) { filterType ->
            if (filterType == null) {
                pointTypeVisibility.fill(true)
            } else {
                val idx = PointType.entries.indexOf(filterType)
                pointTypeVisibility.fill(false)
                if (idx >= 0) pointTypeVisibility[idx] = true
            }
            updatePointsOnMap()
        }
    }

    private fun showCoordEditDialog(title: String, initialLat: Double, initialLon: Double, onConfirm: (Double, Double) -> Unit) {
        val ctx = requireContext()
        val isDd = booleanArrayOf(true)
        val textColor = ContextCompat.getColor(ctx, R.color.text_primary)
        val hintColor = ContextCompat.getColor(ctx, R.color.text_disabled)
        val accentColor = ContextCompat.getColor(ctx, R.color.primary)
        val inputBg = ContextCompat.getColor(ctx, R.color.dark_card)

        fun editText(hint: String, isDecimal: Boolean = true): android.widget.EditText {
            return android.widget.EditText(ctx).apply {
                this.hint = hint
                setTextColor(textColor)
                setHintTextColor(hintColor)
                setTextSize(14f)
                setPadding(12, 10, 12, 10)
                setBackgroundColor(inputBg)
                inputType = android.text.InputType.TYPE_CLASS_TEXT
            }
        }

        val latN = android.widget.RadioButton(ctx).apply { text = "N"; id = android.view.View.generateViewId(); isChecked = true; setTextColor(textColor) }
        val latS = android.widget.RadioButton(ctx).apply { text = "S"; id = android.view.View.generateViewId(); setTextColor(textColor) }
        val lngE = android.widget.RadioButton(ctx).apply { text = "E"; id = android.view.View.generateViewId(); isChecked = true; setTextColor(textColor) }
        val lngW = android.widget.RadioButton(ctx).apply { text = "W"; id = android.view.View.generateViewId(); setTextColor(textColor) }

        val latDd = editText("纬度 (DD 如: 39.91)").apply { setText(String.format("%.6f", initialLat)) }
        val latDeg = editText("度", isDecimal = false)
        val latMin = editText("分", isDecimal = false)
        val latSec = editText("秒")
        val latDmsRow = android.widget.LinearLayout(ctx).apply {
            orientation = android.widget.LinearLayout.HORIZONTAL
            visibility = android.view.View.GONE
            addView(latDeg, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(latMin, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(latSec, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
        }
        val latLabel = android.widget.TextView(ctx).apply { text = "纬度"; setTextColor(accentColor); textSize = 13f }
        val latNs = android.widget.RadioGroup(ctx).apply {
            orientation = android.widget.RadioGroup.HORIZONTAL
            addView(latN); addView(latS)
        }

        val lngDd = editText("经度 (DD 如: 116.39)").apply { setText(String.format("%.6f", initialLon)) }
        val lngDeg = editText("度", isDecimal = false)
        val lngMin = editText("分", isDecimal = false)
        val lngSec = editText("秒")
        val lngDmsRow = android.widget.LinearLayout(ctx).apply {
            orientation = android.widget.LinearLayout.HORIZONTAL
            visibility = android.view.View.GONE
            addView(lngDeg, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(lngMin, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(lngSec, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
        }
        val lngLabel = android.widget.TextView(ctx).apply { text = "经度"; setTextColor(accentColor); textSize = 13f }
        val lngEw = android.widget.RadioGroup(ctx).apply {
            orientation = android.widget.RadioGroup.HORIZONTAL
            addView(lngE); addView(lngW)
        }

        fun toggleFormat(dd: Boolean) {
            isDd[0] = dd
            latDd.visibility = if (dd) android.view.View.VISIBLE else android.view.View.GONE
            latDmsRow.visibility = if (dd) android.view.View.GONE else android.view.View.VISIBLE
            lngDd.visibility = if (dd) android.view.View.VISIBLE else android.view.View.GONE
            lngDmsRow.visibility = if (dd) android.view.View.GONE else android.view.View.VISIBLE
        }

        val btnDd = android.widget.RadioButton(ctx).apply { text = "十进制 (DD)"; id = android.view.View.generateViewId(); isChecked = true; setTextColor(textColor) }
        val btnDms = android.widget.RadioButton(ctx).apply { text = "度分秒 (DMS)"; id = android.view.View.generateViewId(); setTextColor(textColor) }
        val formatRow = android.widget.RadioGroup(ctx).apply {
            orientation = android.widget.RadioGroup.HORIZONTAL
            addView(btnDd); addView(btnDms)
            setOnCheckedChangeListener { _, checkedId -> toggleFormat(checkedId == btnDd.id) }
        }

        fun buildCol(label: android.widget.TextView, dd: android.widget.EditText, dmsRow: android.widget.LinearLayout, radioGroup: android.widget.RadioGroup): android.widget.LinearLayout {
            return android.widget.LinearLayout(ctx).apply {
                orientation = android.widget.LinearLayout.VERTICAL
                setPadding(4, 0, 4, 0)
                addView(label)
                addView(dd, android.widget.LinearLayout.LayoutParams(-1, -2))
                addView(dmsRow, android.widget.LinearLayout.LayoutParams(-1, -2))
                val radioLabel = android.widget.TextView(ctx).apply { text = "方向"; setTextColor(hintColor); textSize = 11f }
                addView(radioLabel)
                addView(radioGroup)
            }
        }

        val colsRow = android.widget.LinearLayout(ctx).apply {
            orientation = android.widget.LinearLayout.HORIZONTAL
            addView(buildCol(latLabel, latDd, latDmsRow, latNs), android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(android.widget.Space(ctx), android.widget.LinearLayout.LayoutParams(16, 0))
            addView(buildCol(lngLabel, lngDd, lngDmsRow, lngEw), android.widget.LinearLayout.LayoutParams(0, -2, 1f))
        }

        val root = android.widget.ScrollView(ctx).apply {
            val inner = android.widget.LinearLayout(ctx).apply {
                orientation = android.widget.LinearLayout.VERTICAL
                setPadding(32, 16, 32, 16)
                addView(formatRow)
                addView(android.widget.Space(ctx), android.widget.LinearLayout.LayoutParams(-1, 12))
                addView(colsRow)
            }
            addView(inner)
        }

        android.app.AlertDialog.Builder(ctx)
            .setTitle(title)
            .setView(root)
            .setPositiveButton("确定") { _, _ ->
                try {
                    val lat: Double; val lng: Double
                    if (isDd[0]) {
                        val latStr = latDd.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "")
                        val lngStr = lngDd.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "")
                        if (latStr.isEmpty() || lngStr.isEmpty()) {
                            com.google.android.material.snackbar.Snackbar.make(binding.root, "输入格式有误", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                            return@setPositiveButton
                        }
                        try {
                            lat = latStr.toDouble()
                            lng = lngStr.toDouble()
                        } catch (_: NumberFormatException) {
                            com.google.android.material.snackbar.Snackbar.make(binding.root, "输入格式有误", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                            return@setPositiveButton
                        }
                    } else {
                        lat = (latDeg.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) +
                              (latMin.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) / 60.0 +
                              (latSec.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) / 3600.0
                        lng = (lngDeg.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) +
                              (lngMin.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) / 60.0 +
                              (lngSec.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) / 3600.0
                    }
                    val latSigned = if (latNs.checkedRadioButtonId == latN.id) lat else -lat
                    val lngSigned = if (lngEw.checkedRadioButtonId == lngE.id) lng else -lng
                    if (latSigned < -90 || latSigned > 90 || lngSigned < -180 || lngSigned > 180) {
                        com.google.android.material.snackbar.Snackbar.make(binding.root, "坐标超出范围", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                        return@setPositiveButton
                    }
                    onConfirm(latSigned, lngSigned)
                } catch (_: Exception) {
                    com.google.android.material.snackbar.Snackbar.make(binding.root, "输入格式有误", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                }
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun showCoordInputDialog() {
        val ctx = requireContext()
        val isDd = booleanArrayOf(true)
        val textColor = ContextCompat.getColor(ctx, R.color.text_primary)
        val hintColor = ContextCompat.getColor(ctx, R.color.text_disabled)
        val accentColor = ContextCompat.getColor(ctx, R.color.primary)
        val inputBg = ContextCompat.getColor(ctx, R.color.dark_card)

        fun editText(hint: String, isDecimal: Boolean = true): android.widget.EditText {
            return android.widget.EditText(ctx).apply {
                this.hint = hint
                setTextColor(textColor)
                setHintTextColor(hintColor)
                setTextSize(14f)
                setPadding(12, 10, 12, 10)
                setBackgroundColor(inputBg)
                inputType = android.text.InputType.TYPE_CLASS_TEXT
            }
        }

        val latN = android.widget.RadioButton(ctx).apply { text = "N"; id = android.view.View.generateViewId(); isChecked = true; setTextColor(textColor) }
        val latS = android.widget.RadioButton(ctx).apply { text = "S"; id = android.view.View.generateViewId(); setTextColor(textColor) }
        val lngE = android.widget.RadioButton(ctx).apply { text = "E"; id = android.view.View.generateViewId(); isChecked = true; setTextColor(textColor) }
        val lngW = android.widget.RadioButton(ctx).apply { text = "W"; id = android.view.View.generateViewId(); setTextColor(textColor) }

        // --- Lat inputs ---
        val latDd = editText("纬度 (DD 如: 39.91)")
        val latDeg = editText("度", isDecimal = false)
        val latMin = editText("分", isDecimal = false)
        val latSec = editText("秒")
        val latDmsRow = android.widget.LinearLayout(ctx).apply {
            orientation = android.widget.LinearLayout.HORIZONTAL
            visibility = android.view.View.GONE
            addView(latDeg, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(latMin, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(latSec, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
        }
        val latLabel = android.widget.TextView(ctx).apply { text = "纬度"; setTextColor(accentColor); textSize = 13f }
        val latNs = android.widget.RadioGroup(ctx).apply {
            orientation = android.widget.RadioGroup.HORIZONTAL
            addView(latN); addView(latS)
        }

        // --- Lng inputs ---
        val lngDd = editText("经度 (DD 如: 116.39)")
        val lngDeg = editText("度", isDecimal = false)
        val lngMin = editText("分", isDecimal = false)
        val lngSec = editText("秒")
        val lngDmsRow = android.widget.LinearLayout(ctx).apply {
            orientation = android.widget.LinearLayout.HORIZONTAL
            visibility = android.view.View.GONE
            addView(lngDeg, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(lngMin, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(lngSec, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
        }
        val lngLabel = android.widget.TextView(ctx).apply { text = "经度"; setTextColor(accentColor); textSize = 13f }
        val lngEw = android.widget.RadioGroup(ctx).apply {
            orientation = android.widget.RadioGroup.HORIZONTAL
            addView(lngE); addView(lngW)
        }

        fun toggleFormat(dd: Boolean) {
            isDd[0] = dd
            latDd.visibility = if (dd) android.view.View.VISIBLE else android.view.View.GONE
            latDmsRow.visibility = if (dd) android.view.View.GONE else android.view.View.VISIBLE
            lngDd.visibility = if (dd) android.view.View.VISIBLE else android.view.View.GONE
            lngDmsRow.visibility = if (dd) android.view.View.GONE else android.view.View.VISIBLE
        }

        val btnDd = android.widget.RadioButton(ctx).apply { text = "十进制 (DD)"; id = android.view.View.generateViewId(); isChecked = true; setTextColor(textColor) }
        val btnDms = android.widget.RadioButton(ctx).apply { text = "度分秒 (DMS)"; id = android.view.View.generateViewId(); setTextColor(textColor) }
        val formatRow = android.widget.RadioGroup(ctx).apply {
            orientation = android.widget.RadioGroup.HORIZONTAL
            addView(btnDd); addView(btnDms)
            setOnCheckedChangeListener { _, checkedId -> toggleFormat(checkedId == btnDd.id) }
        }

        fun buildCol(label: android.widget.TextView, dd: android.widget.EditText, dmsRow: android.widget.LinearLayout, radioGroup: android.widget.RadioGroup): android.widget.LinearLayout {
            return android.widget.LinearLayout(ctx).apply {
                orientation = android.widget.LinearLayout.VERTICAL
                setPadding(4, 0, 4, 0)
                addView(label)
                addView(dd, android.widget.LinearLayout.LayoutParams(-1, -2))
                addView(dmsRow, android.widget.LinearLayout.LayoutParams(-1, -2))
                val radioLabel = android.widget.TextView(ctx).apply { text = "方向"; setTextColor(hintColor); textSize = 11f }
                addView(radioLabel)
                addView(radioGroup)
            }
        }

        val colsRow = android.widget.LinearLayout(ctx).apply {
            orientation = android.widget.LinearLayout.HORIZONTAL
            addView(buildCol(latLabel, latDd, latDmsRow, latNs), android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(android.widget.Space(ctx), android.widget.LinearLayout.LayoutParams(16, 0))
            addView(buildCol(lngLabel, lngDd, lngDmsRow, lngEw), android.widget.LinearLayout.LayoutParams(0, -2, 1f))
        }

        val root = android.widget.ScrollView(ctx).apply {
            val inner = android.widget.LinearLayout(ctx).apply {
                orientation = android.widget.LinearLayout.VERTICAL
                setPadding(32, 16, 32, 16)
                addView(formatRow)
                addView(android.widget.Space(ctx), android.widget.LinearLayout.LayoutParams(-1, 12))
                addView(colsRow)
            }
            addView(inner)
        }

        android.app.AlertDialog.Builder(ctx)
            .setTitle("输入坐标添加规划点")
            .setView(root)
            .setPositiveButton("添加") { _, _ ->
                try {
                    val lat: Double; val lng: Double
                    if (isDd[0]) {
                        val latStr = latDd.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "")
                        val lngStr = lngDd.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "")
                        if (latStr.isEmpty() || lngStr.isEmpty()) {
                            com.google.android.material.snackbar.Snackbar.make(binding.root, "输入格式有误", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                            return@setPositiveButton
                        }
                        try {
                            lat = latStr.toDouble()
                            lng = lngStr.toDouble()
                        } catch (_: NumberFormatException) {
                            com.google.android.material.snackbar.Snackbar.make(binding.root, "输入格式有误", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                            return@setPositiveButton
                        }
                    } else {
                        lat = (latDeg.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) +
                              (latMin.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) / 60.0 +
                              (latSec.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) / 3600.0
                        lng = (lngDeg.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) +
                              (lngMin.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) / 60.0 +
                              (lngSec.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) / 3600.0
                    }
                    val latSigned = if (latNs.checkedRadioButtonId == latN.id) lat else -lat
                    val lngSigned = if (lngEw.checkedRadioButtonId == lngE.id) lng else -lng
                    if (latSigned < -90 || latSigned > 90 || lngSigned < -180 || lngSigned > 180) {
                        com.google.android.material.snackbar.Snackbar.make(binding.root, "坐标超出范围", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                        return@setPositiveButton
                    }
                    AppRepository.addPlanningPoint(TrackPoint(latitude = latSigned, longitude = lngSigned))
                    binding.mapView.setViewpointCenterAsync(Point(lngSigned, latSigned, SpatialReferences.getWgs84()), 5000.0)
                    com.google.android.material.snackbar.Snackbar.make(binding.root, "已添加点", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                } catch (_: Exception) {
                    com.google.android.material.snackbar.Snackbar.make(binding.root, "输入格式有误", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                }
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun savePlan() {
        val pts = AppRepository.planningPoints.value ?: emptyList()
        if (pts.isEmpty()) {
            com.google.android.material.snackbar.Snackbar.make(binding.root, "请至少添加一个点", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
            return
        }
        val input = com.google.android.material.textfield.TextInputEditText(requireContext()).apply {
            hint = "路线名称"
            setText("规划路线 ${java.text.SimpleDateFormat("HH:mm", java.util.Locale.getDefault()).format(java.util.Date())}")
            setTextColor(android.graphics.Color.WHITE)
            setHintTextColor(android.graphics.Color.GRAY)
            selectAll()
        }
        android.app.AlertDialog.Builder(requireContext())
            .setTitle("保存规划路线")
            .setView(input)
            .setPositiveButton("保存") { _, _ ->
                val name = input.text?.toString()?.trim() ?: "规划路线"
                val route = AppRepository.savePlanningRoute(name)
                AppRepository.setDisplayRoute(route)
                com.google.android.material.snackbar.Snackbar.make(binding.root, "规划路线已保存", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun cancelPlan() {
        AppRepository.cancelPlanning()
        planningOverlay.graphics.clear()
    }

    private fun observeLocation() {
        AppRepository.currentLocation.observe(viewLifecycleOwner) { tp ->
            tp?.let { updateLocationInfo(it) }
        }
        AppRepository.gpsStatus.observe(viewLifecycleOwner) { status ->
            updateGpsStatusUI(status)
        }
    }

    private fun updateLocationInfo(tp: TrackPoint) {
        binding.locationInfo.visibility = View.VISIBLE
        val elev = if (tp.elevation.isNaN() || tp.elevation <= 0.0) {
            autoFetchElevation(tp.latitude, tp.longitude)
            "获取中"
        } else String.format("%.1f", tp.elevation)
        binding.locationInfo.text = String.format(
            "GPS: %.6f, %.6f | 高程: %sm | 精度: ±%.0fm",
            tp.latitude, tp.longitude, elev, tp.accuracy
        )
        updateLocateButton(tp.accuracy)
    }

    private fun autoFetchElevation(lat: Double, lng: Double) {
        val now = System.currentTimeMillis()
        if (now - lastElevationFetchTime < 10_000) return
        lastElevationFetchTime = now
        lifecycleScope.launch {
            try {
                val url = "https://api.open-elevation.com/api/v1/lookup?locations=$lat,$lng"
                val json = withContext(Dispatchers.IO) { java.net.URL(url).readText() }
                val obj = com.google.gson.Gson().fromJson(json, com.google.gson.JsonObject::class.java)
                val results = obj.getAsJsonArray("results")
                if (results != null && results.size() >= 1) {
                    val elev = results[0].asJsonObject.get("elevation").asDouble
                    val gpsText = binding.locationInfo.text?.toString() ?: ""
                    val prefix = gpsText.substringBefore(" | 高程:")
                    binding.locationInfo.text = "$prefix | 高程: ${"%.1f".format(elev)}m"
                }
            } catch (_: Exception) { }
        }
    }

    private fun updateLocateButton(accuracy: Float) {
        val accText = binding.gpsAccuracyFloat
        accText.visibility = View.VISIBLE
        accText.text = if (accuracy > 0f) "±${accuracy.toInt()}m" else "定位中..."
        val card = binding.btnLocateCard
        if (accuracy > 0f && accuracy < 15f) {
            card.setCardBackgroundColor(0xFF2EA043.toInt())
            binding.btnLocate.imageTintList = ContextCompat.getColorStateList(requireContext(), android.R.color.white)
        } else if (accuracy > 0f) {
            card.setCardBackgroundColor(0xFFD29922.toInt())
            binding.btnLocate.imageTintList = ContextCompat.getColorStateList(requireContext(), android.R.color.white)
        } else {
            card.setCardBackgroundColor(0xFF30363D.toInt())
            binding.btnLocate.imageTintList = ContextCompat.getColorStateList(requireContext(), R.color.text_primary)
        }
    }

    private fun updateGpsStatusUI(status: GpsStatus) {
        binding.gpsStatusBar.visibility = View.VISIBLE
        when (status) {
            GpsStatus.DISABLED -> {
                binding.gpsStatusText.text = getString(R.string.gps_disabled)
                binding.gpsIcon.setImageResource(R.drawable.ic_gps_searching)
                binding.gpsIcon.imageTintList = ContextCompat.getColorStateList(requireContext(), R.color.error)
            }
            GpsStatus.SEARCHING -> {
                binding.gpsStatusText.text = getString(R.string.gps_searching)
                binding.gpsIcon.setImageResource(R.drawable.ic_gps_searching)
                binding.gpsIcon.imageTintList = ContextCompat.getColorStateList(requireContext(), R.color.tertiary)
            }
            GpsStatus.FIXED -> {
                binding.gpsStatusText.text = getString(R.string.gps_fixed)
                binding.gpsIcon.setImageResource(R.drawable.ic_gps_fixed)
                binding.gpsIcon.imageTintList = ContextCompat.getColorStateList(requireContext(), R.color.secondary)
            }
            GpsStatus.LOST -> {
                binding.gpsStatusText.text = getString(R.string.gps_lost)
                binding.gpsIcon.setImageResource(R.drawable.ic_gps_searching)
                binding.gpsIcon.imageTintList = ContextCompat.getColorStateList(requireContext(), R.color.error)
            }
        }
        binding.gpsAccuracyText.text = AppRepository.currentLocation.value?.accuracy?.formatAccuracy() ?: ""
    }

    private fun autoEnableLocation() {
        if (checkLocationPermission()) {
            enableLocationDisplay()
            startLocationService()
        }
    }

    private fun checkLocationPermission(): Boolean {
        if (ContextCompat.checkSelfPermission(requireContext(), Manifest.permission.ACCESS_FINE_LOCATION)
            != PackageManager.PERMISSION_GRANTED
        ) {
            locationPermissionLauncher.launch(
                arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)
            )
            return false
        }
        return true
    }

    private fun enableLocationDisplay() {
        binding.mapView.locationDisplay.apply {
            autoPanMode = LocationDisplay.AutoPanMode.COMPASS_NAVIGATION
            startAsync()
        }
        startLocationService()
    }

    private fun startLocationService() {
        try {
            val intent = Intent(requireContext(), LocationService::class.java).apply {
                action = LocationService.ACTION_START
            }
            ContextCompat.startForegroundService(requireContext(), intent)
        } catch (_: Exception) {}
    }

    private fun stopLocationService() {
        try {
            val intent = Intent(requireContext(), LocationService::class.java).apply {
                action = LocationService.ACTION_STOP
            }
            requireContext().startService(intent)
        } catch (_: Exception) {}
    }

    private fun toggleLocationDisplay() {
        val ld = binding.mapView.locationDisplay
        if (ld.isStarted) {
            ld.stop()
            stopLocationService()
        } else {
            enableLocationDisplay()
        }
    }

    // ---- Basemap ----
    private fun showBasemapSwitcher() {
        val items = arrayOf("影像图", "地形图", "地质底图", "DEM", "矢量底图")
        android.app.AlertDialog.Builder(requireContext())
            .setTitle("切换底图")
            .setSingleChoiceItems(items, currentBasemapIndex) { dialog, which ->
                currentBasemapIndex = which
                val style = currentBasemapStyle()
                map.basemap = Basemap(style)
                scene?.basemap = Basemap(style)
                dialog.dismiss()
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun showLayerControl() {
        val ctx = requireContext()
        val scrollRoot = android.widget.ScrollView(ctx)
        val root = android.widget.LinearLayout(ctx).apply {
            orientation = android.widget.LinearLayout.VERTICAL
            setPadding(48, 24, 48, 24)
        }
        scrollRoot.addView(root)

        // ---- Helper: create an expandable section with parent/child sync ----
        fun makeSection(
            headerLabel: String,
            subLabels: Array<String>,
            subInitial: BooleanArray,
            onSubToggle: (Int, Boolean) -> Unit,
            onHeaderToggle: (Boolean) -> Unit
        ): android.widget.LinearLayout {
            val section = android.widget.LinearLayout(ctx).apply {
                orientation = android.widget.LinearLayout.VERTICAL
                layoutParams = android.widget.LinearLayout.LayoutParams(
                    android.widget.LinearLayout.LayoutParams.MATCH_PARENT,
                    android.widget.LinearLayout.LayoutParams.WRAP_CONTENT
                )
            }
            val headerCb = android.widget.CheckBox(ctx).apply { text = headerLabel; textSize = 16f }
            val expandIcon = android.widget.TextView(ctx).apply { text = "▶"; textSize = 12f; setPadding(0,0,16,0) }
            val subContainer = android.widget.LinearLayout(ctx).apply {
                orientation = android.widget.LinearLayout.VERTICAL
                visibility = android.view.View.GONE
                tag = false
                layoutParams = android.widget.LinearLayout.LayoutParams(
                    android.widget.LinearLayout.LayoutParams.MATCH_PARENT,
                    android.widget.LinearLayout.LayoutParams.WRAP_CONTENT
                ).apply { marginStart = 56 }
            }
            val childCbs = mutableListOf<android.widget.CheckBox>()
            var suppressing = false

            subLabels.forEachIndexed { i, label ->
                val cb = android.widget.CheckBox(ctx).apply { text = label; textSize = 14f; isChecked = subInitial[i] }
                cb.setOnCheckedChangeListener { _, isChecked ->
                    if (suppressing) return@setOnCheckedChangeListener
                    onSubToggle(i, isChecked)
                    suppressing = true
                    headerCb.isChecked = childCbs.all { it.isChecked }
                    suppressing = false
                }
                childCbs.add(cb)
                subContainer.addView(cb)
            }
            headerCb.isChecked = childCbs.all { it.isChecked }
            headerCb.setOnCheckedChangeListener { _, isChecked ->
                if (suppressing) return@setOnCheckedChangeListener
                onHeaderToggle(isChecked)
                suppressing = true
                childCbs.forEach { it.isChecked = isChecked }
                suppressing = false
            }

            val headerRow = android.widget.LinearLayout(ctx).apply {
                orientation = android.widget.LinearLayout.HORIZONTAL
                addView(headerCb, android.widget.LinearLayout.LayoutParams(0, android.widget.LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
                addView(expandIcon)
                setOnClickListener {
                    val expanded = subContainer.tag as? Boolean ?: false
                    subContainer.visibility = if (expanded) android.view.View.GONE else android.view.View.VISIBLE
                    expandIcon.text = if (expanded) "▶" else "▼"
                    subContainer.tag = !expanded
                }
            }
            section.addView(headerRow)
            section.addView(subContainer)
            return section
        }

        val ptNames = PointType.entries.map { it.displayName }.toTypedArray()
        val ptSection = makeSection(
            headerLabel = "地质点",
            subLabels = ptNames,
            subInitial = pointTypeVisibility,
            onSubToggle = { idx, checked ->
                pointTypeVisibility[idx] = checked
                updatePointsOnMap()
            },
            onHeaderToggle = { checked ->
                for (i in pointTypeVisibility.indices) pointTypeVisibility[i] = checked
                updatePointsOnMap()
            }
        )
        root.addView(ptSection)

        val routeSection = makeSection(
            headerLabel = "路线轨迹",
            subLabels = arrayOf("规划路线（蓝色虚线）", "记录路线（红色实线）"),
            subInitial = booleanArrayOf(plannedRouteOverlay.isVisible, recordedRouteOverlay.isVisible),
            onSubToggle = { idx, checked ->
                when (idx) {
                    0 -> { plannedRouteOverlay.isVisible = checked; refreshRouteLayers(); if (!checked && currentDisplayedRoute?.status == RouteStatus.PLANNED) { displayRouteOverlay.graphics.clear(); currentDisplayedRoute = null } }
                    1 -> { recordedRouteOverlay.isVisible = checked; refreshRouteLayers(); if (!checked && currentDisplayedRoute?.status != RouteStatus.PLANNED) { displayRouteOverlay.graphics.clear(); currentDisplayedRoute = null } }
                }
            },
            onHeaderToggle = { checked ->
                plannedRouteOverlay.isVisible = checked
                recordedRouteOverlay.isVisible = checked
                refreshRouteLayers()
                if (!checked) { displayRouteOverlay.graphics.clear(); currentDisplayedRoute = null }
            }
        )
        root.addView(routeSection)

        val measureCb = android.widget.CheckBox(ctx).apply {
            text = "量测标注"; textSize = 16f; isChecked = measureOverlay.isVisible
            setOnCheckedChangeListener { _, isChecked -> measureOverlay.isVisible = isChecked }
        }
        root.addView(measureCb)

        val navCb = android.widget.CheckBox(ctx).apply {
            text = "导航标注"; textSize = 16f; isChecked = navOverlay.isVisible
            setOnCheckedChangeListener { _, isChecked -> navOverlay.isVisible = isChecked }
        }
        root.addView(navCb)

        android.app.AlertDialog.Builder(ctx)
            .setTitle("图层控制")
            .setView(scrollRoot)
            .setPositiveButton("确定", null)
            .show()
    }

    // ---- Measurement ----
    private fun showMeasurementTool() {
        if (measureMode != MeasureMode.NONE) {
            val items = if (measurePoints.size >= 2 && measureMode in arrayOf(MeasureMode.DISTANCE, MeasureMode.AREA))
                arrayOf("坐标输入", "撤销上一个点", "退出量测") else arrayOf("坐标输入", "退出量测")
            android.app.AlertDialog.Builder(requireContext())
                .setTitle("量测工具")
                .setItems(items) { _, which ->
                    when (items[which]) {
                        "坐标输入" -> showMeasureCoordInput()
                        "撤销上一个点" -> undoLastMeasurePoint()
                        "退出量测" -> exitMeasureMode()
                    }
                }
                .show()
            return
        }
        android.app.AlertDialog.Builder(requireContext())
            .setTitle("地图量测")
            .setItems(arrayOf("距离量测", "面积量测", "方位角", "坡度")) { _, which ->
                startMeasureMode(
                    when (which) {
                        0 -> MeasureMode.DISTANCE
                        1 -> MeasureMode.AREA
                        2 -> MeasureMode.BEARING
                        3 -> MeasureMode.SLOPE
                        else -> MeasureMode.DISTANCE
                    }
                )
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun showMeasureCoordInput() {
        val ctx = requireContext()
        val isDd = booleanArrayOf(true)
        val textColor = ContextCompat.getColor(ctx, R.color.text_primary)
        val hintColor = ContextCompat.getColor(ctx, R.color.text_disabled)
        val accentColor = ContextCompat.getColor(ctx, R.color.primary)
        val inputBg = ContextCompat.getColor(ctx, R.color.dark_card)

        fun editText(hint: String, isDecimal: Boolean = true): android.widget.EditText {
            return android.widget.EditText(ctx).apply {
                this.hint = hint
                setTextColor(textColor)
                setHintTextColor(hintColor)
                setTextSize(14f)
                setPadding(12, 10, 12, 10)
                setBackgroundColor(inputBg)
                inputType = android.text.InputType.TYPE_CLASS_TEXT
            }
        }

        val latN = android.widget.RadioButton(ctx).apply { text = "N"; id = android.view.View.generateViewId(); isChecked = true; setTextColor(textColor) }
        val latS = android.widget.RadioButton(ctx).apply { text = "S"; id = android.view.View.generateViewId(); setTextColor(textColor) }
        val lngE = android.widget.RadioButton(ctx).apply { text = "E"; id = android.view.View.generateViewId(); isChecked = true; setTextColor(textColor) }
        val lngW = android.widget.RadioButton(ctx).apply { text = "W"; id = android.view.View.generateViewId(); setTextColor(textColor) }

        val latDd = editText("纬度 (DD 如: 39.91)")
        val latDeg = editText("度", isDecimal = false)
        val latMin = editText("分", isDecimal = false)
        val latSec = editText("秒")
        val latDmsRow = android.widget.LinearLayout(ctx).apply {
            orientation = android.widget.LinearLayout.HORIZONTAL
            visibility = android.view.View.GONE
            addView(latDeg, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(latMin, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(latSec, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
        }
        val latLabel = android.widget.TextView(ctx).apply { text = "纬度"; setTextColor(accentColor); textSize = 13f }
        val latNs = android.widget.RadioGroup(ctx).apply {
            orientation = android.widget.RadioGroup.HORIZONTAL
            addView(latN); addView(latS)
        }

        val lngDd = editText("经度 (DD 如: 116.39)")
        val lngDeg = editText("度", isDecimal = false)
        val lngMin = editText("分", isDecimal = false)
        val lngSec = editText("秒")
        val lngDmsRow = android.widget.LinearLayout(ctx).apply {
            orientation = android.widget.LinearLayout.HORIZONTAL
            visibility = android.view.View.GONE
            addView(lngDeg, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(lngMin, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(lngSec, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
        }
        val lngLabel = android.widget.TextView(ctx).apply { text = "经度"; setTextColor(accentColor); textSize = 13f }
        val lngEw = android.widget.RadioGroup(ctx).apply {
            orientation = android.widget.RadioGroup.HORIZONTAL
            addView(lngE); addView(lngW)
        }

        fun toggleFormat(dd: Boolean) {
            isDd[0] = dd
            latDd.visibility = if (dd) android.view.View.VISIBLE else android.view.View.GONE
            latDmsRow.visibility = if (dd) android.view.View.GONE else android.view.View.VISIBLE
            lngDd.visibility = if (dd) android.view.View.VISIBLE else android.view.View.GONE
            lngDmsRow.visibility = if (dd) android.view.View.GONE else android.view.View.VISIBLE
        }

        val btnDd = android.widget.RadioButton(ctx).apply { text = "十进制 (DD)"; id = android.view.View.generateViewId(); isChecked = true; setTextColor(textColor) }
        val btnDms = android.widget.RadioButton(ctx).apply { text = "度分秒 (DMS)"; id = android.view.View.generateViewId(); setTextColor(textColor) }
        val formatRow = android.widget.RadioGroup(ctx).apply {
            orientation = android.widget.RadioGroup.HORIZONTAL
            addView(btnDd); addView(btnDms)
            setOnCheckedChangeListener { _, checkedId -> toggleFormat(checkedId == btnDd.id) }
        }

        fun buildCol(label: android.widget.TextView, dd: android.widget.EditText, dmsRow: android.widget.LinearLayout, radioGroup: android.widget.RadioGroup): android.widget.LinearLayout {
            return android.widget.LinearLayout(ctx).apply {
                orientation = android.widget.LinearLayout.VERTICAL
                setPadding(4, 0, 4, 0)
                addView(label)
                addView(dd, android.widget.LinearLayout.LayoutParams(-1, -2))
                addView(dmsRow, android.widget.LinearLayout.LayoutParams(-1, -2))
                val radioLabel = android.widget.TextView(ctx).apply { text = "方向"; setTextColor(hintColor); textSize = 11f }
                addView(radioLabel)
                addView(radioGroup)
            }
        }

        val colsRow = android.widget.LinearLayout(ctx).apply {
            orientation = android.widget.LinearLayout.HORIZONTAL
            addView(buildCol(latLabel, latDd, latDmsRow, latNs), android.widget.LinearLayout.LayoutParams(0, -2, 1f))
            addView(android.widget.Space(ctx), android.widget.LinearLayout.LayoutParams(16, 0))
            addView(buildCol(lngLabel, lngDd, lngDmsRow, lngEw), android.widget.LinearLayout.LayoutParams(0, -2, 1f))
        }

        val root = android.widget.ScrollView(ctx).apply {
            val inner = android.widget.LinearLayout(ctx).apply {
                orientation = android.widget.LinearLayout.VERTICAL
                setPadding(32, 16, 32, 16)
                addView(formatRow)
                addView(android.widget.Space(ctx), android.widget.LinearLayout.LayoutParams(-1, 12))
                addView(colsRow)
            }
            addView(inner)
        }

        android.app.AlertDialog.Builder(ctx)
            .setTitle("输入坐标添加测量点")
            .setView(root)
            .setPositiveButton("添加") { _, _ ->
                try {
                    val lat: Double; val lng: Double
                    if (isDd[0]) {
                        val latStr = latDd.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "")
                        val lngStr = lngDd.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "")
                        if (latStr.isEmpty() || lngStr.isEmpty()) {
                            com.google.android.material.snackbar.Snackbar.make(binding.root, "输入格式有误", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                            return@setPositiveButton
                        }
                        try {
                            lat = latStr.toDouble()
                            lng = lngStr.toDouble()
                        } catch (_: NumberFormatException) {
                            com.google.android.material.snackbar.Snackbar.make(binding.root, "输入格式有误", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                            return@setPositiveButton
                        }
                    } else {
                        lat = (latDeg.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) +
                              (latMin.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) / 60.0 +
                              (latSec.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) / 3600.0
                        lng = (lngDeg.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) +
                              (lngMin.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) / 60.0 +
                              (lngSec.text.toString().trim().replace(",", ".").replace(Regex("[^-0-9.]"), "").toDoubleOrNull() ?: 0.0) / 3600.0
                    }
                    val latSigned = if (latNs.checkedRadioButtonId == latN.id) lat else -lat
                    val lngSigned = if (lngEw.checkedRadioButtonId == lngE.id) lng else -lng
                    if (latSigned < -90 || latSigned > 90 || lngSigned < -180 || lngSigned > 180) {
                        com.google.android.material.snackbar.Snackbar.make(binding.root, "坐标超出范围", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                        return@setPositiveButton
                    }
                    addMeasurePoint(Point(lngSigned, latSigned, SpatialReferences.getWgs84()))
                    binding.mapView.setViewpointCenterAsync(Point(lngSigned, latSigned, SpatialReferences.getWgs84()), 5000.0)
                    com.google.android.material.snackbar.Snackbar.make(binding.root, "已添加测量点", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                } catch (_: Exception) {
                    com.google.android.material.snackbar.Snackbar.make(binding.root, "输入格式有误", com.google.android.material.snackbar.Snackbar.LENGTH_SHORT).show()
                }
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun startMeasureMode(mode: MeasureMode) {
        measureMode = mode
        measurePoints.clear()
        measureOverlay.graphics.clear()
        binding.locationInfo.visibility = View.VISIBLE
        binding.locationInfo.setTextColor(ContextCompat.getColor(requireContext(), R.color.tertiary))
        binding.locationInfo.text = when (mode) {
            MeasureMode.DISTANCE -> "距离量测: 点击地图添加测量点，再次点按量测按钮退出"
            MeasureMode.AREA -> "面积量测: 点击地图添加顶点，至少3个点计算面积"
            MeasureMode.BEARING -> "方位角: 点击起点，再点终点"
            MeasureMode.SLOPE -> "坡度: 点击起点，再点终点"
            else -> ""
        }
        setMapTapHandler(::handleMapTap)
    }

    private fun undoLastMeasurePoint() {
        if (measurePoints.isEmpty()) return
        measurePoints.removeAt(measurePoints.size - 1)
        measureOverlay.graphics.clear()
        measurePoints.forEachIndexed { i, pt ->
            measureOverlay.graphics.add(Graphic(pt, SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, 0xFF58A6FF.toInt(), 12f)))
            if (i > 0) {
                measureOverlay.graphics.add(
                    Graphic(Polyline(PointCollection(listOf(measurePoints[i - 1], pt))),
                        SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, 0xFF58A6FF.toInt(), 3f))
                )
            }
        }
        when (measureMode) {
            MeasureMode.DISTANCE -> if (measurePoints.size >= 2) showDistanceResult() else updateMeasureHint()
            MeasureMode.AREA -> if (measurePoints.size >= 3) showAreaResult() else updateMeasureHint()
            else -> {}
        }
    }

    private fun updateMeasureHint() {
        binding.locationInfo.text = when (measureMode) {
            MeasureMode.DISTANCE -> "距离量测: 至少需要2个点，当前${measurePoints.size}个"
            MeasureMode.AREA -> "面积量测: 至少需要3个点，当前${measurePoints.size}个"
            else -> ""
        } + "  点按量测按钮可输入坐标"
    }

    private fun addMeasurePoint(point: Point) {
        measurePoints.add(point)
        measureOverlay.graphics.add(
            Graphic(point, SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, 0xFF58A6FF.toInt(), 12f))
        )
        val n = measurePoints.size

        when (measureMode) {
            MeasureMode.DISTANCE -> {
                if (n >= 2) {
                    val prev = measurePoints[n - 2]
                    val line = Polyline(PointCollection(listOf(prev, point)))
                    val segColor = if (n == 2) 0xFF3FB950.toInt() else 0xFF58A6FF.toInt()
                    measureOverlay.graphics.add(
                        Graphic(line, SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, segColor, 3f))
                    )
                    showDistanceResult()
                }
            }
            MeasureMode.AREA -> {
                if (n >= 2) {
                    measureOverlay.graphics.add(
                        Graphic(Polyline(PointCollection(listOf(measurePoints[n - 2], point))),
                            SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, 0xFF58A6FF.toInt(), 3f))
                    )
                }
                if (n >= 3) {
                    // Replace individual edges with polygon fill
                    measureOverlay.graphics.clear()
                    measurePoints.forEach { pt ->
                        measureOverlay.graphics.add(Graphic(pt, SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, 0xFF58A6FF.toInt(), 12f)))
                    }
                    showAreaResult()
                }
            }
            MeasureMode.BEARING -> {
                if (n >= 2) {
                    showBearingResult()
                    android.app.AlertDialog.Builder(requireContext())
                        .setTitle("方位角量测结果")
                        .setMessage(binding.locationInfo.text)
                        .setPositiveButton("确定") { _, _ -> exitMeasureMode() }
                        .show()
                }
            }
            MeasureMode.SLOPE -> {
                if (n >= 2) {
                    showSlopeResult {
                        android.app.AlertDialog.Builder(requireContext())
                            .setTitle("坡度量测结果")
                            .setMessage(binding.locationInfo.text)
                            .setPositiveButton("确定") { _, _ -> exitMeasureMode() }
                            .show()
                    }
                }
            }
            else -> {}
        }
    }

    private fun showDistanceResult() {
        if (measurePoints.size < 2) return
        val last = measurePoints.size - 1
        val segResult = GeometryEngine.distanceGeodetic(
            measurePoints[last - 1], measurePoints[last],
            LinearUnit(LinearUnitId.METERS), AngularUnit(AngularUnitId.DEGREES), GeodeticCurveType.GEODESIC
        )
        val seg = segResult.distance
        var total = 0.0
        for (i in 1 until measurePoints.size) {
            val r = GeometryEngine.distanceGeodetic(
                measurePoints[i - 1], measurePoints[i],
                LinearUnit(LinearUnitId.METERS), AngularUnit(AngularUnitId.DEGREES), GeodeticCurveType.GEODESIC
            )
            total += r.distance
        }
        val totalStr = if (total >= 1000) String.format("%.2f km", total / 1000) else String.format("%.1f m", total)
        val segStr = if (seg >= 1000) String.format("%.2f km", seg / 1000) else String.format("%.1f m", seg)
        binding.locationInfo.text = String.format("总距离: %s | 第%d段: %s | 点击继续加点", totalStr, last, segStr)
        // Text label at midpoint
        val mid = Point((measurePoints[last - 1].x + measurePoints[last].x) / 2.0,
            (measurePoints[last - 1].y + measurePoints[last].y) / 2.0,
            measurePoints[last].spatialReference)
        measureOverlay.graphics.add(
            Graphic(mid, TextSymbol(12f, totalStr, -0x1,
                TextSymbol.HorizontalAlignment.CENTER, TextSymbol.VerticalAlignment.MIDDLE).apply {
                outlineColor = 0xFF000000.toInt(); outlineWidth = 0f; backgroundColor = 0xCC000000.toInt()
            })
        )
    }

    private fun showAreaResult() {
        if (measurePoints.size < 3) return
        val polygon = Polygon(PointCollection(measurePoints))
        val area = GeometryEngine.areaGeodetic(polygon, AreaUnit(AreaUnitId.SQUARE_METERS), GeodeticCurveType.GEODESIC)
        val areaStr = if (area >= 1000000) String.format("%.3f km²", area / 1000000) else String.format("%.1f m²", area)
        measureOverlay.graphics.add(
            Graphic(polygon, SimpleFillSymbol(
                SimpleFillSymbol.Style.SOLID, 0x3058A6FF.toInt(),
                SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, 0xFF58A6FF.toInt(), 2f)
            ))
        )
        val perim = GeometryEngine.lengthGeodetic(
            Polyline(PointCollection(measurePoints)), LinearUnit(LinearUnitId.METERS), GeodeticCurveType.GEODESIC
        )
        val perStr = if (perim >= 1000) String.format("%.2f km", perim / 1000) else String.format("%.1f m", perim)
        binding.locationInfo.text = String.format("面积: %s | 周长: %s | 继续加点或点量测按钮退出", areaStr, perStr)
        // Text label at polygon centroid
        val centroid = GeometryEngine.labelPoint(polygon)
        if (centroid != null) {
            measureOverlay.graphics.add(
                Graphic(centroid, TextSymbol(12f, "$areaStr\n$perStr", -0x1,
                    TextSymbol.HorizontalAlignment.CENTER, TextSymbol.VerticalAlignment.MIDDLE).apply {
                    outlineColor = 0xFF000000.toInt(); outlineWidth = 0f; backgroundColor = 0xCC000000.toInt()
                })
            )
        }
    }

    private fun showBearingResult() {
        if (measurePoints.size < 2) return
        val p1 = measurePoints[0]; val p2 = measurePoints[1]
        val wgs84 = SpatialReferences.getWgs84()
        val wp1 = GeometryEngine.project(p1, wgs84) as? Point ?: p1
        val wp2 = GeometryEngine.project(p2, wgs84) as? Point ?: p2
        val dLon = Math.toRadians(wp2.x - wp1.x)
        val lat1 = Math.toRadians(wp1.y); val lat2 = Math.toRadians(wp2.y)
        val x = Math.sin(dLon) * Math.cos(lat2)
        val y = Math.cos(lat1) * Math.sin(lat2) - Math.sin(lat1) * Math.cos(lat2) * Math.cos(dLon)
        var bearing = Math.toDegrees(Math.atan2(x, y))
        bearing = (bearing + 360) % 360

        val result = GeometryEngine.distanceGeodetic(
            p1, p2, LinearUnit(LinearUnitId.METERS), AngularUnit(AngularUnitId.DEGREES), GeodeticCurveType.GEODESIC
        )
        val d = result.distance
        val dStr = if (d >= 1000) String.format("%.2f km", d / 1000) else String.format("%.1f m", d)
        binding.locationInfo.text = String.format("方位角: %.1f° | 距离: %s", bearing, dStr)
        // Text label at midpoint
        val mid = Point((p1.x + p2.x) / 2.0, (p1.y + p2.y) / 2.0, p1.spatialReference)
        measureOverlay.graphics.add(
            Graphic(mid, TextSymbol(12f, String.format("%.1f°", bearing), -0x1,
                TextSymbol.HorizontalAlignment.CENTER, TextSymbol.VerticalAlignment.MIDDLE).apply {
                outlineColor = 0xFF000000.toInt(); outlineWidth = 0f; backgroundColor = 0xCC000000.toInt()
            })
        )
    }

    private fun showSlopeResult(onComplete: () -> Unit = {}) {
        if (measurePoints.size < 2) return
        val p1 = measurePoints[0]; val p2 = measurePoints[1]
        val result = GeometryEngine.distanceGeodetic(
            p1, p2, LinearUnit(LinearUnitId.METERS), AngularUnit(AngularUnitId.DEGREES), GeodeticCurveType.GEODESIC
        )
        val d = result.distance
        val elevDiff = (p2.z - p1.z).toDouble()
        if (d > 0 && !p2.z.isNaN() && !p1.z.isNaN() && !(elevDiff == 0.0 && (p2.z == 0.0 || p1.z == 0.0))) {
            displaySlopeResult(d, elevDiff)
            onComplete()
        } else {
            queryElevationAsync(p1, p2, d, onComplete)
        }
    }

    private fun displaySlopeResult(d: Double, elevDiff: Double) {
        val slopeDeg = if (d > 0) Math.toDegrees(Math.atan(elevDiff / d)) else 0.0
        val slopePct = if (d > 0) (elevDiff / d) * 100 else 0.0
        binding.locationInfo.text = String.format("坡度: %.1f° (%.1f%%) | 高差: %.1fm | 距离: %.1fm",
            slopeDeg, slopePct, elevDiff, d)
        val mid = Point((measurePoints[0].x + measurePoints[1].x) / 2.0,
            (measurePoints[0].y + measurePoints[1].y) / 2.0,
            measurePoints[0].spatialReference)
        measureOverlay.graphics.add(
            Graphic(mid, TextSymbol(12f, String.format("%.1f° %.1f%%", slopeDeg, slopePct), -0x1,
                TextSymbol.HorizontalAlignment.CENTER, TextSymbol.VerticalAlignment.MIDDLE).apply {
                outlineColor = 0xFF000000.toInt(); outlineWidth = 0f; backgroundColor = 0xCC000000.toInt()
            })
        )
    }

    private fun queryElevationAsync(p1: Point, p2: Point, d: Double, onComplete: () -> Unit = {}) {
        binding.locationInfo.text = "正在查询高程数据..."
        val wgs84 = SpatialReferences.getWgs84()
        val wp1 = (if (p1.spatialReference != wgs84) GeometryEngine.project(p1, wgs84) else p1) as? Point ?: p1
        val wp2 = (if (p2.spatialReference != wgs84) GeometryEngine.project(p2, wgs84) else p2) as? Point ?: p2
        lifecycleScope.launch {
            try {
                val url = "https://api.open-elevation.com/api/v1/lookup?locations=${wp1.y},${wp1.x}|${wp2.y},${wp2.x}"
                val json = withContext(Dispatchers.IO) {
                    java.net.URL(url).readText()
                }
                val obj = com.google.gson.Gson().fromJson(json, com.google.gson.JsonObject::class.java)
                val results = obj.getAsJsonArray("results")
                if (results != null && results.size() >= 2) {
                    val e1 = results[0].asJsonObject.get("elevation").asDouble
                    val e2 = results[1].asJsonObject.get("elevation").asDouble
                    displaySlopeResult(d, e2 - e1)
                    onComplete()
                } else {
                    binding.locationInfo.text = "坡度: 无法获取高程数据，请检查网络连接"
                }
            } catch (e: Exception) {
                android.util.Log.e("MapFragment", "Elevation query failed", e)
                binding.locationInfo.text = "坡度: 高程查询失败 (${e.message ?: "网络错误"})"
            }
        }
    }

    private fun querySingleElevationAsync(lat: Double, lng: Double, latStr: String, lngStr: String) {
        lifecycleScope.launch {
            try {
                val url = "https://api.open-elevation.com/api/v1/lookup?locations=$lat,$lng"
                val json = withContext(Dispatchers.IO) {
                    java.net.URL(url).readText()
                }
                val obj = com.google.gson.Gson().fromJson(json, com.google.gson.JsonObject::class.java)
                val results = obj.getAsJsonArray("results")
                if (results != null && results.size() >= 1) {
                    val elev = results[0].asJsonObject.get("elevation").asDouble
                    binding.locationInfo.text = "纬度: $latStr  经度: $lngStr  高程: ${String.format("%.1f", elev)}m"
                }
            } catch (_: Exception) { }
        }
    }

    private fun exitMeasureMode() {
        measureMode = MeasureMode.NONE
        measurePoints.clear()
        measureOverlay.graphics.clear()
        binding.locationInfo.setTextColor(ContextCompat.getColor(requireContext(), R.color.text_secondary))
        val loc = AppRepository.currentLocation.value
        if (loc != null) updateLocationInfo(loc) else binding.locationInfo.visibility = View.GONE
        clearMapTapHandler()
    }

    // ---- Navigation ----
    private fun toggleNavPanel() {
        if (isNavPanelVisible) hideNavPanel() else showNavPanel()
    }

    private fun showNavPanel() {
        isNavPanelVisible = true
        binding.navPanel.visibility = View.VISIBLE
        navStartPoint = getCurrentLocationPoint()
        if (navStartPoint != null) {
            binding.navStartLabel.text = String.format(
                "我的位置 (%.5f, %.5f)", navStartPoint!!.y, navStartPoint!!.x
            )
        }
        selectNavMode(NavMode.DRIVE)
    }

    private fun hideNavPanel() {
        isNavPanelVisible = false
        binding.navPanel.visibility = View.GONE
        navOverlay.graphics.clear()
        binding.navRouteInfo.visibility = View.GONE
    }

    private fun selectNavMode(mode: NavMode) {
        navMode = mode
        val activeTint = ContextCompat.getColor(requireContext(), R.color.primary)
        val inactiveTint = ContextCompat.getColor(requireContext(), R.color.text_primary)
        binding.navModeDrive.imageTintList = ContextCompat.getColorStateList(requireContext(),
            if (mode == NavMode.DRIVE) R.color.primary else R.color.text_primary)
        binding.navModeWalk.imageTintList = ContextCompat.getColorStateList(requireContext(),
            if (mode == NavMode.WALK) R.color.primary else R.color.text_primary)
        binding.navModeBike.imageTintList = ContextCompat.getColorStateList(requireContext(),
            if (mode == NavMode.BIKE) R.color.primary else R.color.text_primary)
    }

    private fun swapNavStartEnd() {
        val temp = navStartPoint
        navStartPoint = navEndPoint
        navEndPoint = temp
        binding.navStartLabel.text = if (navStartPoint != null)
            String.format("起点 (%.5f, %.5f)", navStartPoint!!.y, navStartPoint!!.x)
        else "我的位置"
        if (navEndPoint != null) {
            binding.navDestinationInput.setText(
                String.format("%.5f, %.5f", navEndPoint!!.y, navEndPoint!!.x)
            )
        }
        navOverlay.graphics.clear()
        binding.navRouteInfo.visibility = View.GONE
    }

    private fun geocodeDestination(afterSearch: () -> Unit = {}) {
        val query = binding.navDestinationInput.text.toString().trim()
        if (query.isEmpty()) return
        lifecycleScope.launch {
            binding.navRouteInfo.visibility = View.VISIBLE
            binding.navRouteInfo.text = "正在搜索..."
            var found = false
            try {
                val result = withContext(Dispatchers.IO) {
                    val geocoder = Geocoder(requireContext(), Locale.CHINA)
                    geocoder.getFromLocationName(query, 5)
                }
                if (result.isNullOrEmpty()) {
                    tryParseCoordQuery(query)
                } else {
                    val addr = result[0]
                    navEndPoint = Point(addr.longitude, addr.latitude, SpatialReferences.getWgs84())
                    showNavEndOnMap(addr)
                    found = true
                }
            } catch (e: Exception) {
                if (navEndPoint == null) tryParseCoordQuery(query)
            }
            if (navEndPoint != null) afterSearch()
        }
    }

    private fun tryParseCoordQuery(query: String) {
        try {
            val parts = query.split(",").map { it.trim().toDouble() }
            if (parts.size >= 2) {
                navEndPoint = Point(parts[1], parts[0], SpatialReferences.getWgs84())
                val locText = String.format("%.5f, %.5f", parts[0], parts[1])
                showNavEndMarker(navEndPoint!!, locText)
                binding.navDestinationInput.setText(locText)
            } else {
                binding.navRouteInfo.text = "未找到地点，请尝试输入坐标 (纬度,经度)"
            }
        } catch (_: Exception) {
            binding.navRouteInfo.text = "未找到地点"
        }
    }

    private fun showNavEndOnMap(address: Address) {
        val locText = String.format("%s, %s",
            address.featureName ?: "", address.locality ?: "")
        showNavEndMarker(navEndPoint!!, locText)
        binding.navRouteInfo.text = locText
    }

    private fun showNavEndMarker(point: Point, label: String) {
        navOverlay.graphics.clear()
        navOverlay.graphics.add(
            Graphic(point, SimpleMarkerSymbol(SimpleMarkerSymbol.Style.DIAMOND, 0xFF30363D.toInt(), 18f).apply {
                outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, 0xFF58A6FF.toInt(), 2f)
            })
        )
        navOverlay.graphics.add(
            Graphic(point, TextSymbol(13f, label, 0xFFC9D1D9.toInt(),
                TextSymbol.HorizontalAlignment.CENTER, TextSymbol.VerticalAlignment.BOTTOM))
        )
        binding.mapView.setViewpointCenterAsync(point, 15000.0)
    }

    private fun solveRoute() {
        if (navStartPoint == null) {
            navStartPoint = getCurrentLocationPoint()
            if (navStartPoint == null) {
                Snackbar.make(binding.root, "请先点击定位按钮获取当前位置", Snackbar.LENGTH_SHORT).show()
                return
            }
            binding.navStartLabel.text = String.format("我的位置 (%.5f, %.5f)", navStartPoint!!.y, navStartPoint!!.x)
        }
        if (navEndPoint == null) {
            val query = binding.navDestinationInput.text.toString().trim()
            if (query.isEmpty()) {
                Snackbar.make(binding.root, "请输入目的地", Snackbar.LENGTH_SHORT).show()
                return
            }
            geocodeDestination(afterSearch = { drawDirectLine() })
            return
        }
        binding.navRouteInfo.visibility = View.VISIBLE
        binding.navRouteInfo.text = "正在计算..."
        drawDirectLine()
    }

    private fun showRouteOnMap(geometry: com.esri.arcgisruntime.geometry.Geometry) {
        val sp = navStartPoint ?: return
        val ep = navEndPoint ?: return
        navOverlay.graphics.clear()
        navOverlay.graphics.add(
            Graphic(sp, SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, 0xFF3FB950.toInt(), 14f).apply {
                outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, -0x1, 2f)
            })
        )
        navOverlay.graphics.add(
            Graphic(ep, SimpleMarkerSymbol(SimpleMarkerSymbol.Style.DIAMOND, 0xFF30363D.toInt(), 18f).apply {
                outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, 0xFF58A6FF.toInt(), 2f)
            })
        )
        navOverlay.graphics.add(
            Graphic(geometry, SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, 0xFF58A6FF.toInt(), 5f))
        )
        val extent = GeometryEngine.combineExtents(listOf(sp, ep))
        if (extent != null) binding.mapView.setViewpointGeometryAsync(extent, 50.0)
    }

    private fun drawDirectLine() {
        try {
            val sp = navStartPoint ?: run { binding.navRouteInfo.text = "无起点"; return }
            val ep = navEndPoint ?: run { binding.navRouteInfo.text = "无终点"; return }
            val d = haversineKm(sp.y, sp.x, ep.y, ep.x)
            val speedKmh = when (navMode) { NavMode.DRIVE -> 40.0; NavMode.WALK -> 5.0; NavMode.BIKE -> 15.0; else -> 40.0 }
            val minutes = (d / speedKmh * 60.0 + 0.5).toInt()
            val modeName = when (navMode) { NavMode.DRIVE -> "驾车"; NavMode.WALK -> "步行"; NavMode.BIKE -> "骑行"; else -> "驾车" }
            binding.navRouteInfo.text = String.format("直线 %.1f km | %s约 %d分钟", d, modeName, minutes)
            try {
                navOverlay.graphics.clear()
                navOverlay.graphics.add(Graphic(sp,
                    SimpleMarkerSymbol(SimpleMarkerSymbol.Style.CIRCLE, 0xFF3FB950.toInt(), 14f).apply {
                        outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, -0x1, 2f)
                    }))
                navOverlay.graphics.add(Graphic(ep,
                    SimpleMarkerSymbol(SimpleMarkerSymbol.Style.DIAMOND, 0xFF30363D.toInt(), 18f).apply {
                        outline = SimpleLineSymbol(SimpleLineSymbol.Style.SOLID, 0xFF58A6FF.toInt(), 2f)
                    }))
                navOverlay.graphics.add(Graphic(Polyline(PointCollection(listOf(sp, ep))),
                    SimpleLineSymbol(SimpleLineSymbol.Style.DASH, 0xFF58A6FF.toInt(), 3f)))
            } catch (e2: Exception) {
                android.util.Log.e("Nav", "graphics failed", e2)
            }
        } catch (e: Exception) {
            android.util.Log.e("Nav", "drawDirectLine", e)
            binding.navRouteInfo.text = "计算失败: " + e.message
        }
    }

    private fun haversineKm(lat1: Double, lon1: Double, lat2: Double, lon2: Double): Double {
        val R = 6371.0
        val dLat = Math.toRadians(lat2 - lat1)
        val dLon = Math.toRadians(lon2 - lon1)
        val a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
                Math.cos(Math.toRadians(lat1)) * Math.cos(Math.toRadians(lat2)) *
                Math.sin(dLon / 2) * Math.sin(dLon / 2)
        val c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
        return R * c
    }

    private fun getCurrentLocationPoint(): Point? {
        return try {
            binding.mapView.locationDisplay.location?.position
        } catch (_: Exception) {
            null
        }
    }

    // ---- Offline Map ----
    companion object {
        private val MAP_SERVICE_URLS = arrayOf(
            "https://server.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer",
            "https://server.arcgisonline.com/arcgis/rest/services/World_Topo_Map/MapServer",
            "https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer"
        )
    }

    private fun showDownloadTool() {
        val items = arrayOf("下载当前区域", "根据 GeoJSON 范围下载", "管理离线地图", "加载离线地图")
        android.app.AlertDialog.Builder(requireContext())
            .setTitle("离线地图")
            .setItems(items) { _, which ->
                when (which) {
                    0 -> downloadCurrentArea()
                    1 -> { geoJsonPickerLauncher.launch("*/*") }
                    2 -> manageOfflineMaps()
                    3 -> loadOfflineMap()
                }
            }
            .show()
    }

    private fun downloadDir(): File {
        val dir = File(requireContext().filesDir, "offline_maps")
        dir.mkdirs()
        return dir
    }

    private fun downloadCurrentArea() {
        val env = binding.mapView.visibleArea?.extent ?: return
        val nameInput = android.widget.EditText(requireContext()).apply {
            setText("offline_${System.currentTimeMillis() / 1000}")
            setSelection(text.length)
        }
        android.app.AlertDialog.Builder(requireContext())
            .setTitle("下载离线地图")
            .setMessage("输入离线地图名称")
            .setView(nameInput)
            .setPositiveButton("下载") { _, _ ->
                val name = nameInput.text.toString().trim().ifEmpty { "offline_${System.currentTimeMillis()}" }
                startDownload(name, env)
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun downloadGeoJsonArea(uri: Uri) {
        try {
            val content = requireContext().contentResolver.openInputStream(uri)
                ?.bufferedReader()?.use { it.readText() }
            if (content.isNullOrEmpty()) {
                android.app.AlertDialog.Builder(requireContext())
                    .setTitle("读取失败")
                    .setMessage("无法读取所选文件")
                    .setPositiveButton("确定", null)
                    .show()
                return
            }
            val bbox = OfflineTileDownloader.parseExtentFromGeoJson(content)
            if (bbox == null) {
                android.app.AlertDialog.Builder(requireContext())
                    .setTitle("解析失败")
                    .setMessage("未能在 GeoJSON 中找到有效坐标，或文件格式不正确。\n请选择包含点/线/面要素的 GeoJSON 文件。")
                    .setPositiveButton("确定", null)
                    .show()
                return
            }

            val zoomInput = android.widget.EditText(requireContext()).apply {
                setText("3-16")
                hint = "最小级-最大级 (0-19)"
                inputType = android.text.InputType.TYPE_CLASS_NUMBER
            }
            android.app.AlertDialog.Builder(requireContext())
                .setTitle("下载离线地图")
                .setMessage(
                    "根据 GeoJSON 数据范围下载 OSM 瓦片。\n" +
                    "范围: %.2f, %.2f ~ %.2f, %.2f\n\n".format(bbox[0], bbox[1], bbox[2], bbox[3]) +
                    "请输入缩放级别范围："
                )
                .setView(zoomInput)
                .setPositiveButton("继续") { _, _ ->
                    val parts = zoomInput.text.toString().trim().split("-")
                    val minZ = parts.getOrNull(0)?.toIntOrNull()?.coerceIn(0, 19) ?: 3
                    val maxZ = parts.getOrNull(1)?.toIntOrNull()?.coerceIn(0, 19) ?: 16
                    if (minZ > maxZ) {
                        android.app.AlertDialog.Builder(requireContext())
                            .setTitle("参数错误")
                            .setMessage("最小级别不能大于最大级别")
                            .setPositiveButton("确定", null)
                            .show()
                        return@setPositiveButton
                    }
                    confirmGeoJsonDownload(bbox, minZ, maxZ)
                }
                .setNegativeButton("取消", null)
                .show()
        } catch (e: Exception) {
            android.app.AlertDialog.Builder(requireContext())
                .setTitle("读取失败")
                .setMessage("读取文件失败: ${e.localizedMessage ?: "未知错误"}")
                .setPositiveButton("确定", null)
                .show()
        }
    }

    private fun confirmGeoJsonDownload(bbox: DoubleArray, minZ: Int, maxZ: Int) {
        val total = OfflineTileDownloader.estimateTileCount(bbox, minZ, maxZ)
        if (total > 20000) {
            android.app.AlertDialog.Builder(requireContext())
                .setTitle("瓦片数量较多")
                .setMessage("该范围在 $minZ-$maxZ 级共需约 $total 张瓦片，可能耗时较长并占用较多流量。仍要继续吗？")
                .setPositiveButton("继续") { _, _ -> startGeoJsonDownload(bbox, minZ, maxZ, total) }
                .setNegativeButton("取消", null)
                .show()
        } else {
            startGeoJsonDownload(bbox, minZ, maxZ, total)
        }
    }

    private fun startGeoJsonDownload(bbox: DoubleArray, minZ: Int, maxZ: Int, total: Long) {
        val ctx = requireContext()
        val progressDialog = android.app.ProgressDialog(ctx).apply {
            setTitle("下载离线地图")
            setMessage("正在准备...")
            setProgressStyle(android.app.ProgressDialog.STYLE_HORIZONTAL)
            setCancelable(false)
            max = total.coerceAtMost(Int.MAX_VALUE.toLong()).toInt().coerceAtLeast(1)
            show()
        }
        lifecycleScope.launch(Dispatchers.Main) {
            try {
                val result = withContext(Dispatchers.IO) {
                    val name = "geojson_${System.currentTimeMillis() / 1000}"
                    val outDir = downloadDir()
                    val mbtilesPath = File(outDir, "$name.mbtiles")
                    val downloaded = OfflineTileDownloader.downloadToMbtiles(
                        bbox, minZ, maxZ, name, mbtilesPath
                    ) { done, totalCount ->
                        activity?.runOnUiThread {
                            progressDialog.progress = done
                            progressDialog.setMessage("下载中 $done/$totalCount 张瓦片")
                        }
                    }
                    name to downloaded
                }
                progressDialog.dismiss()
                android.app.AlertDialog.Builder(ctx)
                    .setTitle("下载完成")
                    .setMessage("已保存 ${result.first}.mbtiles（共 ${result.second} 张瓦片）\n可在\"加载离线地图\"中查看")
                    .setPositiveButton("确定", null)
                    .show()
            } catch (e: Exception) {
                android.util.Log.e("Offline", "GeoJSON download failed", e)
                progressDialog.dismiss()
                android.app.AlertDialog.Builder(ctx)
                    .setTitle("下载失败")
                    .setMessage(e.localizedMessage ?: "未知错误")
                    .setPositiveButton("确定", null)
                    .show()
            }
        }
    }

    private fun startDownload(name: String, area: com.esri.arcgisruntime.geometry.Envelope) {
        val ctx = requireContext()
        val progressDialog = android.app.ProgressDialog(ctx).apply {
            setTitle("下载离线地图")
            setMessage("正在准备...")
            setProgressStyle(android.app.ProgressDialog.STYLE_HORIZONTAL)
            setCancelable(false)
            max = 100
            show()
        }

        lifecycleScope.launch(Dispatchers.Main) {
            try {
                // 依次尝试多个服务 URL，第一个加载成功的使用
                var task: ExportTileCacheTask? = null
                var lastError: String? = null
                for (url in MAP_SERVICE_URLS) {
                    val t = ExportTileCacheTask(url)
                    val ok = kotlin.coroutines.suspendCoroutine<Boolean> { cont ->
                        t.addDoneLoadingListener {
                            cont.resumeWith(Result.success(t.getLoadStatus() == LoadStatus.LOADED))
                        }
                        t.loadAsync()
                    }
                    if (ok) {
                        task = t
                        break
                    } else {
                        lastError = t.getLoadError()?.localizedMessage
                    }
                }
                if (task == null) {
                    throw Exception("无法连接地图服务，请检查网络。${lastError ?: "公共地图服务不支持离线导出"}")
                }

                withContext(Dispatchers.IO) {
                    val scale = withContext(Dispatchers.Main) { binding.mapView.mapScale }
                    val maxScale = Math.min(scale / 5.0, 1000000.0)
                    val minScale = Math.max(scale * 5.0, 1000.0)
                    val paramsFuture = task!!.createDefaultExportTileCacheParametersAsync(area, minScale, maxScale)
                    val params = paramsFuture.get()

                    val outputDir = File(downloadDir(), name)
                    outputDir.mkdirs()
                    val tpkPath = File(outputDir, "$name.tpk").absolutePath

                    val job = task!!.exportTileCacheAsync(params, tpkPath)
                    job.start()

                    val doneLatch = java.util.concurrent.CountDownLatch(1)
                    var jobError: Exception? = null
                    job.addJobDoneListener {
                        if (job.getStatus() == com.esri.arcgisruntime.concurrent.Job.Status.FAILED) {
                            jobError = job.getError()
                        }
                        doneLatch.countDown()
                    }
                    job.addProgressChangedListener {
                        val pct = job.getProgress().coerceIn(0, 100)
                        activity?.runOnUiThread {
                            progressDialog.progress = pct
                            progressDialog.setMessage("下载中 $pct%")
                        }
                    }
                    doneLatch.await()
                    jobError?.let { throw it }

                    job.getResult()
                    withContext(Dispatchers.Main) {
                        progressDialog.dismiss()
                        android.app.AlertDialog.Builder(ctx)
                            .setTitle("下载完成")
                            .setMessage("离线地图已保存: ${name}.tpk")
                            .setPositiveButton("确定", null)
                            .show()
                    }
                }
            } catch (e: Exception) {
                android.util.Log.e("Offline", "Download failed", e)
                withContext(Dispatchers.Main) {
                    progressDialog.dismiss()
                    val msg = e.localizedMessage ?: "未知错误"
                    val extra = if (msg.contains("不支持离线导出")) {
                        "\n\n提示：公共地图服务不支持离线导出。\n您可以尝试：\n1. 使用\"下载当前区域\"下载小范围\n2. 从其他渠道获取 .tpk 文件后使用\"加载离线地图\""
                    } else ""
                    android.app.AlertDialog.Builder(ctx)
                        .setTitle("下载失败")
                        .setMessage(msg + extra)
                        .setPositiveButton("确定", null)
                        .show()
                }
            }
        }
    }

    private fun manageOfflineMaps() {
        val files = downloadDir().listFiles { f -> f.extension == "tpk" || f.extension == "mbtiles" || (f.isDirectory && File(f, "${f.name}.tpk").exists()) } ?: arrayOf()
        if (files.isEmpty()) {
            android.app.AlertDialog.Builder(requireContext())
                .setTitle("离线地图")
                .setMessage("暂无已下载的离线地图")
                .setPositiveButton("确定", null)
                .show()
            return
        }
        val displayNames = files.map { f ->
            val sizeMb = if (f.isDirectory) {
                val tpk = File(f, "${f.name}.tpk"); if (tpk.exists()) tpk.length() / 1024.0 / 1024.0 else 0.0
            } else f.length() / 1024.0 / 1024.0
            "${f.nameWithoutExtension} (${"%.0f".format(sizeMb)}MB)"
        }.toTypedArray()
        android.app.AlertDialog.Builder(requireContext())
            .setTitle("管理离线地图")
            .setItems(displayNames) { _, which ->
                val f = files[which]
                android.app.AlertDialog.Builder(requireContext())
                    .setTitle("删除离线地图?")
                    .setMessage(f.nameWithoutExtension)
                    .setPositiveButton("删除") { _, _ ->
                        if (f.isDirectory) f.deleteRecursively() else f.delete()
                        manageOfflineMaps()
                    }
                    .setNegativeButton("取消", null)
                    .show()
            }
            .show()
    }

    private fun loadOfflineMap() {
        val files = downloadDir().listFiles { f -> f.extension == "tpk" || f.extension == "mbtiles" || (f.isDirectory && File(f, "${f.name}.tpk").exists()) } ?: arrayOf()
        if (files.isEmpty()) {
            android.app.AlertDialog.Builder(requireContext())
                .setTitle("加载离线地图")
                .setMessage("暂无离线地图，请先下载")
                .setPositiveButton("确定", null)
                .show()
            return
        }
        val names = files.map { it.nameWithoutExtension }.toTypedArray()
        android.app.AlertDialog.Builder(requireContext())
            .setTitle("选择离线地图")
            .setItems(names) { _, which ->
                val f = files[which]
                val tpkPath = if (f.isDirectory) File(f, "${f.name}.tpk").absolutePath else f.absolutePath
                try {
                    val tc = TileCache(tpkPath)
                    tc.loadAsync()
                    tc.addDoneLoadingListener {
                        if (tc.getLoadStatus() == LoadStatus.LOADED) {
                            activity?.runOnUiThread {
                                val tiledLayer = ArcGISTiledLayer(tc)
                                map = ArcGISMap(Basemap(tiledLayer))
                                binding.mapView.map = map
                            }
                        }
                    }
                } catch (e: Exception) {
                    android.app.AlertDialog.Builder(requireContext())
                        .setTitle("加载失败")
                        .setMessage(e.localizedMessage ?: "未知错误")
                        .setPositiveButton("确定", null)
                        .show()
                }
            }
            .show()
    }

    override fun onDestroyView() {
        super.onDestroyView()
        try { binding.mapView.dispose() } catch (_: Exception) {}
        try { binding.sceneView.dispose() } catch (_: Exception) {}
    }
}
