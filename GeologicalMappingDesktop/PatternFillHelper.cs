using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using DrawingColor = System.Drawing.Color;

namespace GeologicalMappingDesktop
{
    /// <summary>面填充符号层类型（仿 QGIS 符号层）</summary>
    public enum FillSymbolLayerKind
    {
        Solid = 0,
        Lines = 1,
        CrossHatch = 2,
        PointsGrid = 3,
        PointsRandom = 4,
        Arrows = 5,
        SvgOrImage = 6,
        Gradient = 7
    }

    /// <summary>单个填充符号层参数</summary>
    public class FillSymbolLayerDef
    {
        public FillSymbolLayerKind Kind { get; set; } = FillSymbolLayerKind.Solid;
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = "填充层";

        // 通用
        public DrawingColor Color { get; set; } = DrawingColor.FromArgb(180, 88, 166, 255);
        public double Opacity { get; set; } = 1.0; // 0~1
        public double Angle { get; set; } // 度
        public double Size { get; set; } = 12; // 点大小 / SVG 尺寸
        public double Spacing { get; set; } = 10;
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double LineWidth { get; set; } = 1.2;
        public double Density { get; set; } = 1.0; // 箭头密度
        public double Length { get; set; } = 10; // 箭头/线段长度
        public int Seed { get; set; } = 42;
        public string FilePath { get; set; }

        // 线样式图案：从线符号编辑器选择的样式层
        public ObservableCollection<LineSymbolLayerDef> PatternLineLayers { get; set; } = new ObservableCollection<LineSymbolLayerDef>();
        // 点样式图案：从点符号编辑器选择的样式层
        public ObservableCollection<PointSymbolLayerDef> PatternPointLayers { get; set; } = new ObservableCollection<PointSymbolLayerDef>();
        // 渐变填充
        public string GradientType { get; set; } = "Linear"; // Linear / Radial
        public DrawingColor GradientColor1 { get; set; } = DrawingColor.FromArgb(255, 88, 166, 255);
        public DrawingColor GradientColor2 { get; set; } = DrawingColor.FromArgb(255, 255, 255, 255);
        public double GradientAngle { get; set; } = 0;

        public FillSymbolLayerDef Clone()
        {
            var clone = (FillSymbolLayerDef)MemberwiseClone();
            clone.PatternLineLayers = new ObservableCollection<LineSymbolLayerDef>();
            foreach (var ll in PatternLineLayers) clone.PatternLineLayers.Add(ll.Clone());
            clone.PatternPointLayers = new ObservableCollection<PointSymbolLayerDef>();
            foreach (var pl in PatternPointLayers) clone.PatternPointLayers.Add(pl.Clone());
            return clone;
        }

        public string DisplayLabel
        {
            get
            {
                string k;
                switch (Kind)
                {
                    case FillSymbolLayerKind.Solid: k = "实心"; break;
                    case FillSymbolLayerKind.Lines: k = "平行线"; break;
                    case FillSymbolLayerKind.CrossHatch: k = "交叉线"; break;
                    case FillSymbolLayerKind.PointsGrid: k = "点阵"; break;
                    case FillSymbolLayerKind.PointsRandom: k = "随机点"; break;
                    case FillSymbolLayerKind.Arrows: k = "箭头"; break;
                    case FillSymbolLayerKind.SvgOrImage: k = "SVG/图"; break;
                    case FillSymbolLayerKind.Gradient: k = "渐变"; break;
                    default: k = Kind.ToString(); break;
                }
                return (Enabled ? "● " : "○ ") + k + (string.IsNullOrEmpty(Name) || Name == "填充层" ? "" : " · " + Name);
            }
        }
    }

    public enum PointSymbolLayerKind
    {
        SimpleMarker = 0,
        SvgMarker = 1,
        VectorField = 2,
        FontMarker = 3
    }

    public class PointSymbolLayerDef
    {
        public PointSymbolLayerKind Kind { get; set; } = PointSymbolLayerKind.SimpleMarker;
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = "点符号层";

        // 通用
        public DrawingColor Color { get; set; } = DrawingColor.FromArgb(255, 88, 166, 255);
        public double Size { get; set; } = 10;
        public double Angle { get; set; } = 0;
        public double Opacity { get; set; } = 1.0;

        // 锚点（0-1 归一化，默认 0.5=中心）
        public double AnchorX { get; set; } = 0.5;
        public double AnchorY { get; set; } = 0.5;

        // 偏移（像素）
        public double OffsetX { get; set; } = 0;
        public double OffsetY { get; set; } = 0;

        // 缩放倍率
        public double Scale { get; set; } = 1.0;

        // Simple Marker
        public string MarkerStyle { get; set; } = "Circle";
        public DrawingColor OutlineColor { get; set; } = DrawingColor.White;
        public double OutlineWidth { get; set; } = 0.5;

        // SVG Marker
        public string FilePath { get; set; } = "";

        // Vector Field
        public string DirectionField { get; set; } = "";
        public string MagnitudeField { get; set; } = "";

        // Font Marker
        public string FontFamily { get; set; } = "Arial";
        public string CharString { get; set; } = "★";

        // ShortLine / Arrow
        public double LineLength { get; set; } = 20; // 短直线长度 / 箭头尖端对齐偏移

        public override string ToString() => DisplayLabel;

        public PointSymbolLayerDef Clone()
        {
            return (PointSymbolLayerDef)MemberwiseClone();
        }

        public string DisplayLabel
        {
            get
            {
                string icon;
                string info;
                switch (Kind)
                {
                    case PointSymbolLayerKind.SimpleMarker:
                        switch (MarkerStyle)
                        {
                            case "Circle": icon = "●"; info = "实心圆"; break;
                            case "HollowCircle": icon = "○"; info = "空心圆"; break;
                            case "Triangle": icon = "▲"; info = "实心三角"; break;
                            case "HollowTriangle": icon = "△"; info = "空心三角"; break;
                            case "Cross": icon = "✚"; info = "十字"; break;
                            case "X": icon = "✕"; info = "叉"; break;
                            case "Star": icon = "★"; info = "星形"; break;
                            case "Diamond": icon = "◆"; info = "菱形"; break;
                            case "Square": icon = "■"; info = "正方形"; break;
                            case "Hexagon": icon = "⬢"; info = "六边形"; break;
                            case "ShortLine": icon = "━"; info = "短直线"; break;
                            case "Arrow": icon = "▶"; info = "箭头"; break;
                            default: icon = "●"; info = ""; break;
                        }
                        return (Enabled ? "" : "○ ") + icon + " " + info;
                    case PointSymbolLayerKind.SvgMarker:
                        string fn = string.IsNullOrEmpty(FilePath) ? "(无文件)" : System.IO.Path.GetFileName(FilePath);
                        return (Enabled ? "" : "○ ") + "🖼 " + fn;
                    case PointSymbolLayerKind.VectorField:
                        return (Enabled ? "" : "○ ") + "↗ 向量场[" + DirectionField + "/" + MagnitudeField + "]";
                    case PointSymbolLayerKind.FontMarker:
                        return (Enabled ? "" : "○ ") + "🔤 " + (CharString ?? "?") + " (" + FontFamily + ")";
                    default:
                        return (Enabled ? "● " : "○ ") + Kind.ToString();
                }
            }
        }
    }

