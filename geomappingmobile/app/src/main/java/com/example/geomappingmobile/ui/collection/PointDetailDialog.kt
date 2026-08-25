package com.example.geomappingmobile.ui.collection

import android.app.Dialog
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.net.Uri
import android.os.Bundle
import android.view.LayoutInflater
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.ArrayAdapter
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.core.content.FileProvider
import androidx.fragment.app.DialogFragment
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.example.geomappingmobile.data.model.GeologicalPoint
import com.example.geomappingmobile.data.model.PointType
import com.example.geomappingmobile.data.repository.AppRepository
import com.example.geomappingmobile.databinding.DialogPointDetailBinding
import com.example.geomappingmobile.util.formatCoord
import com.example.geomappingmobile.util.formatElevation
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.io.FileOutputStream
import java.util.Date
import java.util.UUID
import kotlin.math.abs

class PointDetailDialog : DialogFragment() {

    private var _binding: DialogPointDetailBinding? = null
    private val binding get() = _binding!!

    private var point: GeologicalPoint? = null
    private var onSaveListener: ((GeologicalPoint) -> Unit)? = null

    private val tempPhotoPaths = mutableListOf<String>()
    private val tempSketchPaths = mutableListOf<String>()
    private val tempVideoPaths = mutableListOf<String>()

    private var currentPhotoUri: Uri? = null
    private var currentSketchUri: Uri? = null
    private var currentVideoUri: Uri? = null

