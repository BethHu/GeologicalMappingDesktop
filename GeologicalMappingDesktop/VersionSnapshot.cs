using System;
using System.Collections.Generic;
using System.Text;
using Esri.ArcGISRuntime.Geometry;

namespace GeologicalMappingDesktop
{
    public class VersionSnapshot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Creator { get; set; }
        public string Description { get; set; }
        public List<LayerVersionState> LayerStates { get; set; } = new List<LayerVersionState>();

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"Id\":\"{EscapeJson(Id)}\",");
            sb.Append($"\"Name\":\"{EscapeJson(Name)}\",");
            sb.Append($"\"CreatedAt\":\"{CreatedAt:yyyy-MM-dd HH:mm:ss}\",");
            sb.Append($"\"Creator\":\"{EscapeJson(Creator)}\",");
            sb.Append($"\"Description\":\"{EscapeJson(Description ?? "")}\",");
            sb.Append("\"LayerStates\":[");
            for (int i = 0; i < LayerStates.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(LayerStates[i].ToJson());
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static VersionSnapshot FromJson(string json, ref int pos)
        {
            var v = new VersionSnapshot();
            v.Id = ReadJsonString(json, ref pos, "Id");
            v.Name = ReadJsonString(json, ref pos, "Name");
            string dtStr = ReadJsonString(json, ref pos, "CreatedAt");
            DateTime.TryParse(dtStr, out DateTime dt);
            v.CreatedAt = dt;
            v.Creator = ReadJsonString(json, ref pos, "Creator");
            v.Description = ReadJsonString(json, ref pos, "Description");
            ReadJsonToToken(json, ref pos, "LayerStates");
            if (pos < json.Length && json[pos] == '[')
            {
                pos++;
                while (pos < json.Length && json[pos] != ']')
                {
                    if (json[pos] == '{')
                    {
                        v.LayerStates.Add(LayerVersionState.FromJson(json, ref pos));
                    }
                    else pos++;
                }
                if (pos < json.Length) pos++;
            }
            return v;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        private static string ReadJsonString(string json, ref int pos, string key)
        {
            string marker = "\"" + key + "\":\"";
            int idx = json.IndexOf(marker, pos);
            if (idx < 0) return "";
            idx += marker.Length;
            int end = idx;
            while (end < json.Length)
            {
                if (json[end] == '\\') end += 2;
                else if (json[end] == '"') break;
                else end++;
            }
            string val = json.Substring(idx, end - idx);
            pos = end + 1;
            return val.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
        }

        private static void ReadJsonToToken(string json, ref int pos, string key)
        {
            string marker = "\"" + key + "\":";
            int idx = json.IndexOf(marker, pos);
            if (idx >= 0)
            {
                pos = idx + marker.Length;
                while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
            }
        }
    }

    public class LayerVersionState
    {
        public string LayerName { get; set; }
        public string LayerType { get; set; }
        public string FilePath { get; set; }
        public int Wkid { get; set; }
        public string GeometryTypeName { get; set; }
        public List<FieldDef> Fields { get; set; } = new List<FieldDef>();
        public List<FeatureState> Features { get; set; } = new List<FeatureState>();

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"LayerName\":\"{Escape(LayerName)}\",");
            sb.Append($"\"LayerType\":\"{Escape(LayerType)}\",");
            sb.Append($"\"FilePath\":\"{Escape(FilePath)}\",");
            sb.Append($"\"Wkid\":{Wkid},");
            sb.Append($"\"GeometryTypeName\":\"{Escape(GeometryTypeName)}\",");
            sb.Append("\"Fields\":[");
            for (int i = 0; i < Fields.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{" +
                    $"\"Name\":\"{Escape(Fields[i].Name)}\"," +
                    $"\"FieldType\":\"{Fields[i].FieldType}\"," +
                    $"\"FieldTypeInt\":{(int)Fields[i].ArcGisFieldType}," +
                    $"\"Length\":{Fields[i].Length}}}");
            }
            sb.Append("],\"Features\":[");
            for (int i = 0; i < Features.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(Features[i].ToJson());
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static LayerVersionState FromJson(string json, ref int pos)
        {
            var lvs = new LayerVersionState();
            lvs.LayerName = ReadString(json, ref pos, "LayerName");
            lvs.LayerType = ReadString(json, ref pos, "LayerType");
            lvs.FilePath = ReadString(json, ref pos, "FilePath");
            string wkt = ReadInt(json, ref pos, "Wkid");
            int.TryParse(wkt, out int wkid);
            lvs.Wkid = wkid;
            lvs.GeometryTypeName = ReadString(json, ref pos, "GeometryTypeName");
            ReadToToken(json, ref pos, "Fields");
            if (pos < json.Length && json[pos] == '[')
            {
                pos++;
                while (pos < json.Length && json[pos] != ']')
                {
                    if (json[pos] == '{')
                    {
                        pos++;
                        var fd = new FieldDef();
                        fd.Name = ReadStringInline(json, ref pos, "Name");
                        fd.FieldType = ReadStringInline(json, ref pos, "FieldType");
                        string fi = ReadStringInline(json, ref pos, "FieldTypeInt");
                        int.TryParse(fi, out int fti);
                        fd.ArcGisFieldType = (Esri.ArcGISRuntime.Data.FieldType)fti;
                        string lenStr = ReadStringInline(json, ref pos, "Length");
                        int.TryParse(lenStr, out int len);
                        fd.Length = len;
                        lvs.Fields.Add(fd);
                        SkipToChar(json, ref pos, '}');
                        if (pos < json.Length) pos++;
                    }
                    else pos++;
                }
                if (pos < json.Length) pos++;
            }
            ReadToToken(json, ref pos, "Features");
            if (pos < json.Length && json[pos] == '[')
            {
                pos++;
                while (pos < json.Length && json[pos] != ']')
                {
                    if (json[pos] == '{')
                    {
                        lvs.Features.Add(FeatureState.FromJson(json, ref pos));
                    }
                    else pos++;
                }
                if (pos < json.Length) pos++;
            }
            SkipToChar(json, ref pos, '}');
            if (pos < json.Length) pos++;
            return lvs;
        }

        private static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        private static string ReadString(string json, ref int pos, string key)
        {
            string m = "\"" + key + "\":\"";
            int i = json.IndexOf(m, pos);
            if (i < 0) return "";
            i += m.Length;
            int e = i;
            while (e < json.Length && json[e] != '"') e++;
            string v = json.Substring(i, e - i);
            pos = e + 1;
            return v;
        }
        private static string ReadStringInline(string json, ref int pos, string key)
        {
            string m = "\"" + key + "\":";
            int i = json.IndexOf(m, pos);
            if (i < 0 || i > pos + 200) return "";
            i += m.Length;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            string v;
            if (i < json.Length && json[i] == '"')
            {
                i++;
                int e = i;
                while (e < json.Length)
                {
                    if (json[e] == '\\') e += 2;
                    else if (json[e] == '"') break;
                    else e++;
                }
                v = json.Substring(i, e - i);
                pos = e + 1;
            }
            else
            {
                int e = i;
                while (e < json.Length && json[e] != ',' && json[e] != '}') e++;
                v = json.Substring(i, e - i);
                pos = e;
            }
            return v;
        }
        private static string ReadInt(string json, ref int pos, string key)
        {
            string m = "\"" + key + "\":";
            int i = json.IndexOf(m, pos);
            if (i < 0) return "0";
            i += m.Length;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            int e = i;
            while (e < json.Length && (char.IsDigit(json[e]) || json[e] == '-')) e++;
            string v = json.Substring(i, e - i);
            pos = e;
            return v;
        }
        private static void ReadToToken(string json, ref int pos, string key)
        {
            string m = "\"" + key + "\":";
            int i = json.IndexOf(m, pos);
            if (i >= 0) { pos = i + m.Length; while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++; }
        }
        private static void SkipToChar(string json, ref int pos, char c)
        {
            while (pos < json.Length && json[pos] != c) pos++;
        }
    }

    public class FieldDef
    {
        public string Name { get; set; }
        public string FieldType { get; set; }
        public Esri.ArcGISRuntime.Data.FieldType ArcGisFieldType { get; set; }
        public int Length { get; set; }
    }

    public class FeatureState
    {
        public string GeometryJson { get; set; }
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"GeometryJson\":\"");
            sb.Append(Escape(GeometryJson ?? ""));
            sb.Append("\",\"Attributes\":{");
            bool first = true;
            foreach (var kv in Attributes)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{Escape(kv.Key ?? "")}\":");
                if (kv.Value == null || kv.Value == DBNull.Value)
                    sb.Append("null");
                else if (kv.Value is string s)
                    sb.Append($"\"{Escape(s)}\"");
                else if (kv.Value is double d)
                    sb.Append(d.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
                else if (kv.Value is int iv)
                    sb.Append(iv.ToString(System.Globalization.CultureInfo.InvariantCulture));
                else if (kv.Value is long lv)
                    sb.Append(lv.ToString(System.Globalization.CultureInfo.InvariantCulture));
                else if (kv.Value is float fv)
                    sb.Append(fv.ToString("G9", System.Globalization.CultureInfo.InvariantCulture));
                else if (kv.Value is bool bv)
                    sb.Append(bv ? "true" : "false");
                else if (kv.Value is DateTime dtv)
                    sb.Append($"\"{dtv:yyyy-MM-dd HH:mm:ss}\"");
                else
                    sb.Append($"\"{Escape(kv.Value.ToString())}\"");
            }
            sb.Append("}}");
            return sb.ToString();
        }

        public static FeatureState FromJson(string json, ref int pos)
        {
            var fs = new FeatureState();
            if (pos >= json.Length || json[pos] != '{') { SkipToChar(json, ref pos, '}'); if (pos < json.Length) pos++; return fs; }
            pos++;
            fs.GeometryJson = ReadFeatureString(json, ref pos, "GeometryJson");
            ReadToTokenInline(json, ref pos, "Attributes");
            if (pos < json.Length && json[pos] == '{')
            {
                pos++;
                while (pos < json.Length && json[pos] != '}')
                {
                    while (pos < json.Length && (char.IsWhiteSpace(json[pos]) || json[pos] == ',')) pos++;
                    if (pos >= json.Length || json[pos] == '}') break;
                    if (json[pos] == '"')
                    {
                        int ks = pos + 1;
                        int ke = json.IndexOf('"', ks);
                        string key = json.Substring(ks, ke - ks);
                        pos = ke + 1;
                        while (pos < json.Length && (char.IsWhiteSpace(json[pos]) || json[pos] == ':')) pos++;
                        object val = ReadValue(json, ref pos);
                        if (key != null && !key.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase))
                            fs.Attributes[key] = val;
                    }
                    else pos++;
                }
                if (pos < json.Length) pos++;
            }
            SkipToChar(json, ref pos, '}');
            if (pos < json.Length) pos++;
            return fs;
        }

        private static object ReadValue(string json, ref int pos)
        {
            if (pos >= json.Length) return null;
            if (json[pos] == '"')
            {
                pos++;
                int e = pos;
                while (e < json.Length)
                {
                    if (json[e] == '\\') e += 2;
                    else if (json[e] == '"') break;
                    else e++;
                }
                string s = json.Substring(pos, e - pos);
                pos = e + 1;
                return s.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            if (json[pos] == 'n' && json.Substring(pos).StartsWith("null")) { pos += 4; return null; }
            if (json[pos] == 't' && json.Substring(pos).StartsWith("true")) { pos += 4; return true; }
            if (json[pos] == 'f' && json.Substring(pos).StartsWith("false")) { pos += 5; return false; }
            int es = pos;
            while (es < json.Length && json[es] != ',' && json[es] != '}' && json[es] != ']') es++;
            string num = json.Substring(pos, es - pos).Trim();
            pos = es;
            if (double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dv))
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

        private static string ReadFeatureString(string json, ref int pos, string key)
        {
            string m = "\"" + key + "\":\"";
            int i = json.IndexOf(m, pos);
            if (i < 0 || i > pos + 300) return "";
            i += m.Length;
            int e = i;
            while (e < json.Length) { if (json[e] == '\\') e += 2; else if (json[e] == '"') break; else e++; }
            string v = json.Substring(i, e - i);
            pos = e + 1;
            return v.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
        private static void ReadToTokenInline(string json, ref int pos, string key)
        {
            string m = "\"" + key + "\":";
            int i = json.IndexOf(m, pos);
            if (i >= 0 && i <= pos + 300) { pos = i + m.Length; while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++; }
        }
        private static void SkipToChar(string json, ref int pos, char c)
        {
            while (pos < json.Length && json[pos] != c) pos++;
        }

        private static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public class VersionDiff
    {
        public string LayerName { get; set; }
        public int AddedCount { get { return Added?.Count ?? 0; } }
        public int ModifiedCount { get { return Modified?.Count ?? 0; } }
        public int DeletedCount { get { return Deleted?.Count ?? 0; } }
        public List<FeatureState> Added { get; set; } = new List<FeatureState>();
        public List<FeatureDiffEntry> Modified { get; set; } = new List<FeatureDiffEntry>();
        public List<FeatureState> Deleted { get; set; } = new List<FeatureState>();
    }

    public class FeatureDiffEntry
    {
        public FeatureState Before { get; set; }
        public FeatureState After { get; set; }
        public List<string> ChangedAttributes { get; set; } = new List<string>();
        public bool GeometryChanged { get; set; }
    }
}
