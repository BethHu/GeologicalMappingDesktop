using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using DrawingColor = System.Drawing.Color;

namespace GeologicalMappingDesktop
{
    public partial class LithologyAutoAssignWindow : Window
    {
        private readonly MapView _sourceMapView;
        private readonly System.Collections.ObjectModel.ObservableCollection<ClassDefinition> _dialogClasses;
        private readonly string _preferredField;

        public LithologyAutoAssignWindow(MapView mapView, System.Collections.ObjectModel.ObservableCollection<ClassDefinition> dialogClasses = null, string preferredField = null)
        {
            _sourceMapView = mapView;
            _dialogClasses = dialogClasses;
            _preferredField = preferredField;
            InitializeComponent();
            Loaded += LithologyAutoAssignWindow_Loaded;
        }

        // ---- 数据模型 ----

        public class FieldValueItem
        {
            public string Value { get; set; }
            public string DisplayText => Value ?? "(空)";
        }

        public class PatternItem
        {
            public LithologyPatternDef Def { get; set; }
            public BitmapSource PreviewSource { get; set; }
            public string Code => Def.Code;
            public string Name => Def.Name;
        }

        public class MatchItem
        {
            public string FieldValue { get; set; }
            public LithologyPatternDef PatternDef { get; set; }
            public string PatternCode => PatternDef?.Code ?? "";
            public BitmapSource PreviewSource { get; set; }
        }

        private readonly ObservableCollection<FieldValueItem> _fieldValues = new ObservableCollection<FieldValueItem>();
        private readonly ObservableCollection<PatternItem> _patterns = new ObservableCollection<PatternItem>();
        private readonly ObservableCollection<MatchItem> _matches = new ObservableCollection<MatchItem>();
        private Dictionary<string, BitmapSource> _previewCache = new Dictionary<string, BitmapSource>();

        private FeatureLayer _selectedLayer;

        private async void LithologyAutoAssignWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 加载要素图层
            LoadFeatureLayers();
            // 加载花纹库
            LoadPatternLibrary();
        }

        private void LoadFeatureLayers()
        {
            var layers = _sourceMapView?.Map?.OperationalLayers?
                .OfType<FeatureLayer>()
                .Where(fl => fl.FeatureTable?.GeometryType == GeometryType.Polygon)
                .ToList();
            if (layers == null || layers.Count == 0)
            {
                CboFeatureLayers.ItemsSource = new List<string> { "(当前地图没有面要素图层)" };
                CboFeatureLayers.IsEnabled = false;
                return;
            }
            CboFeatureLayers.ItemsSource = layers;
            CboFeatureLayers.DisplayMemberPath = "Name";
            CboFeatureLayers.SelectedIndex = 0;
        }

