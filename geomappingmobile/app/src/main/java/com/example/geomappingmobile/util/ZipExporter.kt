package com.example.geomappingmobile.util

import android.content.Context
import com.example.geomappingmobile.data.model.GeologicalPoint
import com.example.geomappingmobile.data.model.Route
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

object ZipExporter {

    private val dateFormat = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.getDefault())

    fun export(
        context: Context,
        points: List<GeologicalPoint>,
        routes: List<Route>,
    ): File {
        val exportDir = File(context.filesDir, "exports")
        exportDir.mkdirs()

        val fileName = "GeoPackage_${dateFormat.format(Date())}.zip"
        val zipFile = File(exportDir, fileName)

        ZipOutputStream(FileOutputStream(zipFile)).use { zos ->
            val geojson = GeoJsonExporter.export(points, routes)
            zos.putNextEntry(ZipEntry("data.geojson"))
            zos.write(geojson.toByteArray(Charsets.UTF_8))
            zos.closeEntry()

            val added = mutableSetOf<String>()

            // Photos
            for (pt in points) {
                for (path in pt.photoPaths) {
                    val src = File(path)
                    if (src.exists() && added.add(src.name)) {
                        zos.putNextEntry(ZipEntry("photos/${src.name}"))
                        FileInputStream(src).use { it.copyTo(zos) }
                        zos.closeEntry()
                    }
                }
            }

            // Sketches
            for (pt in points) {
                for (path in pt.sketchPaths) {
                    val src = File(path)
                    if (src.exists() && added.add(src.name)) {
                        zos.putNextEntry(ZipEntry("sketches/${src.name}"))
                        FileInputStream(src).use { it.copyTo(zos) }
                        zos.closeEntry()
                    }
                }
            }

            // Attachments
            for (pt in points) {
                for (path in pt.attachmentPaths) {
                    val src = File(path)
                    if (src.exists() && added.add(src.name)) {
                        zos.putNextEntry(ZipEntry("attachments/${src.name}"))
                        FileInputStream(src).use { it.copyTo(zos) }
                        zos.closeEntry()
                    }
                }
            }

            // Videos
            for (pt in points) {
                for (path in pt.videoPaths) {
                    val src = File(path)
                    if (src.exists() && added.add(src.name)) {
                        zos.putNextEntry(ZipEntry("videos/${src.name}"))
                        FileInputStream(src).use { it.copyTo(zos) }
                        zos.closeEntry()
                    }
                }
            }
        }

        return zipFile
    }
}
