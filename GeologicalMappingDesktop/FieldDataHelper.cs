using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;

namespace GeologicalMappingDesktop
{
    /// <summary>
    /// 野外数据分类规则（与移动端 PointType/RouteStatus 保持一致）。
    /// 点：移动端 7 类点类型；线：移动端路线状态 + 桌面端地质线类型。
    /// </summary>
    public static class FieldDataClassification
    {
        public class PointTypeDef
        {
            public string TypeCode;
            public string DisplayName;
            public System.Drawing.Color Color;
            public PointTypeDef(string typeCode, string displayName, System.Drawing.Color color)
            { TypeCode = typeCode; DisplayName = displayName; Color = color; }
        }

        public class LineTypeDef
        {
            public string TypeCode;
            public string DisplayName;
            public System.Drawing.Color Color;
            public LineTypeDef(string typeCode, string displayName, System.Drawing.Color color)
            { TypeCode = typeCode; DisplayName = displayName; Color = color; }
        }

        public static readonly PointTypeDef[] PointTypes = new PointTypeDef[]
        {
            new PointTypeDef("GEOLOGICAL", "地质观察点", System.Drawing.Color.FromArgb(255, 0x3F, 0xB9, 0x50)),
            new PointTypeDef("STRUCTURAL", "构造测量点", System.Drawing.Color.FromArgb(255, 0xD2, 0x99, 0x22)),
            new PointTypeDef("SAMPLE", "样品点", System.Drawing.Color.FromArgb(255, 0x58, 0xA6, 0xFF)),
            new PointTypeDef("PHOTO", "照片点", System.Drawing.Color.FromArgb(255, 0xA3, 0x71, 0xF7)),
            new PointTypeDef("LANDFORM", "地貌点", System.Drawing.Color.FromArgb(255, 0xF8, 0x51, 0x49)),
            new PointTypeDef("HYDROLOGICAL", "水文点", System.Drawing.Color.FromArgb(255, 0x21, 0x88, 0xFF)),
            new PointTypeDef("ENGINEERING", "工程地质点", System.Drawing.Color.FromArgb(255, 0xDB, 0x6D, 0x28)),
        };

        // 移动端路线状态分类
        public static readonly LineTypeDef[] RouteStatuses = new LineTypeDef[]
        {
            new LineTypeDef("COMPLETED", "已完成路线", System.Drawing.Color.FromArgb(255, 0xEF, 0x53, 0x50)),
            new LineTypeDef("PLANNED", "计划路线", System.Drawing.Color.FromArgb(255, 0x42, 0xA5, 0xF5)),
            new LineTypeDef("RECORDING", "记录中路线", System.Drawing.Color.FromArgb(255, 0x3F, 0xB9, 0x50)),
            new LineTypeDef("PAUSED", "已暂停路线", System.Drawing.Color.FromArgb(255, 0xD2, 0x99, 0x22)),
        };

        // 桌面端地质线类型标注（阶段 3 使用，导入时优先识别 lineType）
        public static readonly LineTypeDef[] GeologicalLineTypes = new LineTypeDef[]
        {
            new LineTypeDef("FAULT", "断层线", System.Drawing.Color.FromArgb(255, 0xF8, 0x51, 0x49)),
            new LineTypeDef("STRATUM_BOUNDARY", "地层界线", System.Drawing.Color.FromArgb(255, 0x58, 0xA6, 0xFF)),
            new LineTypeDef("ROCK_BOUNDARY", "岩体界线", System.Drawing.Color.FromArgb(255, 0x8B, 0x94, 0x9E)),
            new LineTypeDef("TECTONIC", "构造线", System.Drawing.Color.FromArgb(255, 0xD2, 0x99, 0x22)),
            new LineTypeDef("SECTION", "剖面线", System.Drawing.Color.FromArgb(255, 0xA3, 0x71, 0xF7)),
            new LineTypeDef("TRACK", "实测路线", System.Drawing.Color.FromArgb(255, 0x3F, 0xB9, 0x50)),
        };

        public const string POINT_UNCLASSIFIED = "未分类点";
        public const string LINE_UNCLASSIFIED = "未分类线";

        public static string NormalizeCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            return code.Trim().ToUpperInvariant();
        }

