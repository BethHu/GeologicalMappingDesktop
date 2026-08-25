using System.Collections.Generic;
using Esri.ArcGISRuntime.Symbology;
using DrawingColor = System.Drawing.Color;

namespace GeologicalMappingDesktop
{
    public class LineSymbolPatternDef
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Style { get; set; } = "Solid";
        public DrawingColor Color { get; set; } = DrawingColor.Black;
        public double Width { get; set; } = 1.0;
        public string Description { get; set; }
    }

    internal static class LineSymbolPatternManager
    {
        // 每种线用明显不同的颜色，避免"都是红色"的情况
        // 注意：必须声明在 DefaultPatterns 之前，否则静态初始化时这些字段还是 Color.Empty（全透明黑）
        private static readonly DrawingColor C_NormalFault = DrawingColor.FromArgb(255, 230, 30, 30);    // 亮红
        private static readonly DrawingColor C_ReverseFault = DrawingColor.FromArgb(255, 200, 30, 30);   // 红
        private static readonly DrawingColor C_StrikeSlip = DrawingColor.FromArgb(255, 220, 50, 40);     // 红
        private static readonly DrawingColor C_UnknownFault = DrawingColor.FromArgb(255, 220, 30, 30);   // 红虚线
        private static readonly DrawingColor C_Conformity = DrawingColor.FromArgb(255, 40, 40, 40);      // 黑
        private static readonly DrawingColor C_Disconformity = DrawingColor.FromArgb(255, 100, 150, 200); // 蓝灰
        private static readonly DrawingColor C_Unconformity = DrawingColor.FromArgb(255, 180, 120, 60);   // 棕
        private static readonly DrawingColor C_InferredFault = DrawingColor.FromArgb(255, 200, 50, 50);  // 红点线
        private static readonly DrawingColor C_InferredBoundary = DrawingColor.FromArgb(255, 160, 160, 160); // 浅灰
        private static readonly DrawingColor C_IntrusiveContact = DrawingColor.FromArgb(255, 180, 60, 220); // 紫

        public static List<LineSymbolPatternDef> DefaultPatterns { get; } = CreateDefaultPatterns();

        public static LineSymbolPatternDef FindByCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            return DefaultPatterns.Find(p => string.Equals(p.Code, code.Trim(), System.StringComparison.OrdinalIgnoreCase));
        }

        public static SimpleLineSymbol BuildLineSymbol(LineSymbolPatternDef def)
        {
            if (def == null) return null;
            return new SimpleLineSymbol(ParseStyle(def.Style), def.Color, def.Width);
        }

        public static List<LineSymbolLayerDef> ToLayerDefs(LineSymbolPatternDef def)
        {
            var list = new List<LineSymbolLayerDef>();
            list.Add(new LineSymbolLayerDef
            {
                Kind = LineSymbolLayerKind.SimpleStroke,
                Color = def.Color,
                Width = def.Width,
                StrokeStyle = def.Style ?? "Solid",
                Enabled = true,
                Name = def.Name + "主线"
            });
            // 正断层 / 逆断层 / 走滑断层 → 添加箭头
            if (def.Code == "NormalFault")
                list.Add(new LineSymbolLayerDef { Kind = LineSymbolLayerKind.ArrowLine, ArrowPlacement = "End", Color = def.Color, Width = 1.5, Enabled = true, Name = "箭头" });
            else if (def.Code == "ReverseFault")
                list.Add(new LineSymbolLayerDef { Kind = LineSymbolLayerKind.ArrowLine, ArrowPlacement = "Begin", Color = def.Color, Width = 1.5, Enabled = true, Name = "箭头" });
            else if (def.Code == "StrikeSlipFault")
                list.Add(new LineSymbolLayerDef { Kind = LineSymbolLayerKind.ArrowLine, ArrowPlacement = "BeginAndEnd", Color = def.Color, Width = 1.5, Enabled = true, Name = "双向箭头" });
            return list;
        }

        private static SimpleLineSymbolStyle ParseStyle(string style)
        {
            switch (style?.ToLowerInvariant())
            {
                case "dash": return SimpleLineSymbolStyle.Dash;
                case "dot": return SimpleLineSymbolStyle.Dot;
                case "dashdot": return SimpleLineSymbolStyle.DashDot;
                case "dashdotdot": return SimpleLineSymbolStyle.DashDotDot;
                case "dashdotdotdot": return SimpleLineSymbolStyle.DashDotDot;
                default: return SimpleLineSymbolStyle.Solid;
            }
        }

        private static List<LineSymbolPatternDef> CreateDefaultPatterns()
        {
            return new List<LineSymbolPatternDef>
            {
                new LineSymbolPatternDef
                {
                    Code = "NormalFault",
                    Name = "正断层",
                    Style = "Solid",
                    Color = C_NormalFault,
                    Width = 2.5,
                    Description = "正断层（红色粗实线）"
                },
                new LineSymbolPatternDef
                {
                    Code = "ReverseFault",
                    Name = "逆断层",
                    Style = "Solid",
                    Color = C_ReverseFault,
                    Width = 2.0,
                    Description = "逆断层（红色实线）"
                },
                new LineSymbolPatternDef
                {
                    Code = "StrikeSlipFault",
                    Name = "走滑断层",
                    Style = "Solid",
                    Color = C_StrikeSlip,
                    Width = 2.0,
                    Description = "走滑断层（红色实线）"
                },
                new LineSymbolPatternDef
                {
                    Code = "UnknownFault",
                    Name = "性质不明断层",
                    Style = "Dash",
                    Color = C_UnknownFault,
                    Width = 1.8,
                    Description = "性质不明断层（红色虚线）"
                },
                new LineSymbolPatternDef
                {
                    Code = "Conformity",
                    Name = "整合接触",
                    Style = "Solid",
                    Color = C_Conformity,
                    Width = 1.0,
                    Description = "整合接触（黑色实线）"
                },
                new LineSymbolPatternDef
                {
                    Code = "Disconformity",
                    Name = "平行不整合",
                    Style = "DashDotDot",
                    Color = C_Disconformity,
                    Width = 1.5,
                    Description = "平行不整合（蓝灰点画线）"
                },
                new LineSymbolPatternDef
                {
                    Code = "Unconformity",
                    Name = "角度不整合",
                    Style = "Dash",
                    Color = C_Unconformity,
                    Width = 1.8,
                    Description = "角度不整合（棕色虚线）"
                },
                new LineSymbolPatternDef
                {
                    Code = "InferredFault",
                    Name = "推测断层",
                    Style = "Dot",
                    Color = C_InferredFault,
                    Width = 1.2,
                    Description = "推测断层（红色点线）"
                },
                new LineSymbolPatternDef
                {
                    Code = "InferredBoundary",
                    Name = "推测界线",
                    Style = "Dot",
                    Color = C_InferredBoundary,
                    Width = 0.8,
                    Description = "推测界线（浅灰点线）"
                },
                new LineSymbolPatternDef
                {
                    Code = "IntrusiveContact",
                    Name = "侵入接触",
                    Style = "DashDot",
                    Color = C_IntrusiveContact,
                    Width = 1.8,
                    Description = "侵入接触（紫色点划线）"
                }
            };
        }
    }
}
