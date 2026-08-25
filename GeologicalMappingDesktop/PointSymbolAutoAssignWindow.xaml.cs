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
    public class StyleToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var style = value as string;
            var target = parameter as string;
            return string.Equals(style, target, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public partial class PointSymbolAutoAssignWindow : Window
    {
        private readonly MapView _sourceMapView;
        private readonly System.Collections.ObjectModel.ObservableCollection<ClassDefinition> _dialogClasses;
        private readonly string _preferredField;

        public PointSymbolAutoAssignWindow(MapView mapView, System.Collections.ObjectModel.ObservableCollection<ClassDefinition> dialogClasses = null, string preferredField = null)
        {
            _sourceMapView = mapView;
            _dialogClasses = dialogClasses;
            _preferredField = preferredField;
            InitializeComponent();
            Loaded += PointSymbolAutoAssignWindow_Loaded;
        }

        // ---- 数据模型 ----

        public class FieldValueItem
        {
            public string Value { get; set; }
            public string DisplayText => Value ?? "(空)";
        }

        public class PatternItem
        {
            public PointSymbolPatternDef Def { get; set; }
            public string Code => Def.Code;
            public string Name => Def.Name;
            public string Description => Def.Description;
            public System.Windows.Media.Color PreviewColor => System.Windows.Media.Color.FromArgb(Def.Color.A, Def.Color.R, Def.Color.G, Def.Color.B);
        }

        public class MatchItem
        {
            public string FieldValue { get; set; }
            public PointSymbolPatternDef PatternDef { get; set; }
            public string PatternCode => PatternDef?.Code ?? "";
            public string PatternName => PatternDef?.Name ?? "";
        }

        private readonly ObservableCollection<FieldValueItem> _fieldValues = new ObservableCollection<FieldValueItem>();
        private readonly ObservableCollection<PatternItem> _patterns = new ObservableCollection<PatternItem>();
        private readonly ObservableCollection<MatchItem> _matches = new ObservableCollection<MatchItem>();

        private FeatureLayer _selectedLayer;

        private void PointSymbolAutoAssignWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadFeatureLayers();
            LoadPatternLibrary();
        }

        private void LoadFeatureLayers()
        {
            var layers = _sourceMapView?.Map?.OperationalLayers?
                .OfType<FeatureLayer>()
                .Where(fl => fl.FeatureTable?.GeometryType == GeometryType.Point ||
                             fl.FeatureTable?.GeometryType == GeometryType.Multipoint)
                .ToList();
            if (layers == null || layers.Count == 0)
            {
                CboFeatureLayers.ItemsSource = new List<string> { "(当前地图没有点要素图层)" };
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
                            return n.Contains("code") || n.Contains("代号") || n.Contains("类型") || n.Contains("矿物") || n.Contains("mineral") || n.Contains("type");
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
            _patterns.Clear();
            foreach (var def in PointSymbolPatternManager.DefaultPatterns)
            {
                _patterns.Add(new PatternItem { Def = def });
            }
            LstPatterns.ItemsSource = _patterns;
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
                string code = ExtractMineralCode(fv.Value);
                if (string.IsNullOrEmpty(code)) continue;

                var match = PointSymbolPatternManager.FindByCode(code);
                if (match == null) continue;

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
            LstMatches.ItemsSource = null;
            LstMatches.ItemsSource = _matches;
        }

        private static string ExtractMineralCode(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (value.Contains("铁") || value.Contains("磁铁") || value.Contains("赤铁")) return "Fe";
            if (value.Contains("铜")) return "Cu";
            if (value.Contains("金")) return "Au";
            if (value.Contains("煤")) return "Coal";
            if (value.Contains("铅锌") || value.Contains("铅") && value.Contains("锌")) return "PbZn";
            if (value.Contains("钨")) return "W";
            if (value.Contains("锡")) return "Sn";
            if (value.Contains("汞")) return "Hg";
            if (value.Contains("铀")) return "U";
            if (value.Contains("稀土")) return "REE";
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
                uvr.DefaultSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, DrawingColor.Gray, 8);

                foreach (var match in _matches)
                {
                    if (match.PatternDef == null) continue;
                    if (_dialogClasses != null)
                    {
                        var cd = _dialogClasses.FirstOrDefault(c => string.Equals((c.Value ?? "").Trim(), (match.FieldValue ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                        if (cd != null)
                        {
                            cd.PointLayers.Clear();
                            cd.PointLayers.Add(PointSymbolPatternManager.ToLayerDef(match.PatternDef));
                        }
                    }
                    else
                    {
                        var symbol = PointSymbolPatternManager.BuildPointSymbol(match.PatternDef);
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

                MessageBox.Show($"已应用 {_matches.Count} 个点符号匹配", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
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