    private val photoCameraLauncher = registerForActivityResult(ActivityResultContracts.TakePicture()) { success ->
        if (success && currentPhotoUri != null) {
            showPhotoAnnotator(currentPhotoUri.toString())
        }
    }
    private val photoGalleryLauncher = registerForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        uri?.let { showPhotoAnnotator(it.toString()) }
    }
    private val sketchCameraLauncher = registerForActivityResult(ActivityResultContracts.TakePicture()) { success ->
        if (success && currentSketchUri != null) {
            tempSketchPaths.add(currentSketchUri.toString())
            refreshSketchGallery()
        }
    }
    private val sketchGalleryLauncher = registerForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        uri?.let {
            tempSketchPaths.add(it.toString())
            refreshSketchGallery()
        }
    }
    private val videoRecordLauncher = registerForActivityResult(ActivityResultContracts.CaptureVideo()) { success ->
        if (success && currentVideoUri != null) {
            tempVideoPaths.add(currentVideoUri.toString())
            refreshVideoGallery()
        }
    }
    private val videoGalleryLauncher = registerForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        uri?.let {
            tempVideoPaths.add(it.toString())
            refreshVideoGallery()
        }
    }

    override fun onCreateDialog(savedInstanceState: Bundle?): Dialog {
        _binding = DialogPointDetailBinding.inflate(LayoutInflater.from(requireContext()))
        point = arguments?.getSerializable(ARG_POINT) as? GeologicalPoint
        point?.photoPaths?.let { tempPhotoPaths.addAll(it) }
        point?.sketchPaths?.let { tempSketchPaths.addAll(it) }
        point?.videoPaths?.let { tempVideoPaths.addAll(it) }

        val dialog = AlertDialog.Builder(requireContext())
            .setView(binding.root)
            .create()

        dialog.window?.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE)
        dialog.window?.setLayout(
            WindowManager.LayoutParams.MATCH_PARENT,
            WindowManager.LayoutParams.WRAP_CONTENT
        )

        setupViews()
        return dialog
    }

    override fun onStart() {
        super.onStart()
        dialog?.window?.setLayout(
            WindowManager.LayoutParams.MATCH_PARENT,
            WindowManager.LayoutParams.WRAP_CONTENT
        )
    }

    private fun setupViews() {
        val currentPoint = point ?: GeologicalPoint()

        val types = PointType.entries.map { it.displayName }.toTypedArray()
        val spinnerAdapter = object : ArrayAdapter<String>(
            requireContext(), android.R.layout.simple_spinner_item, types
        ) {
            override fun getDropDownView(position: Int, convertView: View?, parent: ViewGroup): View {
                val view = super.getDropDownView(position, convertView, parent)
                (view as? TextView)?.setTextColor(Color.parseColor("#C9D1D9"))
                return view
            }
        }
        spinnerAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        binding.spinnerPointType.adapter = spinnerAdapter
        binding.spinnerPointType.setSelection(currentPoint.pointType.ordinal)

        binding.etPointId.setText(currentPoint.customId.ifBlank { currentPoint.id })
        binding.etLongitude.setText(currentPoint.longitude.formatCoord())
        binding.etLatitude.setText(currentPoint.latitude.formatCoord())
        binding.tvElevation.text = currentPoint.elevation.formatElevation()
        binding.etDescription.setText(currentPoint.description)

        // ====== 自动定位逻辑（修正版） ======
        binding.btnAutoLocate.setOnClickListener {
            val latText = binding.etLatitude.text?.toString()?.trim()
            val lngText = binding.etLongitude.text?.toString()?.trim()
            val lat = latText?.toDoubleOrNull()
            val lng = lngText?.toDoubleOrNull()
            val gpsLocation = AppRepository.currentLocation.value

            val hasValidCoord = lat != null && lng != null && lat != 0.0 && lng != 0.0

            if (gpsLocation != null) {
                if (hasValidCoord) {
                    binding.tvElevation.text = "正在查询..."
                    queryDialogElevation(lat, lng) { elev ->
                        binding.tvElevation.text = elev.formatElevation()
                    }
                    Toast.makeText(requireContext(), "已更新高程", Toast.LENGTH_SHORT).show()
                } else {
                    binding.etLatitude.setText(gpsLocation.latitude.formatCoord())
                    binding.etLongitude.setText(gpsLocation.longitude.formatCoord())
                    // GPS already provides altitude, use as fallback
                    val gpsElev = gpsLocation.elevation
                    if (gpsElev != 0.0) {
                        binding.tvElevation.text = gpsElev.formatElevation()
                    } else {
                        binding.tvElevation.text = "正在查询..."
                        queryDialogElevation(gpsLocation.latitude, gpsLocation.longitude) { elev ->
                            binding.tvElevation.text = elev.formatElevation()
                        }
                    }
                    Toast.makeText(requireContext(), "已更新为GPS位置", Toast.LENGTH_SHORT).show()
                }
            } else {
                Toast.makeText(requireContext(), "暂未获取到GPS信号，请稍后再试", Toast.LENGTH_SHORT).show()
            }
        }

        binding.btnAutoId.setOnClickListener {
            binding.etPointId.setText(UUID.randomUUID().toString().take(8).uppercase())
        }

        // Photos
        binding.btnPhotoGallery.setOnClickListener {
            photoGalleryLauncher.launch("image/*")
        }
        binding.btnTakePhoto.setOnClickListener {
            val uri = createImageUri("photos")
            if (uri != null) {
                currentPhotoUri = uri
                photoCameraLauncher.launch(uri)
            }
        }
        refreshPhotoGallery()

        // Sketches
        binding.btnSketchGallery.setOnClickListener {
            sketchGalleryLauncher.launch("image/*")
        }
        binding.btnSketchCamera.setOnClickListener {
            val uri = createImageUri("sketches")
            if (uri != null) {
                currentSketchUri = uri
                sketchCameraLauncher.launch(uri)
            }
        }
        binding.btnSketchDraw.setOnClickListener {
            showBlankSketch()
        }
        refreshSketchGallery()

        // Videos
        binding.btnVideoGallery.setOnClickListener {
            videoGalleryLauncher.launch("video/*")
        }
        binding.btnRecordVideo.setOnClickListener {
            val uri = createVideoUri()
            if (uri != null) {
                currentVideoUri = uri
                videoRecordLauncher.launch(uri)
            } else {
                Toast.makeText(requireContext(), "无法创建视频文件", Toast.LENGTH_SHORT).show()
            }
        }
        refreshVideoGallery()

        // Save / Delete
        binding.btnSavePoint.setOnClickListener {
            savePoint(currentPoint)
        }
        binding.btnDeletePoint.setOnClickListener {
            AppRepository.deletePoint(currentPoint.id)
            dialog?.dismiss()
        }

        // 自动获取高程：打开对话框时如果已有经纬度且高程为0，自动查询
        val lat = currentPoint.latitude
        val lng = currentPoint.longitude
        if (lat != 0.0 && lng != 0.0 && (currentPoint.elevation.isNaN() || currentPoint.elevation <= 0.0)) {
            binding.tvElevation.text = "正在查询..."
            queryDialogElevation(lat, lng) { elev ->
                if (elev > 0.0) binding.tvElevation.text = elev.formatElevation()
                else binding.tvElevation.text = currentPoint.elevation.formatElevation()
            }
        }
    }

    private fun createImageUri(subDir: String): Uri? {
        val pid = point?.id ?: "unknown"
        val filename = "${pid}_${UUID.randomUUID().toString().take(8)}.jpg"
        val dir = File(requireContext().filesDir, subDir)
        dir.mkdirs()
        val file = File(dir, filename)
        return FileProvider.getUriForFile(requireContext(), "${requireContext().packageName}.fileprovider", file)
    }

    private fun createVideoUri(): Uri? {
        val pid = point?.id ?: "unknown"
        val filename = "${pid}_VIDEO_${UUID.randomUUID().toString().take(8)}.mp4"
        val dir = File(requireContext().filesDir, "videos")
        dir.mkdirs()
        val file = File(dir, filename)
        return FileProvider.getUriForFile(requireContext(), "${requireContext().packageName}.fileprovider", file)
    }

    private fun refreshPhotoGallery() {
        binding.tvPhotoCount.text = tempPhotoPaths.size.toString()
        if (tempPhotoPaths.isEmpty()) {
            binding.photoRecyclerView.visibility = View.GONE
            return
        }
        binding.photoRecyclerView.visibility = View.VISIBLE
        binding.photoRecyclerView.layoutManager = LinearLayoutManager(requireContext(), LinearLayoutManager.HORIZONTAL, false)
        binding.photoRecyclerView.adapter = PhotoAdapter(tempPhotoPaths) { index ->
            tempPhotoPaths.removeAt(index)
            refreshPhotoGallery()
        }
    }

    private fun refreshSketchGallery() {
        binding.tvSketchCount.text = tempSketchPaths.size.toString()
        if (tempSketchPaths.isEmpty()) {
            binding.sketchRecyclerView.visibility = View.GONE
            return
        }
        binding.sketchRecyclerView.visibility = View.VISIBLE
        binding.sketchRecyclerView.layoutManager = LinearLayoutManager(requireContext(), LinearLayoutManager.HORIZONTAL, false)
        binding.sketchRecyclerView.adapter = PhotoAdapter(tempSketchPaths) { index ->
            tempSketchPaths.removeAt(index)
            refreshSketchGallery()
        }
    }

    private fun refreshVideoGallery() {
        binding.tvVideoCount.text = tempVideoPaths.size.toString()
        if (tempVideoPaths.isEmpty()) {
            binding.videoRecyclerView.visibility = View.GONE
            return
        }
        binding.videoRecyclerView.visibility = View.VISIBLE
        binding.videoRecyclerView.layoutManager = LinearLayoutManager(requireContext(), LinearLayoutManager.HORIZONTAL, false)
        binding.videoRecyclerView.adapter = VideoAdapter(tempVideoPaths) { index ->
            tempVideoPaths.removeAt(index)
            refreshVideoGallery()
        }
    }

    private fun showBlankSketch() {
        val view = SketchView(requireContext())
        AlertDialog.Builder(requireContext())
            .setTitle("空白画板")
            .setView(view)
            .setPositiveButton("保存") { _, _ ->
                saveSketchBitmap(view.getBitmap())
            }
            .setNegativeButton("取消", null)
            .setNeutralButton("清空") { _, _ -> view.clear() }
            .show()
    }

    private fun saveSketchBitmap(bmp: Bitmap) {
        val dir = File(requireContext().filesDir, "sketches")
        dir.mkdirs()
        val pid = point?.id ?: "unknown"
        val file = File(dir, "${pid}_SKETCH_${UUID.randomUUID().toString().take(8)}.png")
        try {
            FileOutputStream(file).use { out ->
                bmp.compress(Bitmap.CompressFormat.PNG, 100, out)
            }
            tempSketchPaths.add(Uri.fromFile(file).toString())
            refreshSketchGallery()
        } catch (_: Exception) {}
    }

    private fun queryDialogElevation(lat: Double, lng: Double, onResult: (Double) -> Unit = {}) {
        lifecycleScope.launch {
            try {
                val urls = listOf(
                    "https://api.open-elevation.com/api/v1/lookup?locations=$lat,$lng",
                    "https://api.opentopodata.org/v1/aster30m?locations=$lat,$lng"
                )
                var elev: Double? = null
                for (urlStr in urls) {
                    try {
                        val json = withContext(Dispatchers.IO) {
                            java.net.URL(urlStr).readText()
                        }
                        val obj = com.google.gson.Gson().fromJson(json, com.google.gson.JsonObject::class.java)
                        val results = obj.getAsJsonArray("results")
                        if (results != null && results.size() >= 1) {
                            elev = results[0].asJsonObject.get("elevation").asDouble
                            break
                        }
                    } catch (_: Exception) { }
                }
                if (elev != null) {
                    onResult(elev)
                } else {
                    onResult(0.0)
                }
            } catch (_: Exception) { }
        }
    }

    private fun savePoint(currentPoint: GeologicalPoint) {
        val selectedType = PointType.entries[binding.spinnerPointType.selectedItemPosition]
        val copiedPhotos = tempPhotoPaths.mapNotNull { copyToLocal(it, "photos") }.toMutableList()
        val copiedSketches = tempSketchPaths.mapNotNull { copyToLocal(it, "sketches") }.toMutableList()
        val copiedVideos = tempVideoPaths.mapNotNull { copyToLocal(it, "videos", isVideo = true) }.toMutableList()
        val elevText = binding.tvElevation.text?.toString() ?: ""
        val elevation = elevText.replace(" m", "").toDoubleOrNull() ?: currentPoint.elevation
        val savedPoint = currentPoint.copy(
            pointType = selectedType,
            customId = binding.etPointId.text?.toString() ?: currentPoint.id,
            longitude = binding.etLongitude.text?.toString()?.toDoubleOrNull() ?: currentPoint.longitude,
            latitude = binding.etLatitude.text?.toString()?.toDoubleOrNull() ?: currentPoint.latitude,
            elevation = elevation,
            description = binding.etDescription.text?.toString() ?: currentPoint.description,
            photoPaths = copiedPhotos,
            sketchPaths = copiedSketches,
            videoPaths = copiedVideos,
            timestamp = Date()
        )
        onSaveListener?.invoke(savedPoint)
        dialog?.dismiss()
    }

    private fun copyToLocal(uriStr: String, subDir: String, isVideo: Boolean = false): String? {
        if (uriStr.startsWith("file://")) return uriStr.removePrefix("file://")
        if (uriStr.startsWith("/")) return uriStr
        try {
            val uri = Uri.parse(uriStr)
            val dir = File(requireContext().filesDir, subDir)
            dir.mkdirs()
            val pid = point?.id ?: subDir
            val ext = if (isVideo) {
                val path = uri.lastPathSegment ?: ""
                when {
                    path.contains(".mov", true) -> ".mov"
                    path.contains(".3gp", true) -> ".3gp"
                    path.contains(".avi", true) -> ".avi"
                    path.contains(".mkv", true) -> ".mkv"
                    else -> ".mp4"
                }
            } else ".jpg"
            val prefix = if (isVideo) "${pid}_VIDEO_" else "${pid}_"
            val file = File(dir, "${prefix}${UUID.randomUUID().toString().take(8)}$ext")
            requireContext().contentResolver.openInputStream(uri)?.use { input ->
                FileOutputStream(file).use { output -> input.copyTo(output) }
            }
            return file.absolutePath
        } catch (_: Exception) {
            return null
        }
    }

    fun setOnSaveListener(listener: (GeologicalPoint) -> Unit) {
        onSaveListener = listener
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }

    private fun showPhotoAnnotator(uriStr: String) {
        AlertDialog.Builder(requireContext())
            .setTitle("图片处理")
            .setItems(arrayOf("裁剪", "编辑", "✓ 直接使用")) { _, which ->
                when (which) {
                    0 -> showCropView(uriStr)
                    1 -> showAnnotationView(uriStr)
                    2 -> { tempPhotoPaths.add(uriStr); refreshPhotoGallery() }
                }
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun showCropView(uriStr: String) {
        val bm = try {
            val input = requireContext().contentResolver.openInputStream(Uri.parse(uriStr))
            BitmapFactory.decodeStream(input)
        } catch (_: Exception) { null } ?: run { tempPhotoPaths.add(uriStr); refreshPhotoGallery(); return }
        val view = PhotoCropView(requireContext(), bm)
        val dialog = AlertDialog.Builder(requireContext())
            .setView(view)
            .setPositiveButton("裁剪", null)
            .setNegativeButton("取消", null)
            .create()
        dialog.setOnShowListener {
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener {
                val cropped = view.getCroppedBitmap()
                if (cropped != null) {
                    val dir = File(requireContext().filesDir, "photos")
                    dir.mkdirs()
                    val file = File(dir, "PHOTO_${UUID.randomUUID().toString().take(8)}.jpg")
                    try {
                        FileOutputStream(file).use { out ->
                            cropped.compress(Bitmap.CompressFormat.JPEG, 90, out)
                        }
                        showPhotoAnnotator(Uri.fromFile(file).toString())
                        dialog.dismiss()
                    } catch (_: Exception) {}
                } else dialog.dismiss()
            }
        }
        dialog.window?.setLayout(
            WindowManager.LayoutParams.MATCH_PARENT, (WindowManager.LayoutParams.MATCH_PARENT * 0.7).toInt()
        )
        dialog.show()
    }

    private fun showAnnotationView(uriStr: String) {
        val bm = try {
            val input = requireContext().contentResolver.openInputStream(Uri.parse(uriStr))
            BitmapFactory.decodeStream(input)
        } catch (_: Exception) { null }
        if (bm == null) { tempPhotoPaths.add(uriStr); refreshPhotoGallery(); return }
        val view = PhotoAnnotatorView(requireContext(), bm)
        val dialog = AlertDialog.Builder(requireContext())
            .setView(view)
            .setPositiveButton("✓ 保存标注", null)
            .setNegativeButton("取消", null)
            .create()
        dialog.setOnShowListener {
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener {
                val annotated = view.getAnnotatedBitmap()
                val dir = File(requireContext().filesDir, "photos")
                dir.mkdirs()
                val file = File(dir, "PHOTO_${UUID.randomUUID().toString().take(8)}.jpg")
                try {
                    FileOutputStream(file).use { out ->
                        annotated.compress(Bitmap.CompressFormat.JPEG, 90, out)
                    }
                    dialog.dismiss()
                    showPhotoAnnotator(Uri.fromFile(file).toString())
                } catch (_: Exception) { dialog.dismiss() }
            }
        }
        dialog.window?.setLayout(
            WindowManager.LayoutParams.MATCH_PARENT,
            WindowManager.LayoutParams.WRAP_CONTENT
        )
        dialog.show()
    }

    companion object {
        private const val ARG_POINT = "point"

        fun newInstance(point: GeologicalPoint): PointDetailDialog {
            return PointDetailDialog().apply {
                arguments = Bundle().apply {
                    putSerializable(ARG_POINT, point)
                }
            }
        }
    }
}

enum class AnnotationTool { RECT, CIRCLE, LINE, FREE_DRAW, TEXT }

// ============================================================
//  PhotoAnnotatorView - 完整新版（深色科技风）
// ============================================================
private class PhotoAnnotatorView(context: Context, private val srcBitmap: Bitmap) : View(context) {
    private val bmW = srcBitmap.width.toFloat()
    private val bmH = srcBitmap.height.toFloat()
    private val tools = AnnotationTool.entries.toTypedArray()
    private val colors = intArrayOf(Color.RED, Color.GREEN, Color.BLUE, Color.YELLOW, Color.CYAN, Color.MAGENTA, Color.WHITE)
    var currentTool = AnnotationTool.RECT
    var currentColor = Color.RED
    private var strokeWidth = 4f
    private val annotations = mutableListOf<AnnotShape>()
    private val undoStack = mutableListOf<List<AnnotShape>>()
    private val redoStack = mutableListOf<List<AnnotShape>>()
    private var startX = 0f; private var startY = 0f
    private var curX = 0f; private var curY = 0f
    private var drawing = false
    private var currentPath: Path? = null
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeCap = Paint.Cap.ROUND }
    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL; textSize = 44f }
    private var textInput: String? = null
    private var currentPoints = mutableListOf<Pair<Float, Float>>()
    private val density = context.resources.displayMetrics.density
    private val rowH = 52f * density
    private val row2H = 42f * density
    private val pad = 12f * density
    private val toolBarHeight = rowH + row2H + pad * 3
    private val row1Y: Float get() = height - toolBarHeight + pad
    private val row2Y: Float get() = row1Y + rowH + pad
    private val row1Items = 7
    private val row2Items = 10
    private val row1Slot: Float get() = width.toFloat() / row1Items
    private val row2Slot: Float get() = width.toFloat() / row2Items
    private val row1Center: (Int) -> Float = { row1Slot * it + row1Slot / 2 }
    private val row2Center: (Int) -> Float = { row2Slot * it + row2Slot / 2 }

    private val scale: Float by lazy {
        val sx = width.toFloat() / bmW
        val sy = (height - toolBarHeight) / bmH
        minOf(sx, sy)
    }
    private val offsetX: Float by lazy { (width - bmW * scale) / 2f }
    private val offsetY: Float by lazy { ((height - toolBarHeight) - bmH * scale) / 2f }

    private fun toBmpX(vx: Float) = (vx - offsetX) / scale
    private fun toBmpY(vy: Float) = (vy - offsetY) / scale

    private data class AnnotShape(
        val tool: AnnotationTool,
        val x1: Float, val y1: Float, val x2: Float, val y2: Float,
        val points: List<Pair<Float, Float>>,
        val color: Int,
        val text: String,
        val width: Float
    )

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawColor(0xFF0D0D14.toInt())

        val l = offsetX; val t = offsetY
        val w = bmW * scale; val h = bmH * scale
        val clipPath = Path().apply {
            addRoundRect(RectF(l, t, l + w, t + h), 16f, 16f, Path.Direction.CW)
        }
        canvas.save()
        canvas.clipPath(clipPath)
        canvas.drawBitmap(srcBitmap, null, RectF(l, t, l + w, t + h), null)
        canvas.restore()

        canvas.save()
        canvas.translate(offsetX, offsetY)
        canvas.scale(scale, scale)
        for (a in annotations) drawShapeOnBmp(canvas, a)
        if (drawing && currentPath != null && currentTool != AnnotationTool.TEXT) {
            paint.apply { color = currentColor; strokeWidth = strokeWidth }
            canvas.drawPath(currentPath!!, paint)
        }
        if (drawing && currentTool == AnnotationTool.TEXT && textInput != null) {
            fillPaint.color = currentColor
            canvas.drawText(textInput!!, curX, curY, fillPaint)
        }
        canvas.restore()

        drawTechToolbar(canvas)
    }

    private fun drawTechToolbar(canvas: Canvas) {
        val barTop = height - rowH - row2H - pad * 3
        val bgPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xCC1A1A2E.toInt()
            setShadowLayer(12f, 0f, -4f, 0x66000000.toInt())
        }
        canvas.drawRoundRect(0f, barTop - pad, width.toFloat(), height.toFloat(), 20f, 20f, bgPaint)

        val linePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0x3358A6FF.toInt()
            strokeWidth = 1.5f * density
        }
        canvas.drawLine(0f, barTop + rowH + pad/2, width.toFloat(), barTop + rowH + pad/2, linePaint)

        val tp = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFFC9D1D9.toInt()
            textSize = 16f * density
            textAlign = Paint.Align.CENTER
        }
        val selPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFF58A6FF.toInt()
            setShadowLayer(12f, 0f, 0f, 0x6658A6FF.toInt())
        }
        val btnPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFF30363D.toInt()
        }
        val disabledPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFF21262D.toInt()
            alpha = 100
        }
        val bSize = minOf(rowH * 0.65f, row1Slot * 0.55f)

        tools.forEachIndexed { i, t ->
            val cx = row1Center(i)
            val cy = barTop + rowH / 2
            val isSelected = t == currentTool
            val bg = if (isSelected) selPaint else btnPaint
            canvas.drawRoundRect(cx - bSize/2, cy - bSize/2, cx + bSize/2, cy + bSize/2, 12f, 12f, bg)
            val icon = when (t) {
                AnnotationTool.RECT -> "▭"
                AnnotationTool.CIRCLE -> "●"
                AnnotationTool.LINE -> "↗"
                AnnotationTool.FREE_DRAW -> "✎"
                AnnotationTool.TEXT -> "T"
            }
            canvas.drawText(icon, cx, cy + 6f * density, tp)
        }

        listOf(Pair("↩", undoStack.isNotEmpty()), Pair("↪", redoStack.isNotEmpty())).forEachIndexed { i, (label, enabled) ->
            val cx = row1Center(tools.size + i)
            val cy = barTop + rowH / 2
            val bg = if (enabled) btnPaint else disabledPaint
            canvas.drawRoundRect(cx - bSize/2, cy - bSize/2, cx + bSize/2, cy + bSize/2, 12f, 12f, bg)
            canvas.drawText(label, cx, cy + 6f * density, tp.apply { alpha = if (enabled) 255 else 100 })
        }

        val cSize = minOf(row2H * 0.55f, row2Slot * 0.50f)
        val cy = barTop + rowH + pad + row2H / 2
        // 7 color circles (indices 0-6)
        colors.forEachIndexed { i, c ->
            val cx = row2Center(i)
            val r = cSize / 2
            if (c == currentColor) {
                val glow = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                    color = 0x6658A6FF.toInt()
                    setShadowLayer(16f, 0f, 0f, 0x6658A6FF.toInt())
                }
                canvas.drawCircle(cx, cy, r + 6f, glow)
            }
            canvas.drawCircle(cx, cy, r, Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = c
                style = Paint.Style.FILL
            })
            canvas.drawCircle(cx, cy, r, Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = if (c == currentColor) 0xFFFFFFFF.toInt() else 0x4DFFFFFF.toInt()
                style = Paint.Style.STROKE
                strokeWidth = 2f * density
            })
        }
        // "-" button at index 7, "+" button at index 9
        val btnSize = minOf(row2H * 0.45f, row2Slot * 0.40f)
        listOf(Pair(7, "－"), Pair(9, "＋")).forEach { (idx, label) ->
            val cx = row2Center(idx)
            canvas.drawRoundRect(cx - btnSize/2, cy - btnSize/2, cx + btnSize/2, cy + btnSize/2, 8f, 8f, btnPaint)
            canvas.drawText(label, cx, cy + 6f * density, tp)
        }
        // Brush width label at index 8
        val labelCx = row2Center(8)
        canvas.drawText("粗细", labelCx, cy - 4f * density,
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = 0xFF8B949E.toInt()
                textSize = 11f * density
                textAlign = Paint.Align.CENTER
            })
        canvas.drawText("${strokeWidth.toInt()}px", labelCx, cy + 10f * density,
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = 0xFFC9D1D9.toInt()
                textSize = 13f * density
                textAlign = Paint.Align.CENTER
            })
    }

    private fun drawShapeOnBmp(canvas: Canvas, a: AnnotShape) {
        paint.color = a.color
        paint.strokeWidth = a.width
        fillPaint.color = a.color
        when (a.tool) {
            AnnotationTool.RECT -> canvas.drawRect(a.x1, a.y1, a.x2, a.y2, paint)
            AnnotationTool.CIRCLE -> {
                val cx = (a.x1 + a.x2) / 2
                val cy = (a.y1 + a.y2) / 2
                val radius = kotlin.math.sqrt((a.x2 - a.x1) * (a.x2 - a.x1) + (a.y2 - a.y1) * (a.y2 - a.y1)) / 2
                canvas.drawCircle(cx, cy, radius, paint)
            }
            AnnotationTool.LINE -> canvas.drawLine(a.x1, a.y1, a.x2, a.y2, paint)
            AnnotationTool.FREE_DRAW -> {
                val p = Path()
                a.points.forEachIndexed { idx, pt ->
                    if (idx == 0) p.moveTo(pt.first, pt.second)
                    else p.lineTo(pt.first, pt.second)
                }
                canvas.drawPath(p, paint)
            }
            AnnotationTool.TEXT -> canvas.drawText(a.text, a.x1, a.y1, fillPaint)
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val x = event.x
        val y = event.y
        val barTop = height - rowH - row2H - pad * 3
        if (event.action == MotionEvent.ACTION_DOWN && y > barTop - pad) {
            val idx1 = (x / row1Slot).toInt()
            val idx2 = (x / row2Slot).toInt()
            if (y in barTop..(barTop + rowH) && idx1 < tools.size) {
                currentTool = tools[idx1]
                if (currentTool == AnnotationTool.TEXT) promptText()
                invalidate()
                return true
            }
            if (y in barTop..(barTop + rowH) && idx1 == tools.size) {
                if (undoStack.isNotEmpty()) {
                    redoStack.add(annotations.toList())
                    annotations.clear()
                    annotations.addAll(undoStack.removeAt(undoStack.lastIndex))
                    invalidate()
                }
                return true
            }
            if (y in barTop..(barTop + rowH) && idx1 == tools.size + 1) {
                if (redoStack.isNotEmpty()) {
                    undoStack.add(annotations.toList())
                    annotations.clear()
                    annotations.addAll(redoStack.removeAt(redoStack.lastIndex))
                    invalidate()
                }
                return true
            }
            if (y in (barTop + rowH + pad)..(barTop + rowH + pad + row2H) && idx2 < colors.size) {
                currentColor = colors[idx2]
                invalidate()
                return true
            }
            if (y in (barTop + rowH + pad)..(barTop + rowH + pad + row2H) && idx2 == 7) {
                strokeWidth = maxOf(1f, strokeWidth - 1f)
                invalidate()
                return true
            }
            if (y in (barTop + rowH + pad)..(barTop + rowH + pad + row2H) && idx2 == 9) {
                strokeWidth = minOf(20f, strokeWidth + 1f)
                invalidate()
                return true
            }
            return true
        }
        val bx = toBmpX(x)
        val by = toBmpY(y)
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                curX = bx
                curY = by
                startX = bx
                startY = by
                drawing = true
                if (currentTool == AnnotationTool.FREE_DRAW) {
                    currentPath = Path().apply { moveTo(bx, by) }
                    currentPoints = mutableListOf(Pair(bx, by))
                }
                if (currentTool == AnnotationTool.TEXT) {
                    curX = bx
                    curY = by
                    promptText()
                }
                return true
            }
            MotionEvent.ACTION_MOVE -> {
                curX = bx
                curY = by
                if (currentTool == AnnotationTool.FREE_DRAW && currentPath != null) {
                    currentPath!!.lineTo(bx, by)
                    currentPoints.add(Pair(bx, by))
                }
                invalidate()
                return true
            }
            MotionEvent.ACTION_UP -> {
                if (drawing) {
                    drawing = false
                    if (currentTool != AnnotationTool.TEXT && currentTool != AnnotationTool.FREE_DRAW) {
                        val dx = abs(bx - startX)
                        val dy = abs(by - startY)
                        if (dx > 10 || dy > 10) {
                            annotations.add(
                                AnnotShape(
                                    currentTool,
                                    startX, startY,
                                    bx, by,
                                    emptyList(),
                                    currentColor,
                                    "",
                                    strokeWidth
                                )
                            )
                            undoStack.add(annotations.toList())
                            redoStack.clear()
                        }
                    }
                    if (currentTool == AnnotationTool.FREE_DRAW && currentPoints.size > 1) {
                        annotations.add(
                            AnnotShape(
                                AnnotationTool.FREE_DRAW,
                                0f, 0f, 0f, 0f,
                                currentPoints.toList(),
                                currentColor,
                                "",
                                strokeWidth
                            )
                        )
                        undoStack.add(annotations.toList())
                        redoStack.clear()
                    }
                }
                currentPath = null
                invalidate()
                return true
            }
        }
        return true
    }

    private fun promptText() {
        val et = android.widget.EditText(context).apply {
            setText(textInput ?: "")
            setSingleLine()
        }
        AlertDialog.Builder(context)
            .setTitle("输入标注文字")
            .setView(et)
            .setPositiveButton("确定") { _, _ ->
                textInput = et.text.toString()
                if (textInput?.isNotEmpty() == true) {
                    annotations.add(
                        AnnotShape(
                            AnnotationTool.TEXT,
                            curX, curY,
                            0f, 0f,
                            emptyList(),
                            currentColor,
                            textInput!!,
                            strokeWidth
                        )
                    )
                    undoStack.add(annotations.toList())
                    redoStack.clear()
                }
                textInput = null
                invalidate()
            }
            .setNegativeButton("取消", null)
            .show()
    }

    fun getAnnotatedBitmap(): Bitmap {
        val bmp = Bitmap.createBitmap(srcBitmap.width, srcBitmap.height, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bmp)
        canvas.drawBitmap(srcBitmap, 0f, 0f, null)
        for (a in annotations) drawShapeOnBmp(canvas, a)
        return bmp
    }
}

