using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;

namespace GeologicalMappingDesktop
{
    internal static class ExportHelper
    {
        public static async Task ExportToShapefile(Layer layer, string outputPathWithoutExt)
        {
            var table = GetFeatureTable(layer);
            if (table == null)
                throw new InvalidOperationException("图层不是矢量图层，无法导出为 Shapefile");
            await ExportTableToShapefile(table, outputPathWithoutExt);
        }

        public static async Task ExportToShapefile(FeatureTable table, string outputPathWithoutExt, SpatialReference targetSr = null)
        {
            await ExportTableToShapefile(table, outputPathWithoutExt, targetSr);
        }

        private static async Task ExportTableToShapefile(FeatureTable table, string outputPathWithoutExt, SpatialReference targetSr = null)
        {
            var queryParams = new QueryParameters();
            var features = await table.QueryFeaturesAsync(queryParams);
            var featuresList = features.ToList();
            var fields = table.Fields;

            var shpPath = outputPathWithoutExt + ".shp";
            var shxPath = outputPathWithoutExt + ".shx";
            var dbfPath = outputPathWithoutExt + ".dbf";
            var prjPath = outputPathWithoutExt + ".prj";

            var geomType = table.GeometryType;
            int shpType = GeometryTypeToShapeType(geomType);

            var fieldCols = fields.Where(f => f.FieldType != FieldType.Geometry && f.FieldType != FieldType.OID).ToList();

            int[] fieldLens = new int[fieldCols.Count];
            for (int i = 0; i < fieldCols.Count; i++)
            {
                var f = fieldCols[i];
                switch (f.FieldType)
                {
                    case FieldType.Float64:
                    case FieldType.Float32: fieldLens[i] = 20; break;
                    case FieldType.Int32:
                    case FieldType.Int16: fieldLens[i] = 12; break;
                    case FieldType.Text: fieldLens[i] = Math.Min(f.Length > 0 ? f.Length : 50, 254); break;
                    case FieldType.Date: fieldLens[i] = 8; break;
                    default: fieldLens[i] = 50; break;
                }
            }
            int dbfRecordLen = 1 + fieldLens.Sum();

            using (var shpStream = new MemoryStream())
            using (var shxStream = new MemoryStream())
            using (var shpWriter = new BinaryWriter(shpStream))
            using (var shxWriter = new BinaryWriter(shxStream))
            {
                int[] contentLengths = new int[featuresList.Count];
                int totalRecords = 0;

                long[] recordOffsets = new long[featuresList.Count];

                for (int idx = 0; idx < featuresList.Count; idx++)
                {
                    var f = featuresList[idx];
                    var geom = f.Geometry;
                    if (geom == null) continue;

                    byte[] geomBytes = GeometryToShape(geom, shpType);
                    if (geomBytes == null) continue;

                    int contentWords = geomBytes.Length / 2;
                    contentLengths[idx] = contentWords;
                    totalRecords++;

                    recordOffsets[idx] = shpStream.Position;

                    // Record header (big-endian)
                    shpWriter.Write(ShpUtil.ReverseEndian(idx + 1)); // Record Number
                    shpWriter.Write(ShpUtil.ReverseEndian(contentWords)); // Content Length
                    shpWriter.Write(geomBytes);

                    // SHX: offset (in 16-bit words from file start) and content length (big-endian)
                    shxWriter.Write(ShpUtil.ReverseEndian(50 + (int)(recordOffsets[idx] / 2)));
                    shxWriter.Write(ShpUtil.ReverseEndian(contentWords));
                }

                int fileLen = (int)((shpStream.Length + 100) / 2);
                var shpHeader = BuildShpHeader(shpType, fileLen, featuresList);
                var shxHeader = BuildShpHeader(shpType, 50 + totalRecords * 4, featuresList);

                shpWriter.Flush(); shxWriter.Flush();

                File.WriteAllBytes(shpPath, shpHeader.Concat(shpStream.ToArray()).ToArray());
                File.WriteAllBytes(shxPath, shxHeader.Concat(shxStream.ToArray()).ToArray());
            }

            using (var dbfStream = new MemoryStream())
            using (var dbfWriter = new BinaryWriter(dbfStream))
            {
                WriteDbfHeader(dbfWriter, featuresList.Count, fieldCols, fieldLens, dbfRecordLen);

                byte[] attrBuf = new byte[dbfRecordLen];
                foreach (var f in featuresList)
                {
                    Array.Clear(attrBuf, 0, attrBuf.Length);
                    attrBuf[0] = 0x20;
                    int offset = 1;
                    for (int fi = 0; fi < fieldCols.Count; fi++)
                    {
                        var field = fieldCols[fi];
                        int flen = fieldLens[fi];
                        var val = f.Attributes[field.Name];
                        string strVal = "";
                        if (val != null && val != DBNull.Value)
                        {
                            switch (field.FieldType)
                            {
                                case FieldType.Float64:
                                case FieldType.Float32:
                                    strVal = string.Format("{0:F8}", Convert.ToDouble(val)); break;
                                case FieldType.Int32:
                                case FieldType.Int16:
                                    strVal = Convert.ToInt64(val).ToString(); break;
                                case FieldType.Date:
                                    strVal = Convert.ToDateTime(val).ToString("yyyyMMdd"); break;
                                default:
                                    strVal = val.ToString(); break;
                            }
                        }
                        var bytes = Encoding.ASCII.GetBytes(strVal.PadRight(flen).Substring(0, flen));
                        Array.Copy(bytes, 0, attrBuf, offset, bytes.Length);
                        offset += flen;
                    }
                    dbfWriter.Write(attrBuf);
                }

                dbfWriter.Flush();
                File.WriteAllBytes(dbfPath, dbfStream.ToArray());
            }

            WritePrj(prjPath, targetSr ?? table.SpatialReference);
        }