    public enum LineSymbolLayerKind
    {
        SimpleStroke = 0,     // 实线/虚线/点划线/点线组合/哈希线
        MarkerLine = 1,       // 沿线等距放置点符号
        ArrowLine = 2,        // 线端或沿线添加箭头
        WaveLine = 3,         // 波浪线 (Geometry Generator)
        SawtoothLine = 4      // 锯齿线
    }

    public class LineSymbolLayerDef
    {
        public LineSymbolLayerKind Kind { get; set; } = LineSymbolLayerKind.SimpleStroke;
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = "线符号层";

        // 通用线属性
        public DrawingColor Color { get; set; } = DrawingColor.FromArgb(255, 88, 166, 255);
        public double Width { get; set; } = 2;
        public double Opacity { get; set; } = 1.0;
        public string StrokeStyle { get; set; } = "Solid"; // Solid|Dash|Dot|DashDot|DashDotDot|Hash
        public double DashSpacing { get; set; } = 5; // 虚线间距

        // 锚点（0-1 归一化，默认 0.5=中心）
        public double AnchorX { get; set; } = 0.5;
        public double AnchorY { get; set; } = 0.5;

        // 偏移（像素）
        public double OffsetX { get; set; } = 0;
        public double OffsetY { get; set; } = 0;

        // 缩放倍率
        public double Scale { get; set; } = 1.0;

        // MarkerLine: 沿线标记点符号（以点符号层列表存储，支持多层叠加）
        public ObservableCollection<PointSymbolLayerDef> MarkerPointLayers { get; set; } = new ObservableCollection<PointSymbolLayerDef>();
        public string MarkerStyle { get; set; } = "Circle"; // 点符号样式（简化为旧版兼容）
        public double MarkerSize { get; set; } = 6;
        public double MarkerInterval { get; set; } = 20; // 间距(像素)
        public string MarkerPlacement { get; set; } = "Along"; // Along|Begin|End|BeginAndEnd
        public DrawingColor MarkerColor { get; set; } = DrawingColor.FromArgb(255, 88, 166, 255);
        public string MarkerShape { get; set; } = "Circle"; // Circle|Triangle|Diamond|Cross|X|Square
        public double MarkerOutlineWidth { get; set; } = 0.5;
        public DrawingColor MarkerOutlineColor { get; set; } = DrawingColor.White;

        // ArrowLine: 箭头
        public string ArrowPlacement { get; set; } = "End"; // Begin|End|BeginAndEnd|Along
        public double ArrowSize { get; set; } = 10;

        // WaveLine / SawtoothLine
        public double WaveAmplitude { get; set; } = 8; // 波幅
        public double WaveLength { get; set; } = 20;   // 波长

        public override string ToString() => DisplayLabel;

        public LineSymbolLayerDef Clone()
        {
            var clone = (LineSymbolLayerDef)MemberwiseClone();
            clone.MarkerPointLayers = new ObservableCollection<PointSymbolLayerDef>();
            foreach (var pl in MarkerPointLayers)
                clone.MarkerPointLayers.Add(pl.Clone());
            return clone;
        }

        public string DisplayLabel
        {
            get
            {
                string info;
                switch (Kind)
                {
                    case LineSymbolLayerKind.SimpleStroke:
                        info = $"线({StrokeStyle} w={Width:F1})";
                        break;
                    case LineSymbolLayerKind.MarkerLine:
                        info = $"标记线({MarkerShape} @{MarkerInterval:F0}px)";
                        break;
                    case LineSymbolLayerKind.ArrowLine:
                        info = $"箭头({ArrowPlacement})";
                        break;
                    case LineSymbolLayerKind.WaveLine:
                        info = $"波浪线(A={WaveAmplitude:F0})";
                        break;
                    case LineSymbolLayerKind.SawtoothLine:
                        info = $"锯齿线(A={WaveAmplitude:F0})";
                        break;
                    default:
                        info = Kind.ToString();
                        break;
                }
                return (Enabled ? "" : "○ ") + info;
            }
        }
    }

    /// <summary>生成可平铺的岩性花纹位图，并构建 ArcGIS PictureFillSymbol / SimpleFillSymbol</summary>
    internal static class PatternFillHelper
    {
        public const int DefaultTileSize = 64;

        public static DrawingColor WithOpacity(DrawingColor c, double opacity)
        {
            int a = (int)Math.Max(0, Math.Min(255, Math.Round(255 * Math.Max(0, Math.Min(1, opacity)))));
            return DrawingColor.FromArgb(a, c.R, c.G, c.B);
        }

        public static Bitmap GenerateCompositeTile(IList<FillSymbolLayerDef> layers, int tileSize = DefaultTileSize)
        {
            tileSize = Math.Max(16, Math.Min(256, tileSize));
            var bmp = new Bitmap(tileSize, tileSize, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(DrawingColor.Transparent);
                if (layers == null) return bmp;
                // 先画底色再画花纹：索引 0 为最底层（底色），后续层依次叠加上去
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    if (layer == null || !layer.Enabled) continue;
                    DrawLayer(g, layer, tileSize);
                }
            }
            return bmp;
        }

