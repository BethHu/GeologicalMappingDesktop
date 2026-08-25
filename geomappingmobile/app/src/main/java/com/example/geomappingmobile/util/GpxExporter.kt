package com.example.geomappingmobile.util

import com.example.geomappingmobile.data.model.GeologicalPoint
import com.example.geomappingmobile.data.model.Route
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

object GpxExporter {

    private val dateFormat = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US)

    fun export(points: List<GeologicalPoint>, routes: List<Route>): String {
        val sb = StringBuilder()
        sb.appendLine("""<?xml version="1.0" encoding="UTF-8"?>""")
        sb.appendLine("""<gpx version="1.1" creator="GeoMappingMobile" xmlns="http://www.topografix.com/GPX/1/1">""")

        for (pt in points) {
            sb.appendLine("""  <wpt lat="${formatCoord(pt.latitude)}" lon="${formatCoord(pt.longitude)}">""")
            sb.appendLine("    <name>${escapeXml(pt.customId.ifEmpty { pt.id })}</name>")
            sb.appendLine("    <desc>${escapeXml(pt.description)}</desc>")
            sb.appendLine("    <cmt>类型: ${escapeXml(pt.pointType.displayName)}</cmt>")
            sb.appendLine("    <ele>${pt.elevation}</ele>")
            sb.appendLine("    <time>${dateFormat.format(pt.timestamp)}</time>")
            sb.appendLine("    <type>${escapeXml(pt.pointType.name)}</type>")
            sb.appendLine("  </wpt>")
        }

        for (route in routes) {
            sb.appendLine("  <trk>")
            sb.appendLine("    <name>${escapeXml(route.name)}</name>")
            sb.appendLine("    <desc>${escapeXml(route.description)}</desc>")
            sb.appendLine("    <trkseg>")
            for (tp in route.trackPoints) {
                sb.appendLine("""      <trkpt lat="${formatCoord(tp.latitude)}" lon="${formatCoord(tp.longitude)}">""")
                sb.appendLine("        <ele>${tp.elevation}</ele>")
                sb.appendLine("        <time>${dateFormat.format(tp.timestamp)}</time>")
                sb.appendLine("      </trkpt>")
            }
            sb.appendLine("    </trkseg>")
            sb.appendLine("  </trk>")
        }

        sb.appendLine("</gpx>")
        return sb.toString()
    }

    private fun formatCoord(value: Double): String = String.format("%.7f", value)

    private fun escapeXml(s: String): String {
        return s.replace("&", "&amp;")
            .replace("<", "&lt;")
            .replace(">", "&gt;")
            .replace("\"", "&quot;")
            .replace("'", "&apos;")
    }
}
