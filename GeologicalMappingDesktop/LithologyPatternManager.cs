using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Symbology;
using DrawingColor = System.Drawing.Color;

namespace GeologicalMappingDesktop
{
    /// <summary>岩性花纹定义：每种岩性的代号、名称和 FillSymbolLayerDef 列表</summary>
    public class LithologyPatternDef
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public List<FillSymbolLayerDef> Layers { get; set; } = new List<FillSymbolLayerDef>();
    }

    internal static class LithologyPatternManager
    {
        public static List<LithologyPatternDef> DefaultPatterns { get; } = CreateDefaultPatterns();

        /// <summary>根据岩性代号查找默认花纹，找不到返回 null</summary>
        public static LithologyPatternDef FindByCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            return DefaultPatterns.Find(p => string.Equals(p.Code, code.Trim(), System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>构建 ArcGIS Runtime 面符号</summary>
        public static async Task<Symbol> BuildFillSymbolAsync(LithologyPatternDef def, Color outlineColor, double outlineWidth, int tileSize = 64)
        {
            if (def == null || def.Layers == null || def.Layers.Count == 0) return null;
            var dc = DrawingColor.FromArgb(outlineColor.A, outlineColor.R, outlineColor.G, outlineColor.B);
            return await PatternFillHelper.BuildFillSymbolAsync(def.Layers, dc, outlineWidth, tileSize);
        }

        private static List<LithologyPatternDef> CreateDefaultPatterns()
        {
            var list = new List<LithologyPatternDef>();

            // 1. 花岗岩 — 随机点
            list.Add(new LithologyPatternDef
            {
                Code = "\u79EF", Name = "\u82B1\u5CA9 (\u79EF)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 255, 200, 200), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.PointsRandom, Name = "\u82B1\u5CA9\u70B9",
                        Color = DrawingColor.FromArgb(200, 180, 100, 100), Size = 4, Spacing = 12, Seed = 1
                    }
                }
            });

            // 2. 闪长岩 — 随机点（中等）
            list.Add(new LithologyPatternDef
            {
                Code = "\u03B4", Name = "\u95EA\u957F\u5CA9 (\u03B4)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 200, 220, 200), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.PointsRandom, Name = "\u95EA\u957F\u5CA9\u70B9",
                        Color = DrawingColor.FromArgb(180, 100, 140, 100), Size = 3, Spacing = 10, Seed = 2
                    }
                }
            });

            // 3. 辉长岩 — 随机点（大）
            list.Add(new LithologyPatternDef
            {
                Code = "\u03BD", Name = "\u8F89\u957F\u5CA9 (\u03BD)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 180, 200, 180), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.PointsRandom, Name = "\u8F89\u957F\u5CA9\u70B9",
                        Color = DrawingColor.FromArgb(200, 60, 80, 60), Size = 5, Spacing = 14, Seed = 3
                    }
                }
            });

            // 4. 砂岩 — 密点
            list.Add(new LithologyPatternDef
            {
                Code = "S", Name = "\u7802\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 255, 230, 180), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.PointsRandom, Name = "\u7802\u7C92",
                        Color = DrawingColor.FromArgb(150, 160, 130, 80), Size = 2, Spacing = 6, Seed = 4
                    }
                }
            });

            // 5. 粉砂岩 — 极细密点
            list.Add(new LithologyPatternDef
            {
                Code = "Ss", Name = "\u7C89\u7802\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 240, 220, 200), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.PointsRandom, Name = "\u7C89\u7802\u7C92",
                        Color = DrawingColor.FromArgb(120, 180, 150, 120), Size = 1.5, Spacing = 4, Seed = 5
                    }
                }
            });

            // 6. 页岩 — 水平平行线
            list.Add(new LithologyPatternDef
            {
                Code = "Sh", Name = "\u9875\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 200, 200, 210), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.Lines, Name = "\u5C42\u7406\u7EBF",
                        Color = DrawingColor.FromArgb(200, 100, 100, 120), Spacing = 8, LineWidth = 0.8, Angle = 0
                    }
                }
            });

            // 7. 泥岩 — 细密水平线
            list.Add(new LithologyPatternDef
            {
                Code = "M", Name = "\u6CE5\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 210, 200, 190), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.Lines, Name = "\u6CE5\u5C42\u7EBF",
                        Color = DrawingColor.FromArgb(180, 140, 120, 100), Spacing = 5, LineWidth = 0.5, Angle = 0
                    }
                }
            });

            // 8. 石灰岩 — 砖墙/矩形
            list.Add(new LithologyPatternDef
            {
                Code = "L", Name = "\u77F3\u7070\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 190, 210, 200), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.CrossHatch, Name = "\u77F3\u7070\u5CA9\u7F51\u683C",
                        Color = DrawingColor.FromArgb(160, 100, 130, 110), Spacing = 10, LineWidth = 1, Angle = 0
                    }
                }
            });

            // 9. 白云岩 — 菱形斜纹
            list.Add(new LithologyPatternDef
            {
                Code = "D", Name = "\u767D\u4E91\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 220, 220, 210), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.CrossHatch, Name = "\u767D\u4E91\u659C\u7EB9",
                        Color = DrawingColor.FromArgb(150, 150, 150, 130), Spacing = 10, LineWidth = 0.8, Angle = 30
                    }
                }
            });

            // 10. 砾岩 — 圆圈
            list.Add(new LithologyPatternDef
            {
                Code = "Cg", Name = "\u783E\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 230, 210, 180), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.PointsGrid, Name = "\u783E\u77F3",
                        Color = DrawingColor.FromArgb(180, 160, 120, 80), Size = 6, Spacing = 14
                    }
                }
            });

            // 11. 角砾岩 — 三角形点
            list.Add(new LithologyPatternDef
            {
                Code = "Br", Name = "\u89D2\u783E\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 220, 200, 170), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.PointsRandom, Name = "\u89D2\u783E",
                        Color = DrawingColor.FromArgb(200, 120, 90, 50), Size = 7, Spacing = 16, Seed = 6
                    }
                }
            });

            // 12. 玄武岩 — 密小黑点
            list.Add(new LithologyPatternDef
            {
                Code = "\u03B2", Name = "\u7384\u6B66\u5CA9 (\u03B2)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 160, 170, 180), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.PointsRandom, Name = "\u7384\u6B66\u5CA9\u70B9",
                        Color = DrawingColor.FromArgb(200, 40, 50, 60), Size = 3, Spacing = 7, Seed = 7
                    }
                }
            });

            // 13. 安山岩 — 中等密点
            list.Add(new LithologyPatternDef
            {
                Code = "\u03B1", Name = "\u5B89\u5C71\u5CA9 (\u03B1)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 200, 180, 170), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.PointsRandom, Name = "\u5B89\u5C71\u5CA9\u70B9",
                        Color = DrawingColor.FromArgb(180, 120, 90, 70), Size = 3.5, Spacing = 9, Seed = 8
                    }
                }
            });

            // 14. 片麻岩 — 交替平行线
            list.Add(new LithologyPatternDef
            {
                Code = "Gn", Name = "\u7247\u9EBB\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 210, 200, 190), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.Lines, Name = "\u9EBB\u7406\u7EBF1",
                        Color = DrawingColor.FromArgb(200, 160, 140, 120), Spacing = 12, LineWidth = 2, Angle = 0
                    },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.Lines, Name = "\u9EBB\u7406\u7EBF2",
                        Color = DrawingColor.FromArgb(120, 200, 180, 160), Spacing = 12, LineWidth = 2, Angle = 0,
                        OffsetY = 6
                    }
                }
            });

            // 15. 片岩 — 波状/粗糙线
            list.Add(new LithologyPatternDef
            {
                Code = "Sc", Name = "\u7247\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 190, 180, 175), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.Lines, Name = "\u7247\u7406\u7EBF",
                        Color = DrawingColor.FromArgb(180, 120, 100, 90), Spacing = 8, LineWidth = 1, Angle = 10
                    },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.Lines, Name = "\u7247\u7406\u7EBF2",
                        Color = DrawingColor.FromArgb(100, 140, 120, 110), Spacing = 8, LineWidth = 0.5, Angle = 10,
                        OffsetY = 2
                    }
                }
            });

            // 16. 千枚岩 — 细密波线
            list.Add(new LithologyPatternDef
            {
                Code = "Ph", Name = "\u5343\u679A\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 200, 195, 185), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.Lines, Name = "\u5343\u679A\u7EB9",
                        Color = DrawingColor.FromArgb(160, 150, 130, 110), Spacing = 5, LineWidth = 0.5, Angle = 15
                    }
                }
            });

            // 17. 板岩 — 紧密平行线
            list.Add(new LithologyPatternDef
            {
                Code = "Sl", Name = "\u677F\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 180, 180, 190), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.Lines, Name = "\u677F\u7406\u7EBF",
                        Color = DrawingColor.FromArgb(200, 80, 80, 100), Spacing = 4, LineWidth = 0.8, Angle = 0
                    }
                }
            });

            // 18. 大理岩 — 交叉网格
            list.Add(new LithologyPatternDef
            {
                Code = "Mb", Name = "\u5927\u7406\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 240, 235, 230), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.CrossHatch, Name = "\u5927\u7406\u7F51\u7EB9",
                        Color = DrawingColor.FromArgb(100, 180, 170, 160), Spacing = 8, LineWidth = 0.6, Angle = 45
                    }
                }
            });

            // 19. 石英岩 — 密点交错
            list.Add(new LithologyPatternDef
            {
                Code = "Q", Name = "\u77F3\u82F1\u5CA9",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 230, 230, 235), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef
                    {
                        Kind = FillSymbolLayerKind.PointsRandom, Name = "\u77F3\u82F1\u7C92",
                        Color = DrawingColor.FromArgb(120, 180, 180, 200), Size = 1.8, Spacing = 5, Seed = 9
                    }
                }
            });

            // 20. 第四系 — 无填充
            list.Add(new LithologyPatternDef
            {
                Code = "Q4", Name = "\u7B2C\u56DB\u7CFB",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 240, 240, 220), Name = "\u5E95\u8272" }
                }
            });

            // 21. 侏罗纪 — 细密斜线
            list.Add(new LithologyPatternDef
            {
                Code = "J", Name = "\u4F8F\u7F57\u7EAA (J)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 180, 210, 230), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Lines, Color = DrawingColor.FromArgb(160, 80, 120, 160), Spacing = 8, LineWidth = 0.6, Angle = 45, Name = "\u4F8F\u7F57\u7EB9" }
                }
            });

            // 22. 白垩纪 — 交叉斜纹
            list.Add(new LithologyPatternDef
            {
                Code = "K", Name = "\u767D\u57A3\u7EAA (K)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 200, 220, 200), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.CrossHatch, Color = DrawingColor.FromArgb(150, 100, 140, 100), Spacing = 10, LineWidth = 0.7, Angle = 0, Name = "\u767D\u57A3\u7EB9" }
                }
            });

            // 23. 三叠纪 — 波浪横线
            list.Add(new LithologyPatternDef
            {
                Code = "T", Name = "\u4E09\u53E0\u7EAA (T)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 210, 190, 180), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Lines, Color = DrawingColor.FromArgb(170, 130, 100, 80), Spacing = 10, LineWidth = 0.8, Angle = 0, Name = "\u4E09\u53E0\u7EB9" }
                }
            });

            // 24. 二叠纪 — 粗密斜线
            list.Add(new LithologyPatternDef
            {
                Code = "P", Name = "\u4E8C\u53E0\u7EAA (P)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 190, 170, 160), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Lines, Color = DrawingColor.FromArgb(180, 110, 80, 70), Spacing = 6, LineWidth = 1.2, Angle = -45, Name = "\u4E8C\u53E0\u7EB9" }
                }
            });

            // 25. 石炭纪 — 砖格纹
            list.Add(new LithologyPatternDef
            {
                Code = "C", Name = "\u77F3\u70AD\u7EAA (C)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 170, 190, 170), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.CrossHatch, Color = DrawingColor.FromArgb(160, 80, 110, 80), Spacing = 8, LineWidth = 0.8, Angle = 0, Name = "\u77F3\u70AD\u7EB9" }
                }
            });

            // 26. 泥盆纪 — 疏斜纹
            list.Add(new LithologyPatternDef
            {
                Code = "D", Name = "\u6CE5\u76C6\u7EAA (D)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 200, 180, 150), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Lines, Color = DrawingColor.FromArgb(140, 140, 110, 80), Spacing = 12, LineWidth = 0.7, Angle = 30, Name = "\u6CE5\u76C6\u7EB9" }
                }
            });

            // 27. 志留纪 — 细点纹
            list.Add(new LithologyPatternDef
            {
                Code = "S", Name = "\u5FD7\u7559\u7EAA (S)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 180, 200, 210), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.PointsRandom, Color = DrawingColor.FromArgb(160, 80, 120, 140), Size = 2.5, Spacing = 8, Seed = 10, Name = "\u5FD7\u7559\u70B9" }
                }
            });

            // 28. 奥陶纪 — 短横纹
            list.Add(new LithologyPatternDef
            {
                Code = "O", Name = "\u5965\u9676\u7EAA (O)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 170, 190, 200), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.PointsGrid, Color = DrawingColor.FromArgb(150, 100, 130, 150), Size = 3, Spacing = 10, Name = "\u5965\u9676\u70B9" }
                }
            });

            // 29. 寒武纪 — 菱形格纹
            list.Add(new LithologyPatternDef
            {
                Code = "\u2208", Name = "\u5BD2\u6B66\u7EAA (\u2208)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 190, 180, 190), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.CrossHatch, Color = DrawingColor.FromArgb(150, 120, 100, 120), Spacing = 10, LineWidth = 0.6, Angle = 45, Name = "\u5BD2\u6B66\u7EB9" }
                }
            });

            // 30. 前寒武系 — 粗密交叉
            list.Add(new LithologyPatternDef
            {
                Code = "An\u2208", Name = "\u524D\u5BD2\u6B66\u7CFB (An\u2208)",
                Layers = new List<FillSymbolLayerDef>
                {
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.Solid, Color = DrawingColor.FromArgb(255, 160, 160, 180), Name = "\u5E95\u8272" },
                    new FillSymbolLayerDef { Kind = FillSymbolLayerKind.CrossHatch, Color = DrawingColor.FromArgb(180, 80, 80, 100), Spacing = 7, LineWidth = 1.2, Angle = 0, Name = "\u524D\u5BD2\u6B66\u7EB9" }
                }
            });

            return list;
        }
    }
}
