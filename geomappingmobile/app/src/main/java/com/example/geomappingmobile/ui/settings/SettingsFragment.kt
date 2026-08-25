package com.example.geomappingmobile.ui.settings

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.EditText
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.FileProvider
import androidx.fragment.app.Fragment
import androidx.lifecycle.lifecycleScope
import com.example.geomappingmobile.data.model.Route
import com.example.geomappingmobile.data.repository.AppRepository
import com.example.geomappingmobile.data.model.GeologicalPoint
import com.example.geomappingmobile.databinding.FragmentSettingsBinding
import com.example.geomappingmobile.service.CloudSyncService
import com.example.geomappingmobile.util.GeoJsonExporter
import com.example.geomappingmobile.util.GeoJsonImporter
import com.example.geomappingmobile.util.GpxExporter
import com.example.geomappingmobile.util.ZipExporter
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.snackbar.Snackbar
import kotlinx.coroutines.launch
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class SettingsFragment : Fragment() {

    private var _binding: FragmentSettingsBinding? = null
    private val binding get() = _binding!!

    private val filePickerLauncher = registerForActivityResult(
        ActivityResultContracts.GetContent()
    ) { uri: Uri? ->
        uri?.let { handleImportFile(it) }
    }

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentSettingsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        setupClickListeners()
        updateCloudSyncButton()
    }

    private fun updateCloudSyncButton() {
        val ctx = requireContext()
        val token = CloudSyncService.getSavedToken(ctx)
        val username = CloudSyncService.getSavedUsername(ctx)
        if (token.isNotEmpty() && username.isNotEmpty()) {
            binding.btnCloudSync.text = "👤 $username"
        } else {
            binding.btnCloudSync.text = "登录 / 注册"
        }
    }

    private fun setupClickListeners() {
        binding.btnExportData.setOnClickListener { showExportDialog() }
        binding.btnImportData.setOnClickListener { showImportDialog() }
        binding.btnCloudSync.setOnClickListener { showCloudSyncDialog() }
        binding.btnUploadCloud.setOnClickListener { doCloudUpload() }
        binding.btnDownloadCloud.setOnClickListener { doCloudDownload() }
        binding.btnClearCache.setOnClickListener {
            MaterialAlertDialogBuilder(requireContext())
                .setTitle("清除缓存")
                .setMessage("确定要清除本地缓存数据吗？")
                .setPositiveButton("确定") { _, _ -> clearCache() }
                .setNegativeButton("取消", null)
                .show()
        }
    }

    private fun showImportDialog() {
        MaterialAlertDialogBuilder(requireContext())
            .setTitle("导入数据")
            .setMessage("选择 GeoJSON 文件导入地质点和路线数据\n\n支持从桌面端导出的 GeoJSON 格式")
            .setPositiveButton("选择文件") { _, _ ->
                filePickerLauncher.launch("application/geo+json")
            }
            .setNeutralButton("选择所有文件") { _, _ ->
                filePickerLauncher.launch("*/*")
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun handleImportFile(uri: Uri) {
        try {
            val inputStream = requireContext().contentResolver.openInputStream(uri)
            val content = inputStream?.bufferedReader(Charsets.UTF_8)?.readText() ?: return
            inputStream.close()

            val result = GeoJsonImporter.import(content)
            if (result.error != null) {
                MaterialAlertDialogBuilder(requireContext())
                    .setTitle("导入失败")
                    .setMessage(result.error)
                    .setPositiveButton("确定", null)
                    .show()
                return
            }

            // Ask whether to merge or replace
            val hasExistingData = (AppRepository.points.value?.isNotEmpty() == true) ||
                    (AppRepository.routes.value?.isNotEmpty() == true)

            if (hasExistingData) {
                MaterialAlertDialogBuilder(requireContext())
                    .setTitle("导入选项")
                    .setMessage("共解析到 ${result.points.size} 个地质点, ${result.routes.size} 条路线\n当前已有数据，请选择导入方式：")
                    .setPositiveButton("合并到现有数据") { _, _ ->
                        doImport(result.points, result.routes, merge = true)
                    }
                    .setNegativeButton("替换现有数据") { _, _ ->
                        doImport(result.points, result.routes, merge = false)
                    }
                    .setNeutralButton("取消", null)
                    .show()
            } else {
                doImport(result.points, result.routes, merge = true)
            }

        } catch (e: Exception) {
            MaterialAlertDialogBuilder(requireContext())
                .setTitle("导入失败")
                .setMessage("读取文件失败: ${e.message}")
                .setPositiveButton("确定", null)
                .show()
        }
    }

    private fun doImport(points: List<GeologicalPoint>, routes: List<Route>, merge: Boolean) {
        if (!merge) {
            // Replace: clear existing data first
            AppRepository.points.value?.forEach { AppRepository.deletePoint(it.id) }
            AppRepository.routes.value?.forEach { AppRepository.deleteRoute(it.id) }
        }

        for (point in points) {
            val existing = AppRepository.getPointById(point.id)
            if (existing != null) {
                AppRepository.updatePoint(point)
            } else {
                AppRepository.addPoint(point)
            }
        }
        for (route in routes) {
            val existing = AppRepository.getRouteById(route.id)
            if (existing != null) {
                AppRepository.updateRoute(route)
            } else {
                AppRepository.addRoute(route)
            }
        }

        MaterialAlertDialogBuilder(requireContext())
            .setTitle("导入成功")
            .setMessage("成功导入 ${points.size} 个地质点, ${routes.size} 条路线")
            .setPositiveButton("确定", null)
            .show()
    }

    private fun showExportDialog() {
        val points = AppRepository.points.value ?: emptyList()
        val routes = AppRepository.routes.value ?: emptyList()

        if (points.isEmpty() && routes.isEmpty()) {
            MaterialAlertDialogBuilder(requireContext())
                .setTitle("数据导出")
                .setMessage("没有可导出的数据。请先采集地质点或记录路线。")
                .setPositiveButton("确定", null)
                .show()
            return
        }

        val msg = "共 ${points.size} 个地质点，${routes.size} 条路线\n选择导出格式："
        val formats = arrayOf("GeoJSON (.geojson)", "GPX (.gpx)", "CSV (.csv)", "ZIP数据包 (.zip)")
        MaterialAlertDialogBuilder(requireContext())
            .setTitle("数据导出")
            .setItems(formats) { _, which ->
                when (which) {
                    0 -> exportFile("geojson", points, routes)
                    1 -> exportFile("gpx", points, routes)
                    2 -> exportCsv(points, routes)
                    3 -> exportZip(points, routes)
                }
            }
            .show()
    }

    private fun exportFile(format: String, points: List<GeologicalPoint>, routes: List<Route>) {
        try {
            val ctx = requireContext()
            val exportDir = File(ctx.filesDir, "exports")
            exportDir.mkdirs()

            val fileName = "GeoData_${java.text.SimpleDateFormat("yyyyMMdd_HHmmss", java.util.Locale.getDefault()).format(java.util.Date())}.$format"
            val file = File(exportDir, fileName)

            val content = when (format) {
                "gpx" -> GpxExporter.export(points, routes)
                else -> GeoJsonExporter.export(points, routes)
            }
            file.writeText(content, Charsets.UTF_8)

            shareFile(file)
        } catch (e: Exception) {
            android.util.Log.e("SettingsFragment", "Export failed", e)
            MaterialAlertDialogBuilder(requireContext())
                .setTitle("导出失败")
                .setMessage(e.message ?: "未知错误")
                .setPositiveButton("确定", null)
                .show()
        }
    }

    private val csvDateFormat = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.getDefault())

    private fun exportCsv(points: List<GeologicalPoint>, routes: List<Route>) {
        try {
            val ctx = requireContext()
            val exportDir = File(ctx.filesDir, "exports")
            exportDir.mkdirs()

            val fileName = "GeoData_${java.text.SimpleDateFormat("yyyyMMdd_HHmmss", java.util.Locale.getDefault()).format(java.util.Date())}.csv"
            val file = File(exportDir, fileName)

            val sb = StringBuilder()
            sb.appendLine("=== 地质点 ===")
            sb.appendLine("ID,自定义编号,类型,纬度,经度,高程,精度,时间,描述,照片数,草图数,附件数")
            for (pt in points) {
                val desc = pt.description.replace("\n", " ").replace("\"", "\"\"")
                sb.appendLine("${pt.id},${pt.customId},${pt.pointType.displayName},${pt.latitude},${pt.longitude},${pt.elevation},${pt.accuracy},${csvDateFormat.format(pt.timestamp)},${desc},${pt.photoPaths.size},${pt.sketchPaths.size},${pt.attachmentPaths.size}")
            }
            sb.appendLine()
            sb.appendLine("=== 路线 ===")
            sb.appendLine("ID,名称,描述,状态,轨迹点数,关联地质点数,总距离(m),总时长(ms),创建时间,更新时间")
            for (route in routes) {
                sb.appendLine("${route.id},${route.name},${route.description},${route.status.name},${route.trackPointCount},${route.pointCount},${route.totalDistance},${route.totalDuration},${csvDateFormat.format(route.createdAt)},${csvDateFormat.format(route.updatedAt)}")
            }
            sb.appendLine()
            sb.appendLine("=== 轨迹点 ===")
            sb.appendLine("路线ID,路线名称,序号,纬度,经度,高程,时间")
            for (route in routes) {
                route.trackPoints.forEachIndexed { i, tp ->
                    sb.appendLine("${route.id},${route.name},${i + 1},${tp.latitude},${tp.longitude},${tp.elevation},${csvDateFormat.format(tp.timestamp)}")
                }
            }
            file.writeText(sb.toString(), Charsets.UTF_8)

            shareFile(file)
        } catch (e: Exception) {
            android.util.Log.e("SettingsFragment", "CSV export failed", e)
            MaterialAlertDialogBuilder(requireContext())
                .setTitle("导出失败")
                .setMessage(e.message ?: "未知错误")
                .setPositiveButton("确定", null)
                .show()
        }
    }


    private fun showCloudSyncDialog() {
        val ctx = requireContext()
        val savedToken = CloudSyncService.getSavedToken(ctx)
        val savedUsername = CloudSyncService.getSavedUsername(ctx)
        val savedUrl = CloudSyncService.getSavedServerUrl(ctx)

        val hasAccount = savedToken.isNotEmpty()

        if (hasAccount) {
            // Already logged in — show user info + logout option
            MaterialAlertDialogBuilder(ctx)
                .setTitle("👤 $savedUsername")
                .setMessage("已登录云端账号\n服务器: ${savedUrl.ifEmpty { "未设置" }}")
                .setPositiveButton("退出登录") { _, _ ->
                    CloudSyncService.clearAuth(ctx)
                    updateCloudSyncButton()
                    Snackbar.make(binding.root, "已退出登录", Snackbar.LENGTH_SHORT).show()
                }
                .setNegativeButton("关闭", null)
                .show()
            return
        }

        // Login / Register form
        val serverUrlInput = EditText(ctx).apply {
            setText(savedUrl)
            hint = "电脑 IP:3580 (例如 192.168.1.101:3580)"
            inputType = android.text.InputType.TYPE_CLASS_TEXT or
                    android.text.InputType.TYPE_TEXT_VARIATION_URI
        }
        val usernameInput = EditText(ctx).apply {
            hint = "用户名"
            inputType = android.text.InputType.TYPE_CLASS_TEXT
        }
        val passwordInput = EditText(ctx).apply {
            hint = "密码"
            inputType = android.text.InputType.TYPE_CLASS_TEXT or
                    android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD
        }

        val layout = android.widget.LinearLayout(ctx).apply {
            orientation = android.widget.LinearLayout.VERTICAL
            setPadding(48, 16, 48, 16)
            addView(serverUrlInput)
            addView(usernameInput)
            addView(passwordInput)
        }

        MaterialAlertDialogBuilder(ctx)
            .setTitle("云端账号")
            .setView(layout)
            .setPositiveButton("登录") { _, _ ->
                doCloudAuth(
                    serverUrlInput.text.toString().trim(),
                    usernameInput.text.toString().trim(),
                    passwordInput.text.toString(),
                    isRegister = false
                )
            }
            .setNeutralButton("注册") { _, _ ->
                doCloudAuth(
                    serverUrlInput.text.toString().trim(),
                    usernameInput.text.toString().trim(),
                    passwordInput.text.toString(),
                    isRegister = true
                )
            }
            .setNegativeButton("取消", null)
            .create()
            .show()
    }

    private fun doCloudAuth(serverUrl: String, username: String, password: String, isRegister: Boolean) {
        if (serverUrl.isEmpty() || username.isEmpty() || password.isEmpty()) {
            Snackbar.make(binding.root, "请填写完整信息", Snackbar.LENGTH_SHORT).show()
            return
        }

        lifecycleScope.launch {
            val loading = MaterialAlertDialogBuilder(requireContext())
                .setTitle(if (isRegister) "注册中" else "登录中")
                .setMessage("请稍候...")
                .setCancelable(false)
                .show()

            val result = CloudSyncService.auth(serverUrl, username, password, isRegister)
            loading.dismiss()

            if (result.success) {
                CloudSyncService.saveAuth(requireContext(), result.token, result.username, serverUrl)
                updateCloudSyncButton()
                // Immediately show user info dialog
                showCloudSyncDialog()
            } else {
                MaterialAlertDialogBuilder(requireContext())
                    .setTitle("失败")
                    .setMessage(result.message)
                    .setPositiveButton("确定", null)
                    .show()
            }
        }
    }

    private fun doCloudUpload() {
        val ctx = requireContext()
        val token = CloudSyncService.getSavedToken(ctx)
        val serverUrl = CloudSyncService.getSavedServerUrl(ctx)

        if (token.isEmpty()) {
            Snackbar.make(binding.root, "请先在云端账号中登录", Snackbar.LENGTH_SHORT).show()
            return
        }

        val points = AppRepository.points.value ?: emptyList()
        val routes = AppRepository.routes.value ?: emptyList()

        lifecycleScope.launch {
            val loading = MaterialAlertDialogBuilder(ctx)
                .setTitle("上传中")
                .setMessage("正在上传 ${points.size} 个点, ${routes.size} 条路线...")
                .setCancelable(false)
                .show()

            val result = CloudSyncService.sync(ctx, serverUrl, token, points, routes)
            loading.dismiss()

            if (result.success) {
                Snackbar.make(binding.root, "上传成功！${result.pointsPushed} 点, ${result.routesPushed} 路线", Snackbar.LENGTH_LONG).show()
            } else {
                MaterialAlertDialogBuilder(ctx)
                    .setTitle("上传失败")
                    .setMessage(result.message)
                    .setPositiveButton("确定", null)
                    .show()
            }
        }
    }

    private fun doCloudDownload() {
        val ctx = requireContext()
        val token = CloudSyncService.getSavedToken(ctx)
        val serverUrl = CloudSyncService.getSavedServerUrl(ctx)

        if (token.isEmpty()) {
            Snackbar.make(binding.root, "请先在云端账号中登录", Snackbar.LENGTH_SHORT).show()
            return
        }

        lifecycleScope.launch {
            val loading = MaterialAlertDialogBuilder(ctx)
                .setTitle("下载中")
                .setMessage("正在从云端下载数据...")
                .setCancelable(false)
                .show()

            val result = CloudSyncService.sync(ctx, serverUrl, token, emptyList(), emptyList())
            loading.dismiss()

            if (result.success) {
                for (pt in result.pulledPoints) {
                    val existing = AppRepository.getPointById(pt.id)
                    if (existing != null) AppRepository.updatePoint(pt)
                    else AppRepository.addPoint(pt)
                }
                for (route in result.pulledRoutes) {
                    val existing = AppRepository.getRouteById(route.id)
                    if (existing != null) AppRepository.updateRoute(route)
                    else AppRepository.addRoute(route)
                }
                Snackbar.make(binding.root, "下载完成！${result.pointsPulled} 点, ${result.routesPulled} 路线", Snackbar.LENGTH_LONG).show()
            } else {
                MaterialAlertDialogBuilder(ctx)
                    .setTitle("下载失败")
                    .setMessage(result.message)
                    .setPositiveButton("确定", null)
                    .show()
            }
        }
    }

    private fun doCloudSync() {
        val ctx = requireContext()
        val token = CloudSyncService.getSavedToken(ctx)
        val serverUrl = CloudSyncService.getSavedServerUrl(ctx)

        if (token.isEmpty()) {
            Snackbar.make(binding.root, "请先登录", Snackbar.LENGTH_SHORT).show()
            return
        }

        val points = AppRepository.points.value ?: emptyList()
        val routes = AppRepository.routes.value ?: emptyList()

        lifecycleScope.launch {
            val loading = MaterialAlertDialogBuilder(ctx)
                .setTitle("同步中")
                .setMessage("正在与云端同步...\n${points.size} 个地质点, ${routes.size} 条路线")
                .setCancelable(false)
                .show()

            val result = CloudSyncService.sync(ctx, serverUrl, token, points, routes)
            loading.dismiss()

            if (result.success) {
                // Merge pulled data on main thread
                for (pt in result.pulledPoints) {
                    val existing = AppRepository.getPointById(pt.id)
                    if (existing != null) AppRepository.updatePoint(pt)
                    else AppRepository.addPoint(pt)
                }
                for (route in result.pulledRoutes) {
                    val existing = AppRepository.getRouteById(route.id)
                    if (existing != null) AppRepository.updateRoute(route)
                    else AppRepository.addRoute(route)
                }
                MaterialAlertDialogBuilder(ctx)
                    .setTitle("同步成功")
                    .setMessage("上传: ${result.pointsPushed} 点, ${result.routesPushed} 路线\n拉取: ${result.pointsPulled} 点, ${result.routesPulled} 路线")
                    .setPositiveButton("确定", null)
                    .show()
            } else {
                MaterialAlertDialogBuilder(ctx)
                    .setTitle("同步失败")
                    .setMessage(result.message)
                    .setPositiveButton("确定", null)
                    .show()
            }
        }
    }

    private fun exportZip(points: List<GeologicalPoint>, routes: List<Route>) {
        try {
            val ctx = requireContext()
            val zipFile = ZipExporter.export(ctx, points, routes)
            shareFile(zipFile)
        } catch (e: Exception) {
            android.util.Log.e("SettingsFragment", "ZIP export failed", e)
            MaterialAlertDialogBuilder(requireContext())
                .setTitle("导出失败")
                .setMessage(e.message ?: "未知错误")
                .setPositiveButton("确定", null)
                .show()
        }
    }

    private fun shareFile(file: File) {
        val uri = FileProvider.getUriForFile(requireContext(), "${requireContext().packageName}.fileprovider", file)
        val intent = Intent(Intent.ACTION_SEND).apply {
            type = when {
                file.name.endsWith(".geojson") -> "application/geo+json"
                file.name.endsWith(".gpx") -> "application/gpx+xml"
                file.name.endsWith(".csv") -> "text/csv"
                file.name.endsWith(".zip") -> "application/zip"
                else -> "*/*"
            }
            putExtra(Intent.EXTRA_STREAM, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        startActivity(Intent.createChooser(intent, "分享 ${file.name}"))
    }

    private fun clearCache() {
        try {
            val cacheDir = requireContext().cacheDir
            cacheDir.deleteRecursively()
            cacheDir.mkdirs()
            val exportsDir = File(requireContext().filesDir, "exports")
            exportsDir.deleteRecursively()
            exportsDir.mkdirs()
            Snackbar.make(binding.root, "缓存已清除", Snackbar.LENGTH_SHORT).show()
        } catch (e: Exception) {
            android.util.Log.e("SettingsFragment", "Clear cache failed", e)
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