        private async void CboFieldName_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            await LoadFieldValuesAsync();
        }

        private async void CboFeatureLayers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _selectedLayer = CboFeatureLayers.SelectedItem as FeatureLayer;
            if (_selectedLayer == null) return;

            try
            {
                if (_selectedLayer.LoadStatus != LoadStatus.Loaded)
                    await _selectedLayer.LoadAsync();
                var table = _selectedLayer.FeatureTable;
                if (table == null) return;

                // 加载字段
                var fields = table.Fields?.ToList();
                if (fields != null && fields.Any())
                {
                    CboFieldName.ItemsSource = fields;
                    CboFieldName.DisplayMemberPath = "Name";
                    // 优先选择与分类渲染一致的字段，避免值不匹配
                    var preferred = fields.FirstOrDefault(f => !string.IsNullOrEmpty(_preferredField) && string.Equals(f.Name, _preferredField, StringComparison.OrdinalIgnoreCase));
                    if (preferred == null)
                        preferred = fields.FirstOrDefault(f =>
                        {
                            var n = f.Name.ToLowerInvariant();
                            return n.Contains("litholog") || n.Contains("岩性") || n.Contains("地层") || n.Contains("代号") || n.Contains("\u4EE3\u53F7") || n.Contains("code") || n.Contains("formation");
                        });
                    CboFieldName.SelectedItem = preferred ?? fields[0];
                }
            }
            catch { }
        }

        private void LoadPatternLibrary()
        {
            _patterns.Clear();
            foreach (var def in LithologyPatternManager.DefaultPatterns)
            {
                var preview = GeneratePreview(def);
                _patterns.Add(new PatternItem { Def = def, PreviewSource = preview });
            }
            LstPatterns.ItemsSource = _patterns;
        }

        /// <summary>为花纹定义生成 48x48 预览图</summary>
        private BitmapSource GeneratePreview(LithologyPatternDef def)
        {
            if (def == null) return null;
            string key = def.Code;
            if (_previewCache.TryGetValue(key, out var cached))
                return cached;

            try
            {
                int sz = 48;
                using (var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(DrawingColor.FromArgb(0, 0, 0, 0));
                    var layers = def.Layers;
                    for (int i = 0; i < layers.Count; i++)
                    {
                        var layer = layers[i];
                        if (!layer.Enabled) continue;
                        using (var tile = new Bitmap(sz, sz, PixelFormat.Format32bppArgb))
                        using (var tg = Graphics.FromImage(tile))
                        {
                            tg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            if (layer.Kind == FillSymbolLayerKind.Solid)
                            {
                                using (var brush = new SolidBrush(layer.Color))
                                    tg.FillRectangle(brush, -1, -1, sz + 2, sz + 2);
                            }
                            else
                            {
                                // 用 PatternFillHelper 生成小图
                                var innerDef = new FillSymbolLayerDef
                                {
                                    Kind = layer.Kind,
                                    Color = layer.Color,
                                    Angle = layer.Angle,
                                    Size = Math.Max(2, layer.Size * sz / 64.0),
                                    Spacing = Math.Max(2, layer.Spacing * sz / 64.0),
                                    LineWidth = Math.Max(0.3, layer.LineWidth),
                                    Seed = layer.Seed,
                                    OffsetX = layer.OffsetX,
                                    OffsetY = layer.OffsetY,
                                    Enabled = true,
                                    PatternPointLayers = layer.PatternPointLayers,
                                    PatternLineLayers = layer.PatternLineLayers
                                };
                                using (var tileBmp = PatternFillHelper.GenerateCompositeTile(new[] { innerDef }, sz))
                                using (var tileG = Graphics.FromImage(tileBmp))
                                    g.DrawImageUnscaled(tileBmp, 0, 0);
                                continue;
                            }
                            g.DrawImageUnscaled(tile, 0, 0);
                        }
                    }
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        ms.Position = 0;
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        bi.Freeze();
                        _previewCache[key] = bi;
                        return bi;
                    }
                }
            }
            catch { return null; }
        }

        private async void BtnLoadFieldValues_Click(object sender, RoutedEventArgs e)
        {
            await LoadFieldValuesAsync();
        }

        private async System.Threading.Tasks.Task LoadFieldValuesAsync()
        {
            if (_selectedLayer == null) return;
            var field = CboFieldName.SelectedItem as Field;
            if (field == null) return;

            _fieldValues.Clear();
            try
            {
                var table = _selectedLayer.FeatureTable;
                if (table == null) return;
                var query = new QueryParameters { WhereClause = "1=1" };
                var result = await table.QueryFeaturesAsync(query);
                var values = result.Select(f => f.Attributes[field.Name]?.ToString()?.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(v => v)
                    .ToList();
                foreach (var v in values)
                    _fieldValues.Add(new FieldValueItem { Value = v });
                LstFieldValues.ItemsSource = _fieldValues;
            }
            catch { }
        }

        private void BtnMatch_Click(object sender, RoutedEventArgs e)
        {
            var selectedValues = LstFieldValues.SelectedItems?.Cast<FieldValueItem>().ToList();
            var selectedPattern = LstPatterns.SelectedItem as PatternItem;
            if (selectedValues == null || selectedValues.Count == 0 || selectedPattern == null) return;

            foreach (var val in selectedValues)
            {
                var existing = _matches.FirstOrDefault(m => string.Equals(m.FieldValue, val.Value, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.PatternDef = selectedPattern.Def;
                    existing.PreviewSource = selectedPattern.PreviewSource;
                }
                else
                {
                    _matches.Add(new MatchItem
                    {
                        FieldValue = val.Value,
                        PatternDef = selectedPattern.Def,
                        PreviewSource = selectedPattern.PreviewSource
                    });
                }
            }
            LstMatches.ItemsSource = null;
            LstMatches.ItemsSource = _matches;
        }

        private void BtnUnmatch_Click(object sender, RoutedEventArgs e)
        {
            var selected = LstMatches.SelectedItems?.Cast<MatchItem>().ToList();
            if (selected == null) return;
            foreach (var s in selected) _matches.Remove(s);
        }

        private void BtnAutoMatch_Click(object sender, RoutedEventArgs e)
        {
            // 自动匹配：根据字段值中的代号（如 "J", "K", "γ" 等）自动找花纹
            var fieldValues = _fieldValues.ToList();
            if (fieldValues.Count == 0) return;

            foreach (var fv in fieldValues)
            {
                var match = LithologyPatternManager.FindByCode(fv.Value);
                if (match == null)
                {
                    // 尝试提取简短代号
                    string code = ExtractLithologyCode(fv.Value);
                    if (!string.IsNullOrEmpty(code))
                        match = LithologyPatternManager.FindByCode(code);
                }
                if (match != null)
                {
                    var preview = GeneratePreview(match);
                    var existing = _matches.FirstOrDefault(m => string.Equals(m.FieldValue, fv.Value, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.PatternDef = match;
                        existing.PreviewSource = preview;
                    }
                    else
                    {
                        _matches.Add(new MatchItem { FieldValue = fv.Value, PatternDef = match, PreviewSource = preview });
                    }
                }
            }
            LstMatches.ItemsSource = null;
            LstMatches.ItemsSource = _matches;
        }

        private static string ExtractLithologyCode(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            // 尝试匹配常见岩性代号模式：γ, δ, ν, β, α, J, K, T, P, C, D, S, O, ∈
            var match = System.Text.RegularExpressions.Regex.Match(value.Trim(), @"^[γδνβαJKT]|J[123]|K[12]|T[123]|P[12]|C[12]|D[123]|S[123]|O[123]|\u2208");
            if (match.Success) return match.Value;
            // 尝试匹配中文：花岗岩→γ, 闪长岩→δ 等
            if (value.Contains("花") || value.Contains("花岗")) return "γ";
            if (value.Contains("闪长")) return "δ";
            if (value.Contains("辉长")) return "ν";
            if (value.Contains("玄") || value.Contains("玄武")) return "β";
            if (value.Contains("安山")) return "α";
            if (value.Contains("砂") || value.Contains("砂岩")) return "S";
            if (value.Contains("粉砂")) return "Ss";
            if (value.Contains("页") || value.Contains("页岩")) return "Sh";
            if (value.Contains("泥") || value.Contains("泥岩")) return "M";
            if (value.Contains("石灰") || value.Contains("灰岩")) return "L";
            if (value.Contains("白云")) return "D";
            if (value.Contains("砾") || value.Contains("砾岩")) return "Cg";
            if (value.Contains("角砾")) return "Br";
            if (value.Contains("片麻")) return "Gn";
            if (value.Contains("片岩")) return "Sc";
            if (value.Contains("千枚")) return "Ph";
            if (value.Contains("板") || value.Contains("板岩")) return "Sl";
            if (value.Contains("大理")) return "Mb";
            if (value.Contains("石英")) return "Q";
            if (value.Contains("第四") || value.Contains("冲积") || value.Contains("坡积")) return "Q4";
            return null;
        }

        /// <summary>应用填充：将匹配的样式应用到要素图层的唯一值渲染器</summary>
        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedLayer == null || _matches.Count == 0) return;

            try
            {
                BtnApply.IsEnabled = false;
                var field = CboFieldName.SelectedItem as Field;
                if (field == null) return;

                var uvr = new UniqueValueRenderer();
                uvr.FieldNames.Add(field.Name);
                var defaultOutline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, DrawingColor.FromArgb(200, 80, 80, 80), 0.5);
                uvr.DefaultSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, DrawingColor.FromArgb(255, 200, 200, 200), defaultOutline);

                foreach (var match in _matches)
                {
                    if (match.PatternDef == null) continue;
                    if (_dialogClasses != null)
                    {
                        var cd = _dialogClasses.FirstOrDefault(c => string.Equals((c.Value ?? "").Trim(), (match.FieldValue ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                        if (cd != null)
                        {
                            cd.FillLayers.Clear();
                            foreach (var l in match.PatternDef.Layers)
                                cd.FillLayers.Add(l.Clone());
                            cd.FillMode = "Pattern";
                        }
                    }
                    else
                    {
                        var outlineColor = DrawingColor.FromArgb(255, 0, 0, 0);
                        var symbol = await LithologyPatternManager.BuildFillSymbolAsync(match.PatternDef, outlineColor, 0.5, 64);
                        if (symbol != null)
                        {
                            var uv = new UniqueValue { Label = match.FieldValue, Symbol = symbol };
                            uv.Values.Add(match.FieldValue);
                            uvr.UniqueValues.Add(uv);
                        }
                    }
                }

                if (_dialogClasses == null)
                    _selectedLayer.Renderer = uvr;

                MessageBox.Show($"已应用 {_matches.Count} 个岩性花纹匹配", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"应用失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnApply.IsEnabled = true;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
