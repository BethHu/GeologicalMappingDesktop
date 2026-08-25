package com.example.geomappingmobile.ui.route

import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.recyclerview.widget.LinearLayoutManager
import com.example.geomappingmobile.R
import com.example.geomappingmobile.data.model.Route
import com.example.geomappingmobile.data.model.RouteStatus
import com.example.geomappingmobile.data.repository.AppRepository
import com.example.geomappingmobile.databinding.FragmentRouteBinding
import com.example.geomappingmobile.service.LocationService
import com.example.geomappingmobile.ui.map.MapFragment
import com.example.geomappingmobile.util.formatCoord
import com.example.geomappingmobile.util.formatDuration
import com.example.geomappingmobile.util.formatDistance
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.textfield.TextInputEditText

class RouteFragment : Fragment() {

    private var _binding: FragmentRouteBinding? = null
    private val binding get() = _binding!!

    private lateinit var adapter: RouteAdapter
    private var isRecording = false
    private val timeHandler = Handler(Looper.getMainLooper())
    private var isTimerRunning = false

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentRouteBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        setupRecyclerView()
        setupClickListeners()
        observeRoutes()
        observeCurrentRoute()
    }

    private fun setupRecyclerView() {
        adapter = RouteAdapter { route -> onRouteClicked(route) }
        binding.routeRecyclerView.layoutManager = LinearLayoutManager(requireContext())
        binding.routeRecyclerView.adapter = adapter
    }

    private fun setupClickListeners() {
        binding.btnToggleRecording.setOnClickListener {
            if (isRecording) {
                stopRecording()
            } else {
                showNewRouteDialog()
            }
        }

        binding.btnRoutePlan.setOnClickListener {
            showRoutePlanDialog()
        }
    }

    private fun showNewRouteDialog() {
        val input = TextInputEditText(requireContext()).apply {
            hint = getString(R.string.route_name_hint)
            setTextColor(resources.getColor(R.color.text_primary, null))
            setHintTextColor(resources.getColor(R.color.text_disabled, null))
        }
        MaterialAlertDialogBuilder(requireContext())
            .setTitle(R.string.route_new_route)
            .setView(input, 32, 16, 32, 16)
            .setPositiveButton(R.string.action_ok) { _, _ ->
                val name = input.text?.toString()?.ifBlank { "路线 ${System.currentTimeMillis() % 10000}" }
                name?.let { startRecording(it) }
            }
            .setNegativeButton(R.string.action_cancel, null)
            .show()
    }

    private fun startRecording(routeName: String) {
        AppRepository.startNewRoute(routeName)
        isRecording = true
        isTimerRunning = true
        startTimeTicker()
        updateRecordingUI()

        val intent = Intent(requireContext(), LocationService::class.java).apply {
            action = LocationService.ACTION_START
        }
        requireContext().startForegroundService(intent)

        val trackIntent = Intent(requireContext(), LocationService::class.java).apply {
            action = LocationService.ACTION_START_TRACKING
        }
        requireContext().startService(trackIntent)
    }

    private fun stopRecording() {
        isTimerRunning = false
        timeHandler.removeCallbacksAndMessages(null)
        AppRepository.completeCurrentRoute()
        isRecording = false
        updateRecordingUI()

        val trackIntent = Intent(requireContext(), LocationService::class.java).apply {
            action = LocationService.ACTION_STOP_TRACKING
        }
        requireContext().startService(trackIntent)

        val stopIntent = Intent(requireContext(), LocationService::class.java).apply {
            action = LocationService.ACTION_STOP
        }
        requireContext().startService(stopIntent)
    }

    private fun startTimeTicker() {
        val tick = object : Runnable {
            override fun run() {
                val route = AppRepository.currentRoute.value
                if (route != null && route.status == RouteStatus.RECORDING) {
                    updateRouteStats(route)
                }
                if (isTimerRunning) {
                    timeHandler.postDelayed(this, 1000)
                }
            }
        }
        timeHandler.post(tick)
    }

    private fun updateRecordingUI() {
        if (isRecording) {
            binding.btnToggleRecording.text = getString(R.string.route_stop)
            binding.btnToggleRecording.setBackgroundColor(resources.getColor(R.color.error, null))
            binding.recordingIndicator.visibility = View.VISIBLE
        } else {
            binding.btnToggleRecording.text = getString(R.string.route_record)
            binding.btnToggleRecording.setBackgroundColor(resources.getColor(R.color.primary, null))
            binding.recordingIndicator.visibility = View.GONE
            binding.trackingStatusText.text = getString(R.string.route_record)
        }
    }

    private fun observeRoutes() {
        AppRepository.routes.observe(viewLifecycleOwner) { routes ->
            val items = mutableListOf<RouteListItem>()
            val planned = routes.filter { it.status == RouteStatus.PLANNED }
            val recorded = routes.filter { it.status != RouteStatus.PLANNED }
            if (planned.isNotEmpty()) {
                items.add(RouteListItem.Header("规划路线"))
                planned.forEach { items.add(RouteListItem.RouteItem(it)) }
            }
            if (recorded.isNotEmpty()) {
                if (items.isNotEmpty()) items.add(RouteListItem.Header("路线记录"))
                recorded.forEach { items.add(RouteListItem.RouteItem(it)) }
            }
            adapter.submitList(items)
            binding.emptyView.visibility = if (items.isEmpty()) View.VISIBLE else View.GONE
        }
    }

    private fun observeCurrentRoute() {
        AppRepository.currentRoute.observe(viewLifecycleOwner) { route ->
            if (route != null && route.status == RouteStatus.RECORDING) {
                isRecording = true
                updateRecordingUI()
                updateRouteStats(route)
            }
        }
    }

    private fun updateRouteStats(route: Route) {
        val distance = route.totalDistance.formatDistance()
        val elapsed = if (route.status == RouteStatus.RECORDING)
            System.currentTimeMillis() - route.getStartTime().time
        else
            route.totalDuration
        val duration = elapsed.formatDuration()
        binding.trackingStatusText.text = "$distance | $duration | ${route.pointCount}个点"
    }

    private fun onRouteClicked(route: Route) {
        MaterialAlertDialogBuilder(requireContext())
            .setTitle(route.name)
            .setItems(arrayOf("查看轨迹", "路线编辑", "路线日志", "路线回放", "删除路线")) { _, which ->
                when (which) {
                    0 -> viewRouteTrack(route)
                    1 -> editRoute(route)
                    2 -> viewRouteJournal(route)
                    3 -> replayRoute(route)
                    4 -> deleteRoute(route)
                }
            }
            .show()
    }

    private fun viewRouteTrack(route: Route) {
        AppRepository.setDisplayRoute(route)
        val bottomNav = requireActivity().findViewById<com.google.android.material.bottomnavigation.BottomNavigationView>(R.id.bottom_nav)
        bottomNav.selectedItemId = R.id.mapFragment
    }

    private fun editRoute(route: Route) {
        MaterialAlertDialogBuilder(requireContext())
            .setTitle("路线编辑 - ${route.name}")
            .setItems(arrayOf("在地图上编辑", "重命名", "管理轨迹点 (${route.trackPoints.size}个)")) { _, which ->
                when (which) {
                    0 -> editRouteOnMap(route)
                    1 -> renameRoute(route)
                    2 -> manageTrackPoints(route)
                }
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun editRouteOnMap(route: Route) {
        AppRepository.setRouteToEdit(route)
        AppRepository.setDisplayRoute(route)
        val bottomNav = requireActivity().findViewById<com.google.android.material.bottomnavigation.BottomNavigationView>(R.id.bottom_nav)
        bottomNav.selectedItemId = R.id.mapFragment
    }

    private fun renameRoute(route: Route) {
        val input = TextInputEditText(requireContext()).apply {
            setText(route.name)
            setTextColor(resources.getColor(R.color.text_primary, null))
            setHintTextColor(resources.getColor(R.color.text_disabled, null))
            selectAll()
        }
        MaterialAlertDialogBuilder(requireContext())
            .setTitle("重命名路线")
            .setView(input, 32, 16, 32, 16)
            .setPositiveButton("确定") { _, _ ->
                val newName = input.text?.toString()?.trim()
                if (!newName.isNullOrBlank()) {
                    val updated = route.copy(name = newName, updatedAt = java.util.Date())
                    AppRepository.updateRoute(updated)
                }
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun editTrackPointCoord(route: Route, index: Int) {
        val tp = route.trackPoints[index]
        val latInput = TextInputEditText(requireContext()).apply {
            hint = "纬度"
            setText(tp.latitude.toString())
            setTextColor(resources.getColor(R.color.text_primary, null))
            setHintTextColor(resources.getColor(R.color.text_disabled, null))
            inputType = android.text.InputType.TYPE_CLASS_NUMBER or android.text.InputType.TYPE_NUMBER_FLAG_DECIMAL or android.text.InputType.TYPE_NUMBER_FLAG_SIGNED
            selectAll()
        }
        val lonInput = TextInputEditText(requireContext()).apply {
            hint = "经度"
            setText(tp.longitude.toString())
            setTextColor(resources.getColor(R.color.text_primary, null))
            setHintTextColor(resources.getColor(R.color.text_disabled, null))
            inputType = android.text.InputType.TYPE_CLASS_NUMBER or android.text.InputType.TYPE_NUMBER_FLAG_DECIMAL or android.text.InputType.TYPE_NUMBER_FLAG_SIGNED
            selectAll()
        }
        val layout = android.widget.LinearLayout(requireContext()).apply {
            orientation = android.widget.LinearLayout.VERTICAL
            addView(latInput)
            val spacer = android.widget.Space(requireContext())
            spacer.layoutParams = android.widget.LinearLayout.LayoutParams(
                android.widget.LinearLayout.LayoutParams.MATCH_PARENT, 24
            )
            addView(spacer)
            addView(lonInput)
            setPadding(32, 16, 32, 16)
        }
        MaterialAlertDialogBuilder(requireContext())
            .setTitle("编辑轨迹点 #${index + 1}")
            .setView(layout)
            .setPositiveButton("确定") { _, _ ->
                val newLat = latInput.text?.toString()?.toDoubleOrNull()
                val newLon = lonInput.text?.toString()?.toDoubleOrNull()
                if (newLat != null && newLon != null) {
                    val newPoints = route.trackPoints.toMutableList().apply {
                        set(index, tp.copy(latitude = newLat, longitude = newLon))
                    }
                    val newDistance = AppRepository.recalculateDistance(newPoints)
                    val updated = route.copy(
                        trackPoints = newPoints,
                        totalDistance = newDistance,
                        updatedAt = java.util.Date()
                    )
                    AppRepository.updateRoute(updated)
                    manageTrackPoints(updated)
                }
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun manageTrackPoints(route: Route) {
        if (route.trackPoints.isEmpty()) {
            android.app.AlertDialog.Builder(requireContext())
                .setTitle("轨迹点管理")
                .setMessage("该路线没有轨迹点")
                .setPositiveButton("确定", null)
                .show()
            return
        }
        // Show a scrollable dialog listing all points with delete
        val pointDescs = route.trackPoints.mapIndexed { i, tp ->
            val ts = java.text.SimpleDateFormat("HH:mm:ss", java.util.Locale.getDefault()).format(tp.timestamp)
            "#${i + 1}  $ts  ${tp.latitude.formatCoord()}, ${tp.longitude.formatCoord()}  精度±${tp.accuracy}m"
        }.toTypedArray()
        MaterialAlertDialogBuilder(requireContext())
            .setTitle("轨迹点 (${route.trackPoints.size})")
            .setItems(pointDescs) { _, which ->
                val tp = route.trackPoints[which]
                val ts = java.text.SimpleDateFormat("HH:mm:ss", java.util.Locale.getDefault()).format(tp.timestamp)
                MaterialAlertDialogBuilder(requireContext())
                    .setTitle("轨迹点 #${which + 1}")
                    .setItems(arrayOf("编辑坐标", "删除")) { _, action ->
                        when (action) {
                            0 -> editTrackPointCoord(route, which)
                            1 -> {
                                val newPoints = route.trackPoints.toMutableList().apply { removeAt(which) }
                                val newDistance = AppRepository.recalculateDistance(newPoints)
                                val updated = route.copy(
                                    trackPoints = newPoints,
                                    totalDistance = newDistance,
                                    updatedAt = java.util.Date()
                                )
                                AppRepository.updateRoute(updated)
                                manageTrackPoints(updated)
                            }
                        }
                    }
                    .setNegativeButton("取消", null)
                    .show()
            }
            .setNegativeButton("返回", null)
            .show()
    }

    private fun viewRouteJournal(route: Route) {
        val sb = StringBuilder()
        sb.appendLine("路线名称: ${route.name}")
        sb.appendLine("创建时间: ${route.createdAt}")
        sb.appendLine("轨迹点数: ${route.trackPointCount}")
        sb.appendLine("采集点数: ${route.pointCount}")
        sb.appendLine("总距离: ${route.totalDistance.formatDistance()}")
        sb.appendLine("总用时: ${route.totalDuration.formatDuration()}")
        if (route.journal.isNotBlank()) {
            sb.appendLine("---")
            sb.appendLine("沿途记录:")
            sb.appendLine(route.journal)
        }
        sb.appendLine("---")
        sb.appendLine("沿线采集点:")
        route.pointIds.forEach { pointId ->
            val point = AppRepository.getPointById(pointId)
            point?.let {
                sb.appendLine("  [${it.pointType.displayName}] ${it.customId}: ${it.description.take(50)}")
            }
        }
        MaterialAlertDialogBuilder(requireContext())
            .setTitle("路线日志")
            .setMessage(sb.toString())
            .setPositiveButton("编写沿途记录") { _, _ -> editRouteJournal(route) }
            .setNegativeButton("关闭", null)
            .show()
    }

    private fun editRouteJournal(route: Route) {
        val input = TextInputEditText(requireContext()).apply {
            setText(route.journal)
            setTextColor(resources.getColor(R.color.text_primary, null))
            setHintTextColor(resources.getColor(R.color.text_disabled, null))
            hint = "输入沿线观察记录（岩性描述、构造特征等）"
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE
            minLines = 4
        }
        val scroll = android.widget.ScrollView(requireContext()).apply {
            addView(input)
            setPadding(32, 16, 32, 16)
        }
        MaterialAlertDialogBuilder(requireContext())
            .setTitle("编辑沿途记录 - ${route.name}")
            .setView(scroll)
            .setPositiveButton("保存") { _, _ ->
                val text = input.text?.toString()?.trim() ?: ""
                val updated = route.copy(journal = text, updatedAt = java.util.Date())
                AppRepository.updateRoute(updated)
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun replayRoute(route: Route) {
        AppRepository.setReplayRoute(route)
        val bottomNav = requireActivity().findViewById<com.google.android.material.bottomnavigation.BottomNavigationView>(R.id.bottom_nav)
        bottomNav.selectedItemId = R.id.mapFragment
    }

    private fun deleteRoute(route: Route) {
        MaterialAlertDialogBuilder(requireContext())
            .setTitle("确认删除")
            .setMessage("确定要删除路线「${route.name}」吗？关联的采集点不会被删除。")
            .setPositiveButton(R.string.action_delete) { _, _ ->
                AppRepository.deleteRoute(route.id)
            }
            .setNegativeButton(R.string.action_cancel, null)
            .show()
    }

    private fun showRoutePlanDialog() {
        val input = TextInputEditText(requireContext()).apply {
            hint = "规划路线名称"
            setText("规划路线 ${System.currentTimeMillis() % 10000}")
            setTextColor(resources.getColor(R.color.text_primary, null))
            setHintTextColor(resources.getColor(R.color.text_disabled, null))
            selectAll()
        }
        MaterialAlertDialogBuilder(requireContext())
            .setTitle("路线规划")
            .setMessage("在地图上点击添加规划点，完成后保存为规划路线。")
            .setView(input, 32, 16, 32, 16)
            .setPositiveButton("开始规划") { _, _ ->
                val name = input.text?.toString()?.trim() ?: "规划路线"
                AppRepository.startPlanning()
                // Switch to map in planning mode
                val bottomNav = requireActivity().findViewById<com.google.android.material.bottomnavigation.BottomNavigationView>(R.id.bottom_nav)
                bottomNav.selectedItemId = R.id.mapFragment
            }
            .setNegativeButton("取消", null)
            .show()
    }

    override fun onDestroyView() {
        isTimerRunning = false
        timeHandler.removeCallbacksAndMessages(null)
        super.onDestroyView()
        _binding = null
    }
}
