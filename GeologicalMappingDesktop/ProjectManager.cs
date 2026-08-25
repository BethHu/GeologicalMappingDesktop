using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;

namespace GeologicalMappingDesktop
{
    public static class AppLog
    {
        private static readonly object _lock = new object();
        private static string _file;

        private static string FilePath
        {
            get
            {
                if (_file == null)
                {
                    _file = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                        "GeologicalMappingDesktop_versions.log");
                }
                return _file;
            }
        }

        public static void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    System.IO.File.AppendAllText(FilePath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
                }
            }
            catch { }
        }

        public static void WriteException(string tag, Exception ex)
        {
            try
            {
                lock (_lock)
                {
                    System.IO.File.AppendAllText(FilePath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}" +
                        $"{ex}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }

    public class ProjectManager
    {
        public string ProjectPath { get; private set; }
        public string ProjectName { get; private set; }
        public bool IsProjectOpen { get; private set; }

        private List<string> loadedLayers = new List<string>();

        // ---- 版本管理 ----
        private const string VERSIONS_DIR = "versions";

        public string GetVersionsFilePath()
        {
            if (IsProjectOpen && !string.IsNullOrEmpty(ProjectPath))
                return Path.Combine(ProjectPath, VERSIONS_DIR, "versions.json");
            return Path.Combine(GetDefaultWorkspaceDir(), VERSIONS_DIR, "versions.json");
        }

        private string GetDefaultWorkspaceDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GeologicalMappingDesktop", "Workspace");
        }

        public List<VersionSnapshot> LoadVersions()
        {
            var result = new List<VersionSnapshot>();
            string vf = GetVersionsFilePath();
            if (!File.Exists(vf))
            {
                AppLog.Write($"[LoadVersions] 文件不存在: {vf} (项目已打开={IsProjectOpen})");
                return result;
            }
            try
            {
                string content = File.ReadAllText(vf);
                AppLog.Write($"[LoadVersions] 文件长度={content.Length}: {vf}");
                // Parse JSON array
                int pos = 0;
                while (pos < content.Length && char.IsWhiteSpace(content[pos])) pos++;
                if (pos < content.Length && content[pos] == '[') pos++;
                while (pos < content.Length)
                {
                    while (pos < content.Length && char.IsWhiteSpace(content[pos])) pos++;
                    if (pos >= content.Length || content[pos] == ']') break;
                    if (content[pos] == '{')
                    {
                        try
                        {
                            var v = VersionSnapshot.FromJson(content, ref pos);
                            if (v != null) result.Add(v);
                        }
                        catch (Exception ex)
                        {
                            // 单个版本损坏时跳过，不影响其它版本
                            AppLog.WriteException("[LoadVersions] 跳过损坏版本", ex);
                            while (pos < content.Length && content[pos] != '}') pos++;
                            if (pos < content.Length) pos++;
                        }
                    }
                    else pos++;
                }
                AppLog.Write($"[LoadVersions] 解析完成，共 {result.Count} 个版本");
            }
            catch (Exception ex)
            {
                AppLog.WriteException("[LoadVersions] 整体失败", ex);
            }
            return result;
        }

        public void SaveVersions(List<VersionSnapshot> versions)
        {
            string dir = Path.GetDirectoryName(GetVersionsFilePath());
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < versions.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(versions[i].ToJson());
            }
            sb.Append("]");
            string vf = GetVersionsFilePath();
            File.WriteAllText(vf, sb.ToString());
            AppLog.Write($"[SaveVersions] 已写入 {versions.Count} 个版本 -> {vf}");
        }

        public bool CreateNewProject(string projectName, string savePath)
        {
            try
            {
                ProjectPath = Path.Combine(savePath, projectName);
                ProjectName = projectName;

                if (!Directory.Exists(ProjectPath))
                {
                    Directory.CreateDirectory(ProjectPath);
                }

                CreateProjectStructure();
                SaveProjectFile(null, null);

                IsProjectOpen = true;
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"创建项目失败: {ex.Message}");
            }
        }

        private void CreateProjectStructure()
        {
            Directory.CreateDirectory(Path.Combine(ProjectPath, "data"));
            Directory.CreateDirectory(Path.Combine(ProjectPath, "layers"));
            Directory.CreateDirectory(Path.Combine(ProjectPath, "images"));
            Directory.CreateDirectory(Path.Combine(ProjectPath, "output"));
            Directory.CreateDirectory(Path.Combine(ProjectPath, "temp"));
        }

        public string GetProjectFilePath()
        {
            return Path.Combine(ProjectPath, $"{ProjectName}.gmp");
        }

        private string GetGeorefFilePath()
        {
            return Path.Combine(ProjectPath, $"{ProjectName}.georef");
        }

        public void SaveProjectFile(string georefLayerName, List<ControlPointData> controlPoints)
        {
            string projectFilePath = GetProjectFilePath();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            string cpJson = "null";
            if (controlPoints != null && controlPoints.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append("[");
                for (int i = 0; i < controlPoints.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    var cp = controlPoints[i];
                    sb.Append("{");
                    sb.Append($"\"SourceX\":{cp.SourceX:G17},\"SourceY\":{cp.SourceY:G17},");
                    sb.Append($"\"TargetX\":{cp.TargetX:G17},\"TargetY\":{cp.TargetY:G17},");
                    sb.Append($"\"Col\":{cp.Col:G17},\"Row\":{cp.Row:G17}");
                    sb.Append("}");
                }
                sb.Append("]");
                cpJson = sb.ToString();
            }

            string layerJson = georefLayerName != null
                ? $"\"{EscapeJson(georefLayerName)}\""
                : "null";

            string json = $@"{{
    ""Version"": ""1.0"",
    ""Name"": ""{EscapeJson(ProjectName)}"",
    ""Modified"": ""{now}"",
    ""GeorefLayer"": {layerJson},
    ""ControlPoints"": {cpJson},
    ""Layers"": [],
    ""MapExtent"": {{
        ""XMin"": 114.0,
        ""YMin"": 30.0,
        ""XMax"": 115.0,
        ""YMax"": 31.0
    }}
}}";
            File.WriteAllText(projectFilePath, json);
        }

        public bool OpenProject(string projectFilePath)
        {
            try
            {
                if (!File.Exists(projectFilePath))
                    throw new FileNotFoundException("项目文件不存在");

                string content = File.ReadAllText(projectFilePath);

                int nameStart = content.IndexOf("\"Name\"") + 8;
                int nameEnd = content.IndexOf("\"", nameStart);
                ProjectName = content.Substring(nameStart, nameEnd - nameStart);
                // Name value is after the colon and space: "Name": "value"
                // The above logic searches for the next quote after "\"Name\"" + 8 chars
                // This finds the value start but counting may be off.
                // Let's redo name parsing properly:
                string nameMarker = "\"Name\": \"";
                int ni = content.IndexOf(nameMarker);
                if (ni >= 0)
                {
                    ni += nameMarker.Length;
                    int ne = content.IndexOf("\"", ni);
                    if (ne > ni)
                        ProjectName = content.Substring(ni, ne - ni);
                }

                ProjectPath = Path.GetDirectoryName(projectFilePath);
                IsProjectOpen = true;
                AppLog.Write($"[OpenProject] 已打开项目: {projectFilePath} (目录={ProjectPath})");

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"打开项目失败: {ex.Message}");
            }
        }

        public class ControlPointData
        {
            public double SourceX, SourceY, TargetX, TargetY, Col, Row;
        }

        public string LoadGeorefLayerName()
        {
            if (!IsProjectOpen) return null;
            string path = GetProjectFilePath();
            if (!File.Exists(path)) return null;
            try
            {
                string content = File.ReadAllText(path);
                string marker = "\"GeorefLayer\": \"";
                int i = content.IndexOf(marker);
                if (i < 0) return null;
                i += marker.Length;
                int e = content.IndexOf("\"", i);
                if (e <= i) return null;
                return content.Substring(i, e - i);
            }
            catch { return null; }
        }

        public List<ControlPointData> LoadControlPoints()
        {
            if (!IsProjectOpen) return null;
            string path = GetProjectFilePath();
            if (!File.Exists(path)) return null;
            try
            {
                string content = File.ReadAllText(path);
                return ParseControlPointsJson(content);
            }
            catch { return null; }
        }

        private static List<ControlPointData> ParseControlPointsJson(string json)
        {
            var result = new List<ControlPointData>();
            string marker = "\"ControlPoints\":";
            int idx = json.IndexOf(marker);
            if (idx < 0) return result;
            idx += marker.Length;
            // skip whitespace
            while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
            if (idx >= json.Length || json[idx] != '[') return result;
            idx++; // skip [
            while (idx < json.Length)
            {
                while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
                if (idx >= json.Length || json[idx] == ']') break;
                if (json[idx] != '{') { idx++; continue; }
                idx++; // skip {
                var cp = new ControlPointData();
                while (idx < json.Length)
                {
                    while (idx < json.Length && (char.IsWhiteSpace(json[idx]) || json[idx] == ',')) idx++;
                    if (idx >= json.Length || json[idx] == '}' || json[idx] == ']') break;
                    // read key
                    if (json[idx] != '"') { idx++; continue; }
                    int ks = idx + 1;
                    int ke = json.IndexOf('"', ks);
                    if (ke < 0) break;
                    string key = json.Substring(ks, ke - ks);
                    idx = ke + 1;
                    while (idx < json.Length && (char.IsWhiteSpace(json[idx]) || json[idx] == ':')) idx++;
                    // read value
                    int vs = idx;
                    int ve = idx;
                    while (ve < json.Length && json[ve] != ',' && json[ve] != '}' && json[ve] != ']') ve++;
                    string valStr = json.Substring(vs, ve - vs).Trim();
                    idx = ve;
                    if (double.TryParse(valStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double val))
                    {
                        switch (key)
                        {
                            case "SourceX": cp.SourceX = val; break;
                            case "SourceY": cp.SourceY = val; break;
                            case "TargetX": cp.TargetX = val; break;
                            case "TargetY": cp.TargetY = val; break;
                            case "Col": cp.Col = val; break;
                            case "Row": cp.Row = val; break;
                        }
                    }
                }
                result.Add(cp);
                while (idx < json.Length && json[idx] != '}') idx++;
                idx++;
            }
            return result;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        public void CloseProject()
        {
            ProjectPath = null;
            ProjectName = null;
            IsProjectOpen = false;
            loadedLayers.Clear();
        }

        public List<string> GetSupportedLayerExtensions()
        {
            return new List<string>
            {
                ".shp",
                ".tif", ".tiff", ".jpg", ".jpeg", ".png", ".bmp",
                ".geodatabase"
            };
        }

        public string GetLayerType(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            if (ext == ".shp")
                return "vector";
            if (ext == ".tif" || ext == ".tiff" || ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp")
                return "raster";
            if (ext == ".geodatabase")
                return "geodatabase";
            return "unknown";
        }
    }
}