        public static async Task ExportToGeoJson(Layer layer, string outputPath)
        {
            var table = GetFeatureTable(layer);
            if (table == null) return;

            var queryParams = new QueryParameters();
            var features = await table.QueryFeaturesAsync(queryParams);

            var sb = new StringBuilder();
            sb.AppendLine("{ \"type\": \"FeatureCollection\", \"features\": [");
            bool first = true;
            foreach (var f in features)
            {
                if (!first) sb.AppendLine(",");
                first = false;
                sb.AppendLine("{ \"type\": \"Feature\", \"geometry\": " + GeometryToGeoJson(f.Geometry) + ", \"properties\": {");
                if (f.Attributes != null)
                {
                    var keys = f.Attributes.Keys.ToList();
                    int ai = 0;
                    foreach (var n in keys)
                    {
                        sb.Append("  " + EscapeJson(n) + ": " + FormatAttrJson(f.Attributes[n]));
                        if (ai < keys.Count - 1) sb.Append(",");
                        sb.AppendLine();
                        ai++;
                    }
                }
                sb.Append("} }");
            }
            sb.AppendLine();
            sb.AppendLine("] }");
            File.WriteAllText(outputPath, sb.ToString());
        }

        public static void ExportRaster(Layer layer, string outputPath)
        {
            if (layer is RasterLayer rl && rl.Raster != null)
            {
                var src = rl.Raster.Path;
                if (!string.IsNullOrEmpty(src) && File.Exists(src))
                    File.Copy(src, outputPath, true);
                else
                    throw new InvalidOperationException("无法找到栅格源文件");
            }
            else
                throw new InvalidOperationException("不是栅格图层");
        }

        private static FeatureTable GetFeatureTable(Layer layer)
        {
            if (layer is FeatureLayer fl)
                return fl.FeatureTable;
            var item = MainWindow.Instance?.LayerManager?.Layers?.FirstOrDefault(l => l.Layer == layer);
            return item?.MemoryFeatureTable;
        }

        private static int GeometryTypeToShapeType(GeometryType gt)
        {
            switch (gt)
            {
                case GeometryType.Point:
                case GeometryType.Multipoint: return 1;
                case GeometryType.Polyline: return 3;
                case GeometryType.Polygon: return 5;
                default: return -1;
            }
        }

