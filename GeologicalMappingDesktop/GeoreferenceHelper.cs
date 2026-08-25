using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeologicalMappingDesktop
{
    /// <summary>
    /// 地理配准变换：相似 / 仿射 / 二阶多项式，以及世界文件写出。
    /// 像素坐标系：col 向右增大，row 向下增大；(0,0) 为左上角像素中心。
    /// </summary>
    public static class GeoreferenceHelper
    {
        public class Gcp
        {
            public double Col, Row, MapX, MapY;
        }

        public class AffineParams
        {
            // x = A*col + B*row + C
            // y = D*col + E*row + F
            public double A, B, C, D, E, F;
        }

        public class Poly2Params
        {
            // x = a0 + a1*c + a2*r + a3*c*r + a4*c^2 + a5*r^2
            // y = b0 + b1*c + b2*r + b3*c*r + b4*c^2 + b5*r^2
            public double[] Ax = new double[6];
            public double[] Ay = new double[6];
        }

        public static int MinPoints(string transformType)
        {
            switch (transformType)
            {
                case "Similarity": return 2;
                case "Affine": return 3;
                case "Polynomial2": return 6;
                default: return 3;
            }
        }

        public static AffineParams SolveSimilarity(IList<Gcp> pts)
        {
            // 相似变换: x = s*cos* c + s*sin * r + tx  (+sin*r 因 row 向下, 等效于标准向上坐标中的 -sin*(-r))
            //           y = s*sin* c - s*cos * r + ty   （E 取 -cosS，因 row 向下增大而地图 Y 向上增大）
            // 最小二乘 4 参数
            int n = pts.Count;
            if (n < 2) return null;

            double sc = 0, sr = 0, sx = 0, sy = 0;
            foreach (var p in pts)
            {
                sc += p.Col; sr += p.Row; sx += p.MapX; sy += p.MapY;
            }
            double mc = sc / n, mr = sr / n, mx = sx / n, my = sy / n;

            double sxx = 0, sxy = 0, syx = 0, syy = 0, scc = 0, srr = 0;
            // 用复数/正交 Procrustes
            double sumC2R2 = 0;
            double sumXcYr = 0, sumYcXr = 0;
            foreach (var p in pts)
            {
                double dc = p.Col - mc, dr = p.Row - mr;
                double dx = p.MapX - mx, dy = p.MapY - my;
                sumC2R2 += dc * dc + dr * dr;
                sumXcYr += dx * dc - dy * dr;   // v·u where u=(dc,-dr)
                sumYcXr += -dx * dr - dy * dc; // v×u where u=(dc,-dr)
            }
            if (Math.Abs(sumC2R2) < 1e-20) return null;

            double cosS = sumXcYr / sumC2R2; // s*cos
            double sinS = sumYcXr / sumC2R2; // s*sin
            double tx = mx - cosS * mc - sinS * mr;
            double ty = my - sinS * mc + cosS * mr;

            // 像素坐标系 row 向下增大，地图坐标系 Y 向上增大，因此 E 取负值
            return new AffineParams
            {
                A = cosS,
                B = sinS,
                C = tx,
                D = sinS,
                E = -cosS,
                F = ty
            };
        }

        public static AffineParams SolveAffine(IList<Gcp> pts)
        {
            int n = pts.Count;
            if (n < 3) return null;

            // 正规方程 3x3 解 x 与 y 两组
            double[,] N = new double[3, 3];
            double[] bx = new double[3];
            double[] by = new double[3];

            foreach (var p in pts)
            {
                double[] v = { p.Col, p.Row, 1.0 };
                for (int i = 0; i < 3; i++)
                {
                    bx[i] += v[i] * p.MapX;
                    by[i] += v[i] * p.MapY;
                    for (int j = 0; j < 3; j++)
                        N[i, j] += v[i] * v[j];
                }
            }

            var cx = Solve3(N, bx);
            var cy = Solve3(N, by);
            if (cx == null || cy == null) return null;

            return new AffineParams
            {
                A = cx[0], B = cx[1], C = cx[2],
                D = cy[0], E = cy[1], F = cy[2]
            };
        }

        public static Poly2Params SolvePolynomial2(IList<Gcp> pts)
        {
            int n = pts.Count;
            if (n < 6) return null;

            // 6 参数正规方程
            double[,] N = new double[6, 6];
            double[] bx = new double[6];
            double[] by = new double[6];

            foreach (var p in pts)
            {
                double c = p.Col, r = p.Row;
                double[] v = { 1, c, r, c * r, c * c, r * r };
                for (int i = 0; i < 6; i++)
                {
                    bx[i] += v[i] * p.MapX;
                    by[i] += v[i] * p.MapY;
                    for (int j = 0; j < 6; j++)
                        N[i, j] += v[i] * v[j];
                }
            }

            var ax = SolveN(N, bx, 6);
            var ay = SolveN(N, by, 6);
            if (ax == null || ay == null) return null;

            return new Poly2Params { Ax = ax, Ay = ay };
        }

        public static void TransformAffine(AffineParams p, double col, double row, out double x, out double y)
        {
            x = p.A * col + p.B * row + p.C;
            y = p.D * col + p.E * row + p.F;
        }

        public static void TransformPoly2(Poly2Params p, double col, double row, out double x, out double y)
        {
            double c = col, r = row;
            double[] v = { 1, c, r, c * r, c * c, r * r };
            x = 0; y = 0;
            for (int i = 0; i < 6; i++)
            {
                x += p.Ax[i] * v[i];
                y += p.Ay[i] * v[i];
            }
        }

        public static double ComputeRmse(IList<Gcp> pts, string transformType, AffineParams affine, Poly2Params poly2)
        {
            if (pts == null || pts.Count == 0) return double.NaN;
            double sum = 0;
            int n = 0;
            foreach (var p in pts)
            {
                double px, py;
                if (transformType == "Polynomial2" && poly2 != null)
                    TransformPoly2(poly2, p.Col, p.Row, out px, out py);
                else if (affine != null)
                    TransformAffine(affine, p.Col, p.Row, out px, out py);
                else
                    continue;
                double dx = px - p.MapX;
                double dy = py - p.MapY;
                sum += dx * dx + dy * dy;
                n++;
            }
            if (n == 0) return double.NaN;
            return Math.Sqrt(sum / n);
        }

        public static double PointResidual(Gcp p, string transformType, AffineParams affine, Poly2Params poly2)
        {
            double px, py;
            if (transformType == "Polynomial2" && poly2 != null)
                TransformPoly2(poly2, p.Col, p.Row, out px, out py);
            else if (affine != null)
                TransformAffine(affine, p.Col, p.Row, out px, out py);
            else
                return double.NaN;
            double dx = px - p.MapX;
            double dy = py - p.MapY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>从世界文件读取仿射参数</summary>
        public static AffineParams ReadWorldFile(string rasterPath)
        {
            string wf = GetWorldFilePath(rasterPath);
            if (!File.Exists(wf)) return null;
            string[] lines;
            try { lines = File.ReadAllLines(wf); } catch { return null; }
            if (lines.Length < 6) return null;
            double[] vals = new double[6];
            for (int i = 0; i < 6; i++)
            {
                if (!double.TryParse(lines[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out vals[i]))
                    return null;
            }
            return new AffineParams { A = vals[0], D = vals[1], B = vals[2], E = vals[3], C = vals[4], F = vals[5] };
        }

        /// <summary>
        /// 将仿射参数写成世界文件（.tfw/.jgw/.pgw/.bpw）
        /// </summary>
        public static void WriteWorldFile(string rasterPath, AffineParams p)
        {
            string wf = GetWorldFilePath(rasterPath);
            // 世界文件 6 行：A, D, B, E, C, F（必用不变区域性，小数点用 . 而非 ,）
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            File.WriteAllText(wf,
                p.A.ToString("G17", ic) + Environment.NewLine +
                p.D.ToString("G17", ic) + Environment.NewLine +
                p.B.ToString("G17", ic) + Environment.NewLine +
                p.E.ToString("G17", ic) + Environment.NewLine +
                p.C.ToString("G17", ic) + Environment.NewLine +
                p.F.ToString("G17", ic) + Environment.NewLine);
        }

        public static string GetWorldFilePath(string rasterPath)
        {
            string dir = Path.GetDirectoryName(rasterPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(rasterPath);
            string ext = (Path.GetExtension(rasterPath) ?? "").ToLowerInvariant();
            string wext;
            switch (ext)
            {
                case ".tif":
                case ".tiff": wext = ".tfw"; break;
                case ".jpg":
                case ".jpeg": wext = ".jgw"; break;
                case ".png": wext = ".pgw"; break;
                case ".bmp": wext = ".bpw"; break;
                default: wext = ".wld"; break;
            }
            return Path.Combine(dir, name + wext);
        }

        public static string BackupWorldFile(string rasterPath)
        {
            string wf = GetWorldFilePath(rasterPath);
            if (!File.Exists(wf)) return null;
            string bak = wf + ".bak_georef";
            File.Copy(wf, bak, true);
            return bak;
        }

        public static void RestoreWorldFile(string rasterPath, string backupPath)
        {
            string wf = GetWorldFilePath(rasterPath);
            if (!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
            {
                File.Copy(backupPath, wf, true);
            }
            else if (File.Exists(wf))
            {
                try { File.Delete(wf); } catch { }
            }
        }

        /// <summary>
        /// 从仿射参数近似二阶多项式（用四角采样）用于写世界文件
        /// </summary>
        public static AffineParams ApproximateAffineFromPoly2(Poly2Params poly, int width, int height)
        {
            var samples = new List<Gcp>();
            int[] cols = { 0, width - 1, 0, width - 1, width / 2 };
            int[] rows = { 0, 0, height - 1, height - 1, height / 2 };
            for (int i = 0; i < cols.Length; i++)
            {
                TransformPoly2(poly, cols[i], rows[i], out double x, out double y);
                samples.Add(new Gcp { Col = cols[i], Row = rows[i], MapX = x, MapY = y });
            }
            return SolveAffine(samples);
        }

        /// <summary>获取 .aux.xml 文件路径（PAM 元数据，ArcGIS Runtime 100.x 通过此文件识别光栅坐标系）</summary>
        public static string GetAuxXmlPath(string rasterPath)
        {
            return rasterPath + ".aux.xml";
        }

        /// <summary>将坐标系写入 .aux.xml 文件，Runtime 100.x 读取此文件而非 .prj</summary>
        public static void WriteAuxXml(string rasterPath, int wkid)
        {
            string wkt = GetEsriWkt(wkid);
            WriteAuxXmlFromWkt(rasterPath, wkt);
        }

        /// <summary>将自定义 WKT 写入 .aux.xml 文件</summary>
        public static void WriteAuxXmlFromWkt(string rasterPath, string wkt)
        {
            if (string.IsNullOrEmpty(wkt)) return;
            string xml = $"<?xml version=\"1.0\"?>\r\n<PAMDataset>\r\n  <SRS>{wkt}</SRS>\r\n</PAMDataset>";
            File.WriteAllText(GetAuxXmlPath(rasterPath), xml);
        }

        private static double[] Solve3(double[,] A, double[] b)
        {
            // 拷贝
            double[,] m = new double[3, 4];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++) m[i, j] = A[i, j];
                m[i, 3] = b[i];
            }
            return GaussJordan(m, 3);
        }

        private static double[] SolveN(double[,] A, double[] b, int n)
        {
            double[,] m = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) m[i, j] = A[i, j];
                m[i, n] = b[i];
            }
            return GaussJordan(m, n);
        }

        private static double[] GaussJordan(double[,] a, int n)
        {
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                double max = Math.Abs(a[col, col]);
                for (int r = col + 1; r < n; r++)
                {
                    double v = Math.Abs(a[r, col]);
                    if (v > max) { max = v; pivot = r; }
                }
                if (max < 1e-18) return null;
                if (pivot != col)
                {
                    for (int c = 0; c <= n; c++)
                    {
                        double tmp = a[col, c];
                        a[col, c] = a[pivot, c];
                        a[pivot, c] = tmp;
                    }
                }
                double div = a[col, col];
                for (int c = 0; c <= n; c++) a[col, c] /= div;
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double f = a[r, col];
                    for (int c = 0; c <= n; c++) a[r, c] -= f * a[col, c];
                }
            }
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = a[i, n];
            return x;
        }

        /// <summary>获取与光栅文件关联的 .prj 文件路径</summary>
        public static string GetPrjFilePath(string rasterPath)
        {
            string dir = Path.GetDirectoryName(rasterPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(rasterPath);
            return Path.Combine(dir, name + ".prj");
        }

        /// <summary>将坐标系信息写入 .prj 文件（ESRI WKT 格式）</summary>
        public static void WritePrjFile(string rasterPath, int wkid)
        {
            string prjPath = GetPrjFilePath(rasterPath);
            string wkt = GetEsriWkt(wkid);
            if (!string.IsNullOrEmpty(wkt))
                File.WriteAllText(prjPath, wkt);
        }

        /// <summary>获取指定 WKID 的 ESRI WKT 字符串（公开方法，支持更多 CRS 动态生成）</summary>
        public static string GetEsriWkt(int wkid)
        {
            switch (wkid)
            {
                case 4326:
                    return "GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137.0,298.257223563]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]]";
                case 3857:
                    return "PROJCS[\"WGS_1984_Web_Mercator_Auxiliary_Sphere\",GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137.0,298.257223563]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Mercator_Auxiliary_Sphere\"],PARAMETER[\"False_Easting\",0.0],PARAMETER[\"False_Northing\",0.0],PARAMETER[\"Central_Meridian\",0.0],PARAMETER[\"Standard_Parallel_1\",0.0],PARAMETER[\"Auxiliary_Sphere_Type\",0.0],UNIT[\"Meter\",1.0]]";
                case 4490:
                    return "GEOGCS[\"GCS_China_Geodetic_Coordinate_System_2000\",DATUM[\"D_China_2000\",SPHEROID[\"CGCS2000\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]]";
                case 4610:
                    return "GEOGCS[\"GCS_Xian_1980\",DATUM[\"D_Xian_1980\",SPHEROID[\"Xian_1980\",6378140.0,298.257]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]]";
                case 4214:
                    return "GEOGCS[\"GCS_Beijing_1954\",DATUM[\"D_Beijing_1954\",SPHEROID[\"Krasovsky_1940\",6378245.0,298.3]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]]";
                default:
                    return GenerateEsriWkt(wkid);
            }
        }

        /// <summary>动态生成 ESRI WKT，支持 CGCS2000 高斯-克吕格 3 度带和 UTM 等</summary>
        private static string GenerateEsriWkt(int wkid)
        {
            if (wkid <= 0) return null;

            // CGCS2000 3-degree Gauss-Kruger zones: 4526 (zone 25, CM=75°E) ~ 4550 (zone 49, CM=147°E)
            if (wkid >= 4526 && wkid <= 4550)
            {
                int zone = wkid - 4501;
                double cm = 75.0 + (wkid - 4526) * 3.0;
                return $"PROJCS[\"CGCS2000_3_Degree_GK_Zone_{zone}\",GEOGCS[\"GCS_China_Geodetic_Coordinate_System_2000\",DATUM[\"D_China_2000\",SPHEROID[\"CGCS2000\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Gauss_Kruger\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",0.0],PARAMETER[\"Central_Meridian\",{cm:F1}],PARAMETER[\"Scale_Factor\",1.0],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]";
            }
            // CGCS2000 6-degree Gauss-Kruger zones: 4501~4508 (zones 13~20, CM=75~117)
            if (wkid >= 4501 && wkid <= 4508)
            {
                int zone = wkid - 4501 + 13;
                double cm = -180.0 + zone * 6.0;
                return $"PROJCS[\"CGCS2000_GK_Zone_{zone}\",GEOGCS[\"GCS_China_Geodetic_Coordinate_System_2000\",DATUM[\"D_China_2000\",SPHEROID[\"CGCS2000\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Gauss_Kruger\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",0.0],PARAMETER[\"Central_Meridian\",{cm:F1}],PARAMETER[\"Scale_Factor\",1.0],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]";
            }

            // WGS 84 / UTM North: 32601 ~ 32660
            if (wkid >= 32601 && wkid <= 32660)
            {
                int zone = wkid - 32600;
                double cm = -177 + (zone - 1) * 6;
                return $"PROJCS[\"WGS_1984_UTM_Zone_{zone}N\",GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137.0,298.257223563]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",0.0],PARAMETER[\"Central_Meridian\",{cm:F1}],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]";
            }

            // WGS 84 / UTM South: 32701 ~ 32760
            if (wkid >= 32701 && wkid <= 32760)
            {
                int zone = wkid - 32700;
                double cm = -177 + (zone - 1) * 6;
                return $"PROJCS[\"WGS_1984_UTM_Zone_{zone}S\",GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137.0,298.257223563]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",10000000.0],PARAMETER[\"Central_Meridian\",{cm:F1}],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]";
            }

            // Xian 1980 Gauss-Kruger zones (3-degree): 2360~2370 (need research)
            // Beijing 1954 GK zones: 21460~21500 (need research)

            return null;
        }
    }
}