        private static void DrawLayer(Graphics g, FillSymbolLayerDef def, int tile)
        {
            var color = WithOpacity(def.Color, def.Opacity);
            switch (def.Kind)
            {
                case FillSymbolLayerKind.Solid:
                    using (var b = new SolidBrush(color))
                        g.FillRectangle(b, (float)def.OffsetX, (float)def.OffsetY, tile, tile);
                    break;
                case FillSymbolLayerKind.Lines:
                    if (def.PatternLineLayers != null && def.PatternLineLayers.Any(l => l.Enabled))
                        DrawPatternLines(g, def.PatternLineLayers, def.Opacity, tile, def.Angle, def.Spacing, def.OffsetX, def.OffsetY);
                    else
                        DrawLines(g, color, tile, def.Angle, def.Spacing, def.LineWidth, def.OffsetX, def.OffsetY, false);
                    break;
                case FillSymbolLayerKind.CrossHatch:
                    if (def.PatternLineLayers != null && def.PatternLineLayers.Any(l => l.Enabled))
                    {
                        DrawPatternLines(g, def.PatternLineLayers, def.Opacity, tile, def.Angle, def.Spacing, def.OffsetX, def.OffsetY);
                        DrawPatternLines(g, def.PatternLineLayers, def.Opacity, tile, def.Angle + 90, def.Spacing, def.OffsetX, def.OffsetY);
                    }
                    else
                    {
                        DrawLines(g, color, tile, def.Angle, def.Spacing, def.LineWidth, def.OffsetX, def.OffsetY, false);
                        DrawLines(g, color, tile, def.Angle + 90, def.Spacing, def.LineWidth, def.OffsetX, def.OffsetY, false);
                    }
                    break;
                case FillSymbolLayerKind.PointsGrid:
                    if (def.PatternPointLayers != null && def.PatternPointLayers.Any(l => l.Enabled))
                        DrawPatternPoints(g, def.PatternPointLayers, def.Opacity, tile, def.Spacing, def.OffsetX, def.OffsetY, def.Angle, false, def.Seed);
                    else
                        DrawPoints(g, color, tile, def.Size, def.Spacing, def.OffsetX, def.OffsetY, def.Angle, false, def.Seed);
                    break;
                case FillSymbolLayerKind.PointsRandom:
                    if (def.PatternPointLayers != null && def.PatternPointLayers.Any(l => l.Enabled))
                        DrawPatternPoints(g, def.PatternPointLayers, def.Opacity, tile, def.Spacing, def.OffsetX, def.OffsetY, def.Angle, true, def.Seed);
                    else
                        DrawPoints(g, color, tile, def.Size, def.Spacing, def.OffsetX, def.OffsetY, def.Angle, true, def.Seed);
                    break;
                case FillSymbolLayerKind.Arrows:
                    DrawArrows(g, color, tile, def.Angle, def.Spacing, def.Length, def.LineWidth, def.Density, def.OffsetX, def.OffsetY);
                    break;
                case FillSymbolLayerKind.SvgOrImage:
                    DrawSvgOrImage(g, def, tile);
                    break;
                case FillSymbolLayerKind.Gradient:
                    DrawGradient(g, def, tile);
                    break;
            }
        }