        private static byte[] BuildShpHeader(int shpType, int fileLength, List<Feature> features)
        {
            byte[] h = new byte[100];

            // Bytes 0-3: File Code (big-endian)
            ShpUtil.WriteBE(h, 0, 9994);
            // Bytes 4-23: Unused (5 int32s)
            // Bytes 24-27: File Length in 16-bit words (big-endian)
            ShpUtil.WriteBE(h, 24, fileLength);
            // Bytes 28-31: Version (little-endian)
            ShpUtil.WriteLE(h, 28, 1000);
            // Bytes 32-35: Shape Type (little-endian)
            ShpUtil.WriteLE(h, 32, shpType);

            double xmin = double.MaxValue, ymin = double.MaxValue, xmax = double.MinValue, ymax = double.MinValue;
            foreach (var f in features)
            {
                var env = f.Geometry?.Extent;
                if (env != null)
                {
                    if (env.XMin < xmin) xmin = env.XMin;
                    if (env.YMin < ymin) ymin = env.YMin;
                    if (env.XMax > xmax) xmax = env.XMax;
                    if (env.YMax > ymax) ymax = env.YMax;
                }
            }
            if (xmin == double.MaxValue) { xmin = ymin = 0; xmax = ymax = 1; }

            // Bytes 36-67: Bounding Box (little-endian doubles)
            Buffer.BlockCopy(BitConverter.GetBytes(xmin), 0, h, 36, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(ymin), 0, h, 44, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(xmax), 0, h, 52, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(ymax), 0, h, 60, 8);
            // Bytes 68-83: Z range
            Buffer.BlockCopy(BitConverter.GetBytes(0.0), 0, h, 68, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(0.0), 0, h, 76, 8);
            // Bytes 84-99: M range
            Buffer.BlockCopy(BitConverter.GetBytes(0.0), 0, h, 84, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(0.0), 0, h, 92, 8);

            return h;
        }

        private static byte[] GeometryToShape(Geometry geom, int shpType)
        {
            if (geom == null) return null;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(shpType);

                if (shpType == 1)
                {
                    var point1 = geom as MapPoint ?? (geom as Multipoint)?.Points?.FirstOrDefault();
                    if (point1 == null) return null;
                    bw.Write(point1.X); bw.Write(point1.Y);
                }
                else if (shpType == 3 || shpType == 5)
                {
                    var env = geom.Extent;
                    bw.Write(env.XMin); bw.Write(env.YMin);
                    bw.Write(env.XMax); bw.Write(env.YMax);

                    var parts = new List<IReadOnlyList<MapPoint>>();
                    if (geom is Polyline pl)
                        foreach (var p in pl.Parts) parts.Add(p.Points.ToList());
                    else if (geom is Polygon pg)
                        foreach (var p in pg.Parts) parts.Add(p.Points.ToList());
                    else return null;

                    int nParts = parts.Count;
                    int nPoints = parts.Sum(p => p.Count);
                    bw.Write(nParts);
                    bw.Write(nPoints);
                    int ptIdx = 0;
                    foreach (var part in parts) { bw.Write(ptIdx); ptIdx += part.Count; }
                    foreach (var part in parts)
                        foreach (var pt in part) { bw.Write(pt.X); bw.Write(pt.Y); }
                }
                else return null;

                bw.Flush();
                return ms.ToArray();
            }
        }

        private static void WriteDbfHeader(BinaryWriter bw, int numRecords,
            List<Field> fields, int[] fieldLens, int recordLen)
        {
            bw.Write((byte)0x03);
            bw.Write((byte)0x00); bw.Write((byte)0x00); bw.Write((byte)0x00);
            bw.Write(numRecords);
            int headerLen = 32 + fields.Count * 32 + 1;
            bw.Write((short)headerLen);
            bw.Write((short)recordLen);
            bw.Write(new byte[20]);

            for (int i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                string name = f.Name.Length > 10 ? f.Name.Substring(0, 10) : f.Name;
                bw.Write(Encoding.ASCII.GetBytes(name.PadRight(11, '\0')));

                char ft = 'C';
                int dec = 0;
                switch (f.FieldType)
                {
                    case FieldType.Float64:
                    case FieldType.Float32: ft = 'N'; dec = 8; break;
                    case FieldType.Int32:
                    case FieldType.Int16: ft = 'N'; dec = 0; break;
                    case FieldType.Text: ft = 'C'; break;
                    case FieldType.Date: ft = 'D'; break;
                }
                bw.Write((byte)ft);
                bw.Write(new byte[4]);
                bw.Write((byte)fieldLens[i]);
                bw.Write((byte)dec);
                bw.Write(new byte[14]);
            }
            bw.Write((byte)0x0D);
        }