// ============================================================
//  PhotoCropView - 保持不变
// ============================================================
private class PhotoCropView(context: Context, private val srcBitmap: Bitmap) : View(context) {
    private val bmW = srcBitmap.width.toFloat()
    private val bmH = srcBitmap.height.toFloat()
    private val scale: Float by lazy {
        minOf(width.toFloat() / bmW, height.toFloat() / bmH)
    }
    private val offsetX: Float by lazy { (width - bmW * scale) / 2f }
    private val offsetY: Float by lazy { (height - bmH * scale) / 2f }
    private var cropLeft = 0.1f
    private var cropTop = 0.1f
    private var cropRight = 0.9f
    private var cropBottom = 0.9f
    private val handleSize = 28f
    private val minCrop = 0.05f
    private var dragging: Int = -1
    private val density = context.resources.displayMetrics.density
    private val btnSize = 36f * density
    private val pad2 = 12f * density
    private val barHeight = btnSize + pad2 * 3
    private val cropUndoStack = mutableListOf<FloatArray>()
    private val cropRedoStack = mutableListOf<FloatArray>()

    private fun saveCrop() { cropUndoStack.add(floatArrayOf(cropLeft, cropTop, cropRight, cropBottom)); cropRedoStack.clear() }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawColor(0xFF000000.toInt())
        val l = cropLeft * width
        val t = cropTop * height
        val r = cropRight * width
        val b = cropBottom * height
        canvas.save()
        canvas.clipRect(l, t, r, b)
        canvas.drawBitmap(srcBitmap, null, RectF(offsetX, offsetY, offsetX + bmW * scale, offsetY + bmH * scale), null)
        canvas.restore()
        canvas.drawRect(l, t, r, b, Paint().apply { color = 0xFF58A6FF.toInt(); style = Paint.Style.STROKE; strokeWidth = 2.5f * density })
        val cornerPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = 0xFF58A6FF.toInt(); style = Paint.Style.FILL }
        val cornerStroke = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.WHITE; style = Paint.Style.STROKE; strokeWidth = 2f }
        val cornerSize = handleSize * density
        val corners = arrayOf(Pair(l, t), Pair(r, t), Pair(l, b), Pair(r, b))
        for ((cx, cy) in corners) {
            canvas.drawCircle(cx, cy, cornerSize / 2 + 4f,
                Paint(Paint.ANTI_ALIAS_FLAG).apply { color = 0x40000000.toInt(); style = Paint.Style.FILL })
            canvas.drawCircle(cx, cy, cornerSize / 2, cornerPaint)
            canvas.drawCircle(cx, cy, cornerSize / 2, cornerStroke)
        }
        val edgePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = 0x6058A6FF.toInt(); style = Paint.Style.STROKE; strokeWidth = 1.5f }
        canvas.drawLine(l, t + cornerSize, l, b - cornerSize, edgePaint)
        canvas.drawLine(r, t + cornerSize, r, b - cornerSize, edgePaint)
        canvas.drawLine(l + cornerSize, t, r - cornerSize, t, edgePaint)
        canvas.drawLine(l + cornerSize, b, r - cornerSize, b, edgePaint)
        val barY = height - barHeight + pad2
        val bg = Paint().apply { color = 0xE6161B22.toInt() }
        canvas.drawRoundRect(0f, barY - pad2, width.toFloat(), height.toFloat(), 16f, 16f, bg)
        val tp = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = 0xFFC9D1D9.toInt(); textSize = 14f * density }
        listOf(Pair("撤销", cropUndoStack.isNotEmpty()), Pair("重置", true), Pair("重做", cropRedoStack.isNotEmpty())).forEachIndexed { i, (label, enabled) ->
            val bw = width / 3f
            val x = i * bw + 12f * density
            val bp = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = if (enabled) 0xFF30363D.toInt() else 0xFF21262D.toInt()
            }
            canvas.drawRoundRect(x, barY, x + bw - 24f * density, barY + btnSize, 8f, 8f, bp)
            canvas.drawText(label, x + (bw - 24f * density) / 2 - tp.measureText(label) / 2, barY + btnSize / 2 + 5f * density,
                tp.apply { alpha = if (enabled) 255 else 100 })
        }
        val ratio = ((r - l) / width * bmW).toInt() to ((b - t) / height * bmH).toInt()
        canvas.drawText("${ratio.first} × ${ratio.second}", width / 2f - tp.measureText("${ratio.first} × ${ratio.second}") / 2, barY - 6f * density,
            Paint(Paint.ANTI_ALIAS_FLAG).apply { color = 0xFF8B949E.toInt(); textSize = 12f * density })
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val x = event.x
        val y = event.y
        val l = cropLeft * width
        val t = cropTop * height
        val r = cropRight * width
        val b = cropBottom * height
        val barY = height - barHeight + pad2
        if (event.action == MotionEvent.ACTION_DOWN && y > barY - pad2) {
            val bw = width / 3f
            val idx = (x / bw).toInt()
            if (idx == 0 && cropUndoStack.isNotEmpty()) {
                cropRedoStack.add(floatArrayOf(cropLeft, cropTop, cropRight, cropBottom))
                val s = cropUndoStack.removeAt(cropUndoStack.lastIndex)
                cropLeft = s[0]; cropTop = s[1]; cropRight = s[2]; cropBottom = s[3]
                invalidate(); return true
            }
            if (idx == 1) {
                cropLeft = 0.1f; cropTop = 0.1f; cropRight = 0.9f; cropBottom = 0.9f
                cropUndoStack.clear(); cropRedoStack.clear(); invalidate(); return true
            }
            if (idx == 2 && cropRedoStack.isNotEmpty()) {
                cropUndoStack.add(floatArrayOf(cropLeft, cropTop, cropRight, cropBottom))
                val s = cropRedoStack.removeAt(cropRedoStack.lastIndex)
                cropLeft = s[0]; cropTop = s[1]; cropRight = s[2]; cropBottom = s[3]
                invalidate(); return true
            }
            return true
        }
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                dragging = listOf(Pair(l, t), Pair(r, t), Pair(l, b), Pair(r, b)).indexOfFirst {
                    abs(x - it.first) < handleSize * density && abs(y - it.second) < handleSize * density
                }
                if (dragging == -1 && x in l..r && y in t..b) dragging = 4
                if (dragging != -1) saveCrop()
                return true
            }
            MotionEvent.ACTION_MOVE -> {
                if (dragging == 4) {
                    val dx = (x - (l + r) / 2) / width
                    val dy = (y - (t + b) / 2) / height
                    cropLeft += dx; cropRight += dx; cropTop += dy; cropBottom += dy
                } else when (dragging) {
                    0 -> { cropLeft = x / width; cropTop = y / height }
                    1 -> { cropRight = x / width; cropTop = y / height }
                    2 -> { cropLeft = x / width; cropBottom = y / height }
                    3 -> { cropRight = x / width; cropBottom = y / height }
                }
                cropLeft = maxOf(0f, minOf(cropLeft, cropRight - minCrop))
                cropTop = maxOf(0f, minOf(cropTop, cropBottom - minCrop))
                cropRight = minOf(1f, maxOf(cropRight, cropLeft + minCrop))
                cropBottom = minOf(1f, maxOf(cropBottom, cropTop + minCrop))
                invalidate(); return true
            }
            MotionEvent.ACTION_UP -> { dragging = -1; return true }
        }
        return true
    }

    fun getCroppedBitmap(): Bitmap? {
        val l = (cropLeft * srcBitmap.width).toInt().coerceIn(0, srcBitmap.width)
        val t = (cropTop * srcBitmap.height).toInt().coerceIn(0, srcBitmap.height)
        val r = (cropRight * srcBitmap.width).toInt().coerceIn(0, srcBitmap.width)
        val b = (cropBottom * srcBitmap.height).toInt().coerceIn(0, srcBitmap.height)
        if (r - l < 10 || b - t < 10) return null
        return Bitmap.createBitmap(srcBitmap, l, t, r - l, b - t)
    }
}

