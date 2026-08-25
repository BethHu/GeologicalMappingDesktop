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
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using DrawingColor = System.Drawing.Color;

namespace GeologicalMappingDesktop
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public partial class LineSymbolAutoAssignWindow : Window
    {
        private readonly MapView _sourceMapView;
        private readonly System.Collections.ObjectModel.ObservableCollection<ClassDefinition> _dialogClasses;
        private readonly string _preferredField;

        public LineSymbolAutoAssignWindow(MapView mapView, System.Collections.ObjectModel.ObservableCollection<ClassDefinition> dialogClasses = null, string preferredField = null)
        {
            _sourceMapView = mapView;
            _dialogClasses = dialogClasses;
            _preferredField = preferredField;
            InitializeComponent();
            Loaded += LineSymbolAutoAssignWindow_Loaded;
        }

        public class FieldValueItem
        {
            public string Value { get; set; }
            public string DisplayText => Value ?? "(空)";
        }

        public class PatternItem
        {
            public LineSymbolPatternDef Def { get; set; }
            public string Code => Def.Code;
            public string Name => Def.Name;
            public string Description => Def.Description;
            public System.Windows.Media.Brush PreviewBrush => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(Def.Color.A, Def.Color.R, Def.Color.G, Def.Color.B));
            public double PreviewThickness => Math.Max(2, Def.Width * 2.5);
            public System.Windows.Media.DoubleCollection PreviewDashArray
            {
                get
                {
                    switch (Def.Style?.ToLowerInvariant())
                    {
                        case "dash": return new System.Windows.Media.DoubleCollection(new double[] { 4, 3 });
                        case "dot": return new System.Windows.Media.DoubleCollection(new double[] { 1, 3 });
                        case "dashdot": return new System.Windows.Media.DoubleCollection(new double[] { 4, 2, 1, 2 });
                        case "dashdotdot": return new System.Windows.Media.DoubleCollection(new double[] { 4, 2, 1, 2, 1, 2 });
                        default: return null;
                    }
                }
            }
            public bool ShowEndArrow => Def.Code == "NormalFault" || Def.Code == "ReverseFault" || Def.Code == "StrikeSlipFault";
            public bool ShowBeginArrow => Def.Code == "StrikeSlipFault";
            // 断层齿：正断层齿在下方、逆断层齿在上方、侵入接触齿朝向围岩（此处用下方表示）
            public bool ShowTeeth => Def.Code == "NormalFault" || Def.Code == "ReverseFault" || Def.Code == "IntrusiveContact";
            public bool TeethTop => Def.Code == "ReverseFault";
        }

        public class MatchItem
        {
            public string FieldValue { get; set; }
            public LineSymbolPatternDef PatternDef { get; set; }
            public string PatternCode => PatternDef?.Code ?? "";
        }

        private readonly ObservableCollection<FieldValueItem> _fieldValues = new ObservableCollection<FieldValueItem>();
        private readonly ObservableCollection<PatternItem> _linePatterns = new ObservableCollection<PatternItem>();
        private readonly ObservableCollection<MatchItem> _matches = new ObservableCollection<MatchItem>();

        private FeatureLayer _selectedLayer;

        private void LineSymbolAutoAssignWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadFeatureLayers();
            LoadPatternLibrary();
        }

        private void LoadFeatureLayers()
        {
            var layers = _sourceMapView?.Map?.OperationalLayers?
                .OfType<FeatureLayer>()
                .Where(fl => fl.FeatureTable?.GeometryType == GeometryType.Polyline)
                .ToList();
            if (layers == null || layers.Count == 0)
            {
                CboFeatureLayers.ItemsSource = new List<string> { "(当前地图没有线要素图层)" };
                CboFeatureLayers.IsEnabled = false;
                return;
            }
            CboFeatureLayers.ItemsSource = layers;
            CboFeatureLayers.DisplayMemberPath = "Name";
            CboFeatureLayers.SelectedIndex = 0;
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
                            return n.Contains("code") || n.Contains("类型") || n.Contains("断层") || n.Contains("fault") || n.Contains("接触") || n.Contains("contact") || n.Contains("界线") || n.Contains("boundary");
                        });
                    CboFieldName.SelectedItem = preferred ?? fields[0];
                }
            }
            catch { }
        }

        private async void CboFieldName_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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

        private void LoadPatternLibrary()
        {
            _linePatterns.Clear();
            foreach (var def in LineSymbolPatternManager.DefaultPatterns)
            {
                _linePatterns.Add(new PatternItem { Def = def });
            }
            LstPatterns.ItemsSource = _linePatterns;
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
                }
                else
                {
                    _matches.Add(new MatchItem
                    {
                        FieldValue = val.Value,
                        PatternDef = selectedPattern.Def
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
            var fieldValues = _fieldValues.ToList();
            if (fieldValues.Count == 0) return;

            foreach (var fv in fieldValues)
            {
                var match = LineSymbolPatternManager.FindByCode(fv.Value);
                if (match == null)
                {
                    string code = ExtractLineCode(fv.Value);
                    if (!string.IsNullOrEmpty(code))
                        match = LineSymbolPatternManager.FindByCode(code);
                }
                if (match != null)
                {
                    var existing = _matches.FirstOrDefault(m => string.Equals(m.FieldValue, fv.Value, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.PatternDef = match;
                    }
                    else
                    {
                        _matches.Add(new MatchItem { FieldValue = fv.Value, PatternDef = match });
                    }
                }
            }
            LstMatches.ItemsSource = null;
            LstMatches.ItemsSource = _matches;
        }

        private static string ExtractLineCode(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (value.Contains("正断层") || value.Contains("正断")) return "NormalFault";
            if (value.Contains("逆断层") || value.Contains("逆断")) return "ReverseFault";
            if (value.Contains("走滑") || value.Contains("平移")) return "StrikeSlipFault";
            if (value.Contains("性质不明") || value.Contains("未知断层")) return "UnknownFault";
            if (value.Contains("整合")) return "Conformity";
            if (value.Contains("角度不整合") || value.Contains("不整合")) return "Unconformity";
            if (value.Contains("平行不整合")) return "Disconformity";
            if (value.Contains("推测断层")) return "InferredFault";
            if (value.Contains("推测界线")) return "InferredBoundary";
            if (value.Contains("侵入接触")) return "IntrusiveContact";
            return null;
        }

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
                uvr.DefaultSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Dash, DrawingColor.FromArgb(128, 128, 128, 128), 0.5);

                foreach (var match in _matches)
                {
                    if (match.PatternDef == null) continue;
                    if (_dialogClasses != null)
                    {
                        var cd = _dialogClasses.FirstOrDefault(c => string.Equals((c.Value ?? "").Trim(), (match.FieldValue ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                        if (cd != null)
                        {
                            cd.LineLayers.Clear();
                            foreach (var l in LineSymbolPatternManager.ToLayerDefs(match.PatternDef))
                                cd.LineLayers.Add(l);
                        }
                    }
                    else
                    {
                        var symbol = LineSymbolPatternManager.BuildLineSymbol(match.PatternDef);
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

                MessageBox.Show($"已应用 {_matches.Count} 个线符号匹配", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
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