        private static void WritePrj(string path, SpatialReference sr)
        {
            if (sr == null) return;
            int wkid = sr.Wkid;
            if (wkid == 0) return;
            string wkt = GeoreferenceHelper.GetEsriWkt(wkid);
            if (string.IsNullOrEmpty(wkt)) return;
            File.WriteAllText(path, wkt);
        }

        private static string GeometryToGeoJson(Geometry geom)
        {
            if (geom == null) return "null";
            var sb = new StringBuilder();
            if (geom is MapPoint mpPt)
            {
                sb.Append("{ \"type\": \"Point\", \"coordinates\": [");
                sb.AppendFormat("{0:F8}, {1:F8}", mpPt.X, mpPt.Y);
                if (mpPt.HasZ) sb.AppendFormat(", {0:F8}", mpPt.Z);
                sb.Append("] }");
            }
            else if (geom is Multipoint mpt)
            {
                sb.Append("{ \"type\": \"MultiPoint\", \"coordinates\": [");
                sb.Append(string.Join(", ", mpt.Points.Select(p => $"[{p.X:F8}, {p.Y:F8}]")));
                sb.Append("] }");
            }
            else if (geom is Polyline pl)
            {
                if (pl.Parts.Count == 1)
                {
                    sb.Append("{ \"type\": \"LineString\", \"coordinates\": [");
                    sb.Append(string.Join(", ", pl.Parts[0].Points.Select(p => $"[{p.X:F8}, {p.Y:F8}]")));
                    sb.Append("] }");
                }
                else
                {
                    sb.Append("{ \"type\": \"MultiLineString\", \"coordinates\": [");
                    sb.Append(string.Join(", ", pl.Parts.Select(p =>
                        "[" + string.Join(", ", p.Points.Select(pt => $"[{pt.X:F8}, {pt.Y:F8}]")) + "]")));
                    sb.Append("] }");
                }
            }
            else if (geom is Polygon poly)
            {
                if (poly.Parts.Count == 1)
                {
                    sb.Append("{ \"type\": \"Polygon\", \"coordinates\": [");
                    sb.Append(string.Join(", ", poly.Parts[0].Points.Select(p => $"[{p.X:F8}, {p.Y:F8}]")));
                    sb.Append("] }");
                }
                else
                {
                    sb.Append("{ \"type\": \"MultiPolygon\", \"coordinates\": [");
                    sb.Append(string.Join(", ", poly.Parts.Select(p =>
                        "[" + string.Join(", ", p.Points.Select(pt => $"[{pt.X:F8}, {pt.Y:F8}]")) + "]")));
                    sb.Append("] }");
                }
            }
            else return "null";
            return sb.ToString();
        }

        private static string EscapeJson(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string FormatAttrJson(object val)
        {
            if (val == null || val == DBNull.Value) return "null";
            if (val is double || val is float || val is int || val is long || val is short)
                return Convert.ToString(val, CultureInfo.InvariantCulture);
            return EscapeJson(val.ToString());
        }
    }

    internal static class ShpUtil
    {
        public static int ReverseEndian(int v)
        {
            return (v << 24) | ((v & 0xFF00) << 8) | ((v >> 8) & 0xFF00) | (v >> 24);
        }

        public static void WriteBE(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)((value >> 24) & 0xFF);
            buf[offset + 1] = (byte)((value >> 16) & 0xFF);
            buf[offset + 2] = (byte)((value >> 8) & 0xFF);
            buf[offset + 3] = (byte)(value & 0xFF);
        }

        public static void WriteLE(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
