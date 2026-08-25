using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using Esri.ArcGISRuntime.Symbology;
using DrawingColor = System.Drawing.Color;

namespace GeologicalMappingDesktop
{
    public class PointSymbolPatternDef
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string MarkerStyle { get; set; } = "Circle";
        public DrawingColor Color { get; set; } = DrawingColor.Black;
        public double Size { get; set; } = 10;
        public string Description { get; set; }
    }

    internal static class PointSymbolPatternManager
    {
        public static List<PointSymbolPatternDef> DefaultPatterns { get; } = CreateDefaultPatterns();

        public static PointSymbolPatternDef FindByCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            return DefaultPatterns.Find(p => string.Equals(p.Code, code.Trim(), System.StringComparison.OrdinalIgnoreCase));
        }

        public static Symbol BuildPointSymbol(PointSymbolPatternDef def)
        {
            if (def == null) return null;
            if (string.Equals(def.MarkerStyle, "Star", System.StringComparison.OrdinalIgnoreCase))
                return BuildStarSymbol(def.Color, def.Size);
            return new SimpleMarkerSymbol(ParseMarkerStyle(def.MarkerStyle), def.Color, def.Size);
        }

        public static PointSymbolLayerDef ToLayerDef(PointSymbolPatternDef def)
        {
            return new PointSymbolLayerDef
            {
                Kind = PointSymbolLayerKind.SimpleMarker,
                MarkerStyle = def.MarkerStyle ?? "Circle",
                Color = def.Color,
                Size = def.Size,
                Enabled = true,
                Name = def.Name
            };
        }

        private static SimpleMarkerSymbolStyle ParseMarkerStyle(string style)
        {
            switch (style?.ToLowerInvariant())
            {
                case "square": return SimpleMarkerSymbolStyle.Square;
                case "diamond": return SimpleMarkerSymbolStyle.Diamond;
                case "triangle": return SimpleMarkerSymbolStyle.Triangle;
                case "cross": return SimpleMarkerSymbolStyle.Cross;
                case "x": return SimpleMarkerSymbolStyle.X;
                default: return SimpleMarkerSymbolStyle.Circle;
            }
        }

        private static PictureMarkerSymbol BuildStarSymbol(DrawingColor color, double size)
        {
            sbyte sz = (sbyte)System.Math.Max(12, (int)size);
            using (var bmp = PatternFillHelper.GenerateStarBitmap(sz, color, DrawingColor.Transparent, 0))
            {
                string tempPath = Path.Combine(Path.GetTempPath(), $"star_{Guid.NewGuid():N}.png");
                bmp.Save(tempPath, ImageFormat.Png);
                try
                {
                    return new PictureMarkerSymbol(new System.Uri(tempPath)) { Width = size, Height = size };
                }
                catch
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    throw;
                }
            }
        }

        private static List<PointSymbolPatternDef> CreateDefaultPatterns()
        {
            return new List<PointSymbolPatternDef>
            {
                new PointSymbolPatternDef
                {
                    Code = "Fe",
                    Name = "铁矿",
                    MarkerStyle = "Cross",
                    Color = DrawingColor.FromArgb(20, 20, 20),
                    Size = 14,
                    Description = "铁矿（深黑十字）"
                },
                new PointSymbolPatternDef
                {
                    Code = "Cu",
                    Name = "铜矿",
                    MarkerStyle = "Circle",
                    Color = DrawingColor.FromArgb(220, 140, 40),
                    Size = 14,
                    Description = "铜矿（古铜色实心圆）"
                },
                new PointSymbolPatternDef
                {
                    Code = "Au",
                    Name = "金矿",
                    MarkerStyle = "Square",
                    Color = DrawingColor.FromArgb(255, 215, 0),
                    Size = 14,
                    Description = "金矿（金色实心方）"
                },
                new PointSymbolPatternDef
                {
                    Code = "Coal",
                    Name = "煤矿",
                    MarkerStyle = "Circle",
                    Color = DrawingColor.FromArgb(40, 40, 40),
                    Size = 12,
                    Description = "煤矿（黑色实心圆）"
                },
                new PointSymbolPatternDef
                {
                    Code = "PbZn",
                    Name = "铅锌矿",
                    MarkerStyle = "Diamond",
                    Color = DrawingColor.FromArgb(100, 100, 100),
                    Size = 14,
                    Description = "铅锌矿（灰色菱形）"
                },
                new PointSymbolPatternDef
                {
                    Code = "W",
                    Name = "钨矿",
                    MarkerStyle = "Triangle",
                    Color = DrawingColor.FromArgb(60, 120, 200),
                    Size = 16,
                    Description = "钨矿（蓝色三角）"
                },
                new PointSymbolPatternDef
                {
                    Code = "Sn",
                    Name = "锡矿",
                    MarkerStyle = "Circle",
                    Color = DrawingColor.FromArgb(160, 100, 30),
                    Size = 12,
                    Description = "锡矿（棕色圆）"
                },
                new PointSymbolPatternDef
                {
                    Code = "Hg",
                    Name = "汞矿",
                    MarkerStyle = "Diamond",
                    Color = DrawingColor.FromArgb(220, 40, 40),
                    Size = 14,
                    Description = "汞矿（红色菱形）"
                },
                new PointSymbolPatternDef
                {
                    Code = "U",
                    Name = "铀矿",
                    MarkerStyle = "X",
                    Color = DrawingColor.FromArgb(40, 220, 40),
                    Size = 16,
                    Description = "铀矿（亮绿叉号）"
                },
                new PointSymbolPatternDef
                {
                    Code = "REE",
                    Name = "稀土矿",
                    MarkerStyle = "Star",
                    Color = DrawingColor.FromArgb(200, 60, 220),
                    Size = 16,
                    Description = "稀土矿（紫色星形）"
                }
            };
        }
    }
}
