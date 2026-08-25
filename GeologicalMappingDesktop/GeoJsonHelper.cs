using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Data;

namespace GeologicalMappingDesktop
{
    public static class GeoJsonHelper
    {
        public static string ExportToGeoJson(List<FeatureState> features, string layerName)
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"FeatureCollection\",");
            sb.Append("\"metadata\":{");
            sb.Append($"\"generatedAt\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
            sb.Append($"\"generator\":\"GeologicalMappingDesktop\",");
            sb.Append($"\"layerName\":\"{EscapeJson(layerName)}\"");
            sb.Append("},\"features\":[");
            for (int i = 0; i < features.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(FeatureStateToGeoJson(features[i]));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string FeatureStateToGeoJson(FeatureState fs)
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"Feature\",\"geometry\":");
            if (!string.IsNullOrEmpty(fs.GeometryJson))
                sb.Append(ArcGisJsonToGeoJsonGeometry(fs.GeometryJson));
            else
                sb.Append("null");
            sb.Append(",\"properties\":{");
            bool first = true;
            foreach (var kv in fs.Attributes)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{EscapeJson(kv.Key)}\":{SerializeValue(kv.Value)}");
            }
            sb.Append("}}");
            return sb.ToString();
        }

        private static string ArcGisJsonToGeoJsonGeometry(string arcgisJson)
        {
            try
            {
                var geom = Geometry.FromJson(arcgisJson);
                return GeometryToGeoJson(geom);
            }
            catch { return "null"; }
        }

        private static string GeometryToGeoJson(Geometry geom)
        {
            if (geom == null) return "null";
            switch (geom.GeometryType)
            {
                case GeometryType.Point:
                    var pt = (MapPoint)geom;
                    return $"{{\"type\":\"Point\",\"coordinates\":[{pt.X.ToString("G", CultureInfo.InvariantCulture)},{pt.Y.ToString("G", CultureInfo.InvariantCulture)}]}}";
                case GeometryType.Polyline:
                    return PolylineToGeoJson((Polyline)geom);
                case GeometryType.Polygon:
                    return PolygonToGeoJson((Polygon)geom);
                case GeometryType.Multipoint:
                    return MultipointToGeoJson((Multipoint)geom);
                default:
                    return "null";
            }
        }

        private static string PolylineToGeoJson(Polyline polyline)
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"MultiLineString\",\"coordinates\":[");
            for (int p = 0; p < polyline.Parts.Count; p++)
            {
                if (p > 0) sb.Append(",");
                sb.Append("[");
                var pts = polyline.Parts[p].Points;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append($"[{pts[i].X.ToString("G", CultureInfo.InvariantCulture)},{pts[i].Y.ToString("G", CultureInfo.InvariantCulture)}]");
                }
                sb.Append("]");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string PolygonToGeoJson(Polygon polygon)
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"Polygon\",\"coordinates\":[");
            for (int p = 0; p < polygon.Parts.Count; p++)
            {
                if (p > 0) sb.Append(",");
                sb.Append("[");
                var pts = polygon.Parts[p].Points;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append($"[{pts[i].X.ToString("G", CultureInfo.InvariantCulture)},{pts[i].Y.ToString("G", CultureInfo.InvariantCulture)}]");
                }
                sb.Append("]");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string MultipointToGeoJson(Multipoint multipoint)
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"MultiPoint\",\"coordinates\":[");
            var pts = multipoint.Points;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"[{pts[i].X.ToString("G", CultureInfo.InvariantCulture)},{pts[i].Y.ToString("G", CultureInfo.InvariantCulture)}]");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static List<FeatureState> ParseGeoJsonFeatures(string geoJson)
        {
            var result = new List<FeatureState>();
            int pos = 0;
            SkipWhitespace(geoJson, ref pos);

            System.Diagnostics.Debug.WriteLine($"[GeoJsonHelper] ParseGeoJsonFeatures: input length={geoJson?.Length ?? 0}, first200={(geoJson?.Length > 200 ? geoJson.Substring(0, 200) : geoJson)}");

            if (pos >= geoJson.Length || geoJson[pos] != '{')
            {
                System.Diagnostics.Debug.WriteLine("[GeoJsonHelper] ParseGeoJsonFeatures: not a JSON object, returning empty");
                return result;
            }

            pos++; // skip {
            while (pos < geoJson.Length)
            {
                SkipWhitespace(geoJson, ref pos);
                if (pos >= geoJson.Length || geoJson[pos] == '}') break;
                string key = ReadJsonKey(geoJson, ref pos);
                SkipWhitespace(geoJson, ref pos);
                if (pos < geoJson.Length && geoJson[pos] == ':') pos++;
                SkipWhitespace(geoJson, ref pos);

                if (key == "features" && pos < geoJson.Length && geoJson[pos] == '[')
                {
                    pos++;
                    int featureCount = 0;
                    while (pos < geoJson.Length)
                    {
                        SkipWhitespace(geoJson, ref pos);
                        if (pos >= geoJson.Length || geoJson[pos] == ']') break;
                        if (geoJson[pos] == '{')
                        {
                            var fs = ParseSingleFeature(geoJson, ref pos);
                            if (fs != null) { result.Add(fs); featureCount++; }
                            else System.Diagnostics.Debug.WriteLine($"[GeoJsonHelper] ParseSingleFeature returned null at feature index {featureCount}");
                        }
                        else pos++;
                    }
                    System.Diagnostics.Debug.WriteLine($"[GeoJsonHelper] ParseGeoJsonFeatures: parsed {featureCount} features from array");
                    if (pos < geoJson.Length) pos++;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[GeoJsonHelper] ParseGeoJsonFeatures: skipping key='{key}'");
                    SkipValue(geoJson, ref pos);
                }
                SkipWhitespace(geoJson, ref pos);
                if (pos < geoJson.Length && geoJson[pos] == ',') pos++;
            }
            System.Diagnostics.Debug.WriteLine($"[GeoJsonHelper] ParseGeoJsonFeatures: total result count={result.Count}");
            return result;
        }

        private static FeatureState ParseSingleFeature(string json, ref int pos)
        {
            int startPos = pos;
            if (pos >= json.Length || json[pos] != '{') { SkipToChar(json, ref pos, '}'); if (pos < json.Length) pos++; return null; }
            pos++;
            var fs = new FeatureState();
            string geomJson = null;
            var attrs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            while (pos < json.Length)
            {
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length || json[pos] == '}') break;
                string key = ReadJsonKey(json, ref pos);
                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ':') pos++;
                SkipWhitespace(json, ref pos);

                if (key == "geometry")
                {
                    if (pos < json.Length && json[pos] == 'n') { pos += 4; }
                    else
                    {
                        int start = pos;
                        int depth = 0;
                        while (pos < json.Length)
                        {
                            if (json[pos] == '{') depth++;
                            else if (json[pos] == '}') { depth--; if (depth <= 0) break; }
                            pos++;
                        }
                        if (pos < json.Length) pos++;
                        string rawGeom = json.Substring(start, pos - start);
                        try
                        {
                            var arcGisGeom = GeoJsonToArcGisGeometry(rawGeom);
                            if (arcGisGeom != null)
                                geomJson = arcGisGeom.ToJson();
                        }
                        catch { }
                    }
                }
                else if (key == "properties" && pos < json.Length && json[pos] == '{')
                {
                    pos++;
                    while (pos < json.Length)
                    {
                        SkipWhitespace(json, ref pos);
                        if (pos >= json.Length || json[pos] == '}') break;
                        string pk = ReadJsonKey(json, ref pos);
                        SkipWhitespace(json, ref pos);
                        if (pos < json.Length && json[pos] == ':') pos++;
                        SkipWhitespace(json, ref pos);
                        object val = ReadJsonValue(json, ref pos);
                        if (pk != null)
                            attrs[pk] = val;
                        SkipWhitespace(json, ref pos);
                        if (pos < json.Length && json[pos] == ',') pos++;
                    }
                    if (pos < json.Length) pos++;
                }
                else
                {
                    SkipValue(json, ref pos);
                }
                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ',') pos++;
            }
            if (pos < json.Length) pos++;

            fs.GeometryJson = geomJson ?? "";
            fs.Attributes = attrs;
            string geomTypeName = "none";
            if (!string.IsNullOrEmpty(geomJson))
            {
                try { geomTypeName = Esri.ArcGISRuntime.Geometry.Geometry.FromJson(geomJson).GeometryType.ToString(); } catch { geomTypeName = "parse_error"; }
            }
            System.Diagnostics.Debug.WriteLine($"[GeoJsonHelper] ParseSingleFeature: geomType={geomTypeName}, attrCount={attrs.Count}, attrKeys=[{string.Join(",", attrs.Keys)}]");
            return fs;
        }

        private static Geometry GeoJsonToArcGisGeometry(string geoJsonGeom)
        {
            // Parse GeoJSON geometry to ArcGIS geometry
            int p = 0;
            SkipWhitespace(geoJsonGeom, ref p);
            if (p >= geoJsonGeom.Length || geoJsonGeom[p] != '{') return null;
            p++;

            string type = null;
            string coords = null;
            while (p < geoJsonGeom.Length)
            {
                SkipWhitespace(geoJsonGeom, ref p);
                if (p >= geoJsonGeom.Length || geoJsonGeom[p] == '}') break;
                string key = ReadJsonKey(geoJsonGeom, ref p);
                SkipWhitespace(geoJsonGeom, ref p);
                if (p < geoJsonGeom.Length && geoJsonGeom[p] == ':') p++;
                SkipWhitespace(geoJsonGeom, ref p);

                if (key == "type")
                {
                    type = ReadJsonStringValue(geoJsonGeom, ref p);
                }
                else if (key == "coordinates")
                {
                    int start = p;
                    int depth = 0;
                    while (p < geoJsonGeom.Length)
                    {
                        if (geoJsonGeom[p] == '[') depth++;
                        else if (geoJsonGeom[p] == ']') { depth--; if (depth < 0) break; }
                        p++;
                    }
                    if (p < geoJsonGeom.Length) p++;
                    coords = geoJsonGeom.Substring(start, p - start);
                }
                else
                {
                    SkipValue(geoJsonGeom, ref p);
                }
                SkipWhitespace(geoJsonGeom, ref p);
                if (p < geoJsonGeom.Length && geoJsonGeom[p] == ',') p++;
            }

            if (type == null || coords == null) return null;

            switch (type)
            {
                case "Point":
                    {
                        var xy = ParseCoordPair(coords);
                        if (xy != null)
                            return new MapPoint(xy.Value.lon, xy.Value.lat, SpatialReferences.Wgs84);
                        return null;
                    }
                case "MultiPoint":
                    {
                        var pts = ParseCoordList(coords);
                        if (pts.Count > 0)
                            return new Multipoint(pts, SpatialReferences.Wgs84);
                        return null;
                    }
                case "LineString":
                    {
                        var pts = ParseCoordList(coords);
                        if (pts.Count >= 2)
                            return new Polyline(new List<IEnumerable<MapPoint>> { pts }, SpatialReferences.Wgs84);
                        return null;
                    }
                case "MultiLineString":
                    {
                        var lines = ParseMultiCoordList(coords);
                        if (lines.Count > 0)
                            return new Polyline(lines, SpatialReferences.Wgs84);
                        return null;
                    }
                case "Polygon":
                    {
                        var rings = SanitizeRings(ParseMultiCoordList(coords));
                        if (rings.Count > 0)
                            return new Polygon(rings, SpatialReferences.Wgs84);
                        return null;
                    }
                case "MultiPolygon":
                    {
                        var rings = SanitizeRings(ParsePolygonList(coords));
                        if (rings.Count > 0)
                            return new Polygon(rings, SpatialReferences.Wgs84);
                        return null;
                    }
                default:
                    return null;
            }
        }

        // MultiPolygon coordinates: [ [ring, ring...], [ring, ...], ... ] — one array per polygon, each containing its rings.
        private static List<List<MapPoint>> ParsePolygonList(string coordStr)
        {
            var result = new List<List<MapPoint>>();
            int p = 0;
            SkipWhitespace(coordStr, ref p);
            if (p >= coordStr.Length || coordStr[p] != '[') return result;
            p++;
            while (p < coordStr.Length)
            {
                SkipWhitespace(coordStr, ref p);
                if (p >= coordStr.Length || coordStr[p] == ']') break;
                if (coordStr[p] == '[')
                {
                    int end = p + 1;
                    int d = 0;
                    while (end < coordStr.Length)
                    {
                        if (coordStr[end] == '[') d++;
                        else if (coordStr[end] == ']') { d--; if (d < 0) break; }
                        end++;
                    }
                    string polygon = coordStr.Substring(p, end - p + 1);
                    result.AddRange(ParseMultiCoordList(polygon));
                    p = end + 1;
                }
                else p++;
                SkipWhitespace(coordStr, ref p);
                if (p < coordStr.Length && coordStr[p] == ',') p++;
            }
            return result;
        }

        // 丢弃退化环（<3 点）、去除相邻重复点，并确保环闭合，避免 ArcGIS Runtime 拒绝多边形。
        private static List<List<MapPoint>> SanitizeRings(List<List<MapPoint>> rings)
        {
            var clean = new List<List<MapPoint>>();
            foreach (var ring in rings)
            {
                if (ring.Count < 3) continue;
                var list = new List<MapPoint>();
                foreach (var pt in ring)
                {
                    if (list.Count == 0 || list[list.Count - 1].X != pt.X || list[list.Count - 1].Y != pt.Y)
                        list.Add(pt);
                }
                if (list.Count >= 3)
                {
                    if (list[0].X != list[list.Count - 1].X || list[0].Y != list[list.Count - 1].Y)
                        list.Add(list[0]);
                    clean.Add(list);
                }
            }
            return clean;
        }

        private static (double lon, double lat)? ParseCoordPair(string coordStr)
        {
            int p = 0;
            SkipWhitespace(coordStr, ref p);
            if (p >= coordStr.Length || coordStr[p] != '[') return null;
            p++;
            string lonStr = ReadUntil(coordStr, ref p, c => c == ',' || c == ']');
            if (string.IsNullOrEmpty(lonStr)) return null;
            SkipWhitespace(coordStr, ref p);
            if (p < coordStr.Length && coordStr[p] == ',') p++;
            SkipWhitespace(coordStr, ref p);
            string latStr = ReadUntil(coordStr, ref p, c => c == ',' || c == ']');
            if (string.IsNullOrEmpty(latStr)) return null;
            if (double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) &&
                double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                return (lon, lat);
            return null;
        }

        private static List<MapPoint> ParseCoordList(string coordStr)
        {
            var result = new List<MapPoint>();
            int p = 0;
            SkipWhitespace(coordStr, ref p);
            if (p >= coordStr.Length || coordStr[p] != '[') return result;
            p++;
            int depth = 0;
            int start = -1;
            while (p < coordStr.Length)
            {
                if (coordStr[p] == '[') { depth++; if (depth == 1) start = p; p++; }
                else if (coordStr[p] == ']') { depth--; if (depth == 0) break; p++; }
                else p++;
            }
            // Parse the content
            string content = coordStr;
            int cp = 0;
            SkipWhitespace(content, ref cp);
            if (cp < content.Length && content[cp] == '[') cp++;
            while (cp < content.Length)
            {
                SkipWhitespace(content, ref cp);
                if (cp >= content.Length || content[cp] == ']') break;
                if (content[cp] == '[')
                {
                    int end = cp + 1;
                    int d = 0;
                    while (end < content.Length)
                    {
                        if (content[end] == '[') d++;
                        else if (content[end] == ']') { d--; if (d < 0) break; }
                        end++;
                    }
                    string pair = content.Substring(cp, end - cp + 1);
                    var xy = ParseCoordPair(pair);
                    if (xy != null)
                        result.Add(new MapPoint(xy.Value.lon, xy.Value.lat, SpatialReferences.Wgs84));
                    cp = end + 1;
                }
                else cp++;
                SkipWhitespace(content, ref cp);
                if (cp < content.Length && content[cp] == ',') cp++;
            }
            return result;
        }

        private static List<List<MapPoint>> ParseMultiCoordList(string coordStr)
        {
            var result = new List<List<MapPoint>>();
            int p = 0;
            SkipWhitespace(coordStr, ref p);
            if (p >= coordStr.Length || coordStr[p] != '[') return result;
            p++;
            int depth = 0;
            while (p < coordStr.Length)
            {
                SkipWhitespace(coordStr, ref p);
                if (p >= coordStr.Length || coordStr[p] == ']') break;
                if (coordStr[p] == '[')
                {
                    int end = p + 1;
                    int d = 0;
                    while (end < coordStr.Length)
                    {
                        if (coordStr[end] == '[') d++;
                        else if (coordStr[end] == ']') { d--; if (d < 0) break; }
                        end++;
                    }
                    string ring = coordStr.Substring(p, end - p + 1);
                    var pts = ParseCoordList(ring);
                    if (pts.Count > 0)
                        result.Add(pts);
                    p = end + 1;
                }
                else p++;
                SkipWhitespace(coordStr, ref p);
                if (p < coordStr.Length && coordStr[p] == ',') p++;
            }
            return result;
        }

        private static string ReadJsonKey(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length || json[pos] != '"') return null;
            pos++;
            int end = pos;
            while (end < json.Length && json[end] != '"') end++;
            string key = json.Substring(pos, end - pos);
            pos = end + 1;
            return key;
        }

        private static string ReadJsonStringValue(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length || json[pos] != '"') return null;
            pos++;
            int end = pos;
            while (end < json.Length && json[end] != '"') end++;
            string val = json.Substring(pos, end - pos);
            pos = end + 1;
            return val;
        }

        private static object ReadJsonValue(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length) return null;
            if (json[pos] == '"')
            {
                pos++;
                int end = pos;
                while (end < json.Length && json[end] != '"') end++;
                string s = json.Substring(pos, end - pos);
                pos = end + 1;
                return s;
            }
            if (json[pos] == 'n' && pos + 3 < json.Length && json.Substring(pos, 4) == "null") { pos += 4; return null; }
            if (json[pos] == 't' && pos + 3 < json.Length && json.Substring(pos, 4) == "true") { pos += 4; return true; }
            if (json[pos] == 'f' && pos + 4 < json.Length && json.Substring(pos, 5) == "false") { pos += 5; return false; }
            if (json[pos] == '[' || json[pos] == '{')
            {
                int start = pos;
                int depth = 0;
                while (pos < json.Length)
                {
                    if (json[pos] == '{' || json[pos] == '[') depth++;
                    else if (json[pos] == '}' || json[pos] == ']') { depth--; if (depth <= 0) break; }
                    pos++;
                }
                if (pos < json.Length) pos++;
                return json.Substring(start, pos - start);
            }
            int es = pos;
            while (es < json.Length && json[es] != ',' && json[es] != '}' && json[es] != ']') es++;
            string num = json.Substring(pos, es - pos).Trim();
            pos = es;
            if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv))
            {
                if (num.IndexOf('.') < 0 && num.IndexOf('E') < 0 && num.IndexOf('e') < 0)
                {
                    if (dv >= int.MinValue && dv <= int.MaxValue) return (int)dv;
                    return (long)dv;
                }
                return dv;
            }
            return num;
        }

        private static string ReadUntil(string s, ref int pos, Func<char, bool> stopAt)
        {
            int start = pos;
            while (pos < s.Length && !stopAt(s[pos])) pos++;
            return s.Substring(start, pos - start).Trim();
        }

        private static void SkipValue(string json, ref int pos)
        {
            if (pos >= json.Length) return;
            if (json[pos] == '"')
            {
                pos++;
                while (pos < json.Length && json[pos] != '"') pos++;
                if (pos < json.Length) pos++;
            }
            else if (json[pos] == '{' || json[pos] == '[')
            {
                int depth = 0;
                while (pos < json.Length)
                {
                    if (json[pos] == '{' || json[pos] == '[') depth++;
                    else if (json[pos] == '}' || json[pos] == ']') { depth--; if (depth <= 0) break; }
                    pos++;
                }
                if (pos < json.Length) pos++;
            }
            else
            {
                while (pos < json.Length && json[pos] != ',' && json[pos] != '}' && json[pos] != ']') pos++;
            }
        }

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
        }

        private static void SkipToChar(string json, ref int pos, char c)
        {
            while (pos < json.Length && json[pos] != c) pos++;
        }

        private static string SerializeValue(object val)
        {
            if (val == null || val == DBNull.Value) return "null";
            if (val is string s) return $"\"{EscapeJson(s)}\"";
            if (val is double d) return d.ToString("G17", CultureInfo.InvariantCulture);
            if (val is int iv) return iv.ToString(CultureInfo.InvariantCulture);
            if (val is long lv) return lv.ToString(CultureInfo.InvariantCulture);
            if (val is float fv) return fv.ToString("G9", CultureInfo.InvariantCulture);
            if (val is bool bv) return bv ? "true" : "false";
            if (val is DateTime dtv) return $"\"{dtv:yyyy-MM-dd HH:mm:ss}\"";
            return $"\"{EscapeJson(val.ToString())}\"";
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }
}