        private static void DrawGradient(Graphics g, FillSymbolLayerDef def, int tile)
        {
            float ox = (float)def.OffsetX, oy = (float)def.OffsetY;
            float angle = (float)(def.GradientAngle + def.Angle);
            if (def.GradientType == "Radial")
            {
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(ox, oy, tile, tile);
                    using (var brush = new PathGradientBrush(path)
                    {
                        CenterColor = def.GradientColor1,
                        SurroundColors = new[] { def.GradientColor2 }
                    })
                    {
                        g.FillRectangle(brush, ox, oy, tile, tile);
                    }
                }
            }
            else
            {
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new System.Drawing.Rectangle((int)ox, (int)oy, tile, tile),
                    def.GradientColor1, def.GradientColor2, angle))
                {
                    g.FillRectangle(brush, ox, oy, tile, tile);
                }
            }
        }

        /// <summary>根据 PatternLineLayers 绘制平行线（每条线层作为一组平行线叠加）</summary>
        private static void DrawPatternLines(Graphics g, IEnumerable<LineSymbolLayerDef> layers, double parentOpacity,
            int tile, double angleDeg, double spacing, double ox, double oy)
        {
            double rad = angleDeg * Math.PI / 180.0;
            double dx = Math.Cos(rad), dy = Math.Sin(rad);
            double nx = -dy, ny = dx;
            spacing = Math.Max(2, spacing);
            double extent = tile * 2.5;
            int count = (int)Math.Ceiling(extent * 2 / spacing) + 4;
            double cx = tile / 2.0 + ox + 0.5, cy = tile / 2.0 + oy + 0.5;
            foreach (var layer in layers.Where(l => l.Enabled))
            {
                var lc = WithOpacity(layer.Color, layer.Opacity * parentOpacity);
                double lw = Math.Max(0.5, layer.Width * Math.Max(0.1, layer.Scale));
                using (var pen = new Pen(lc, (float)lw))
                {
                    pen.StartCap = LineCap.Flat;
                    pen.EndCap = LineCap.Flat;
                    // 处理虚线样式（使用 DashSpacing 参数）
                    switch (layer.StrokeStyle)
                    {
                        case "Dash":
                            {
                                float d = (float)Math.Max(2, layer.DashSpacing);
                                pen.DashPattern = new[] { d * 2, d };
                                break;
                            }
                        case "Dot":
                            {
                                float d = (float)Math.Max(2, layer.DashSpacing);
                                pen.DashPattern = new[] { 1f, d };
                                break;
                            }
                        case "DashDot":
                            {
                                float d = (float)Math.Max(2, layer.DashSpacing);
                                pen.DashPattern = new[] { d * 2, d, d / 2, d };
                                break;
                            }
                        case "DashDotDot":
                            {
                                float d = (float)Math.Max(2, layer.DashSpacing);
                                pen.DashPattern = new[] { d * 2, d, d / 2, d, d / 2, d };
                                break;
                            }
                    }
                    for (int i = -count; i <= count; i++)
                    {
                        double px = cx + nx * i * spacing;
                        double py = cy + ny * i * spacing;
                        g.DrawLine(pen, (float)(px - dx * extent), (float)(py - dy * extent),
                                      (float)(px + dx * extent), (float)(py + dy * extent));
                    }
                }
            }
        }

        /// <summary>根据 PatternPointLayers 在网格/随机位置上绘制多个点层</summary>
        private static void DrawPatternPoints(Graphics g, IEnumerable<PointSymbolLayerDef> layers, double parentOpacity,
            int tile, double spacing, double ox, double oy, double angleDeg, bool random, int seed)
        {
            spacing = Math.Max(3, spacing);
            var enabledLayers = layers.Where(l => l.Enabled).ToList();
            if (enabledLayers.Count == 0) return;
            double rad = angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            // 收集所有点位
            var points = new List<PointF>();
            if (random)
            {
                var rng = new Random(seed);
                int n = Math.Max(1, (int)((tile * tile) / (spacing * spacing)));
                for (int i = 0; i < n; i++)
                    points.Add(new PointF((float)(rng.NextDouble() * tile), (float)(rng.NextDouble() * tile)));
            }
            else
            {
                for (double row = -tile; row < tile * 2; row += spacing)
                {
                    for (double col = -tile; col < tile * 2; col += spacing)
                    {
                        double lx = col + ox, ly = row + oy;
                        double rx = lx * cos - ly * sin, ry = lx * sin + ly * cos;
                        if (rx >= 0 && rx < tile && ry >= 0 && ry < tile)
                        {
                            points.Add(new PointF((float)rx, (float)ry));
                            bool xr = rx + 16 > tile, xl = rx - 16 < 0;
                            bool yb = ry + 16 > tile, yt = ry - 16 < 0;
                            if (xr) points.Add(new PointF((float)(rx - tile), (float)ry));
                            if (xl) points.Add(new PointF((float)(rx + tile), (float)ry));
                            if (yb) points.Add(new PointF((float)rx, (float)(ry - tile)));
                            if (yt) points.Add(new PointF((float)rx, (float)(ry + tile)));
                            if (xr && yb) points.Add(new PointF((float)(rx - tile), (float)(ry - tile)));
                            if (xl && yb) points.Add(new PointF((float)(rx + tile), (float)(ry - tile)));
                            if (xr && yt) points.Add(new PointF((float)(rx - tile), (float)(ry + tile)));
                            if (xl && yt) points.Add(new PointF((float)(rx + tile), (float)(ry + tile)));
                        }
                    }
                }
            }
            // 在每个点位绘制所有点层
            foreach (var pt in points)
            {
                foreach (var layer in enabledLayers)
                {
                    var pc = WithOpacity(layer.Color, layer.Opacity * parentOpacity);
                    double sz = layer.Size * Math.Max(0.1, layer.Scale);
                    switch (layer.Kind)
                    {
                        case PointSymbolLayerKind.SimpleMarker:
                            var oc = WithOpacity(layer.OutlineColor, layer.Opacity * parentOpacity);
                            double ow = Math.Max(0.5, layer.OutlineWidth);
                            using (var p = new Pen(oc, (float)ow))
                            using (var b = new SolidBrush(pc))
                                g.FillEllipse(b, pt.X - (float)sz / 2, pt.Y - (float)sz / 2, (float)sz, (float)sz);
                            break;
                        default:
                            using (var b = new SolidBrush(pc))
                                g.FillEllipse(b, pt.X - (float)sz / 2, pt.Y - (float)sz / 2, (float)sz, (float)sz);
                            break;
                    }
                }
            }
        }

        private static void DrawLines(Graphics g, DrawingColor color, int tile, double angleDeg,
            double spacing, double lineWidth, double ox, double oy, bool unused)
        {
            spacing = Math.Max(2, spacing);
            lineWidth = Math.Max(0.5, lineWidth);
            double rad = angleDeg * Math.PI / 180.0;
            double dx = Math.Cos(rad);
            double dy = Math.Sin(rad);
            // 法向
            double nx = -dy;
            double ny = dx;
            using (var pen = new Pen(color, (float)lineWidth))
            {
                pen.StartCap = LineCap.Flat;
                pen.EndCap = LineCap.Flat;
                // 覆盖对角线范围，保证平铺无缝
                double extent = tile * 2.5;
                int count = (int)Math.Ceiling(extent * 2 / spacing) + 4;
                double cx = tile / 2.0 + ox + 0.5;
                double cy = tile / 2.0 + oy + 0.5;
                for (int i = -count; i <= count; i++)
                {
                    double px = cx + nx * i * spacing;
                    double py = cy + ny * i * spacing;
                    float x1 = (float)(px - dx * extent);
                    float y1 = (float)(py - dy * extent);
                    float x2 = (float)(px + dx * extent);
                    float y2 = (float)(py + dy * extent);
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            }
        }

        private static void DrawPoints(Graphics g, DrawingColor color, int tile, double size,
            double spacing, double ox, double oy, double angleDeg, bool random, int seed)
        {
            spacing = Math.Max(3, spacing);
            size = Math.Max(1, size);
            using (var brush = new SolidBrush(color))
            {
                if (random)
                {
                    var rng = new Random(seed);
                    int n = Math.Max(1, (int)((tile * tile) / (spacing * spacing)));
                    for (int i = 0; i < n; i++)
                    {
                        float x = (float)(rng.NextDouble() * tile);
                        float y = (float)(rng.NextDouble() * tile);
                        g.FillEllipse(brush, x - (float)size / 2, y - (float)size / 2, (float)size, (float)size);
                    }
                }
                else
                {
                    double rad = angleDeg * Math.PI / 180.0;
                    double cos = Math.Cos(rad);
                    double sin = Math.Sin(rad);
                for (double row = -tile; row < tile * 2; row += spacing)
                    {
                        for (double col = -tile; col < tile * 2; col += spacing)
                        {
                            double lx = col + ox;
                            double ly = row + oy;
                            double rx = lx * cos - ly * sin;
                            double ry = lx * sin + ly * cos;
                            if (rx >= 0 && rx < tile && ry >= 0 && ry < tile)
                            {
                                double half = size / 2;
                                g.FillEllipse(brush, (float)rx - (float)half, (float)ry - (float)half, (float)size, (float)size);
                                if (rx + half > tile) g.FillEllipse(brush, (float)(rx - tile) - (float)half, (float)ry - (float)half, (float)size, (float)size);
                                if (rx - half < 0)    g.FillEllipse(brush, (float)(rx + tile) - (float)half, (float)ry - (float)half, (float)size, (float)size);
                                if (ry + half > tile) g.FillEllipse(brush, (float)rx - (float)half, (float)(ry - tile) - (float)half, (float)size, (float)size);
                                if (ry - half < 0)    g.FillEllipse(brush, (float)rx - (float)half, (float)(ry + tile) - (float)half, (float)size, (float)size);
                            }
                        }
                    }
                }
            }
        }

        private static double Mod(double v, double m)
        {
            double r = v % m;
            return r < 0 ? r + m : r;
        }

        private static void DrawArrows(Graphics g, DrawingColor color, int tile, double angleDeg,
            double spacing, double length, double lineWidth, double density, double ox, double oy)
        {
            spacing = Math.Max(4, spacing / Math.Max(0.2, density));
            length = Math.Max(3, length);
            lineWidth = Math.Max(0.5, lineWidth);
            double rad = angleDeg * Math.PI / 180.0;
            double dx = Math.Cos(rad);
            double dy = Math.Sin(rad);
            double nx = -dy;
            double ny = dx;
            using (var pen = new Pen(color, (float)lineWidth))
            using (var brush = new SolidBrush(color))
            {
                double extent = tile * 2;
                int rows = (int)Math.Ceiling(extent * 2 / spacing) + 2;
                int cols = (int)Math.Ceiling(extent * 2 / Math.Max(spacing, length)) + 2;
                double cx = tile / 2.0 + ox;
                double cy = tile / 2.0 + oy;
                for (int r = -rows; r <= rows; r++)
                {
                    for (int c = -cols; c <= cols; c++)
                    {
                        double px = cx + nx * r * spacing + dx * c * Math.Max(spacing, length * 1.2);
                        double py = cy + ny * r * spacing + dy * c * Math.Max(spacing, length * 1.2);
                        float x0 = (float)(px - dx * length / 2);
                        float y0 = (float)(py - dy * length / 2);
                        float x1 = (float)(px + dx * length / 2);
                        float y1 = (float)(py + dy * length / 2);
                        g.DrawLine(pen, x0, y0, x1, y1);
                        // 箭头头部
                        double head = length * 0.35;
                        double hx1 = x1 - dx * head + nx * head * 0.4;
                        double hy1 = y1 - dy * head + ny * head * 0.4;
                        double hx2 = x1 - dx * head - nx * head * 0.4;
                        double hy2 = y1 - dy * head - ny * head * 0.4;
                        g.DrawLine(pen, x1, y1, (float)hx1, (float)hy1);
                        g.DrawLine(pen, x1, y1, (float)hx2, (float)hy2);
                    }
                }
            }
        }

        private static void DrawSvgOrImage(Graphics g, FillSymbolLayerDef def, int tile)
        {
            if (string.IsNullOrEmpty(def.FilePath) || !File.Exists(def.FilePath))
                return;

            string ext = Path.GetExtension(def.FilePath).ToLowerInvariant();
            double size = Math.Max(4, def.Size);
            double spacing = Math.Max(size + 2, def.Spacing);
            var color = WithOpacity(def.Color, def.Opacity);

            Bitmap motif = null;
            try
            {
                if (ext == ".svg")
                    motif = RenderSvgToBitmap(def.FilePath, (int)Math.Ceiling(size), color, def.Angle);
                else
                    motif = LoadAndTintImage(def.FilePath, (int)Math.Ceiling(size), color, def.Angle);

                if (motif == null) return;

                for (double y = def.OffsetY; y < tile + spacing; y += spacing)
                {
                    for (double x = def.OffsetX; x < tile + spacing; x += spacing)
                    {
                        g.DrawImage(motif, (float)x, (float)y, motif.Width, motif.Height);
                    }
                }
            }
            finally
            {
                motif?.Dispose();
            }
        }

        private static Bitmap LoadAndTintImage(string path, int size, DrawingColor tint, double angleDeg)
        {
            using (var src = Image.FromFile(path))
            {
                var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(DrawingColor.Transparent);
                    g.TranslateTransform(size / 2f, size / 2f);
                    g.RotateTransform((float)angleDeg);
                    g.TranslateTransform(-size / 2f, -size / 2f);
                    g.DrawImage(src, 0, 0, size, size);
                }
                // 简单着色：非透明像素乘 tint
                TintBitmap(bmp, tint);
                return bmp;
            }
        }

        private static void TintBitmap(Bitmap bmp, DrawingColor tint)
        {
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    var p = bmp.GetPixel(x, y);
                    if (p.A < 8) continue;
                    // 用亮度混合 tint
                    double lum = (p.R * 0.3 + p.G * 0.59 + p.B * 0.11) / 255.0;
                    int a = (p.A * tint.A) / 255;
                    bmp.SetPixel(x, y, DrawingColor.FromArgb(a,
                        (int)(tint.R * lum + tint.R * (1 - lum) * 0.15),
                        (int)(tint.G * lum + tint.G * (1 - lum) * 0.15),
                        (int)(tint.B * lum + tint.B * (1 - lum) * 0.15)));
                }
            }
        }

        /// <summary>轻量 SVG：支持 rect/circle/ellipse/line/polyline/polygon/path(M L Z) + fill/stroke</summary>
        public static Bitmap RenderSvgToBitmap(string svgPath, int size, DrawingColor tint, double angleDeg)
        {
            size = Math.Max(8, size);
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            try
            {
                var doc = XDocument.Load(svgPath);
                XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                double vbX = 0, vbY = 0, vbW = 100, vbH = 100;
                var vb = (string)doc.Root?.Attribute("viewBox");
                if (!string.IsNullOrEmpty(vb))
                {
                    var parts = Regex.Split(vb.Trim(), @"[\s,]+");
                    if (parts.Length >= 4)
                    {
                        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out vbX);
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out vbY);
                        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vbW);
                        double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out vbH);
                    }
                }
                else
                {
                    double.TryParse((string)doc.Root?.Attribute("width") ?? "100", NumberStyles.Float, CultureInfo.InvariantCulture, out vbW);
                    double.TryParse((string)doc.Root?.Attribute("height") ?? "100", NumberStyles.Float, CultureInfo.InvariantCulture, out vbH);
                    if (vbW <= 0) vbW = 100;
                    if (vbH <= 0) vbH = 100;
                }

                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(DrawingColor.Transparent);
                    g.TranslateTransform(size / 2f, size / 2f);
                    g.RotateTransform((float)angleDeg);
                    g.TranslateTransform(-size / 2f, -size / 2f);
                    float sx = (float)(size / vbW);
                    float sy = (float)(size / vbH);
                    g.ScaleTransform(sx, sy);
                    g.TranslateTransform((float)(-vbX), (float)(-vbY));

                    foreach (var el in doc.Root.Descendants())
                    {
                        string local = el.Name.LocalName.ToLowerInvariant();
                        var fill = ParseSvgColor(el.Attribute("fill")?.Value, tint, true);
                        var stroke = ParseSvgColor(el.Attribute("stroke")?.Value, tint, false);
                        float sw = ParseFloat(el.Attribute("stroke-width")?.Value, 1f);

                        if (local == "rect")
                        {
                            float x = ParseFloat(el.Attribute("x")?.Value, 0);
                            float y = ParseFloat(el.Attribute("y")?.Value, 0);
                            float w = ParseFloat(el.Attribute("width")?.Value, 0);
                            float h = ParseFloat(el.Attribute("height")?.Value, 0);
                            if (fill.A > 0)
                                using (var b = new SolidBrush(fill)) g.FillRectangle(b, x, y, w, h);
                            if (stroke.A > 0)
                                using (var p = new Pen(stroke, sw)) g.DrawRectangle(p, x, y, w, h);
                        }
                        else if (local == "circle")
                        {
                            float cx = ParseFloat(el.Attribute("cx")?.Value, 0);
                            float cy = ParseFloat(el.Attribute("cy")?.Value, 0);
                            float r = ParseFloat(el.Attribute("r")?.Value, 0);
                            if (fill.A > 0)
                                using (var b = new SolidBrush(fill)) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
                            if (stroke.A > 0)
                                using (var p = new Pen(stroke, sw)) g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
                        }
                        else if (local == "ellipse")
                        {
                            float cx = ParseFloat(el.Attribute("cx")?.Value, 0);
                            float cy = ParseFloat(el.Attribute("cy")?.Value, 0);
                            float rx = ParseFloat(el.Attribute("rx")?.Value, 0);
                            float ry = ParseFloat(el.Attribute("ry")?.Value, 0);
                            if (fill.A > 0)
                                using (var b = new SolidBrush(fill)) g.FillEllipse(b, cx - rx, cy - ry, rx * 2, ry * 2);
                            if (stroke.A > 0)
                                using (var p = new Pen(stroke, sw)) g.DrawEllipse(p, cx - rx, cy - ry, rx * 2, ry * 2);
                        }
                        else if (local == "line")
                        {
                            float x1 = ParseFloat(el.Attribute("x1")?.Value, 0);
                            float y1 = ParseFloat(el.Attribute("y1")?.Value, 0);
                            float x2 = ParseFloat(el.Attribute("x2")?.Value, 0);
                            float y2 = ParseFloat(el.Attribute("y2")?.Value, 0);
                            if (stroke.A == 0) stroke = tint;
                            using (var p = new Pen(stroke, sw)) g.DrawLine(p, x1, y1, x2, y2);
                        }
                        else if (local == "polyline" || local == "polygon")
                        {
                            var pts = ParsePoints(el.Attribute("points")?.Value);
                            if (pts.Length >= 2)
                            {
                                if (local == "polygon" && fill.A > 0)
                                    using (var b = new SolidBrush(fill)) g.FillPolygon(b, pts);
                                if (stroke.A > 0 || local == "polyline")
                                {
                                    if (stroke.A == 0) stroke = tint;
                                    using (var p = new Pen(stroke, sw))
                                    {
                                        if (local == "polygon") g.DrawPolygon(p, pts);
                                        else g.DrawLines(p, pts);
                                    }
                                }
                            }
                        }
                        else if (local == "path")
                        {
                            var pts = ParsePathToPoints(el.Attribute("d")?.Value);
                            if (pts.Length >= 2)
                            {
                                if (fill.A > 0)
                                    using (var b = new SolidBrush(fill)) g.FillPolygon(b, pts);
                                if (stroke.A > 0)
                                    using (var p = new Pen(stroke, sw)) g.DrawPolygon(p, pts);
                                else if (fill.A == 0)
                                    using (var p = new Pen(tint, sw)) g.DrawLines(p, pts);
                            }
                        }
                    }
                }
            }
            catch
            {
                // 回退：画一个着色方块
                using (var g = Graphics.FromImage(bmp))
                using (var b = new SolidBrush(tint))
                {
                    g.Clear(DrawingColor.Transparent);
                    g.FillEllipse(b, 2, 2, size - 4, size - 4);
                }
            }
            return bmp;
        }

        private static float ParseFloat(string s, float def)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;
            s = s.Trim().Replace("px", "").Replace("pt", "");
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return v;
            return def;
        }

        private static DrawingColor ParseSvgColor(string s, DrawingColor tint, bool isFill)
        {
            if (string.IsNullOrWhiteSpace(s) || s == "none")
                return isFill ? DrawingColor.Transparent : DrawingColor.Transparent;
            if (s == "currentColor" || s == "black")
                return tint;
            if (s.StartsWith("#") && s.Length >= 7)
            {
                try
                {
                    int r = Convert.ToInt32(s.Substring(1, 2), 16);
                    int g = Convert.ToInt32(s.Substring(3, 2), 16);
                    int b = Convert.ToInt32(s.Substring(5, 2), 16);
                    // 用 tint 的色相替换，保留亮度
                    return DrawingColor.FromArgb(tint.A, tint.R, tint.G, tint.B);
                }
                catch { }
            }
            return tint;
        }

        private static PointF[] ParsePoints(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new PointF[0];
            var nums = Regex.Matches(s, @"-?\d*\.?\d+(?:[eE][+-]?\d+)?")
                .Cast<Match>()
                .Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture))
                .ToList();
            var pts = new List<PointF>();
            for (int i = 0; i + 1 < nums.Count; i += 2)
                pts.Add(new PointF(nums[i], nums[i + 1]));
            return pts.ToArray();
        }

        private static PointF[] ParsePathToPoints(string d)
        {
            if (string.IsNullOrWhiteSpace(d)) return new PointF[0];
            var pts = new List<PointF>();
            float cx = 0, cy = 0;
            var tokens = Regex.Matches(d, @"[MmLlHhVvZz]|-?\d*\.?\d+(?:[eE][+-]?\d+)?")
                .Cast<Match>().Select(m => m.Value).ToList();
            char cmd = 'M';
            int i = 0;
            while (i < tokens.Count)
            {
                string t = tokens[i];
                if (t.Length == 1 && char.IsLetter(t[0]))
                {
                    cmd = t[0];
                    i++;
                    if (cmd == 'Z' || cmd == 'z')
                    {
                        if (pts.Count > 0) pts.Add(pts[0]);
                        continue;
                    }
                }
                try
                {
                    if (cmd == 'M' || cmd == 'L' || cmd == 'm' || cmd == 'l')
                    {
                        if (i + 1 >= tokens.Count) break;
                        float x = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                        float y = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                        if (cmd == 'm' || cmd == 'l') { x += cx; y += cy; }
                        cx = x; cy = y;
                        pts.Add(new PointF(x, y));
                        if (cmd == 'M') cmd = 'L';
                        if (cmd == 'm') cmd = 'l';
                    }
                    else if (cmd == 'H' || cmd == 'h')
                    {
                        float x = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                        if (cmd == 'h') x += cx;
                        cx = x;
                        pts.Add(new PointF(cx, cy));
                    }
                    else if (cmd == 'V' || cmd == 'v')
                    {
                        float y = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                        if (cmd == 'v') y += cy;
                        cy = y;
                        pts.Add(new PointF(cx, cy));
                    }
                    else
                    {
                        // 跳过未知命令的数值
                        if (i < tokens.Count && !char.IsLetter(tokens[i][0])) i++;
                        else break;
                    }
                }
                catch { break; }
            }
            return pts.ToArray();
        }

        public static byte[] BitmapToPngBytes(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        /// <summary>构建用于地图的填充符号；单层实心且无图案时用 SimpleFillSymbol</summary>
        public static async Task<Symbol> BuildFillSymbolAsync(
            IList<FillSymbolLayerDef> layers,
            DrawingColor outlineColor,
            double outlineWidth,
            int tileSize = DefaultTileSize)
        {
            SimpleLineSymbol outline = null;
            if (outlineWidth > 0.01)
                outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outlineColor, outlineWidth);

            var enabled = layers?.Where(l => l != null && l.Enabled).ToList() ?? new List<FillSymbolLayerDef>();
            // 纯单层实心 → SimpleFillSymbol
            if (enabled.Count == 1 && enabled[0].Kind == FillSymbolLayerKind.Solid)
            {
                var c = WithOpacity(enabled[0].Color, enabled[0].Opacity);
                return new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, c, outline);
            }
            // 无层 → 透明填充 + 轮廓
            if (enabled.Count == 0)
                return new SimpleFillSymbol(SimpleFillSymbolStyle.Null, DrawingColor.Transparent, outline);

            using (var tile = GenerateCompositeTile(enabled, tileSize))
            {
                var bytes = BitmapToPngBytes(tile);
                string tempPath = Path.Combine(Path.GetTempPath(), $"pattern_{Guid.NewGuid():N}.png");
                File.WriteAllBytes(tempPath, bytes);
                var pfs = new PictureFillSymbol(new Uri(tempPath))
                {
                    Width = tileSize,
                    Height = tileSize,
                    Angle = 0
                };
                if (outline != null)
                    pfs.Outline = outline;
                // PictureFillSymbol 从文件异步加载，不删除临时文件以保证后续渲染正常
                return pfs;
            }
        }

        /// <summary>生成五角星位图</summary>
        public static Bitmap GenerateStarBitmap(int size, DrawingColor color, DrawingColor outlineColor, double outlineWidth)
        {
            size = Math.Max(12, size);
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(DrawingColor.Transparent);
                float cx = size / 2f, cy = size / 2f;
                float r = size * 0.45f;
                float ri = r * 0.38f;
                var pts = new PointF[10];
                for (int i = 0; i < 10; i++)
                {
                    double angle = -Math.PI / 2 + i * Math.PI / 5;
                    float radius = (i % 2 == 0) ? r : ri;
                    pts[i] = new PointF(cx + (float)(Math.Cos(angle) * radius),
                                         cy + (float)(Math.Sin(angle) * radius));
                }
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddPolygon(pts);
                    if (outlineWidth > 0 && outlineColor.A > 0)
                    {
                        using (var pen = new Pen(outlineColor, Math.Max(0.5f, (float)outlineWidth)))
                            g.DrawPath(pen, path);
                    }
                    using (var brush = new SolidBrush(color))
                        g.FillPath(brush, path);
                }
            }
            return bmp;
        }

        /// <summary>生成六边形位图</summary>
        public static Bitmap GenerateHexagonBitmap(int size, DrawingColor color, DrawingColor outlineColor, double outlineWidth)
        {
            size = Math.Max(12, size);
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(DrawingColor.Transparent);
                float cx = size / 2f, cy = size / 2f;
                float r = size * 0.45f;
                var pts = new PointF[6];
                for (int i = 0; i < 6; i++)
                {
                    double angle = -Math.PI / 2 + i * Math.PI / 3;
                    pts[i] = new PointF(cx + (float)(Math.Cos(angle) * r),
                                         cy + (float)(Math.Sin(angle) * r));
                }
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddPolygon(pts);
                    if (outlineWidth > 0 && outlineColor.A > 0)
                    {
                        using (var pen = new Pen(outlineColor, Math.Max(0.5f, (float)outlineWidth)))
                            g.DrawPath(pen, path);
                    }
                    using (var brush = new SolidBrush(color))
                        g.FillPath(brush, path);
                }
            }
            return bmp;
        }

        /// <summary>生成产状符号（横线代表走向，箭头垂直于横线代表倾向/倾角）</summary>
        public static Bitmap GenerateStrikeDipBitmap(int size, DrawingColor color, double dipAngle = 45)
        {
            size = Math.Max(12, size);
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(DrawingColor.Transparent);
                float cx = size / 2f, cy = size / 2f;
                float len = size * 0.4f;
                float arrowLen = len * 0.6f;
                float penW = Math.Max(1.5f, size / 12f);
                using (var pen = new Pen(color, penW))
                {
                    pen.StartCap = LineCap.Flat;
                    pen.EndCap = LineCap.Flat;
                    // 走向线（水平）
                    g.DrawLine(pen, cx - len, cy, cx + len, cy);
                }
                // 倾向箭头（垂直向下 = dip 方向）
                float aw = penW * 1.2f;
                using (var pen = new Pen(color, aw))
                {
                    pen.StartCap = LineCap.Flat;
                    pen.EndCap = LineCap.Flat;
                    g.DrawLine(pen, cx, cy + 1, cx, cy + arrowLen);
                    // 箭头头部
                    float head = aw * 3.5f;
                    float tipY = cy + arrowLen;
                    using (var headPen = new Pen(color, aw * 0.8f))
                    {
                        headPen.StartCap = LineCap.Flat;
                        headPen.EndCap = LineCap.Flat;
                        g.DrawLine(headPen, cx - head, tipY - head * 1.2f, cx, tipY);
                        g.DrawLine(headPen, cx + head, tipY - head * 1.2f, cx, tipY);
                    }
                }
                // 在箭头旁标注倾角度数
                string dipText = $"{dipAngle:F0}°";
                using (var font = new Font(new System.Drawing.FontFamily("Arial"), Math.Max(5, size * 0.18f), System.Drawing.FontStyle.Regular))
                using (var brush = new SolidBrush(color))
                {
                    var s = g.MeasureString("00°", font);
                    g.DrawString(dipText, font, brush, cx + len * 0.5f, cy - s.Height / 2f);
                }
            }
            return bmp;
        }

        /// <summary>生成短直线位图（带锚点偏移）</summary>
        public static Bitmap GenerateShortLineBitmap(int size, DrawingColor color, double lineLength, double anchorX, double anchorY, double offsetX, double offsetY)
        {
            size = Math.Max(12, size);
            double penW = Math.Max(1.5, size / 10.0);
            // 为锚点/偏移留白
            int pad = (int)Math.Ceiling(Math.Abs(offsetX)) + (int)Math.Ceiling(Math.Abs(offsetY)) + 4;
            int bmpSz = size + pad * 2;
            var bmp = new Bitmap(bmpSz, bmpSz, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(DrawingColor.Transparent);
                float cx = (float)(bmpSz * anchorX + offsetX);
                float cy = (float)(bmpSz * anchorY + offsetY);
                float halfLen = (float)Math.Max(2, lineLength) / 2f;
                using (var pen = new Pen(color, (float)penW))
                {
                    pen.StartCap = LineCap.Flat;
                    pen.EndCap = LineCap.Flat;
                    g.DrawLine(pen, cx - halfLen, cy, cx + halfLen, cy);
                }
            }
            return bmp;
        }

        /// <summary>生成箭头位图（三角形/楔形，尖端朝右）</summary>
        public static Bitmap GenerateArrowBitmap(int size, DrawingColor color, double lineLength, double anchorX, double anchorY, double offsetX, double offsetY)
        {
            size = Math.Max(12, size);
            // 为锚点/偏移留白
            int pad = (int)Math.Ceiling(Math.Abs(offsetX)) + (int)Math.Ceiling(Math.Abs(offsetY)) + 4;
            int bmpSz = size + pad * 2;
            var bmp = new Bitmap(bmpSz, bmpSz, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(DrawingColor.Transparent);
                float cx = (float)(bmpSz * anchorX + offsetX);
                float cy = (float)(bmpSz * anchorY + offsetY);
                float len = (float)Math.Max(4, lineLength);
                float halfH = len * 0.35f;
                using (var brush = new SolidBrush(color))
                {
                    var pts = new PointF[]
                    {
                        new PointF(cx + len / 2, cy),         // 尖端（右侧）
                        new PointF(cx - len / 2, cy - halfH), // 左上
                        new PointF(cx - len / 2, cy + halfH)  // 左下
                    };
                    g.FillPolygon(brush, pts);
                }
            }
            return bmp;
        }

        public static Bitmap GenerateSimpleMarkerBitmap(int size, DrawingColor color, string markerStyle,
            double anchorX, double anchorY, double offsetX, double offsetY, double outlineWidth, DrawingColor outlineColor)
        {
            size = Math.Max(12, size);
            bool isHollow = markerStyle == "HollowCircle" || markerStyle == "HollowTriangle";
            int pad = (int)Math.Ceiling(Math.Abs(offsetX)) + (int)Math.Ceiling(Math.Abs(offsetY)) + 4;
            int bmpSz = size + pad * 2;
            var bmp = new Bitmap(bmpSz, bmpSz, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(DrawingColor.Transparent);
                float cx = (float)(bmpSz * anchorX + offsetX);
                float cy = (float)(bmpSz * anchorY + offsetY);
                float half = size * 0.5f;
                using (var brush = new SolidBrush(color))
                {
                    switch (markerStyle)
                    {
                        case "Circle":
                            float r = half * 0.8f;
                            g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
                            break;
                        case "HollowCircle":
                            r = half * 0.5f;
                            using (var pen = new Pen(outlineWidth > 0 ? outlineColor : color, (float)Math.Max(1, outlineWidth)))
                                g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                            break;
                        case "Square":
                            g.FillRectangle(brush, cx - half * 0.7f, cy - half * 0.7f, half * 1.4f, half * 1.4f);
                            break;
                        case "Triangle":
                        {
                            float tSz = half * 0.9f;
                            var tri = new PointF[] {
                                new PointF(cx, cy - tSz),
                                new PointF(cx - tSz * 0.866f, cy + tSz * 0.5f),
                                new PointF(cx + tSz * 0.866f, cy + tSz * 0.5f)
                            };
                            g.FillPolygon(brush, tri);
                            break;
                        }
                        case "HollowTriangle":
                        {
                            float tSz = half * 0.7f;
                            var tri = new PointF[] {
                                new PointF(cx, cy - tSz),
                                new PointF(cx - tSz * 0.866f, cy + tSz * 0.5f),
                                new PointF(cx + tSz * 0.866f, cy + tSz * 0.5f)
                            };
                            using (var pen = new Pen(outlineWidth > 0 ? outlineColor : color, (float)Math.Max(1, outlineWidth)))
                                g.DrawPolygon(pen, tri);
                            break;
                        }
                        case "Diamond":
                        {
                            float dSz = half * 0.8f;
                            var dia = new PointF[] {
                                new PointF(cx, cy - dSz),
                                new PointF(cx + dSz, cy),
                                new PointF(cx, cy + dSz),
                                new PointF(cx - dSz, cy)
                            };
                            g.FillPolygon(brush, dia);
                            break;
                        }
                        case "Cross":
                        {
                            float cW = half * 0.3f;
                            float cH = half * 0.8f;
                            g.FillRectangle(brush, cx - cW, cy - cH, cW * 2, cH * 2);
                            g.FillRectangle(brush, cx - cH, cy - cW, cH * 2, cW * 2);
                            break;
                        }
                        case "X":
                        {
                            float xSz = half * 0.7f;
                            var xPts = new PointF[] {
                                new PointF(cx - xSz, cy - xSz * 0.35f),
                                new PointF(cx - xSz * 0.35f, cy - xSz),
                                new PointF(cx, cy - xSz * 0.6f),
                                new PointF(cx + xSz * 0.35f, cy - xSz),
                                new PointF(cx + xSz, cy - xSz * 0.35f),
                                new PointF(cx + xSz * 0.6f, cy),
                                new PointF(cx + xSz, cy + xSz * 0.35f),
                                new PointF(cx + xSz * 0.35f, cy + xSz),
                                new PointF(cx, cy + xSz * 0.6f),
                                new PointF(cx - xSz * 0.35f, cy + xSz),
                                new PointF(cx - xSz, cy + xSz * 0.35f),
                                new PointF(cx - xSz * 0.6f, cy),
                            };
                            g.FillPolygon(brush, xPts);
                            break;
                        }
                    }
                }
                if (!isHollow && outlineWidth > 0)
                {
                    using (var pen = new Pen(outlineColor, (float)outlineWidth))
                    {
                        float r = half * 0.8f;
                        switch (markerStyle)
                        {
                            case "Circle": g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2); break;
                            case "Square": g.DrawRectangle(pen, cx - half * 0.7f, cy - half * 0.7f, half * 1.4f, half * 1.4f); break;
                            case "Triangle":
                                float tSz = half * 0.9f;
                                g.DrawPolygon(pen, new PointF[] { new PointF(cx, cy - tSz), new PointF(cx - tSz * 0.866f, cy + tSz * 0.5f), new PointF(cx + tSz * 0.866f, cy + tSz * 0.5f) });
                                break;
                            case "Diamond":
                                float dSz = half * 0.8f;
                                g.DrawPolygon(pen, new PointF[] { new PointF(cx, cy - dSz), new PointF(cx + dSz, cy), new PointF(cx, cy + dSz), new PointF(cx - dSz, cy) });
                                break;
                        }
                    }
                }
            }
            return bmp;
        }

        public static List<FillSymbolLayerDef> CreateDefaultLayers(DrawingColor fillColor)
        {
            return new List<FillSymbolLayerDef>
            {
                new FillSymbolLayerDef
                {
                    Kind = FillSymbolLayerKind.Solid,
                    Name = "底色",
                    Color = fillColor,
                    Opacity = fillColor.A / 255.0,
                    Enabled = true
                }
            };
        }
    }
}