// ============================================================
//  PhotoAdapter - 保持不变
// ============================================================
class PhotoAdapter(
    private val paths: MutableList<String>,
    private val onDelete: (Int) -> Unit
) : RecyclerView.Adapter<PhotoAdapter.ViewHolder>() {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val context = parent.context
        val root = FrameLayout(context).apply {
            layoutParams = ViewGroup.LayoutParams(100, 100)
            setPadding(4, 4, 4, 4)
        }
        val iv = ImageView(context).apply {
            layoutParams = FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT)
            scaleType = ImageView.ScaleType.CENTER_CROP
            setBackgroundColor(Color.parseColor("#161B22"))
            clipToOutline = true
        }
        root.addView(iv)
        val deleteBtn = ImageView(context).apply {
            layoutParams = FrameLayout.LayoutParams(24, 24).apply {
                gravity = android.view.Gravity.TOP or android.view.Gravity.END
                setMargins(0, 0, 2, 2)
            }
            setImageResource(android.R.drawable.ic_delete)
            setColorFilter(Color.WHITE)
            alpha = 0f
            visibility = View.INVISIBLE
        }
        root.addView(deleteBtn)
        return ViewHolder(root, iv, deleteBtn)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.bind(paths[position], position)
    }

    override fun getItemCount() = paths.size

    inner class ViewHolder(private val root: FrameLayout, private val iv: ImageView, private val deleteBtn: ImageView) : RecyclerView.ViewHolder(root) {
        fun bind(uriStr: String, position: Int) {
            try {
                iv.setImageURI(Uri.parse(uriStr))
            } catch (_: Exception) {}
            root.setOnClickListener { onDelete(position) }
            root.setOnLongClickListener {
                onDelete(position); true
            }
        }
    }
}

