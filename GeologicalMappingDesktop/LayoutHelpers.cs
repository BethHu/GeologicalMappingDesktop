using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GeologicalMappingDesktop
{
    public enum LayoutItemType
    {
        MapFrame,
        Legend,
        ScaleBar,
        NorthArrow,
        TextItem,
        ImageItem,
        Rectangle,
        Line
    }

    public class LayoutItemData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public LayoutItemType Type { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 100;
        public double Height { get; set; } = 80;
        public double Rotation { get; set; }
        public string Name { get; set; } = "";
        public Dictionary<string, string> Props { get; set; } = new Dictionary<string, string>();

        // Map-specific properties (MapFrame type only)
        public string MapRef { get; set; } = "main";
        public double MapScale { get; set; } = 50000;
        public double MapCenterX { get; set; }
        public double MapCenterY { get; set; }
        public int MapWKID { get; set; }
        public double MapExtentXMin { get; set; }
        public double MapExtentYMin { get; set; }
        public double MapExtentXMax { get; set; }
        public double MapExtentYMax { get; set; }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            AppendStr(sb, "Id", Id);
            sb.Append(",");
            AppendStr(sb, "Type", Type.ToString());
            sb.Append(",");
            AppendNum(sb, "X", X);
            sb.Append(",");
            AppendNum(sb, "Y", Y);
            sb.Append(",");
            AppendNum(sb, "Width", Width);
            sb.Append(",");
            AppendNum(sb, "Height", Height);
            sb.Append(",");
            AppendNum(sb, "Rotation", Rotation);
            sb.Append(",");
            AppendStr(sb, "Name", Name);
            sb.Append(",");
            AppendStr(sb, "MapRef", MapRef);
            sb.Append(",");
            AppendNum(sb, "MapScale", MapScale);
            sb.Append(",");
            AppendNum(sb, "MapCenterX", MapCenterX);
            sb.Append(",");
            AppendNum(sb, "MapCenterY", MapCenterY);
            sb.Append(",");
            AppendNum(sb, "MapWKID", MapWKID);
            sb.Append(",");
            AppendNum(sb, "MapExtentXMin", MapExtentXMin);
            sb.Append(",");
            AppendNum(sb, "MapExtentYMin", MapExtentYMin);
            sb.Append(",");
            AppendNum(sb, "MapExtentXMax", MapExtentXMax);
            sb.Append(",");
            AppendNum(sb, "MapExtentYMax", MapExtentYMax);
            sb.Append(",\"Props\":{");
            bool first = true;
            foreach (var kvp in Props)
            {
                if (!first) sb.Append(",");
                first = false;
                AppendStr(sb, kvp.Key, kvp.Value);
            }
            sb.Append("}}");
            return sb.ToString();
        }

        private static void AppendStr(StringBuilder sb, string key, string val)
        {
            sb.AppendFormat("\"{0}\":\"{1}\"", EscapeJson(key), EscapeJson(val ?? ""));
        }

        private static void AppendNum(StringBuilder sb, string key, double val)
        {
            sb.AppendFormat("\"{0}\":{1:R}", EscapeJson(key), val);
        }

        internal static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }

    public class LayoutDocument
    {
        public double PageWidthMm { get; set; } = 841;
        public double PageHeightMm { get; set; } = 1189;
        public double Dpi { get; set; } = 300;
        public string Title { get; set; } = "未命名布局";
        public List<LayoutItemData> Items { get; set; } = new List<LayoutItemData>();

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            AppendStr(sb, "Title", Title);
            sb.Append(",");
            AppendNum(sb, "PageWidthMm", PageWidthMm);
            sb.Append(",");
            AppendNum(sb, "PageHeightMm", PageHeightMm);
            sb.Append(",");
            AppendNum(sb, "Dpi", Dpi);
            sb.Append(",\"Items\":[");
            for (int i = 0; i < Items.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(Items[i].ToJson());
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static LayoutDocument FromJson(string json)
        {
            var doc = new LayoutDocument();
            if (string.IsNullOrEmpty(json)) return doc;
            try
            {
                int pos = 0;
                SkipWs(json, ref pos);
                if (pos >= json.Length || json[pos] != '{') return doc;
                pos++;
                while (pos < json.Length && json[pos] != '}')
                {
                    SkipWs(json, ref pos);
                    var key = ParseStr(json, ref pos);
                    SkipWs(json, ref pos);
                    if (pos < json.Length && json[pos] == ':') pos++;
                    SkipWs(json, ref pos);
                    switch (key)
                    {
                        case "Title": doc.Title = ParseStr(json, ref pos); break;
                        case "PageWidthMm": doc.PageWidthMm = ParseNum(json, ref pos); break;
                        case "PageHeightMm": doc.PageHeightMm = ParseNum(json, ref pos); break;
                        case "Dpi": doc.Dpi = ParseNum(json, ref pos); break;
                        case "Items": doc.Items = ParseItems(json, ref pos); break;
                        default: SkipVal(json, ref pos); break;
                    }
                    SkipWs(json, ref pos);
                    if (pos < json.Length && json[pos] == ',') pos++;
                }
            }
            catch { }
            return doc;
        }

        private static List<LayoutItemData> ParseItems(string json, ref int pos)
        {
            var list = new List<LayoutItemData>();
            if (pos >= json.Length || json[pos] != '[') return list;
            pos++;
            while (pos < json.Length && json[pos] != ']')
            {
                SkipWs(json, ref pos);
                if (json[pos] == '{')
                {
                    var item = ParseItem(json, ref pos);
                    if (item != null) list.Add(item);
                }
                SkipWs(json, ref pos);
                if (pos < json.Length && json[pos] == ',') pos++;
            }
            if (pos < json.Length) pos++;
            return list;
        }

        private static LayoutItemData ParseItem(string json, ref int pos)
        {
            var item = new LayoutItemData();
            if (pos >= json.Length || json[pos] != '{') return null;
            pos++;
            while (pos < json.Length && json[pos] != '}')
            {
                SkipWs(json, ref pos);
                var key = ParseStr(json, ref pos);
                SkipWs(json, ref pos);
                if (pos < json.Length && json[pos] == ':') pos++;
                SkipWs(json, ref pos);
                switch (key)
                {
                    case "Id": item.Id = ParseStr(json, ref pos); break;
                    case "Type":
                        var ts = ParseStr(json, ref pos);
                        Enum.TryParse(ts, out LayoutItemType t);
                        item.Type = t;
                        break;
                    case "X": item.X = ParseNum(json, ref pos); break;
                    case "Y": item.Y = ParseNum(json, ref pos); break;
                    case "Width": item.Width = ParseNum(json, ref pos); break;
                    case "Height": item.Height = ParseNum(json, ref pos); break;
                    case "Rotation": item.Rotation = ParseNum(json, ref pos); break;
                    case "Name": item.Name = ParseStr(json, ref pos); break;
                    case "MapRef": item.MapRef = ParseStr(json, ref pos); break;
                    case "MapScale": item.MapScale = ParseNum(json, ref pos); break;
                    case "MapCenterX": item.MapCenterX = ParseNum(json, ref pos); break;
                    case "MapCenterY": item.MapCenterY = ParseNum(json, ref pos); break;
                    case "MapWKID": item.MapWKID = (int)ParseNum(json, ref pos); break;
                    case "MapExtentXMin": item.MapExtentXMin = ParseNum(json, ref pos); break;
                    case "MapExtentYMin": item.MapExtentYMin = ParseNum(json, ref pos); break;
                    case "MapExtentXMax": item.MapExtentXMax = ParseNum(json, ref pos); break;
                    case "MapExtentYMax": item.MapExtentYMax = ParseNum(json, ref pos); break;
                    case "Props": item.Props = ParseProps(json, ref pos); break;
                    default: SkipVal(json, ref pos); break;
                }
                SkipWs(json, ref pos);
                if (pos < json.Length && json[pos] == ',') pos++;
            }
            if (pos < json.Length) pos++;
            return item;
        }

        private static Dictionary<string, string> ParseProps(string json, ref int pos)
        {
            var dict = new Dictionary<string, string>();
            if (pos >= json.Length || json[pos] != '{') return dict;
            pos++;
            while (pos < json.Length && json[pos] != '}')
            {
                SkipWs(json, ref pos);
                var k = ParseStr(json, ref pos);
                SkipWs(json, ref pos);
                if (pos < json.Length && json[pos] == ':') pos++;
                SkipWs(json, ref pos);
                var v = ParseStr(json, ref pos);
                if (k != null) dict[k] = v ?? "";
                SkipWs(json, ref pos);
                if (pos < json.Length && json[pos] == ',') pos++;
            }
            if (pos < json.Length) pos++;
            return dict;
        }

        private static string ParseStr(string json, ref int pos)
        {
            SkipWs(json, ref pos);
            if (pos >= json.Length || json[pos] != '"') return "";
            pos++;
            var sb = new StringBuilder();
            while (pos < json.Length && json[pos] != '"')
            {
                if (json[pos] == '\\')
                {
                    pos++;
                    if (pos < json.Length)
                    {
                        switch (json[pos])
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            default: sb.Append(json[pos]); break;
                        }
                    }
                }
                else sb.Append(json[pos]);
                pos++;
            }
            if (pos < json.Length) pos++;
            return sb.ToString();
        }

        private static double ParseNum(string json, ref int pos)
        {
            SkipWs(json, ref pos);
            int s = pos;
            if (pos < json.Length && json[pos] == '-') pos++;
            while (pos < json.Length && (char.IsDigit(json[pos]) || json[pos] == '.' || json[pos] == 'e' || json[pos] == 'E' || json[pos] == '+' || json[pos] == '-'))
                pos++;
            if (pos > s && double.TryParse(json.Substring(s, pos - s), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
            return 0;
        }

        private static void SkipVal(string json, ref int pos)
        {
            SkipWs(json, ref pos);
            if (pos >= json.Length) return;
            if (json[pos] == '"') ParseStr(json, ref pos);
            else if (json[pos] == '{') { int d = 1; pos++; while (pos < json.Length && d > 0) { if (json[pos] == '{') d++; else if (json[pos] == '}') d--; pos++; } }
            else if (json[pos] == '[') { int d = 1; pos++; while (pos < json.Length && d > 0) { if (json[pos] == '[') d++; else if (json[pos] == ']') d--; pos++; } }
            else while (pos < json.Length && json[pos] != ',' && json[pos] != '}' && json[pos] != ']') pos++;
        }

        private static void SkipWs(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
        }

        private static void AppendStr(StringBuilder sb, string key, string val)
        {
            sb.AppendFormat("\"{0}\":\"{1}\"", LayoutItemData.EscapeJson(key), LayoutItemData.EscapeJson(val ?? ""));
        }

        private static void AppendNum(StringBuilder sb, string key, double val)
        {
            sb.AppendFormat("\"{0}\":{1:R}", LayoutItemData.EscapeJson(key), val);
        }
    }

    internal static class LayoutMath
    {
        public const double ScreenDpi = 96.0;
        public const double MmPerInch = 25.4;

        public static double MmToPx(double mm) => mm * ScreenDpi / MmPerInch;
        public static double PxToMm(double px) => px * MmPerInch / ScreenDpi;

        public static void GetPageSize(string sizeName, string orientation, out double wMm, out double hMm)
        {
            double a, b;
            switch (sizeName)
            {
                case "A0": a = 841; b = 1189; break;
                case "A1": a = 594; b = 841; break;
                case "A2": a = 420; b = 594; break;
                case "A3": a = 297; b = 420; break;
                case "A4": a = 210; b = 297; break;
                default: a = 841; b = 1189; break;
            }
            if (orientation == "横向")
            {
                wMm = Math.Max(a, b);
                hMm = Math.Min(a, b);
            }
            else
            {
                wMm = Math.Min(a, b);
                hMm = Math.Max(a, b);
            }
        }

        public static string FormatScale(double scale)
        {
            if (scale <= 0) return "1:?";
            if (scale >= 1000000) return $"1:{scale / 1000000:F0}万";
            if (scale >= 1000) return $"1:{scale / 1000:F0}千";
            return $"1:{scale:F0}";
        }
    }
}
