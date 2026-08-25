using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using DrawingColor = System.Drawing.Color;

namespace GeologicalMappingDesktop
{
    public interface ISymbolTemplateEntry
    {
        string SelectionKey { get; }
    }

    public enum LineSymbolPresetKind
    {
        Solid,
        Dash,
        Dot,
        DashDot,
        DashDotDot,
        NormalFault,
        ReverseFault,
        StrikeSlip,
        Thrust,
        Unconformity,
        IntrusionContact
    }

    public enum PointSymbolPresetKind
    {
        Circle,
        Square,
        Diamond,
        Triangle,
        Cross,
        X,
        OreStar,
        OreDiamond,
        Attitude,
        Anticline,
        Syncline,
        SamplePoint
    }

    public sealed class LineSymbolTemplateDef : ISymbolTemplateEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "线模板";
        public LineSymbolPresetKind Kind { get; set; } = LineSymbolPresetKind.Solid;
        public DrawingColor Color { get; set; } = DrawingColor.FromArgb(255, 88, 166, 255);
        public double Width { get; set; } = 2.0;
        public bool IsCustom { get; set; }

        public string SelectionKey => IsCustom ? Id : Kind.ToString();

        public LineSymbolTemplateDef Clone()
        {
            return (LineSymbolTemplateDef)MemberwiseClone();
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Name) ? Kind.ToString() : Name;
        }
    }

    public sealed class PointSymbolTemplateDef : ISymbolTemplateEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "点模板";
        public PointSymbolPresetKind Kind { get; set; } = PointSymbolPresetKind.Circle;
        public DrawingColor FillColor { get; set; } = DrawingColor.FromArgb(255, 88, 166, 255);
        public DrawingColor OutlineColor { get; set; } = DrawingColor.White;
        public double OutlineWidth { get; set; } = 1.0;
        public double Size { get; set; } = 12.0;
        public double Angle { get; set; }
        public string Text { get; set; } = "1";
        public DrawingColor TextColor { get; set; } = DrawingColor.White;
        public double TextSize { get; set; } = 10.0;
        public bool IsCustom { get; set; }

        public string SelectionKey => IsCustom ? Id : Kind.ToString();

        public PointSymbolTemplateDef Clone()
        {
            return (PointSymbolTemplateDef)MemberwiseClone();
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Name) ? Kind.ToString() : Name;
        }
    }

    public sealed class SymbolTemplateLibrary
    {
        public List<LineSymbolTemplateDef> LineTemplates { get; set; } = new List<LineSymbolTemplateDef>();
        public List<PointSymbolTemplateDef> PointTemplates { get; set; } = new List<PointSymbolTemplateDef>();
    }

    internal static class SymbolTemplateStore
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static string LibraryPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GeologicalMappingDesktop");
                return Path.Combine(dir, "symbol_templates.json");
            }
        }

        public static List<LineSymbolTemplateDef> LoadLineTemplates()
        {
            return MergeTemplates(CreateDefaultLineTemplates(), LoadCustom().LineTemplates);
        }

        public static List<PointSymbolTemplateDef> LoadPointTemplates()
        {
            return MergeTemplates(CreateDefaultPointTemplates(), LoadCustom().PointTemplates);
        }

        public static void SaveLineTemplate(LineSymbolTemplateDef template)
        {
            var library = LoadCustom();
            template.IsCustom = true;
            template.Id = string.IsNullOrEmpty(template.Id) ? Guid.NewGuid().ToString("N") : template.Id;
            library.LineTemplates.RemoveAll(t => string.Equals(t.Id, template.Id, StringComparison.OrdinalIgnoreCase));
            library.LineTemplates.Add(template);
            Save(library);
        }

        public static void SavePointTemplate(PointSymbolTemplateDef template)
        {
            var library = LoadCustom();
            template.IsCustom = true;
            template.Id = string.IsNullOrEmpty(template.Id) ? Guid.NewGuid().ToString("N") : template.Id;
            library.PointTemplates.RemoveAll(t => string.Equals(t.Id, template.Id, StringComparison.OrdinalIgnoreCase));
            library.PointTemplates.Add(template);
            Save(library);
        }

        public static Symbol BuildLineSymbol(LineSymbolTemplateDef template)
        {
            if (template == null)
                template = CreateDefaultLineTemplates()[0];

            var color = template.Color;
            var width = Math.Max(0.5, template.Width);
            SimpleLineSymbolStyle style = SimpleLineSymbolStyle.Solid;
            SimpleLineSymbolMarkerPlacement placement = SimpleLineSymbolMarkerPlacement.Begin;
            SimpleLineSymbolMarkerStyle markerStyle = SimpleLineSymbolMarkerStyle.None;

            switch (template.Kind)
            {
                case LineSymbolPresetKind.Solid:
                    style = SimpleLineSymbolStyle.Solid;
                    break;
                case LineSymbolPresetKind.Dash:
                    style = SimpleLineSymbolStyle.Dash;
                    break;
                case LineSymbolPresetKind.Dot:
                    style = SimpleLineSymbolStyle.Dot;
                    break;
                case LineSymbolPresetKind.DashDot:
                    style = SimpleLineSymbolStyle.DashDot;
                    break;
                case LineSymbolPresetKind.DashDotDot:
                    style = SimpleLineSymbolStyle.DashDotDot;
                    break;
                case LineSymbolPresetKind.NormalFault:
                    style = SimpleLineSymbolStyle.Solid;
                    placement = SimpleLineSymbolMarkerPlacement.End;
                    markerStyle = SimpleLineSymbolMarkerStyle.Arrow;
                    break;
                case LineSymbolPresetKind.ReverseFault:
                    style = SimpleLineSymbolStyle.Solid;
                    placement = SimpleLineSymbolMarkerPlacement.Begin;
                    markerStyle = SimpleLineSymbolMarkerStyle.Arrow;
                    break;
                case LineSymbolPresetKind.StrikeSlip:
                    style = SimpleLineSymbolStyle.Solid;
                    placement = SimpleLineSymbolMarkerPlacement.BeginAndEnd;
                    markerStyle = SimpleLineSymbolMarkerStyle.Arrow;
                    break;
                case LineSymbolPresetKind.Thrust:
                    style = SimpleLineSymbolStyle.DashDotDot;
                    placement = SimpleLineSymbolMarkerPlacement.End;
                    markerStyle = SimpleLineSymbolMarkerStyle.Arrow;
                    break;
                case LineSymbolPresetKind.Unconformity:
                    style = SimpleLineSymbolStyle.DashDot;
                    break;
                case LineSymbolPresetKind.IntrusionContact:
                    style = SimpleLineSymbolStyle.DashDotDot;
                    placement = SimpleLineSymbolMarkerPlacement.BeginAndEnd;
                    markerStyle = SimpleLineSymbolMarkerStyle.Arrow;
                    break;
            }

            var symbol = new SimpleLineSymbol(style, color, width);
            symbol.MarkerStyle = markerStyle;
            if (markerStyle != SimpleLineSymbolMarkerStyle.None)
                symbol.MarkerPlacement = placement;
            return symbol;
        }

        public static async System.Threading.Tasks.Task<Symbol> BuildPointSymbolAsync(PointSymbolTemplateDef template)
        {
            if (template == null)
                template = CreateDefaultPointTemplates()[0];

            var fill = template.FillColor;
            var outline = template.OutlineColor;
            var size = Math.Max(4, template.Size);

            switch (template.Kind)
            {
                case PointSymbolPresetKind.Circle:
                    return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, fill, size)
                    {
                        Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outline, template.OutlineWidth)
                    };
                case PointSymbolPresetKind.Square:
                    return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Square, fill, size)
                    {
                        Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outline, template.OutlineWidth)
                    };
                case PointSymbolPresetKind.Diamond:
                    return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Diamond, fill, size)
                    {
                        Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outline, template.OutlineWidth)
                    };
                case PointSymbolPresetKind.Triangle:
                    return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Triangle, fill, size)
                    {
                        Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outline, template.OutlineWidth)
                    };
                case PointSymbolPresetKind.Cross:
                    return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Cross, fill, size)
                    {
                        Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outline, template.OutlineWidth)
                    };
                case PointSymbolPresetKind.X:
                    return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.X, fill, size)
                    {
                        Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outline, template.OutlineWidth)
                    };
                case PointSymbolPresetKind.SamplePoint:
                {
                    var circle = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, fill, size)
                    {
                        Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outline, template.OutlineWidth)
                    };
                    var text = new TextSymbol(
                        string.IsNullOrWhiteSpace(template.Text) ? "1" : template.Text,
                        template.TextColor,
                        template.TextSize,
                        Esri.ArcGISRuntime.Symbology.HorizontalAlignment.Center,
                        Esri.ArcGISRuntime.Symbology.VerticalAlignment.Middle)
                    {
                        OffsetX = 0,
                        OffsetY = 0
                    };
                    return new CompositeSymbol(new Symbol[] { circle, text });
                }
                default:
                {
                    string svg = BuildPointSvg(template.Kind);
                    string tempSvgPath = Path.Combine(Path.GetTempPath(), $"symbol_{Guid.NewGuid():N}.svg");
                    File.WriteAllText(tempSvgPath, svg, Encoding.UTF8);
                    try
                    {
                        using (var bmp = PatternFillHelper.RenderSvgToBitmap(tempSvgPath, (int)Math.Ceiling(size * 2), fill, template.Angle))
                        using (var ms = new MemoryStream(PatternFillHelper.BitmapToPngBytes(bmp)))
                        {
                            var ri = await RuntimeImage.FromStreamAsync(ms);
                            var marker = new PictureMarkerSymbol(ri)
                            {
                                Width = size * 2,
                                Height = size * 2,
                                Angle = template.Angle
                            };
                            if (template.Kind == PointSymbolPresetKind.SamplePoint)
                            {
                                var text = new TextSymbol(
                                    string.IsNullOrWhiteSpace(template.Text) ? "1" : template.Text,
                                    template.TextColor,
                                    template.TextSize,
                                    Esri.ArcGISRuntime.Symbology.HorizontalAlignment.Center,
                                    Esri.ArcGISRuntime.Symbology.VerticalAlignment.Middle)
                                {
                                    OffsetX = 0,
                                    OffsetY = 0
                                };
                                return new CompositeSymbol(new Symbol[] { marker, text });
                            }
                            return marker;
                        }
                    }
                    finally
                    {
                        try { File.Delete(tempSvgPath); } catch { }
                    }
                }
            }
        }

        public static string BuildPointSvg(PointSymbolPresetKind kind)
        {
            switch (kind)
            {
                case PointSymbolPresetKind.OreStar:
                    return "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><path d='M50 8 L61 35 L90 35 L66 53 L75 82 L50 65 L25 82 L34 53 L10 35 L39 35 Z' fill='currentColor' stroke='currentColor' stroke-width='4'/></svg>";
                case PointSymbolPresetKind.OreDiamond:
                    return "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><polygon points='50,8 88,50 50,92 12,50' fill='currentColor' stroke='currentColor' stroke-width='4'/></svg>";
                case PointSymbolPresetKind.Attitude:
                    return "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><circle cx='50' cy='50' r='42' fill='none' stroke='currentColor' stroke-width='6'/><path d='M50 18 L68 46 H57 V82 H43 V46 H32 Z' fill='currentColor'/></svg>";
                case PointSymbolPresetKind.Anticline:
                    return "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><path d='M20 68 C32 38, 68 38, 80 68' fill='none' stroke='currentColor' stroke-width='6' stroke-linecap='round'/><path d='M36 42 L28 28 L44 32 Z' fill='currentColor'/><path d='M64 42 L56 32 L72 28 Z' fill='currentColor'/></svg>";
                case PointSymbolPresetKind.Syncline:
                    return "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><path d='M20 34 C32 64, 68 64, 80 34' fill='none' stroke='currentColor' stroke-width='6' stroke-linecap='round'/><path d='M36 58 L28 72 L44 68 Z' fill='currentColor'/><path d='M64 58 L56 68 L72 72 Z' fill='currentColor'/></svg>";
                case PointSymbolPresetKind.SamplePoint:
                    return "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><circle cx='50' cy='50' r='42' fill='none' stroke='currentColor' stroke-width='6'/></svg>";
                default:
                    return "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><circle cx='50' cy='50' r='40' fill='currentColor' stroke='currentColor' stroke-width='4'/></svg>";
            }
        }

        public static List<LineSymbolTemplateDef> CreateDefaultLineTemplates()
        {
            return new List<LineSymbolTemplateDef>
            {
                new LineSymbolTemplateDef { Name = "实线", Kind = LineSymbolPresetKind.Solid, Color = DrawingColor.FromArgb(255, 50, 50, 50), Width = 2.0 },
                new LineSymbolTemplateDef { Name = "虚线", Kind = LineSymbolPresetKind.Dash, Color = DrawingColor.FromArgb(255, 50, 50, 50), Width = 2.0 },
                new LineSymbolTemplateDef { Name = "点线", Kind = LineSymbolPresetKind.Dot, Color = DrawingColor.FromArgb(255, 50, 50, 50), Width = 2.0 },
                new LineSymbolTemplateDef { Name = "点划线", Kind = LineSymbolPresetKind.DashDot, Color = DrawingColor.FromArgb(255, 50, 50, 50), Width = 2.0 },
                new LineSymbolTemplateDef { Name = "点线组合", Kind = LineSymbolPresetKind.DashDotDot, Color = DrawingColor.FromArgb(255, 50, 50, 50), Width = 2.0 },
                new LineSymbolTemplateDef { Name = "正断层", Kind = LineSymbolPresetKind.NormalFault, Color = DrawingColor.FromArgb(255, 220, 60, 60), Width = 2.0 },
                new LineSymbolTemplateDef { Name = "逆断层", Kind = LineSymbolPresetKind.ReverseFault, Color = DrawingColor.FromArgb(255, 120, 70, 30), Width = 2.0 },
                new LineSymbolTemplateDef { Name = "平移断层", Kind = LineSymbolPresetKind.StrikeSlip, Color = DrawingColor.FromArgb(255, 30, 130, 200), Width = 2.2 },
                new LineSymbolTemplateDef { Name = "推覆构造", Kind = LineSymbolPresetKind.Thrust, Color = DrawingColor.FromArgb(255, 190, 110, 30), Width = 2.2 },
                new LineSymbolTemplateDef { Name = "整合界线", Kind = LineSymbolPresetKind.Solid, Color = DrawingColor.FromArgb(255, 30, 30, 30), Width = 2.2 },
                new LineSymbolTemplateDef { Name = "不整合界线", Kind = LineSymbolPresetKind.DashDot, Color = DrawingColor.FromArgb(255, 30, 30, 30), Width = 2.2 },
                new LineSymbolTemplateDef { Name = "侵入接触", Kind = LineSymbolPresetKind.IntrusionContact, Color = DrawingColor.FromArgb(255, 30, 30, 30), Width = 2.2 }
            };
        }

        public static List<PointSymbolTemplateDef> CreateDefaultPointTemplates()
        {
            return new List<PointSymbolTemplateDef>
            {
                new PointSymbolTemplateDef { Name = "圆点", Kind = PointSymbolPresetKind.Circle, FillColor = DrawingColor.FromArgb(255, 88, 166, 255), OutlineColor = DrawingColor.White, Size = 12 },
                new PointSymbolTemplateDef { Name = "方点", Kind = PointSymbolPresetKind.Square, FillColor = DrawingColor.FromArgb(255, 88, 166, 255), OutlineColor = DrawingColor.White, Size = 12 },
                new PointSymbolTemplateDef { Name = "菱形", Kind = PointSymbolPresetKind.Diamond, FillColor = DrawingColor.FromArgb(255, 88, 166, 255), OutlineColor = DrawingColor.White, Size = 12 },
                new PointSymbolTemplateDef { Name = "三角形", Kind = PointSymbolPresetKind.Triangle, FillColor = DrawingColor.FromArgb(255, 88, 166, 255), OutlineColor = DrawingColor.White, Size = 12 },
                new PointSymbolTemplateDef { Name = "十字", Kind = PointSymbolPresetKind.Cross, FillColor = DrawingColor.FromArgb(255, 88, 166, 255), OutlineColor = DrawingColor.White, Size = 12 },
                new PointSymbolTemplateDef { Name = "X形", Kind = PointSymbolPresetKind.X, FillColor = DrawingColor.FromArgb(255, 88, 166, 255), OutlineColor = DrawingColor.White, Size = 12 },
                new PointSymbolTemplateDef { Name = "矿点-星形", Kind = PointSymbolPresetKind.OreStar, FillColor = DrawingColor.FromArgb(255, 255, 190, 0), OutlineColor = DrawingColor.Black, Size = 14 },
                new PointSymbolTemplateDef { Name = "矿点-填充菱形", Kind = PointSymbolPresetKind.OreDiamond, FillColor = DrawingColor.FromArgb(255, 255, 190, 0), OutlineColor = DrawingColor.Black, Size = 14 },
                new PointSymbolTemplateDef { Name = "产状", Kind = PointSymbolPresetKind.Attitude, FillColor = DrawingColor.FromArgb(255, 255, 255, 255), OutlineColor = DrawingColor.FromArgb(255, 88, 166, 255), Size = 18 },
                new PointSymbolTemplateDef { Name = "褶皱-背斜", Kind = PointSymbolPresetKind.Anticline, FillColor = DrawingColor.FromArgb(255, 255, 255, 255), OutlineColor = DrawingColor.FromArgb(255, 255, 190, 0), Size = 18 },
                new PointSymbolTemplateDef { Name = "褶皱-向斜", Kind = PointSymbolPresetKind.Syncline, FillColor = DrawingColor.FromArgb(255, 255, 255, 255), OutlineColor = DrawingColor.FromArgb(255, 90, 190, 255), Size = 18 },
                new PointSymbolTemplateDef { Name = "采样点", Kind = PointSymbolPresetKind.SamplePoint, FillColor = DrawingColor.FromArgb(255, 255, 255, 255), OutlineColor = DrawingColor.FromArgb(255, 88, 166, 255), Size = 14, Text = "1", TextColor = DrawingColor.Black, TextSize = 10 }
            };
        }

        private static SymbolTemplateLibrary LoadCustom()
        {
            try
            {
                if (!File.Exists(LibraryPath))
                    return new SymbolTemplateLibrary();

                string json = File.ReadAllText(LibraryPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                    return new SymbolTemplateLibrary();

                return Serializer.Deserialize<SymbolTemplateLibrary>(json) ?? new SymbolTemplateLibrary();
            }
            catch
            {
                return new SymbolTemplateLibrary();
            }
        }

        private static void Save(SymbolTemplateLibrary library)
        {
            try
            {
                string dir = Path.GetDirectoryName(LibraryPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                string json = Serializer.Serialize(library ?? new SymbolTemplateLibrary());
                File.WriteAllText(LibraryPath, json, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static List<T> MergeTemplates<T>(List<T> defaults, List<T> custom) where T : class, ISymbolTemplateEntry
        {
            var result = new List<T>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in defaults ?? new List<T>())
            {
                result.Add(item);
                seen.Add(item.SelectionKey);
            }
            foreach (var item in custom ?? new List<T>())
            {
                if (item == null) continue;
                if (seen.Add(item.SelectionKey))
                    result.Add(item);
            }
            return result;
        }
    }
}