class VideoAdapter(
    private val paths: MutableList<String>,
    private val onDelete: (Int) -> Unit
) : RecyclerView.Adapter<VideoAdapter.ViewHolder>() {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val context = parent.context
        val root = FrameLayout(context).apply {
            layoutParams = ViewGroup.LayoutParams(100, 100)
            setPadding(4, 4, 4, 4)
        }
        val iv = ImageView(context).apply {
            layoutParams = FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT
            )
            scaleType = ImageView.ScaleType.CENTER
            setBackgroundColor(Color.parseColor("#161B22"))
            setImageResource(android.R.drawable.ic_media_play)
            setColorFilter(Color.parseColor("#58A6FF"))
        }
        root.addView(iv)
        val label = TextView(context).apply {
            layoutParams = FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.WRAP_CONTENT
            ).apply {
                gravity = android.view.Gravity.BOTTOM
            }
            setBackgroundColor(Color.parseColor("#AA0D1117"))
            setTextColor(Color.WHITE)
            textSize = 9f
            setPadding(4, 2, 4, 2)
            maxLines = 1
        }
        root.addView(label)
        return ViewHolder(root, label)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.bind(paths[position], position)
    }

    override fun getItemCount() = paths.size

    inner class ViewHolder(private val root: FrameLayout, private val label: TextView) :
        RecyclerView.ViewHolder(root) {
        fun bind(uriStr: String, position: Int) {
            val name = try {
                Uri.parse(uriStr).lastPathSegment ?: "video"
            } catch (_: Exception) {
                "video"
            }
            label.text = name.takeLast(12)
            root.setOnClickListener { onDelete(position) }
            root.setOnLongClickListener {
                onDelete(position); true
            }
        }
    }
}