        public static PointTypeDef FindPointType(string code)
        {
            string c = NormalizeCode(code);
            foreach (var t in PointTypes)
                if (string.Equals(t.TypeCode, c, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.DisplayName, code?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return t;
            return null;
        }

        public static LineTypeDef FindLineType(string code)
        {
            string c = NormalizeCode(code);
            foreach (var t in GeologicalLineTypes)
                if (string.Equals(t.TypeCode, c, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.DisplayName, code?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return t;
            return null;
        }

        /// <summary>从要素属性读取 pointType/typeCode/pointTypeDisplay，返回显示名称（未知则 POINT_UNCLASSIFIED）。</summary>
        public static string GetPointDisplayName(FeatureState fs)
        {
            if (fs.Attributes == null) return POINT_UNCLASSIFIED;
            object val = null;
            if (fs.Attributes.TryGetValue("pointType", out val) || fs.Attributes.TryGetValue("typeCode", out val))
            {
                string s = val?.ToString();
                var def = FindPointType(s);
                if (def != null) return def.DisplayName;
            }
            if (fs.Attributes.TryGetValue("pointTypeDisplay", out val))
            {
                string s = val?.ToString();
                var def = FindPointType(s);
                if (def != null) return def.DisplayName;
            }
            return POINT_UNCLASSIFIED;
        }

        /// <summary>从要素属性读取 status / lineType，返回显示名称（未知则 LINE_UNCLASSIFIED）。</summary>
        public static string GetLineDisplayName(FeatureState fs)
        {
            object val = null;
            if (fs.Attributes != null && fs.Attributes.TryGetValue("lineType", out val))
            {
                var def = FindLineType(val?.ToString());
                if (def != null) return def.DisplayName;
            }
            if (fs.Attributes != null && fs.Attributes.TryGetValue("status", out val))
            {
                string s = val?.ToString();
                foreach (var st in RouteStatuses)
                    if (string.Equals(st.TypeCode, s, StringComparison.OrdinalIgnoreCase))
                        return st.DisplayName;
            }
            return LINE_UNCLASSIFIED;
        }

        public static System.Drawing.Color GetPointColor(string displayName)
        {
            foreach (var t in PointTypes)
                if (t.DisplayName == displayName) return t.Color;
            return System.Drawing.Color.FromArgb(255, 0x8B, 0x94, 0x9E);
        }

        public static System.Drawing.Color GetLineColor(string displayName)
        {
            foreach (var t in GeologicalLineTypes)
                if (t.DisplayName == displayName) return t.Color;
            foreach (var st in RouteStatuses)
                if (st.DisplayName == displayName) return st.Color;
            return System.Drawing.Color.FromArgb(255, 0x8B, 0x94, 0x9E);
        }

        /// <summary>按类别拆分点要素，返回 显示名 → 要素列表。</summary>
        public static Dictionary<string, List<FeatureState>> SplitPointsByType(List<FeatureState> points)
        {
            var groups = new Dictionary<string, List<FeatureState>>();
            foreach (var fs in points)
            {
                string key = GetPointDisplayName(fs);
                if (!groups.ContainsKey(key)) groups[key] = new List<FeatureState>();
                groups[key].Add(fs);
            }
            return groups;
        }

        /// <summary>按类别拆分散线要素，返回 显示名 → 要素列表。</summary>
        public static Dictionary<string, List<FeatureState>> SplitLinesByCategory(List<FeatureState> lines)
        {
            var groups = new Dictionary<string, List<FeatureState>>();
            foreach (var fs in lines)
            {
                string key = GetLineDisplayName(fs);
                if (!groups.ContainsKey(key)) groups[key] = new List<FeatureState>();
                groups[key].Add(fs);
            }
            return groups;
        }
    }

    public static class FieldDataHelper
    {
        public class ImportResult
        {
            public List<FeatureState> PointFeatures { get; set; } = new List<FeatureState>();
            public List<FeatureState> LineFeatures { get; set; } = new List<FeatureState>();
            public List<string> ExtractedPhotos { get; set; } = new List<string>();
            public List<string> ExtractedSketches { get; set; } = new List<string>();
            public List<string> ExtractedVideos { get; set; } = new List<string>();
            public string GeoJsonContent { get; set; }
            public string PhotoSourceDir { get; set; }
            public string SketchSourceDir { get; set; }
            public string VideoSourceDir { get; set; }
            public int TotalPoints => PointFeatures.Count;
            public int TotalLines => LineFeatures.Count;
            public int TotalPhotos => ExtractedPhotos.Count;
            public int TotalVideos => ExtractedVideos.Count;
        }

        public static ImportResult ProcessImportZip(string zipPath, string extractDir)
        {
            var result = new ImportResult();

            if (!File.Exists(zipPath))
                throw new FileNotFoundException("ZIP 文件不存在", zipPath);

            // Clean and recreate extract dir
            if (Directory.Exists(extractDir))
                try { Directory.Delete(extractDir, true); } catch { }
            Directory.CreateDirectory(extractDir);

            using (var fileStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    string destPath = Path.Combine(extractDir, entry.FullName);
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destPath);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }

            // Find GeoJSON
            var geoJsonFiles = Directory.GetFiles(extractDir, "*.geojson", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(extractDir, "*.json", SearchOption.AllDirectories))
                .ToList();

            if (geoJsonFiles.Count == 0)
                throw new Exception("ZIP 中未找到 GeoJSON 文件");

            string geoJsonPath = geoJsonFiles[0];
            result.GeoJsonContent = File.ReadAllText(geoJsonPath, Encoding.UTF8);

            // Parse features
            var allFeatures = GeoJsonHelper.ParseGeoJsonFeatures(result.GeoJsonContent);
            foreach (var f in allFeatures)
            {
                if (string.IsNullOrEmpty(f.GeometryJson)) continue;
                try
                {
                    var geom = Geometry.FromJson(f.GeometryJson);
                    if (geom.GeometryType == GeometryType.Point ||
                        geom.GeometryType == GeometryType.Multipoint)
                        result.PointFeatures.Add(f);
                    else if (geom.GeometryType == GeometryType.Polyline)
                        result.LineFeatures.Add(f);
                }
                catch { }
            }

            // Find photos
            var photosDir = Directory.GetDirectories(extractDir, "photos", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (photosDir != null)
            {
                result.ExtractedPhotos.AddRange(Directory.GetFiles(photosDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsImageFile(f)));
                result.PhotoSourceDir = photosDir;
            }

            // Find sketches
            var sketchesDir = Directory.GetDirectories(extractDir, "sketches", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (sketchesDir != null)
            {
                result.ExtractedSketches.AddRange(Directory.GetFiles(sketchesDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsImageFile(f)));
                result.SketchSourceDir = sketchesDir;
            }

            // Find videos
            var videosDir = Directory.GetDirectories(extractDir, "videos", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (videosDir != null)
            {
                result.ExtractedVideos.AddRange(Directory.GetFiles(videosDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsVideoFile(f)));
                result.VideoSourceDir = videosDir;
            }

            return result;
        }

        public static async Task<FeatureCollectionTable> CreateLayerFromFeatures(
            List<FeatureState> features, string layerName, GeometryType geomType,
            SpatialReference sr)
        {
            if (features.Count == 0) return null;

            // Collect all unique attribute keys
            var allKeys = new List<string>();
            var keySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in features)
                foreach (var k in f.Attributes.Keys)
                    if (keySet.Add(k)) allKeys.Add(k);

            // Build fields
            var fields = new List<Field>();
            fields.Add(new Field(FieldType.Text, "NAME", "名称", 255, null, true, true));
            fields.Add(new Field(FieldType.Text, "SourceID", "源ID", 255, null, true, true));
            foreach (var key in allKeys)
            {
                if (string.Equals(key, "NAME", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "SourceID", StringComparison.OrdinalIgnoreCase)) continue;
                object sample = null;
                foreach (var f in features)
                    if (f.Attributes.TryGetValue(key, out sample) && sample != null) break;
                FieldType ft = FieldType.Text;
                if (sample is double || sample is float) ft = FieldType.Float64;
                else if (sample is int || sample is long) ft = FieldType.Int32;
                else if (sample is bool) ft = FieldType.Text;
                int fieldLen = (ft == FieldType.Text) ? 4000 : 255;
                fields.Add(new Field(ft, key, key, fieldLen, null, true, true));
            }

            var table = new FeatureCollectionTable(fields, geomType, sr);
            foreach (var fs in features)
            {
                var feature = table.CreateFeature();
                if (!string.IsNullOrEmpty(fs.GeometryJson))
                    try { feature.Geometry = Geometry.FromJson(fs.GeometryJson); } catch { }

                string nameVal = null;
                if (fs.Attributes.TryGetValue("NAME", out object nv) && nv != null)
                    nameVal = nv.ToString();
                feature.Attributes["NAME"] = nameVal ?? layerName;

                string sourceId = null;
                if (fs.Attributes.TryGetValue("id", out object idVal) && idVal != null)
                    sourceId = idVal.ToString();
                else if (fs.Attributes.TryGetValue("customId", out object cidVal) && cidVal != null)
                    sourceId = cidVal.ToString();
                feature.Attributes["SourceID"] = sourceId ?? layerName;

                foreach (var kv in fs.Attributes)
                {
                    if (string.Equals(kv.Key, "NAME", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(kv.Key, "SourceID", StringComparison.OrdinalIgnoreCase)) continue;
                    if (feature.Attributes.ContainsKey(kv.Key))
                    {
                        try
                        {
                            object attrVal = kv.Value ?? DBNull.Value;
                            if (attrVal is bool bv)
                                attrVal = bv ? "true" : "false";
                            feature.Attributes[kv.Key] = attrVal;
                        }
                        catch (Exception attrEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CreateLayerFromFeatures] attribute set error for '{kv.Key}' = '{kv.Value}': {attrEx.Message}");
                        }
                    }
                }
                try { await table.AddFeatureAsync(feature); }
                catch (Exception addEx) { System.Diagnostics.Debug.WriteLine($"[CreateLayerFromFeatures] AddFeatureAsync failed: {addEx.Message}"); }
            }
            return table;
        }

        public static int CopyPhotosToAttachments(List<string> photoPaths, string projectPath, string pointIdPrefix = "")
        {
            var attachDir = Path.Combine(projectPath, "Attachments");
            if (!Directory.Exists(attachDir)) Directory.CreateDirectory(attachDir);

            int copied = 0;
            foreach (var src in photoPaths)
            {
                try
                {
                    string fileName = Path.GetFileName(src);
                    if (!string.IsNullOrEmpty(pointIdPrefix) && !fileName.StartsWith(pointIdPrefix))
                        fileName = $"{pointIdPrefix}_{fileName}";
                    string dest = Path.Combine(attachDir, fileName);
                    if (!File.Exists(dest))
                    {
                        File.Copy(src, dest, false);
                        copied++;
                    }
                }
                catch { }
            }
            return copied;
        }

        public static int CopySketchesToSketches(List<string> sketchPaths, string projectPath, string pointIdPrefix = "")
        {
            var sketchDir = Path.Combine(projectPath, "Sketches");
            if (!Directory.Exists(sketchDir)) Directory.CreateDirectory(sketchDir);

            int copied = 0;
            foreach (var src in sketchPaths)
            {
                try
                {
                    string fileName = Path.GetFileName(src);
                    if (!string.IsNullOrEmpty(pointIdPrefix) && !fileName.StartsWith(pointIdPrefix))
                        fileName = $"{pointIdPrefix}_{fileName}";
                    string dest = Path.Combine(sketchDir, fileName);
                    if (!File.Exists(dest))
                    {
                        File.Copy(src, dest, false);
                        copied++;
                    }
                }
                catch { }
            }
            return copied;
        }

        public static int CopyVideosToVideos(List<string> videoPaths, string projectPath, string pointIdPrefix = "")
        {
            var videoDir = Path.Combine(projectPath, "Videos");
            if (!Directory.Exists(videoDir)) Directory.CreateDirectory(videoDir);

            int copied = 0;
            foreach (var src in videoPaths)
            {
                try
                {
                    string fileName = Path.GetFileName(src);
                    if (!string.IsNullOrEmpty(pointIdPrefix) && !fileName.StartsWith(pointIdPrefix))
                        fileName = $"{pointIdPrefix}_{fileName}";
                    string dest = Path.Combine(videoDir, fileName);
                    if (!File.Exists(dest))
                    {
                        File.Copy(src, dest, false);
                        copied++;
                    }
                }
                catch { }
            }
            return copied;
        }

        public static int MatchPhotosToPoints(List<FeatureState> points, string photoDir, string attachDir)
        {
            if (!Directory.Exists(photoDir)) return 0;
            if (!Directory.Exists(attachDir)) Directory.CreateDirectory(attachDir);

            var photoFiles = Directory.GetFiles(photoDir, "*.*", SearchOption.AllDirectories)
                .Where(f => IsImageFile(f)).ToList();

            int matched = 0;
            foreach (var point in points)
            {
                string pid = point.Attributes.TryGetValue("id", out object id) ? id?.ToString() : "";
                if (string.IsNullOrEmpty(pid)) continue;

                // Find photos starting with point ID (e.g. "GP0001_xxx.jpg" or "GP0001.jpg")
                var matchingPhotos = photoFiles.Where(f =>
                {
                    string fn = Path.GetFileNameWithoutExtension(f);
                    return fn.Equals(pid, StringComparison.OrdinalIgnoreCase) ||
                           fn.StartsWith(pid + "_", StringComparison.OrdinalIgnoreCase);
                }).ToList();

                if (matchingPhotos.Count == 0) continue;

                var photoPaths = new List<string>();
                foreach (var mp in matchingPhotos)
                {
                    string destName = $"{pid}_{Path.GetFileName(mp)}";
                    string dest = Path.Combine(attachDir, destName);
                    try
                    {
                        if (!File.Exists(dest))
                            File.Copy(mp, dest, false);
                        photoPaths.Add(destName);
                        matched++;
                    }
                    catch { }
                }
                point.Attributes["PhotoFiles"] = string.Join(";", photoPaths);
                point.Attributes["PhotoCount"] = photoPaths.Count;
            }
            return matched;
        }

        public static List<FeatureState> FilterSamplePoints(List<FeatureState> allPoints)
        {
            return allPoints.Where(f =>
            {
                string type = f.Attributes.TryGetValue("pointType", out object pt) ? pt?.ToString() : "";
                string typeCode = f.Attributes.TryGetValue("typeCode", out object tc) ? tc?.ToString() : "";
                return type.IndexOf("样品", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       typeCode.IndexOf("SAMPLE", StringComparison.OrdinalIgnoreCase) >= 0;
            }).ToList();
        }

        public static string GenerateSampleCsv(List<FeatureState> samples, List<string> extraFields)
        {
            var sb = new StringBuilder();
            // Header
            var headers = new List<string> { "编号", "类型", "纬度", "经度", "高程", "描述", "照片数" };
            foreach (var f in extraFields)
                if (!headers.Contains(f)) headers.Add(f);
            sb.AppendLine(string.Join(",", headers.Select(h => $"\"{h}\"")));

            foreach (var s in samples)
            {
                var vals = new List<string>
                {
                    s.Attributes.TryGetValue("id", out var id) ? id?.ToString() : "",
                    s.Attributes.TryGetValue("pointType", out var pt) ? pt?.ToString() : "",
                    s.Attributes.TryGetValue("latitude", out var lat) ? lat?.ToString() : "",
                    s.Attributes.TryGetValue("longitude", out var lng) ? lng?.ToString() : "",
                    s.Attributes.TryGetValue("elevation", out var ele) ? ele?.ToString() : "",
                    s.Attributes.TryGetValue("description", out var desc) ? desc?.ToString()?.Replace("\"", "\"\"") : "",
                    s.Attributes.TryGetValue("PhotoCount", out var pc) ? pc?.ToString() : "0"
                };
                foreach (var f in extraFields)
                {
                    if (!headers.Take(7).Contains(f))
                        vals.Add(s.Attributes.TryGetValue(f, out var v) ? v?.ToString() : "");
                }
                sb.AppendLine(string.Join(",", vals.Select(v => $"\"{v}\"")));
            }
            return sb.ToString();
        }

        public static string DetectIdPrefix(List<FeatureState> features)
        {
            var ids = features
                .Select(f => f.Attributes.TryGetValue("id", out object id) ? id?.ToString() : "")
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
            if (ids.Count == 0) return "";
            // Find common prefix
            string first = ids[0];
            for (int len = first.Length; len > 0; len--)
            {
                string prefix = first.Substring(0, len);
                if (ids.All(id => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    return prefix;
            }
            return "";
        }

        private static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".tif" || ext == ".tiff";
        }

        private static bool IsVideoFile(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mkv" || ext == ".3gp" || ext == ".wmv";
        }
    }
}