// ============================================================
//  SketchView - 完整新版（深色科技风）
// ============================================================
class SketchView(context: Context) : View(context) {
    private val tools = AnnotationTool.entries.toTypedArray()
    private val colors = intArrayOf(Color.RED, Color.GREEN, Color.BLUE, Color.YELLOW, Color.CYAN, Color.MAGENTA, Color.WHITE)
    var currentTool = AnnotationTool.FREE_DRAW
    var currentColor = Color.parseColor("#58A6FF")
    private var strokeWidth = 4f
    private val paths = mutableListOf<Path>()
    private val pathColors = mutableListOf<Int>()
    private var lastX = 0f; private var lastY = 0f
    private var downX = 0f; private var downY = 0f
    private var backgroundBitmap: Bitmap? = null
    private val density = context.resources.displayMetrics.density
    private val rowH = 52f * density
    private val row2H = 42f * density
    private val pad = 12f * density
    private val barHeight = rowH + row2H + pad * 3
    private val row1Y: Float get() = height - barHeight + pad
    private val row2Y: Float get() = row1Y + rowH + pad
    private val row1Slot: Float get() = width / 7f
    private val row2Slot: Float get() = width / 10f
    private val row1Center: (Int) -> Float = { row1Slot * it + row1Slot / 2 }
    private val row2Center: (Int) -> Float = { row2Slot * it + row2Slot / 2 }
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeCap = Paint.Cap.ROUND }
    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL; textSize = 36f * density }
    private data class SkState(val paths: List<Pair<Path, Int>>, val texts: List<TextAnnot>)
    private val skUndoStack = mutableListOf<SkState>()
    private val skRedoStack = mutableListOf<SkState>()
    private val pathWithColor: List<Pair<Path, Int>> get() {
        val p = mutableListOf<Pair<Path, Int>>()
        paths.forEachIndexed { i, path -> p.add(Pair(path, if (i < pathColors.size) pathColors[i] else currentColor)) }
        return p
    }
    private data class TextAnnot(val x: Float, val y: Float, val text: String, val color: Int)
    private val textAnnotations = mutableListOf<TextAnnot>()

    init {
        setBackgroundColor(Color.parseColor("#0D0D14"))
    }

    fun setBackgroundBitmap(bm: Bitmap) {
        backgroundBitmap = bm
        setBackgroundColor(Color.TRANSPARENT)
        invalidate()
    }

    fun getBitmap(): Bitmap {
        val bmp = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bmp)
        val drawAreaH = height - barHeight
        val bg = backgroundBitmap
        if (bg != null) {
            val s = minOf(width.toFloat() / bg.width.toFloat(), drawAreaH.toFloat() / bg.height.toFloat())
            val ox = (width - bg.width * s) / 2f
            val oy = (drawAreaH - bg.height * s) / 2f
            canvas.drawBitmap(bg, null, RectF(ox, oy, ox + bg.width * s, oy + bg.height * s), null)
        } else {
            canvas.drawColor(Color.parseColor("#1A1A2E"))
        }
        paths.forEachIndexed { i, path ->
            paint.color = if (i < pathColors.size) pathColors[i] else currentColor
            paint.strokeWidth = strokeWidth
            canvas.drawPath(path, paint)
        }
        for (ta in textAnnotations) {
            fillPaint.color = ta.color
            canvas.drawText(ta.text, ta.x, ta.y, fillPaint)
        }
        return bmp
    }

    fun clear() {
        paths.clear()
        pathColors.clear()
        textAnnotations.clear()
        invalidate()
    }

    private fun saveState() {
        skUndoStack.add(SkState(pathWithColor, textAnnotations.toList()))
        skRedoStack.clear()
    }

    private fun restoreState(s: SkState) {
        paths.clear()
        pathColors.clear()
        for ((p, c) in s.paths) {
            paths.add(p)
            pathColors.add(c)
        }
        textAnnotations.clear()
        textAnnotations.addAll(s.texts)
        invalidate()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val drawAreaH = height - barHeight
        val bg = backgroundBitmap
        if (bg != null) {
            val s = minOf(width.toFloat() / bg.width.toFloat(), drawAreaH.toFloat() / bg.height.toFloat())
            val ox = (width - bg.width * s) / 2f
            val oy = (drawAreaH - bg.height * s) / 2f
            canvas.drawBitmap(bg, null, RectF(ox, oy, ox + bg.width * s, oy + bg.height * s), null)
        }

        paths.forEachIndexed { i, path ->
            paint.color = if (i < pathColors.size) pathColors[i] else currentColor
            paint.strokeWidth = strokeWidth
            canvas.drawPath(path, paint)
        }
        for (ta in textAnnotations) {
            fillPaint.color = ta.color
            canvas.drawText(ta.text, ta.x, ta.y, fillPaint)
        }

        drawTechToolbar(canvas)
    }

    private fun drawTechToolbar(canvas: Canvas) {
        val barTop = height - rowH - row2H - pad * 3
        val bgPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xCC1A1A2E.toInt()
            setShadowLayer(12f, 0f, -4f, 0x66000000.toInt())
        }
        canvas.drawRoundRect(0f, barTop - pad, width.toFloat(), height.toFloat(), 20f, 20f, bgPaint)

        val linePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0x3358A6FF.toInt()
            strokeWidth = 1.5f * density
        }
        canvas.drawLine(0f, barTop + rowH + pad/2, width.toFloat(), barTop + rowH + pad/2, linePaint)

        val tp = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFFC9D1D9.toInt()
            textSize = 16f * density
            textAlign = Paint.Align.CENTER
        }
        val selPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFF58A6FF.toInt()
            setShadowLayer(12f, 0f, 0f, 0x6658A6FF.toInt())
        }
        val btnPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFF30363D.toInt()
        }
        val disabledPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFF21262D.toInt()
            alpha = 100
        }
        val bSize = minOf(rowH * 0.65f, row1Slot * 0.55f)

        tools.forEachIndexed { i, t ->
            val cx = row1Center(i)
            val cy = barTop + rowH / 2
            val isSelected = t == currentTool
            val bg = if (isSelected) selPaint else btnPaint
            canvas.drawRoundRect(cx - bSize/2, cy - bSize/2, cx + bSize/2, cy + bSize/2, 12f, 12f, bg)
            val icon = when (t) {
                AnnotationTool.RECT -> "▭"
                AnnotationTool.CIRCLE -> "●"
                AnnotationTool.LINE -> "↗"
                AnnotationTool.FREE_DRAW -> "✎"
                AnnotationTool.TEXT -> "T"
            }
            canvas.drawText(icon, cx, cy + 6f * density, tp)
        }

        listOf(Pair("↩", skUndoStack.isNotEmpty()), Pair("↪", skRedoStack.isNotEmpty())).forEachIndexed { i, (label, enabled) ->
            val cx = row1Center(tools.size + i)
            val cy = barTop + rowH / 2
            val bg = if (enabled) btnPaint else disabledPaint
            canvas.drawRoundRect(cx - bSize/2, cy - bSize/2, cx + bSize/2, cy + bSize/2, 12f, 12f, bg)
            canvas.drawText(label, cx, cy + 6f * density, tp.apply { alpha = if (enabled) 255 else 100 })
        }

        val cSize = minOf(row2H * 0.55f, row2Slot * 0.50f)
        val cy = barTop + rowH + pad + row2H / 2
        colors.forEachIndexed { i, c ->
            val cx = row2Center(i)
            val r = cSize / 2
            if (c == currentColor) {
                val glow = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                    color = 0x6658A6FF.toInt()
                    setShadowLayer(16f, 0f, 0f, 0x6658A6FF.toInt())
                }
                canvas.drawCircle(cx, cy, r + 6f, glow)
            }
            canvas.drawCircle(cx, cy, r, Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = c
                style = Paint.Style.FILL
            })
            canvas.drawCircle(cx, cy, r, Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = if (c == currentColor) 0xFFFFFFFF.toInt() else 0x4DFFFFFF.toInt()
                style = Paint.Style.STROKE
                strokeWidth = 2f * density
            })
        }
        val btnSize = minOf(row2H * 0.45f, row2Slot * 0.40f)
        listOf(Pair(7, "－"), Pair(9, "＋")).forEach { (idx, label) ->
            val cx = row2Center(idx)
            canvas.drawRoundRect(cx - btnSize/2, cy - btnSize/2, cx + btnSize/2, cy + btnSize/2, 8f, 8f, btnPaint)
            canvas.drawText(label, cx, cy + 6f * density, tp)
        }
        val labelCx = row2Center(8)
        canvas.drawText("粗细", labelCx, cy - 4f * density,
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = 0xFF8B949E.toInt()
                textSize = 11f * density
                textAlign = Paint.Align.CENTER
            })
        canvas.drawText("${strokeWidth.toInt()}px", labelCx, cy + 10f * density,
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = 0xFFC9D1D9.toInt()
                textSize = 13f * density
                textAlign = Paint.Align.CENTER
            })
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val x = event.x
        val y = event.y
        val barTop = height - rowH - row2H - pad * 3
        val idx1 = (x / row1Slot).toInt()
        val idx2 = (x / row2Slot).toInt()
        if (event.action == MotionEvent.ACTION_DOWN && y > barTop - pad) {
            if (y in barTop..(barTop + rowH) && idx1 < tools.size) {
                currentTool = tools[idx1]
                invalidate()
                return true
            }
            if (y in barTop..(barTop + rowH) && idx1 == tools.size) {
                if (skUndoStack.isNotEmpty()) {
                    skRedoStack.add(SkState(pathWithColor, textAnnotations.toList()))
                    restoreState(skUndoStack.removeAt(skUndoStack.lastIndex))
                }
                return true
            }
            if (y in barTop..(barTop + rowH) && idx1 == tools.size + 1) {
                if (skRedoStack.isNotEmpty()) {
                    skUndoStack.add(SkState(pathWithColor, textAnnotations.toList()))
                    restoreState(skRedoStack.removeAt(skRedoStack.lastIndex))
                }
                return true
            }
            if (y in (barTop + rowH + pad)..(barTop + rowH + pad + row2H) && idx2 < colors.size) {
                currentColor = colors[idx2]
                invalidate()
                return true
            }
            if (y in (barTop + rowH + pad)..(barTop + rowH + pad + row2H) && idx2 == 7) {
                strokeWidth = maxOf(1f, strokeWidth - 1f)
                invalidate()
                return true
            }
            if (y in (barTop + rowH + pad)..(barTop + rowH + pad + row2H) && idx2 == 9) {
                strokeWidth = minOf(20f, strokeWidth + 1f)
                invalidate()
                return true
            }
            return true
        }

        val drawAreaH = height - barHeight
        if (y > drawAreaH) return true

        when (event.action) {
            MotionEvent.ACTION_DOWN -> {
                downX = x
                downY = y
                lastX = x
                lastY = y
                saveState()
                when (currentTool) {
                    AnnotationTool.FREE_DRAW -> {
                        val p = Path()
                        p.moveTo(x, y)
                        paths.add(p)
                        pathColors.add(currentColor)
                    }
                    AnnotationTool.TEXT -> {}
                    else -> {
                        val p = Path()
                        p.moveTo(x, y)
                        paths.add(p)
                        pathColors.add(currentColor)
                    }
                }
                return true
            }
            MotionEvent.ACTION_MOVE -> {
                val dx = abs(x - lastX)
                val dy = abs(y - lastY)
                lastX = x
                lastY = y
                if (dx >= 4 || dy >= 4) {
                    val p = paths.lastOrNull() ?: return true
                    when (currentTool) {
                        AnnotationTool.FREE_DRAW -> p.lineTo(x, y)
                        AnnotationTool.RECT -> {
                            p.reset()
                            p.addRect(
                                minOf(downX, x), minOf(downY, y),
                                maxOf(downX, x), maxOf(downY, y),
                                Path.Direction.CCW
                            )
                        }
                        AnnotationTool.CIRCLE -> {
                            val cx = (downX + x) / 2
                            val cy = (downY + y) / 2
                            val r = kotlin.math.sqrt((x - downX) * (x - downX) + (y - downY) * (y - downY)) / 2
                            p.reset()
                            p.addCircle(cx, cy, r, Path.Direction.CCW)
                        }
                        AnnotationTool.LINE -> {
                            p.reset()
                            p.moveTo(downX, downY)
                            p.lineTo(x, y)
                        }
                        AnnotationTool.TEXT -> {}
                    }
                    invalidate()
                }
            }
            MotionEvent.ACTION_UP -> {
                val p = paths.lastOrNull()
                if (p != null) {
                    when (currentTool) {
                        AnnotationTool.RECT -> {
                            p.reset()
                            p.addRect(
                                minOf(downX, x), minOf(downY, y),
                                maxOf(downX, x), maxOf(downY, y),
                                Path.Direction.CCW
                            )
                        }
                        AnnotationTool.CIRCLE -> {
                            val cx = (downX + x) / 2
                            val cy = (downY + y) / 2
                            val r = kotlin.math.sqrt((x - downX) * (x - downX) + (y - downY) * (y - downY)) / 2
                            p.reset()
                            p.addCircle(cx, cy, r, Path.Direction.CCW)
                        }
                        AnnotationTool.LINE -> {
                            p.reset()
                            p.moveTo(downX, downY)
                            p.lineTo(x, y)
                        }
                        AnnotationTool.FREE_DRAW -> p.lineTo(x, y)
                        AnnotationTool.TEXT -> {}
                    }
                }
                if (currentTool == AnnotationTool.TEXT) {
                    val et = android.widget.EditText(context).apply { setSingleLine() }
                    AlertDialog.Builder(context)
                        .setTitle("输入标注文字")
                        .setView(et)
                        .setPositiveButton("确定") { _, _ ->
                            val txt = et.text.toString()
                            if (txt.isNotEmpty()) {
                                val tw = fillPaint.measureText(txt)
                                val tp = Path()
                                tp.addRect(
                                    x - tw / 2, y - 36f * density,
                                    x + tw / 2, y + 8f * density,
                                    Path.Direction.CCW
                                )
                                paths.add(tp)
                                pathColors.add(currentColor)
                                textAnnotations.add(TextAnnot(x, y, txt, currentColor))
                                invalidate()
                            }
                        }
                        .setNegativeButton("取消", null)
                        .show()
                }
                invalidate()
            }
        }
        return true
    }
}