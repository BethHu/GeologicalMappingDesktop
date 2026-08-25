using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualBasic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Rasters;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Mapping.Labeling;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;

namespace GeologicalMappingDesktop
{
    public class ClassDefinition : System.ComponentModel.INotifyPropertyChanged
    {
        private string _value = "";
        private string _label = "";
        private DrawingColor _color = DrawingColor.FromArgb(255, 88, 166, 255);
        private ImageSource _previewImage;
        public string Value { get => _value; set { _value = value; OnPropertyChanged(nameof(Value)); } }
        public string Label { get => _label; set { _label = value; OnPropertyChanged(nameof(Label)); } }
        public DrawingColor Color { get => _color; set { _color = value; OnPropertyChanged(nameof(Color)); OnPropertyChanged(nameof(ColorBrush)); } }
        public Brush ColorBrush => new SolidColorBrush(MediaColor.FromArgb(Color.A, Color.R, Color.G, Color.B));
        public ImageSource PreviewImage { get => _previewImage; set { _previewImage = value; OnPropertyChanged(nameof(PreviewImage)); } }
        // 自定义符号设置（分类渲染下每个类别独立的符号配置）
        public string FillMode { get; set; } = "Solid";
        public DrawingColor FillColor { get; set; } = DrawingColor.FromArgb(128, 88, 166, 255);
        public DrawingColor OutlineColor { get; set; } = DrawingColor.FromArgb(255, 88, 166, 255);
        public double OutlineWidth { get; set; } = 1;
        public ObservableCollection<FillSymbolLayerDef> FillLayers { get; set; } = new ObservableCollection<FillSymbolLayerDef>();
        public ObservableCollection<PointSymbolLayerDef> PointLayers { get; set; } = new ObservableCollection<PointSymbolLayerDef>();
        public ObservableCollection<LineSymbolLayerDef> LineLayers { get; set; } = new ObservableCollection<LineSymbolLayerDef>();
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    public partial class LayerPropertiesDialog : Window
    {
        /// <summary>跨会话持久化分类的自定义符号层设置</summary>
        private static readonly Dictionary<string, List<ClassDefSymData>> _savedClassData = new Dictionary<string, List<ClassDefSymData>>();

        public class ClassDefSymData
        {
            public string Value { get; set; }
            public List<PointSymbolLayerDef> PointLayers { get; set; }
            public List<FillSymbolLayerDef> FillLayers { get; set; }
            public List<LineSymbolLayerDef> LineLayers { get; set; }
            public string FillMode { get; set; }
            public DrawingColor FillColor { get; set; }
            public DrawingColor OutlineColor { get; set; }
            public double OutlineWidth { get; set; }
        }

        /// <summary>跨会话持久化单一符号层的点/线/面多符号层设置</summary>
        private static readonly Dictionary<string, List<PointSymbolLayerDef>> _savedSinglePoint = new Dictionary<string, List<PointSymbolLayerDef>>();
        private static readonly Dictionary<string, List<LineSymbolLayerDef>> _savedSingleLine = new Dictionary<string, List<LineSymbolLayerDef>>();
        private static readonly Dictionary<string, List<FillSymbolLayerDef>> _savedSingleFill = new Dictionary<string, List<FillSymbolLayerDef>>();

        private readonly LayerItem _layerItem;
        private readonly Layer _layer;
        private bool _loading;
        private bool _syncingLayerUi;
        private GeometryType _geomType = GeometryType.Unknown;
        private bool _symbologyEditable;
        private DrawingColor _pointColor = DrawingColor.FromArgb(255, 88, 166, 255);
        private DrawingColor _lineColor = DrawingColor.FromArgb(255, 88, 166, 255);
        private DrawingColor _fillColor = DrawingColor.FromArgb(128, 88, 166, 255);
        private DrawingColor _outlineColor = DrawingColor.FromArgb(255, 88, 166, 255);
        private double _outlineWidth = 1;
        private readonly List<LineSymbolTemplateDef> _lineTemplates = new List<LineSymbolTemplateDef>();
        private readonly List<PointSymbolTemplateDef> _pointTemplates = new List<PointSymbolTemplateDef>();
        private readonly ObservableCollection<FillSymbolLayerDef> _fillLayers = new ObservableCollection<FillSymbolLayerDef>();
        private List<PointSymbolLayerDef> _singleSymPointLayers = new List<PointSymbolLayerDef>();
        private List<LineSymbolLayerDef> _singleSymLineLayers = new List<LineSymbolLayerDef>();
        private int _liveApplySeq;
        private readonly ObservableCollection<ClassDefinition> _classes = new ObservableCollection<ClassDefinition>();
        private DrawingColor _labelColor = DrawingColor.Red;
        private string _fontFamily = "";
        private readonly List<string> _fieldNames = new List<string>();
        public bool Applied { get; private set; }

        public LayerPropertiesDialog(LayerItem layerItem)
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                string innerInfo = "";
                var inner = ex.InnerException;
                while (inner != null)
                {
                    innerInfo += $"\n  • {inner.GetType().Name}: {inner.Message}\n    {inner.StackTrace?.Replace("\n", "\n    ")}";
                    inner = inner.InnerException;
                }
                MessageBox.Show($"XAML加载失败:\n{ex.GetType().Name}: {ex.Message}\n{innerInfo}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _layerItem = layerItem ?? throw new ArgumentNullException(nameof(layerItem));
            _layer = layerItem.Layer ?? throw new ArgumentNullException(nameof(layerItem.Layer));
            TxtTitle.Text = $"图层属性 — {layerItem.Name}";
            Title = TxtTitle.Text;
            if (ClassGrid != null) ClassGrid.ItemsSource = _classes;
            Loaded += async (s, e) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            _loading = true;
            try
            {
                TxtLayerName.Text = _layer.Name ?? "";
                TxtLayerType.Text = GetLayerTypeName(_layer);
                TxtSourcePath.Text = string.IsNullOrEmpty(_layerItem.FilePath) ? "(内存图层)" : _layerItem.FilePath;
                TxtStorage.Text = GetStorageInfo(_layer, _layerItem);
                var sr = await GetSpatialReferenceAsync(_layer);
                if (sr != null)
                {
                    string wkText = sr.WkText ?? "";
                    string unitName = sr.Unit != null ? sr.Unit.Name : "";
                    string kind = sr.IsGeographic ? "地理坐标系" : (sr.IsProjected ? "投影坐标系" : "坐标系");
                    TxtSpatialRef.Text = string.IsNullOrEmpty(wkText)
                        ? $"{kind}" + (string.IsNullOrEmpty(unitName) ? "" : $" · 单位: {unitName}")
                        : (wkText.Length > 200 ? wkText.Substring(0, 200) + "..." : wkText);
                    TxtWkid.Text = sr.Wkid > 0 ? sr.Wkid.ToString() : (sr.VerticalWkid > 0 ? $"V:{sr.VerticalWkid}" : "—");
                }
                else { TxtSpatialRef.Text = "未知"; TxtWkid.Text = "—"; }
                TxtGeomType.Text = await GetGeometryTypeAsync(_layer);
                TxtFeatureCount.Text = await GetFeatureCountAsync(_layer);
                TxtExtent.Text = await GetExtentTextAsync(_layer);
                await LoadFieldNamesAsync();
                InitFontCombo();
                LoadLabelSettings();
                LoadSymbology();
                double opacityPct = Math.Max(0, Math.Min(100, _layer.Opacity * 100.0));
                SldOpacity.Value = opacityPct;
                TxtOpacityVal.Text = $"{opacityPct:F0}%";
                TxtMaxScale.Text = _layer.MaxScale > 0 ? _layer.MaxScale.ToString("F0") : "0";
                TxtMinScale.Text = _layer.MinScale > 0 ? _layer.MinScale.ToString("F0") : "0";
                ChkVisible.IsChecked = _layer.IsVisible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载图层属性失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _loading = false;
                await RefreshSymbolPreviewAsync();
                RefreshLabelPreview();
            }
        }

        private static string GetLayerTypeName(Layer layer)
        {
            if (layer is FeatureLayer) return "要素图层 (FeatureLayer)";
            if (layer is FeatureCollectionLayer) return "要素集合图层 (FeatureCollectionLayer)";
            if (layer is RasterLayer) return "栅格图层 (RasterLayer)";
            if (layer is ArcGISMapImageLayer) return "地图服务图层 (MapImageLayer)";
            if (layer is ArcGISTiledLayer) return "切片图层 (TiledLayer)";
            return layer.GetType().Name;
        }

        private static string GetStorageInfo(Layer layer, LayerItem item)
        {
            if (item.MemoryFeatureTable != null) return $"内存表 · 字段数 {item.MemoryFeatureTable.Fields?.Count ?? 0}";
            if (layer is FeatureLayer fl && fl.FeatureTable != null)
            {
                var t = fl.FeatureTable; string tn = t.TableName;
                if (string.IsNullOrEmpty(tn)) tn = t.DisplayName;
                if (t is ServiceFeatureTable sft) return $"要素服务 · {(sft.Source != null ? sft.Source.ToString() : tn)}";
                if (t is GeodatabaseFeatureTable gft) return $"地理数据库 · {gft.TableName}";
                if (t is ShapefileFeatureTable shp) return $"Shapefile · {(string.IsNullOrEmpty(shp.Path) ? tn : shp.Path)}";
                if (t is FeatureCollectionTable) return $"要素集合表 · {tn}";
                return t.GetType().Name + (string.IsNullOrEmpty(tn) ? "" : " · " + tn);
            }
            if (layer is FeatureCollectionLayer fcl) return $"要素集合 · {fcl.FeatureCollection?.Tables?.Count ?? 0} 个表";
            if (layer is RasterLayer rl && rl.Raster != null) return "栅格数据 · " + (rl.Raster.Path ?? "内存/未知路径");
            return "—";
        }

        private static async Task<SpatialReference> GetSpatialReferenceAsync(Layer layer)
        {
            try
            {
                if (layer.FullExtent?.SpatialReference != null) return layer.FullExtent.SpatialReference;
                if (layer is FeatureLayer fl && fl.FeatureTable != null)
                { if (fl.FeatureTable.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded) await fl.FeatureTable.LoadAsync(); return fl.FeatureTable.SpatialReference; }
                if (layer is FeatureCollectionLayer fcl)
                {
                    if (fcl.SpatialReference != null) return fcl.SpatialReference;
                    if (fcl.FeatureCollection?.Tables != null) foreach (var table in fcl.FeatureCollection.Tables) if (table.SpatialReference != null) return table.SpatialReference;
                }
                if (layer is RasterLayer rl && rl.Raster != null)
                { if (rl.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded) await rl.LoadAsync(); return rl.FullExtent?.SpatialReference; }
            } catch { } return null;
        }

        private static async Task<string> GetGeometryTypeAsync(Layer layer)
        {
            try
            {
                if (layer is FeatureLayer fl && fl.FeatureTable != null)
                { if (fl.FeatureTable.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded) await fl.FeatureTable.LoadAsync(); return fl.FeatureTable.GeometryType.ToString(); }
                if (layer is FeatureCollectionLayer fcl && fcl.FeatureCollection?.Tables != null)
                { var types = fcl.FeatureCollection.Tables.Select(t => t.GeometryType.ToString()).Distinct(); return string.Join(", ", types); }
                if (layer is RasterLayer) return "Raster";
            } catch { } return "—";
        }

        private async Task<string> GetFeatureCountAsync(Layer layer)
        {
            try
            {
                if (_layerItem.MemoryFeatureTable != null) return (await _layerItem.MemoryFeatureTable.QueryFeatureCountAsync(new QueryParameters())).ToString();
                if (layer is FeatureLayer fl && fl.FeatureTable != null)
                { if (fl.FeatureTable.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded) await fl.FeatureTable.LoadAsync(); return (await fl.FeatureTable.QueryFeatureCountAsync(new QueryParameters())).ToString(); }
                if (layer is FeatureCollectionLayer fcl && fcl.FeatureCollection?.Tables != null)
                { long total = 0; foreach (var table in fcl.FeatureCollection.Tables) total += await table.QueryFeatureCountAsync(new QueryParameters()); return total.ToString(); }
                if (layer is RasterLayer) return "(栅格，无要素计数)";
            } catch (Exception ex) { return $"查询失败 ({ex.Message})"; } return "—";
        }

        private static async Task<string> GetExtentTextAsync(Layer layer)
        {
            try
            {
                Envelope ext = layer.FullExtent;
                if (ext == null || ext.IsEmpty)
                { if (layer.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded) await layer.LoadAsync(); ext = layer.FullExtent; }
                if (ext == null || ext.IsEmpty) return "空 / 未知";
                return $"XMin: {ext.XMin:F6}\nYMin: {ext.YMin:F6}\nXMax: {ext.XMax:F6}\nYMax: {ext.YMax:F6}" + (ext.SpatialReference != null ? $"\nSR: WKID {ext.SpatialReference.Wkid}" : "");
            } catch (Exception ex) { return $"获取失败: {ex.Message}"; }
        }

        private async Task LoadFieldNamesAsync()
        {
            _fieldNames.Clear();
            try
            {
                FeatureTable table = null;
                if (_layer is FeatureLayer fl && fl.FeatureTable != null) table = fl.FeatureTable;
                else if (_layer is FeatureCollectionLayer fcl && fcl.FeatureCollection?.Tables != null) table = fcl.FeatureCollection.Tables.FirstOrDefault();
                if (table != null)
                { if (table.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded) await table.LoadAsync(); foreach (var field in table.Fields) _fieldNames.Add(field.Name); }
            } catch { }
            if (CboLabelField != null) { CboLabelField.Items.Clear(); foreach (var fn in _fieldNames) CboLabelField.Items.Add(fn); if (CboLabelField.Items.Count > 0) CboLabelField.SelectedIndex = 0; }
            if (CboClassField != null) { CboClassField.Items.Clear(); foreach (var fn in _fieldNames) CboClassField.Items.Add(fn); if (CboClassField.Items.Count > 0) CboClassField.SelectedIndex = 0; }
        }

        private void InitFontCombo()
        {
            if (CboFontFamily == null) return;
            CboFontFamily.Items.Clear();
            foreach (var ff in System.Windows.Media.Fonts.SystemFontFamilies.OrderBy(f => f.Source))
                CboFontFamily.Items.Add(ff.Source);
            CboFontFamily.SelectedItem = "Arial";
            if (CboFontFamily.SelectedIndex < 0 && CboFontFamily.Items.Count > 0) CboFontFamily.SelectedIndex = 0;
        }
        private void LoadLabelSettings()
        {
            FeatureLayer targetFl = null;
            if (_layer is FeatureLayer fl) targetFl = fl;
            else if (_layer is FeatureCollectionLayer fcl) targetFl = fcl.Layers?.OfType<FeatureLayer>().FirstOrDefault();
            if (targetFl == null) return;
            ChkLabelVisible.IsChecked = targetFl.LabelsEnabled;
            if (targetFl.LabelDefinitions != null && targetFl.LabelDefinitions.Count > 0)
            {
                var ld = targetFl.LabelDefinitions[0];
                var ts = ld.TextSymbol as TextSymbol;
                if (ts != null)
                {
                    _labelColor = DrawingColor.FromArgb((byte)ts.Color.A, (byte)ts.Color.R, (byte)ts.Color.G, (byte)ts.Color.B);
                    SetBorderColor(BrdLabelColor, _labelColor);
                    SldLabelFontSize.Value = Math.Max(6, Math.Min(72, ts.Size));
                    SldLabelOffsetX.Value = Math.Max(-50, Math.Min(50, ts.OffsetX));
                    SldLabelOffsetY.Value = Math.Max(-50, Math.Min(50, ts.OffsetY));
                    if (!string.IsNullOrEmpty(ts.FontFamily))
                    {
                        _fontFamily = ts.FontFamily;
                        int idx = CboFontFamily?.Items.IndexOf(ts.FontFamily) ?? -1;
                        if (idx >= 0) CboFontFamily.SelectedIndex = idx;
                    }
                }
            }
        }

        private string GetLabelPositionTag()
        {
            if (CboLabelPosition?.SelectedItem is ComboBoxItem item && item.Tag != null) return item.Tag.ToString();
            return "Center";
        }

        private void LabelSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (TxtLabelFontSize != null) TxtLabelFontSize.Text = SldLabelFontSize.Value.ToString("F0");
            if (TxtLabelOffsetX != null) TxtLabelOffsetX.Text = SldLabelOffsetX.Value.ToString("F0");
            if (TxtLabelOffsetY != null) TxtLabelOffsetY.Text = SldLabelOffsetY.Value.ToString("F0");
            RefreshLabelPreview();
        }

        private void BtnLabelColor_Click(object sender, RoutedEventArgs e)
        {
            _labelColor = PickColor(_labelColor);
            SetBorderColor(BrdLabelColor, _labelColor);
            RefreshLabelPreview();
        }

        private void RefreshLabelPreview()
        {
            if (LabelPreviewCanvas == null) return;
            LabelPreviewCanvas.Children.Clear();
            try
            {
                var field = CboLabelField?.SelectedItem?.ToString() ?? "字段";
                var pos = GetLabelPositionTag();
                double fontSize = Math.Max(6, SldLabelFontSize.Value);
                double ox = SldLabelOffsetX.Value; double oy = SldLabelOffsetY.Value;
                bool visible = ChkLabelVisible?.IsChecked == true;
                var fg = new SolidColorBrush(ToWpfColor(_labelColor));
                var bg = new System.Windows.Shapes.Rectangle { Width = 500, Height = 100, Fill = new SolidColorBrush(MediaColor.FromRgb(30, 40, 50)), Stroke = new SolidColorBrush(MediaColor.FromRgb(50, 60, 70)), StrokeThickness = 1 };
                Canvas.SetLeft(bg, 0); Canvas.SetTop(bg, 0); LabelPreviewCanvas.Children.Add(bg);
                double fcx = 250, fcy = 50;
                var marker = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(MediaColor.FromRgb(100, 160, 220)), Stroke = Brushes.White, StrokeThickness = 1 };
                Canvas.SetLeft(marker, fcx - 4); Canvas.SetTop(marker, fcy - 4); LabelPreviewCanvas.Children.Add(marker);
                if (!visible) fg = new SolidColorBrush(MediaColor.FromArgb(80, 150, 150, 150));
                double lx = fcx, ly = fcy;
                switch (pos)
                {
                    case "Top": ly -= 20; break; case "Bottom": ly += 14; break;
                    case "Left": lx -= 50; break; case "Right": lx += 16; break;
                    case "TopLeft": lx -= 40; ly -= 18; break; case "TopRight": lx += 16; ly -= 18; break;
                    case "BottomLeft": lx -= 40; ly += 14; break; case "BottomRight": lx += 16; ly += 14; break;
                }
                lx += ox; ly += oy;
                string ff = CboFontFamily?.SelectedItem?.ToString() ?? "";
                var txt = new TextBlock { Text = $"[{field}]", Foreground = fg, FontSize = Math.Max(8, fontSize * 0.6), FontWeight = FontWeights.Bold, FontFamily = !string.IsNullOrEmpty(ff) ? new System.Windows.Media.FontFamily(ff) : null };
                Canvas.SetLeft(txt, lx); Canvas.SetTop(txt, ly); LabelPreviewCanvas.Children.Add(txt);
                var posLabel = new TextBlock { Text = $"位置: {pos}  |  大小: {fontSize:F0}  |  字体: {(!string.IsNullOrEmpty(ff) ? ff : "Arial")}  |  偏移: ({ox:F0},{oy:F0})", Foreground = new SolidColorBrush(MediaColor.FromRgb(120, 130, 140)), FontSize = 10 };
                Canvas.SetLeft(posLabel, 10); Canvas.SetTop(posLabel, 82); LabelPreviewCanvas.Children.Add(posLabel);
            } catch { }
        }

        private void InitStyleCombos()
        {
            _pointTemplates.Clear(); _lineTemplates.Clear();
            _pointTemplates.AddRange(SymbolTemplateStore.LoadPointTemplates());
            _lineTemplates.AddRange(SymbolTemplateStore.LoadLineTemplates());
            CboRendererType.Items.Clear();
            CboRendererType.Items.Add(new ComboBoxItem { Content = "单一符号渲染", Tag = "SingleSymbol" });
            CboRendererType.Items.Add(new ComboBoxItem { Content = "分类渲染", Tag = "UniqueValues" });
            CboRendererType.SelectedIndex = 0;
        }

        private void LoadSymbology()
        {
            InitStyleCombos();
            SymbolPreviewCanvas.Children.Clear();
            if (ImgPatternPreview != null) ImgPatternPreview.Visibility = Visibility.Collapsed;
            _symbologyEditable = false;
            PanelPointSym.Visibility = Visibility.Collapsed;
            PanelLineSym.Visibility = Visibility.Collapsed;
            PanelFillSym.Visibility = Visibility.Collapsed;
            Renderer renderer = null; _geomType = GeometryType.Unknown;
            try
            {
                if (_layer is FeatureLayer fl) { renderer = fl.Renderer; if (fl.FeatureTable != null) _geomType = fl.FeatureTable.GeometryType; _symbologyEditable = true; }
                else if (_layer is FeatureCollectionLayer fcl)
                {
                    var table = fcl.FeatureCollection?.Tables?.FirstOrDefault();
                    if (table != null) _geomType = table.GeometryType;
                    var firstFl = fcl.Layers?.OfType<FeatureLayer>().FirstOrDefault();
                    if (firstFl != null) renderer = firstFl.Renderer ?? (table as FeatureCollectionTable)?.Renderer;
                    else if (table is FeatureCollectionTable fct) renderer = fct.Renderer;
                    _symbologyEditable = firstFl != null || table != null;
                }
                else if (_layer is RasterLayer) { TxtGeomHint.Text = "几何类型: Raster"; TxtSymbologyNote.Text = "栅格图层无法编辑点线面符号。"; DrawRasterPreview(); return; }
            } catch { }
            TxtGeomHint.Text = "几何类型: " + (_geomType == GeometryType.Unknown ? "—" : _geomType.ToString());
            Symbol symbol = null;
            bool isUvr = renderer is UniqueValueRenderer;
            if (renderer is SimpleRenderer sr) { symbol = sr.Symbol; }
            else if (renderer is UniqueValueRenderer uvr) { symbol = uvr.DefaultSymbol ?? uvr.UniqueValues?.FirstOrDefault()?.Symbol; LoadClassifiedFromRenderer(uvr); }
            else if (renderer is ClassBreaksRenderer cbr) { symbol = cbr.ClassBreaks?.FirstOrDefault()?.Symbol ?? cbr.DefaultSymbol; }
            else if (renderer != null) { }
            if (!isUvr) _classes.Clear();
            SelectComboByTag(CboRendererType, isUvr ? "UniqueValues" : "SingleSymbol");
            if (_geomType == GeometryType.Point || _geomType == GeometryType.Multipoint)
            {
                PanelPointSym.Visibility = Visibility.Visible;
                _singleSymPointLayers.Clear();
                if (!string.IsNullOrEmpty(_layerItem?.Name) && _savedSinglePoint.TryGetValue(_layerItem.Name, out var pCached))
                {
                    foreach (var l in pCached)
                        _singleSymPointLayers.Add(l.Clone());
                }
                else if (symbol is SimpleMarkerSymbol sms)
                    _singleSymPointLayers.Add(SymbolEditorDialog.SimpleMarkerToLayerDef(sms, _pointColor));
                else if (symbol is PictureMarkerSymbol pms)
                    _singleSymPointLayers.Add(SymbolEditorDialog.PictureMarkerToLayerDef(pms, _pointColor));
                else
                    _singleSymPointLayers.Add(new PointSymbolLayerDef { Kind = PointSymbolLayerKind.SimpleMarker, Color = _pointColor, Size = 10, MarkerStyle = "Circle" });
                DrawSinglePointPreview();
            }
            else if (_geomType == GeometryType.Polyline)
            {
                PanelLineSym.Visibility = Visibility.Visible;
                _singleSymLineLayers.Clear();
                // 优先从缓存恢复单符号线设置
                if (!string.IsNullOrEmpty(_layerItem?.Name) && _savedSingleLine.TryGetValue(_layerItem.Name, out var cached))
                {
                    foreach (var l in cached)
                        _singleSymLineLayers.Add(l.Clone());
                }
                else if (symbol is SimpleLineSymbol sls)
                {
                    _lineColor = sls.Color;
                    var def = new LineSymbolLayerDef
                    {
                        Kind = LineSymbolLayerKind.SimpleStroke,
                        Color = sls.Color,
                        Width = Math.Max(0.5, Math.Min(20, sls.Width)),
                        StrokeStyle = ResolveLineStrokeStyle(sls),
                        Enabled = true
                    };
                    if (sls.MarkerStyle == SimpleLineSymbolMarkerStyle.Arrow)
                    {
                        def.Kind = LineSymbolLayerKind.ArrowLine;
                        if (sls.MarkerPlacement == SimpleLineSymbolMarkerPlacement.Begin) def.ArrowPlacement = "Begin";
                        else if (sls.MarkerPlacement == SimpleLineSymbolMarkerPlacement.End) def.ArrowPlacement = "End";
                        else def.ArrowPlacement = "BeginAndEnd";
                    }
                    _singleSymLineLayers.Add(def);
                }
                else
                {
                    _singleSymLineLayers.Add(new LineSymbolLayerDef { Color = _lineColor, Width = 2, StrokeStyle = "Solid" });
                }
                DrawSingleLinePreview();
            }
            else if (_geomType == GeometryType.Polygon || _geomType == GeometryType.Envelope)
            {
                PanelFillSym.Visibility = Visibility.Visible;
                _fillLayers.Clear();
                // 优先从缓存恢复单符号面设置
                if (!string.IsNullOrEmpty(_layerItem?.Name) && _savedSingleFill.TryGetValue(_layerItem.Name, out var cachedFill))
                {
                    _outlineWidth = 1;
                    foreach (var l in cachedFill)
                        _fillLayers.Add(l.Clone());
                    _fillColor = _fillLayers.FirstOrDefault()?.Color ?? DrawingColor.FromArgb(128, 88, 166, 255);
                }
                else if (symbol is SimpleFillSymbol sfs)
                {
                    _fillColor = sfs.Color;
                    if (sfs.Outline != null) { _outlineColor = sfs.Outline.Color; _outlineWidth = Math.Max(0, Math.Min(12, sfs.Outline.Width)); }
                    foreach (var d in PatternFillHelper.CreateDefaultLayers(sfs.Color)) _fillLayers.Add(d);
                }
                else if (symbol is PictureFillSymbol pfs)
                {
                    _fillColor = DrawingColor.FromArgb(200, 128, 128, 128);
                    if (pfs.Outline != null) { _outlineColor = pfs.Outline.Color; _outlineWidth = Math.Max(0, Math.Min(12, pfs.Outline.Width)); }
                    foreach (var d in PatternFillHelper.CreateDefaultLayers(_fillColor)) _fillLayers.Add(d);
                }
                else
                {
                    _fillColor = DrawingColor.FromArgb(128, 88, 166, 255);
                    _outlineWidth = 1;
                    foreach (var d in PatternFillHelper.CreateDefaultLayers(_fillColor)) _fillLayers.Add(d);
                }
                DrawSingleFillPreview();
            }
            else { TxtSymbologyNote.Text = "当前图层几何类型未知，无法编辑符号。"; }
            if (!_symbologyEditable) TxtSymbologyNote.Text = "当前图层类型不支持在此修改符号。";
            PanelClassified.Visibility = isUvr ? Visibility.Visible : Visibility.Collapsed;
            UpdateAutoMatchButtonVisibility(isUvr);
            if (isUvr)
            {
                if (_geomType == GeometryType.Point || _geomType == GeometryType.Multipoint) PanelPointSym.Visibility = Visibility.Collapsed;
                else if (_geomType == GeometryType.Polyline) PanelLineSym.Visibility = Visibility.Collapsed;
                TxtSymbologyNote.Visibility = Visibility.Collapsed;
                PanelSymbolPreview.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateAutoMatchButtonVisibility(bool visible)
        {
            bool poly = _geomType == GeometryType.Polygon || _geomType == GeometryType.Envelope;
            bool line = _geomType == GeometryType.Polyline;
            bool pt = _geomType == GeometryType.Point || _geomType == GeometryType.Multipoint;
            if (BtnLithoAutoFill != null) BtnLithoAutoFill.Visibility = visible && poly ? Visibility.Visible : Visibility.Collapsed;
            if (BtnLineAutoMatch != null) BtnLineAutoMatch.Visibility = visible && line ? Visibility.Visible : Visibility.Collapsed;
            if (BtnPointAutoMatch != null) BtnPointAutoMatch.Visibility = visible && pt ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadClassifiedFromRenderer(UniqueValueRenderer uvr)
        {
            _classes.Clear(); if (uvr?.UniqueValues == null) return;
            // 尝试恢复之前保存的自定义符号层数据
            List<ClassDefSymData> saved = null;
            if (!string.IsNullOrEmpty(_layerItem?.Name) && _savedClassData.TryGetValue(_layerItem.Name, out saved)) { }
            var ramp = GetCurrentColorRamp(); int idx = 0;
            foreach (var uv in uvr.UniqueValues)
            {
                string val = uv.Values?.FirstOrDefault()?.ToString() ?? ""; string label = uv.Label ?? val;
                DrawingColor c = ramp.Count > 0 ? ramp[idx % ramp.Count] : DrawingColor.FromArgb(255, 88, 166, 255);
                if (uv.Symbol is SimpleFillSymbol sfs) c = sfs.Color;
                var cd = new ClassDefinition { Value = val, Label = label, Color = c };
                // 从缓存恢复 PointLayers / FillLayers / LineLayers / FillMode / FillColor / OutlineColor / OutlineWidth
                if (saved != null)
                {
                    var match = saved.FirstOrDefault(s => s.Value == val);
                    if (match != null)
                    {
                        if (match.PointLayers != null)
                            foreach (var pl in match.PointLayers) cd.PointLayers.Add(pl.Clone());
                        if (match.FillLayers != null)
                            foreach (var fl in match.FillLayers) cd.FillLayers.Add(fl.Clone());
                        if (match.LineLayers != null)
                            foreach (var ll in match.LineLayers) cd.LineLayers.Add(ll.Clone());
                        if (!string.IsNullOrEmpty(match.FillMode)) cd.FillMode = match.FillMode;
                        cd.FillColor = match.FillColor.A > 0 ? match.FillColor : DrawingColor.FromArgb(128, c.R, c.G, c.B);
                        cd.OutlineColor = match.OutlineColor.A > 0 ? match.OutlineColor : DrawingColor.FromArgb(255, c.R, c.G, c.B);
                        cd.OutlineWidth = match.OutlineWidth > 0 ? match.OutlineWidth : 1;
                    }
                }
                if (cd.FillLayers.Count == 0)
                {
                    cd.FillLayers.Clear();
                    foreach (var d in PatternFillHelper.CreateDefaultLayers(cd.FillColor))
                        cd.FillLayers.Add(d);
                }
                _classes.Add(cd); idx++;
            }
            RefreshClassList();
            if (uvr.FieldNames != null && uvr.FieldNames.Count > 0 && CboClassField != null) SelectCombo(CboClassField, uvr.FieldNames[0]);
        }

        private List<DrawingColor> GetCurrentColorRamp()
        {
            string tag = "RYG";
            if (CboColorRamp?.SelectedItem is ComboBoxItem item && item.Tag != null) tag = item.Tag.ToString();
            return GenerateColorRamp(tag, Math.Max(8, _classes.Count));
        }

        private static List<DrawingColor> GenerateColorRamp(string tag, int count)
        {
            var colors = new List<DrawingColor>(); if (count < 2) count = 2;
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / (count - 1); DrawingColor c;
                switch (tag)
                {
                    case "RYG": c = t < 0.5 ? DrawingColor.FromArgb(255, (int)(220 * t * 2), (int)(60 + 180 * t * 2), 40) : DrawingColor.FromArgb(255, 220, (int)(240 - 180 * (t - 0.5) * 2), 40); break;
                    case "BPR": c = t < 0.5 ? DrawingColor.FromArgb(255, (int)(80 + 100 * t * 2), (int)(60 + 20 * t * 2), (int)(200 + 55 * t * 2)) : DrawingColor.FromArgb(255, (int)(180 + 75 * (t - 0.5) * 2), (int)(80 - 30 * (t - 0.5) * 2), (int)(255 - 155 * (t - 0.5) * 2)); break;
                    case "MonoBlue": c = DrawingColor.FromArgb(255, (int)(40 + 50 * t), (int)(80 + 80 * t), (int)(150 + 105 * t)); break;
                    case "MonoGreen": c = DrawingColor.FromArgb(255, (int)(30 + 30 * t), (int)(100 + 120 * t), (int)(50 + 50 * t)); break;
                    case "Rainbow": double hue = (1.0 - t) * 240.0; c = HslToRgb(hue, 0.85, 0.55); break;
                    default: c = DrawingColor.FromArgb(255, (int)(88 + 100 * t), (int)(166 - 80 * t), (int)(255 - 100 * t)); break;
                }
                colors.Add(c);
            }
            return colors;
        }

        private static DrawingColor HslToRgb(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s; double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1)); double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; } else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return DrawingColor.FromArgb(255, (int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        private void RefreshClassList()
        {
            if (ClassGrid == null) return;
            ClassGrid.ItemsSource = null; ClassGrid.ItemsSource = _classes;
            RefreshClassPreviews();
            RefreshColorRampPreview();
        }

        private void RefreshClassPreviews()
        {
            if (_classes == null) return;
            foreach (var cd in _classes)
                cd.PreviewImage = RenderSymbolPreview(cd);
        }

        private ImageSource RenderSymbolPreview(ClassDefinition cd)
        {
            var canvas = new Canvas { Width = 46, Height = 18 };
            double cx = 23, cy = 9;
            try
            {
                if (_geomType == GeometryType.Point || _geomType == GeometryType.Multipoint)
                {
                    Symbol sym = BuildPointSymbolFromLayers(cd.PointLayers, cd.Color);
                    DrawPreviewSymbolOnCanvas(canvas, sym, cx, cy, 12);
                }
                else if (_geomType == GeometryType.Polyline)
                {
                    var enabled = (cd.LineLayers?.Where(l => l.Enabled).ToList()) ?? new List<LineSymbolLayerDef>();
                    if (enabled.Count > 0)
                    {
                        // 逐个绘制启用的线层：主线 + 箭头层叠加显示
                        bool arrowDrawn = false;
                        foreach (var def in enabled)
                        {
                            double w = Math.Max(1, Math.Min(5, def.Width * Math.Max(0.1, def.Scale)));
                            var brush = new SolidColorBrush(ToWpfColor(def.Color));
                            if (def.Kind == LineSymbolLayerKind.ArrowLine || def.Kind == LineSymbolLayerKind.MarkerLine)
                            {
                                double asz = 5;
                                bool hasBegin = (def.Kind == LineSymbolLayerKind.ArrowLine && (def.ArrowPlacement == "Begin" || def.ArrowPlacement == "BeginAndEnd")) || (def.Kind == LineSymbolLayerKind.MarkerLine && (def.MarkerPlacement == "Begin" || def.MarkerPlacement == "BeginAndEnd"));
                                bool hasEnd = (def.Kind == LineSymbolLayerKind.ArrowLine && (def.ArrowPlacement == "End" || def.ArrowPlacement == "BeginAndEnd")) || (def.Kind == LineSymbolLayerKind.MarkerLine && (def.MarkerPlacement == "End" || def.MarkerPlacement == "BeginAndEnd"));
                                if (hasEnd) { var pts = new System.Windows.Media.PointCollection { new Point(42, cy), new Point(42 - asz, cy - asz / 2), new Point(42 - asz, cy + asz / 2) }; canvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts, Fill = brush }); }
                                if (hasBegin) { var pts = new System.Windows.Media.PointCollection { new Point(4, cy), new Point(4 + asz, cy - asz / 2), new Point(4 + asz, cy + asz / 2) }; canvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts, Fill = brush }); }
                                arrowDrawn = true;
                            }
                            else
                            {
                                var line = new Line { X1 = 4, Y1 = cy, X2 = 42, Y2 = cy, Stroke = brush, StrokeThickness = w, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                                switch (def.StrokeStyle) { case "Dash": line.StrokeDashArray = new DoubleCollection(new[] { 6.0, 3.0 }); break; case "Dot": line.StrokeDashArray = new DoubleCollection(new[] { 1.0, 3.0 }); break; case "DashDot": line.StrokeDashArray = new DoubleCollection(new[] { 6.0, 3.0, 1.5, 3.0 }); break; case "DashDotDot": line.StrokeDashArray = new DoubleCollection(new[] { 6.0, 3.0, 1.5, 3.0, 1.5, 3.0 }); break; }
                                canvas.Children.Add(line);
                            }
                            _ = arrowDrawn;
                        }
                    }
                    else
                    {
                        canvas.Children.Add(new Line { X1 = 4, Y1 = cy, X2 = 42, Y2 = cy, Stroke = new SolidColorBrush(ToWpfColor(cd.Color)), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                    }
                }
                else
                {
                    Brush fillBrush = new SolidColorBrush(ToWpfColor(cd.FillColor));
                    if (cd.FillLayers != null && cd.FillLayers.Count > 0)
                    {
                        var enabled = cd.FillLayers.Where(l => l.Enabled).ToList();
                        if (enabled.Count > 0 && !(enabled.Count == 1 && enabled[0].Kind == FillSymbolLayerKind.Solid))
                        {
                            try
                            {
                                int tw = 40, th = 14;
                                using (var bmp = PatternFillHelper.GenerateCompositeTile(enabled, 64))
                                using (var full = new System.Drawing.Bitmap(tw, th))
                                using (var g = System.Drawing.Graphics.FromImage(full))
                                {
                                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                                    g.DrawImage(bmp, 0, 0, tw, th);
                                    using (var ms = new MemoryStream())
                                    {
                                        full.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                        ms.Position = 0;
                                        var bi = new BitmapImage();
                                        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
                                        fillBrush = new ImageBrush(bi) { Stretch = Stretch.Fill };
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = 40, Height = 14,
                        Fill = fillBrush,
                        Stroke = new SolidColorBrush(ToWpfColor(cd.OutlineColor)),
                        StrokeThickness = Math.Max(0.5, cd.OutlineWidth)
                    };
                    Canvas.SetLeft(rect, cx - 20);
                    Canvas.SetTop(rect, cy - 7);
                    canvas.Children.Add(rect);
                }
            }
            catch { }
            canvas.Measure(new Size(46, 18));
            canvas.Arrange(new Rect(0, 0, 46, 18));
            var rtb = new RenderTargetBitmap(46, 18, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(canvas);
            return rtb;
        }

        private void DrawPreviewSymbolOnCanvas(Canvas canvas, Symbol symbol, double cx, double cy, double sz)
        {
            Symbol sym = symbol;
            if (sym is CompositeSymbol cs && cs.Symbols.Count > 0)
                sym = cs.Symbols[0];
            if (sym is SimpleMarkerSymbol sms)
            {
                var fill = new SolidColorBrush(ToWpfColor(sms.Color));
                var stroke = sms.Outline != null ? new SolidColorBrush(ToWpfColor(sms.Outline.Color)) : new SolidColorBrush(ToWpfColor(System.Drawing.Color.Gray));
                double sw = sms.Outline?.Width ?? 0.5;
                switch (sms.Style)
                {
                    case SimpleMarkerSymbolStyle.Circle:
                        var e = new Ellipse { Width = sz, Height = sz, Fill = fill, Stroke = stroke, StrokeThickness = sw };
                        Canvas.SetLeft(e, cx - sz / 2); Canvas.SetTop(e, cy - sz / 2);
                        canvas.Children.Add(e);
                        break;
                    case SimpleMarkerSymbolStyle.Square:
                        var r = new System.Windows.Shapes.Rectangle { Width = sz, Height = sz, Fill = fill, Stroke = stroke, StrokeThickness = sw };
                        Canvas.SetLeft(r, cx - sz / 2); Canvas.SetTop(r, cy - sz / 2);
                        canvas.Children.Add(r);
                        break;
                    case SimpleMarkerSymbolStyle.Cross:
                        double hw = sz * 0.45;
                        canvas.Children.Add(new Line { X1 = cx - hw, Y1 = cy, X2 = cx + hw, Y2 = cy, Stroke = fill, StrokeThickness = Math.Max(2, sz / 5) });
                        canvas.Children.Add(new Line { X1 = cx, Y1 = cy - hw, X2 = cx, Y2 = cy + hw, Stroke = fill, StrokeThickness = Math.Max(2, sz / 5) });
                        break;
                    case SimpleMarkerSymbolStyle.X:
                        double a = sz * 0.45 * 0.707;
                        canvas.Children.Add(new Line { X1 = cx - a, Y1 = cy - a, X2 = cx + a, Y2 = cy + a, Stroke = fill, StrokeThickness = Math.Max(2, sz / 5) });
                        canvas.Children.Add(new Line { X1 = cx + a, Y1 = cy - a, X2 = cx - a, Y2 = cy + a, Stroke = fill, StrokeThickness = Math.Max(2, sz / 5) });
                        break;
                    case SimpleMarkerSymbolStyle.Diamond:
                        var poly = new System.Windows.Shapes.Polygon
                        {
                            Points = new System.Windows.Media.PointCollection { new System.Windows.Point(cx, cy - sz / 2), new System.Windows.Point(cx + sz / 2, cy), new System.Windows.Point(cx, cy + sz / 2), new System.Windows.Point(cx - sz / 2, cy) },
                            Fill = fill, Stroke = stroke, StrokeThickness = sw
                        };
                        canvas.Children.Add(poly);
                        break;
                    case SimpleMarkerSymbolStyle.Triangle:
                        var tri = new System.Windows.Shapes.Polygon
                        {
                            Points = new System.Windows.Media.PointCollection { new System.Windows.Point(cx, cy - sz / 2), new System.Windows.Point(cx + sz / 2, cy + sz / 2), new System.Windows.Point(cx - sz / 2, cy + sz / 2) },
                            Fill = fill, Stroke = stroke, StrokeThickness = sw
                        };
                        canvas.Children.Add(tri);
                        break;
                    default:
                        var def = new Ellipse { Width = sz, Height = sz, Fill = fill, Stroke = stroke, StrokeThickness = sw };
                        Canvas.SetLeft(def, cx - sz / 2); Canvas.SetTop(def, cy - sz / 2);
                        canvas.Children.Add(def);
                        break;
                }
            }
            else if (sym is SimpleLineSymbol sls)
            {
                canvas.Children.Add(new Line
                {
                    X1 = 4, Y1 = cy, X2 = 42, Y2 = cy,
                    Stroke = new SolidColorBrush(ToWpfColor(sls.Color)),
                    StrokeThickness = Math.Max(2, Math.Min(8, sls.Width))
                });
            }
            else if (sym is SimpleFillSymbol sfs)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = 40, Height = 14,
                    Fill = new SolidColorBrush(ToWpfColor(sfs.Color)),
                    Stroke = sfs.Outline != null ? new SolidColorBrush(ToWpfColor(sfs.Outline.Color)) : Brushes.Gray,
                    StrokeThickness = sfs.Outline != null ? Math.Max(0.5, sfs.Outline.Width) : 0.5
                };
                Canvas.SetLeft(rect, cx - 20); Canvas.SetTop(rect, cy - 7);
                canvas.Children.Add(rect);
            }
            else if (sym is PictureMarkerSymbol pms)
            {
                try
                {
                    string url = pms.Uri != null ? pms.Uri.ToString() : "";
                    if (string.IsNullOrEmpty(url)) return;
                    var bi = new BitmapImage(new Uri(url));
                    var img = new System.Windows.Controls.Image
                    {
                        Source = bi,
                        Width = sz,
                        Height = sz,
                        Stretch = Stretch.Uniform
                    };
                    Canvas.SetLeft(img, cx - sz / 2); Canvas.SetTop(img, cy - sz / 2);
                    canvas.Children.Add(img);
                }
                catch { }
            }
        }

        private void RefreshColorRampPreview()
        {
            if (BrdColorRampPreview == null) return;
            try
            {
                var ramp = GetCurrentColorRamp(); if (ramp.Count < 2) return;
                var dg = new DrawingGroup();
                for (int i = 0; i < ramp.Count; i++) { double w = 1.0 / ramp.Count; var rect = new RectangleGeometry(new Rect(i * w, 0, w + 0.001, 1)); dg.Children.Add(new GeometryDrawing(new SolidColorBrush(ToWpfColor(ramp[i])), null, rect)); }
                BrdColorRampPreview.Background = new DrawingBrush(dg) { Stretch = Stretch.Fill };
            } catch { }
        }

        private void CboClassField_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_loading) return; }
        private void CboColorRamp_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var ramp = GetCurrentColorRamp();
            for (int i = 0; i < _classes.Count; i++)
            {
                var c = ramp[i % ramp.Count];
                _classes[i].Color = c;
                _classes[i].FillColor = DrawingColor.FromArgb(128, c.R, c.G, c.B);
                _classes[i].OutlineColor = DrawingColor.FromArgb(255, c.R, c.G, c.B);
            }
            RefreshClassList();
        }
        private async void BtnAddClass_Click(object sender, RoutedEventArgs e)
        {
            string field = CboClassField?.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(field)) { await QueryUniqueValuesAsync(); return; }
            var ramp = GetCurrentColorRamp(); int idx = _classes.Count; DrawingColor c = ramp.Count > 0 ? ramp[idx % ramp.Count] : DrawingColor.FromArgb(255, 88, 166, 255); string val = $"值{idx + 1}"; _classes.Add(new ClassDefinition { Value = val, Label = val, Color = c }); RefreshClassList();
        }
        private async Task QueryUniqueValuesAsync()
        {
            string field = CboClassField?.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(field)) return;
            try
            {
                FeatureTable table = null;
                if (_layer is FeatureLayer fl && fl.FeatureTable != null) table = fl.FeatureTable;
                else if (_layer is FeatureCollectionLayer fcl && fcl.FeatureCollection?.Tables != null)
                {
                    var t = fcl.FeatureCollection.Tables.FirstOrDefault(tbl => _geomType == GeometryType.Unknown || tbl.GeometryType == _geomType);
                    if (t == null) t = fcl.FeatureCollection.Tables.FirstOrDefault();
                    table = t;
                }
                if (table == null) return;
                if (table.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded) await table.LoadAsync();
                var features = await table.QueryFeaturesAsync(new QueryParameters());
                var values = new List<string>();
                foreach (var f in features)
                {
                    object attrVal = null;
                    if (f.Attributes != null && f.Attributes.TryGetValue(field, out attrVal) && attrVal != null)
                    {
                        string s = attrVal.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) values.Add(s);
                    }
                }
                values = values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v).ToList();
                _classes.Clear();
                var ramp = GetCurrentColorRamp();
                for (int i = 0; i < values.Count; i++)
                {
                    DrawingColor c = ramp.Count > 0 ? ramp[i % ramp.Count] : DrawingColor.FromArgb(255, 88, 166, 255);
                    _classes.Add(new ClassDefinition { Value = values[i], Label = values[i], Color = c });
                }
                if (_classes.Count > 0) { RefreshClassList(); }
                else { MessageBox.Show($"字段「{field}」未查询到数据值。", "提示", MessageBoxButton.OK, MessageBoxImage.Information); }
            }
            catch (Exception ex) { MessageBox.Show($"查询字段值失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private async void CellColorBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_loading) return;
            var border = sender as Border; if (border == null) return;
            var cd = border.DataContext as ClassDefinition; if (cd == null) return;
            var dlg = new SymbolEditorDialog(cd, _geomType, _fieldNames);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                if (_geomType == GeometryType.Point || _geomType == GeometryType.Multipoint)
                    cd.Color = cd.PointLayers.FirstOrDefault()?.Color ?? cd.Color;
                else if (_geomType == GeometryType.Polyline)
                    cd.Color = cd.LineLayers.FirstOrDefault()?.Color ?? cd.Color;
                else
                    cd.Color = cd.FillColor;
                RefreshColorRampPreview();
                RefreshClassList();
                if (!_loading && _symbologyEditable)
                    await LiveApplyToMapAsync();
            }
        }
        private void ClassGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (_loading) return;
            var cd = e.Row.DataContext as ClassDefinition; if (cd == null) return;
            var tb = e.EditingElement as TextBox; if (tb == null) return;
            if (e.Column.Header?.ToString() == "类别名") cd.Value = tb.Text ?? "";
            else if (e.Column.Header?.ToString() == "标注名") cd.Label = tb.Text ?? "";
        }
        private void ClassGrid_LoadingRow(object sender, DataGridRowEventArgs e) { }
        private void BtnRemoveClass_Click(object sender, RoutedEventArgs e)
        {
            var sel = ClassGrid?.SelectedItem as ClassDefinition;
            if (sel != null) _classes.Remove(sel);
        }

        private bool IsClassifiedMode()
        {
            if (CboRendererType?.SelectedItem is ComboBoxItem ri && ri.Tag != null)
                return ri.Tag.ToString() == "UniqueValues";
            return false;
        }

        private static void SelectCombo(ComboBox cbo, string value)
        {
            for (int i = 0; i < cbo.Items.Count; i++)
            {
                if (string.Equals(cbo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase)) { cbo.SelectedIndex = i; return; }
                if (cbo.Items[i] is ISymbolTemplateEntry entry && string.Equals(entry.SelectionKey, value, StringComparison.OrdinalIgnoreCase)) { cbo.SelectedIndex = i; return; }
            }
            if (cbo.Items.Count > 0) cbo.SelectedIndex = 0;
        }
        private static void SelectComboByTag(ComboBox cbo, string tag)
        {
            for (int i = 0; i < cbo.Items.Count; i++)
            {
                if (cbo.Items[i] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase)) { cbo.SelectedIndex = i; return; }
            }
        }
        private void CboRendererType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            bool classified = IsClassifiedMode();
            PanelPointSym.Visibility = (!classified && _geomType == GeometryType.Point) ? Visibility.Visible : Visibility.Collapsed;
            PanelLineSym.Visibility = (!classified && _geomType == GeometryType.Polyline) ? Visibility.Visible : Visibility.Collapsed;
            PanelFillSym.Visibility = (!classified && (_geomType == GeometryType.Polygon || _geomType == GeometryType.Envelope)) ? Visibility.Visible : Visibility.Collapsed;
            PanelClassified.Visibility = classified ? Visibility.Visible : Visibility.Collapsed;
            UpdateAutoMatchButtonVisibility(classified);
            TxtSymbologyNote.Visibility = classified ? Visibility.Collapsed : Visibility.Visible;
            PanelSymbolPreview.Visibility = classified ? Visibility.Collapsed : Visibility.Visible;
            if (classified && _classes.Count == 0) _ = QueryUniqueValuesAsync();
            if (!classified) _ = RefreshSymbolPreviewAsync(true);
        }
        private static void SetBorderColor(Border brd, DrawingColor c) { if (brd != null) brd.Background = new SolidColorBrush(ToWpfColor(c)); }
        private static MediaColor ToWpfColor(DrawingColor c) { return MediaColor.FromArgb(c.A, c.R, c.G, c.B); }
        private DrawingColor PickColor(DrawingColor current) { try { var dlg = new System.Windows.Forms.ColorDialog { Color = current, FullOpen = true }; if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) return dlg.Color; } catch { } return current; }


        private static string ResolveLineTemplateKey(SimpleLineSymbol symbol)
        {
            if (symbol == null) return LineSymbolPresetKind.Solid.ToString();
            if (symbol.MarkerStyle == SimpleLineSymbolMarkerStyle.Arrow)
            {
                if (symbol.MarkerPlacement == SimpleLineSymbolMarkerPlacement.Begin) return LineSymbolPresetKind.ReverseFault.ToString();
                if (symbol.MarkerPlacement == SimpleLineSymbolMarkerPlacement.End) { if (symbol.Style == SimpleLineSymbolStyle.DashDotDot) return LineSymbolPresetKind.Thrust.ToString(); return LineSymbolPresetKind.NormalFault.ToString(); }
                if (symbol.MarkerPlacement == SimpleLineSymbolMarkerPlacement.BeginAndEnd) { if (symbol.Style == SimpleLineSymbolStyle.DashDotDot) return LineSymbolPresetKind.IntrusionContact.ToString(); return LineSymbolPresetKind.StrikeSlip.ToString(); }
            }
            switch (symbol.Style) { case SimpleLineSymbolStyle.Dash: return LineSymbolPresetKind.Dash.ToString(); case SimpleLineSymbolStyle.Dot: return LineSymbolPresetKind.Dot.ToString(); case SimpleLineSymbolStyle.DashDot: return LineSymbolPresetKind.DashDot.ToString(); case SimpleLineSymbolStyle.DashDotDot: return LineSymbolPresetKind.DashDotDot.ToString(); default: return LineSymbolPresetKind.Solid.ToString(); }
        }

        private async Task<Symbol> BuildSymbolFromUiAsync()
        {
            if (_geomType == GeometryType.Point || _geomType == GeometryType.Multipoint)
                return BuildPointSymbolFromLayers(new ObservableCollection<PointSymbolLayerDef>(_singleSymPointLayers), _pointColor);
            if (_geomType == GeometryType.Polyline) return BuildLineSymbolFromLayers(_singleSymLineLayers, _lineColor);
            if (_geomType == GeometryType.Polygon || _geomType == GeometryType.Envelope)
            {
                var enabled = _fillLayers.Where(l => l.Enabled).ToList();
                if (enabled.Count == 1 && enabled[0].Kind == FillSymbolLayerKind.Solid)
                {
                    var c = PatternFillHelper.WithOpacity(enabled[0].Color, enabled[0].Opacity);
                    SimpleLineSymbol ol = _outlineWidth > 0.01 ? new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, _outlineColor, _outlineWidth) : null;
                    return new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, c, ol);
                }
                return await PatternFillHelper.BuildFillSymbolAsync(enabled, _outlineColor, _outlineWidth, 64);
            }
            return null;
        }

        private async Task RefreshSymbolPreviewAsync(bool liveApply = false)
        {
            if (SymbolPreviewCanvas == null) return;
            try
            {
                if (_geomType == GeometryType.Point || _geomType == GeometryType.Multipoint)
                { if (ImgPatternPreview != null) ImgPatternPreview.Visibility = Visibility.Collapsed; SymbolPreviewCanvas.Visibility = Visibility.Visible; DrawSinglePointPreview(); }
                else if (_geomType == GeometryType.Polyline)
                { if (ImgPatternPreview != null) ImgPatternPreview.Visibility = Visibility.Collapsed; SymbolPreviewCanvas.Visibility = Visibility.Visible; DrawSingleLinePreview(); }
                else if (_geomType == GeometryType.Polygon || _geomType == GeometryType.Envelope)
                {
                    var enabled = _fillLayers.Where(l => l.Enabled).ToList();
                    if (enabled.Count == 1 && enabled[0].Kind == FillSymbolLayerKind.Solid)
                    {
                        if (ImgPatternPreview != null) ImgPatternPreview.Visibility = Visibility.Collapsed;
                        SymbolPreviewCanvas.Visibility = Visibility.Visible;
                        var sc = PatternFillHelper.WithOpacity(enabled[0].Color, enabled[0].Opacity);
                        SimpleLineSymbol ol = _outlineWidth > 0.01 ? new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, _outlineColor, _outlineWidth) : null;
                        DrawSymbolPreview(new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, sc, ol));
                    }
                    else { using (var bmp = PatternFillHelper.GenerateCompositeTile(enabled, 64)) { ShowPatternPreview(bmp); } }
                }
                else { if (ImgPatternPreview != null) ImgPatternPreview.Visibility = Visibility.Collapsed; SymbolPreviewCanvas.Visibility = Visibility.Visible; var symbol = await BuildSymbolFromUiAsync(); if (symbol != null) DrawSymbolPreview(symbol); else DrawRasterPreview(); }
                if (liveApply && !_loading && _symbologyEditable) await LiveApplyToMapAsync();
            } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[Preview] " + ex.Message); }
        }

        private void DrawClassifiedPreview()
        {
            SymbolPreviewCanvas.Children.Clear();
            if (_classes.Count == 0) { var tb = new TextBlock { Text = "无分类", Foreground = new SolidColorBrush(MediaColor.FromRgb(120, 130, 140)), FontSize = 12 }; Canvas.SetLeft(tb, 90); Canvas.SetTop(tb, 40); SymbolPreviewCanvas.Children.Add(tb); return; }
            double x = 10; double boxW = Math.Max(15, Math.Min(40, 220.0 / _classes.Count)); double boxH = 40;
            foreach (var c in _classes)
            {
                var enabled = c.FillLayers.Where(l => l.Enabled).ToList();
                Brush fillBrush;
                if (enabled.Count > 0 && !(enabled.Count == 1 && enabled[0].Kind == FillSymbolLayerKind.Solid))
                {
                    using (var bmp = PatternFillHelper.GenerateCompositeTile(enabled, 16))
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Position = 0;
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        fillBrush = new ImageBrush(bi) { Stretch = Stretch.Fill };
                    }
                }
                else
                {
                    var color = enabled.Count > 0 ? enabled[0].Color : c.Color;
                    fillBrush = new SolidColorBrush(ToWpfColor(color));
                }
                var rect = new System.Windows.Shapes.Rectangle { Width = boxW - 2, Height = boxH, Fill = fillBrush, Stroke = new SolidColorBrush(MediaColor.FromRgb(60, 70, 80)), StrokeThickness = 1 };
                Canvas.SetLeft(rect, x); Canvas.SetTop(rect, 10); SymbolPreviewCanvas.Children.Add(rect);
                var label = new TextBlock { Text = c.Label, Foreground = new SolidColorBrush(MediaColor.FromRgb(200, 210, 220)), FontSize = 8, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = boxW };
                Canvas.SetLeft(label, x); Canvas.SetTop(label, boxH + 14); SymbolPreviewCanvas.Children.Add(label); x += boxW;
            }
        }

        private void DrawPointTemplatePreview(PointSymbolTemplateDef template)
        {
            SymbolPreviewCanvas.Children.Clear(); double cx = 120, cy = 55; double size = Math.Max(10, template?.Size ?? 12);
            var fill = ToWpfColor(template?.FillColor ?? DrawingColor.FromArgb(255, 88, 166, 255));
            var outline = new SolidColorBrush(ToWpfColor(template?.OutlineColor ?? DrawingColor.White));
            var textBrush = new SolidColorBrush(ToWpfColor(template?.TextColor ?? DrawingColor.White));
            switch (template?.Kind ?? PointSymbolPresetKind.Circle)
            {
                case PointSymbolPresetKind.Circle: case PointSymbolPresetKind.SamplePoint:
                { var ellipse = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(fill), Stroke = outline, StrokeThickness = Math.Max(1, template?.OutlineWidth ?? 1) }; Canvas.SetLeft(ellipse, cx - size / 2); Canvas.SetTop(ellipse, cy - size / 2); SymbolPreviewCanvas.Children.Add(ellipse);
                  if (template != null && template.Kind == PointSymbolPresetKind.SamplePoint) { var lbl = new TextBlock { Text = string.IsNullOrWhiteSpace(template.Text) ? "1" : template.Text, Foreground = textBrush, FontSize = Math.Max(8, template.TextSize), FontWeight = FontWeights.Bold }; Canvas.SetLeft(lbl, cx - 8); Canvas.SetTop(lbl, cy - 10); SymbolPreviewCanvas.Children.Add(lbl); } break; }
                default:
                { string tempSvgPath = SaveTempSvg(SymbolTemplateStore.BuildPointSvg(template.Kind)); try { using (var bmp = PatternFillHelper.RenderSvgToBitmap(tempSvgPath, (int)Math.Ceiling(size * 2), template.FillColor, template.Angle)) { ShowPreviewBitmap(bmp, 200, 90, 20, 10, template.OutlineColor, template.OutlineWidth); } } finally { try { File.Delete(tempSvgPath); } catch { } } break; }
            }
        }

        private static string SaveTempSvg(string svg) { string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"symbol_preview_{Guid.NewGuid():N}.svg"); File.WriteAllText(path, svg, Encoding.UTF8); return path; }

        private void ShowPreviewBitmap(System.Drawing.Bitmap bmp, double width, double height, double left, double top, DrawingColor outlineColor, double outlineWidth)
        { using (var ms = new MemoryStream()) { bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); ms.Position = 0; var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze(); var image = new System.Windows.Controls.Image { Source = bi, Width = width, Height = height, Stretch = Stretch.Uniform }; Canvas.SetLeft(image, left); Canvas.SetTop(image, top); SymbolPreviewCanvas.Children.Add(image); } }
        private void ShowPatternPreview(System.Drawing.Bitmap bmp)
        {
            SymbolPreviewCanvas.Visibility = Visibility.Collapsed;
            if (ImgPatternPreview == null) return;
            int tileW = bmp.Width, tileH = bmp.Height;
            int targetW = 200, targetH = 90;
            using (var full = new System.Drawing.Bitmap(targetW, targetH))
            using (var g = System.Drawing.Graphics.FromImage(full))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                for (int y = 0; y < targetH; y += tileH)
                    for (int x = 0; x < targetW; x += tileW)
                        g.DrawImage(bmp, x, y, tileW, tileH);
                if (_outlineWidth > 0.5)
                {
                    using (var pen = new System.Drawing.Pen(_outlineColor, (float)_outlineWidth))
                        g.DrawRectangle(pen, 0, 0, targetW - 1, targetH - 1);
                }
                using (var ms = new MemoryStream())
                {
                    full.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
                    ImgPatternPreview.Source = bi;
                }
            }
            ImgPatternPreview.Visibility = Visibility.Visible;
        }

        private void DrawSymbolPreview(Symbol symbol)
        {
            SymbolPreviewCanvas.Children.Clear(); double cx = 120, cy = 55;
            if (symbol is SimpleMarkerSymbol sms) { double sz = Math.Max(6, Math.Min(48, sms.Size * 1.2)); var ellipse = new Ellipse { Width = sz, Height = sz, Fill = new SolidColorBrush(ToWpfColor(sms.Color)), Stroke = Brushes.White, StrokeThickness = 1 }; Canvas.SetLeft(ellipse, cx - sz / 2); Canvas.SetTop(ellipse, cy - sz / 2); SymbolPreviewCanvas.Children.Add(ellipse); }
            else if (symbol is SimpleLineSymbol sls) { SymbolPreviewCanvas.Children.Add(new Line { X1 = 20, Y1 = cy, X2 = 220, Y2 = cy, Stroke = new SolidColorBrush(ToWpfColor(sls.Color)), StrokeThickness = Math.Max(1, Math.Min(16, sls.Width)), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }); }
            else if (symbol is SimpleFillSymbol sfs)
            {
                Brush fillBrush = new SolidColorBrush(ToWpfColor(sfs.Color));
                if (sfs.Style != SimpleFillSymbolStyle.Solid && sfs.Style != SimpleFillSymbolStyle.Null)
                { var dg = new DrawingGroup(); dg.Children.Add(new GeometryDrawing(fillBrush, null, new RectangleGeometry(new Rect(0, 0, 20, 20)))); var pen = new System.Windows.Media.Pen(new SolidColorBrush(MediaColor.FromRgb(40, 40, 40)), 1); if (sfs.Style == SimpleFillSymbolStyle.Horizontal || sfs.Style == SimpleFillSymbolStyle.Cross || sfs.Style == SimpleFillSymbolStyle.DiagonalCross) dg.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 10), new Point(20, 10)))); if (sfs.Style == SimpleFillSymbolStyle.Vertical || sfs.Style == SimpleFillSymbolStyle.Cross || sfs.Style == SimpleFillSymbolStyle.DiagonalCross) dg.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(10, 0), new Point(10, 20)))); if (sfs.Style == SimpleFillSymbolStyle.ForwardDiagonal || sfs.Style == SimpleFillSymbolStyle.DiagonalCross) dg.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 20), new Point(20, 0)))); if (sfs.Style == SimpleFillSymbolStyle.BackwardDiagonal || sfs.Style == SimpleFillSymbolStyle.DiagonalCross) dg.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(20, 20)))); fillBrush = new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 0.15, 0.25), ViewportUnits = BrushMappingMode.RelativeToBoundingBox }; }
                var rect = new System.Windows.Shapes.Rectangle { Width = 160, Height = 70, Fill = fillBrush, Stroke = sfs.Outline != null ? new SolidColorBrush(ToWpfColor(sfs.Outline.Color)) : Brushes.White, StrokeThickness = sfs.Outline != null ? Math.Max(1, sfs.Outline.Width) : 1 }; Canvas.SetLeft(rect, cx - 80); Canvas.SetTop(rect, cy - 35); SymbolPreviewCanvas.Children.Add(rect);
            }
        }

        private void DrawSinglePointPreview()
        {
            BrdPointSymbolPreview.Child = null;
            if (_singleSymPointLayers.Count == 0) return;
            var enabled = _singleSymPointLayers.Where(l => l.Enabled).ToList();
            if (enabled.Count == 0) return;
            Symbol sym;
            if (enabled.Count == 1)
                sym = BuildSinglePointSymbol(enabled[0]);
            else
            {
                var composite = new CompositeSymbol();
                foreach (var layer in enabled)
                    composite.Symbols.Add(BuildSinglePointSymbol(layer));
                sym = composite;
            }
            var canvas = new Canvas { Width = 48, Height = 48 };
            double cx = 24, cy = 24;
            if (sym is SimpleMarkerSymbol sms)
            {
                double sz = Math.Max(6, Math.Min(48, sms.Size * 1.2));
                var fill = new SolidColorBrush(ToWpfColor(sms.Color));
                var stroke = sms.Outline != null ? new SolidColorBrush(ToWpfColor(sms.Outline.Color)) : Brushes.White;
                double sw = sms.Outline?.Width ?? 1;
                var e = new Ellipse { Width = sz, Height = sz, Fill = fill, Stroke = stroke, StrokeThickness = sw };
                Canvas.SetLeft(e, cx - sz / 2); Canvas.SetTop(e, cy - sz / 2); canvas.Children.Add(e);
            }
            BrdPointSymbolPreview.Child = canvas;
        }

        private void DrawSingleLinePreview()
        {
            BrdLineSymbolPreview.Child = null;
            var enabled = _singleSymLineLayers.Where(l => l.Enabled).ToList();
            if (enabled.Count == 0) return;
            var canvas = new Canvas { Width = 100, Height = 30 };
            double baseY = 15;
            foreach (var def in enabled)
                DrawOneLayerInPreview(canvas, def, baseY + Math.Max(-10, Math.Min(10, def.OffsetY)));
            BrdLineSymbolPreview.Child = canvas;
        }

        private static void DrawOneLayerInPreview(Canvas canvas, LineSymbolLayerDef def, double cy)
        {
            double effW = Math.Max(0.5, Math.Min(10, def.Width * Math.Max(0.1, def.Scale)));
            var brush = new SolidColorBrush(ToWpfColor(def.Color));
            double x1 = 4, x2 = 96;

            if (def.Kind == LineSymbolLayerKind.WaveLine)
            {
                double amp = Math.Max(1, def.WaveAmplitude) * 0.3;
                double wlen = Math.Max(4, def.WaveLength);
                var pg = new PathGeometry(); var pf = new PathFigure { StartPoint = new Point(x1, cy) };
                for (double x = x1; x <= x2; x += 2) { double t = (x - x1) / wlen * Math.PI * 2; pf.Segments.Add(new System.Windows.Media.LineSegment(new Point(x, cy + Math.Sin(t) * amp), true)); }
                pg.Figures.Add(pf); canvas.Children.Add(new System.Windows.Shapes.Path { Data = pg, Stroke = brush, StrokeThickness = effW, Fill = null }); return;
            }
            if (def.Kind == LineSymbolLayerKind.SawtoothLine)
            {
                double amp = Math.Max(1, def.WaveAmplitude) * 0.3;
                double wlen = Math.Max(4, def.WaveLength);
                var pg = new PathGeometry(); var pf = new PathFigure { StartPoint = new Point(x1, cy) };
                for (double x = x1; x <= x2; x += wlen * 0.5) { int seg = (int)((x - x1) / (wlen * 0.5)); double y = cy + (seg % 2 == 0 ? -amp : amp); pf.Segments.Add(new System.Windows.Media.LineSegment(new Point(x, y), true)); }
                pg.Figures.Add(pf); canvas.Children.Add(new System.Windows.Shapes.Path { Data = pg, Stroke = brush, StrokeThickness = effW, Fill = null }); return;
            }

            SimpleLineSymbolStyle style = SimpleLineSymbolStyle.Solid;
            if (def.Kind == LineSymbolLayerKind.SimpleStroke || def.Kind == LineSymbolLayerKind.MarkerLine)
            { switch (def.StrokeStyle) { case "Dash": style = SimpleLineSymbolStyle.Dash; break; case "Dot": style = SimpleLineSymbolStyle.Dot; break; case "DashDot": style = SimpleLineSymbolStyle.DashDot; break; case "DashDotDot": style = SimpleLineSymbolStyle.DashDotDot; break; case "Hash": style = SimpleLineSymbolStyle.Dash; break; } }
            var line = new Line { X1 = x1, Y1 = cy, X2 = x2, Y2 = cy, Stroke = brush, StrokeThickness = effW, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            double ds = Math.Max(2, def.DashSpacing);
            switch (style) { case SimpleLineSymbolStyle.Dash: line.StrokeDashArray = new DoubleCollection(new[] { ds * 2, ds }); break; case SimpleLineSymbolStyle.Dot: line.StrokeDashArray = new DoubleCollection(new[] { 1.0, ds }); break; case SimpleLineSymbolStyle.DashDot: line.StrokeDashArray = new DoubleCollection(new[] { ds * 2, ds, ds / 2, ds }); break; case SimpleLineSymbolStyle.DashDotDot: line.StrokeDashArray = new DoubleCollection(new[] { ds * 2, ds, ds / 2, ds, ds / 2, ds }); break; }
            canvas.Children.Add(line);

            if (def.Kind == LineSymbolLayerKind.ArrowLine)
            { bool showL = def.ArrowPlacement == "Begin" || def.ArrowPlacement == "BeginAndEnd"; bool showR = def.ArrowPlacement == "End" || def.ArrowPlacement == "BeginAndEnd"; double asz = effW + 4; if (showR) { var pts = new System.Windows.Media.PointCollection { new Point(x2, cy), new Point(x2 - asz, cy - asz / 2), new Point(x2 - asz, cy + asz / 2) }; canvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts, Fill = brush }); } if (showL) { var pts2 = new System.Windows.Media.PointCollection { new Point(x1, cy), new Point(x1 + asz, cy - asz / 2), new Point(x1 + asz, cy + asz / 2) }; canvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts2, Fill = brush }); } }
            else if (def.Kind == LineSymbolLayerKind.MarkerLine && def.MarkerPlacement != "Along")
            { bool showL = def.MarkerPlacement == "Begin" || def.MarkerPlacement == "BeginAndEnd"; bool showR = def.MarkerPlacement == "End" || def.MarkerPlacement == "BeginAndEnd"; double asz = effW + 4; if (showR) { var pts = new System.Windows.Media.PointCollection { new Point(x2, cy), new Point(x2 - asz, cy - asz / 2), new Point(x2 - asz, cy + asz / 2) }; canvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts, Fill = brush }); } if (showL) { var pts2 = new System.Windows.Media.PointCollection { new Point(x1, cy), new Point(x1 + asz, cy - asz / 2), new Point(x1 + asz, cy + asz / 2) }; canvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts2, Fill = brush }); } }
        }

        private void DrawSingleFillPreview()
        {
            BrdFillSymbolPreview.Child = null;
            var canvas = new Canvas { Width = 100, Height = 40 };
            try
            {
                if (IsClassifiedMode())
                {
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = 100, Height = 40,
                        Fill = new SolidColorBrush(ToWpfColor(_fillColor)),
                        Stroke = new SolidColorBrush(ToWpfColor(_outlineColor)),
                        StrokeThickness = Math.Max(0.5, _outlineWidth)
                    };
                    canvas.Children.Add(rect);
                }
                else
                {
                    var sym = BuildSymbolFromUiAsync().GetAwaiter().GetResult();
                    if (sym is SimpleFillSymbol sfs)
                    {
                        var rect = new System.Windows.Shapes.Rectangle
                        {
                            Width = 100, Height = 40,
                            Fill = new SolidColorBrush(ToWpfColor(sfs.Color)),
                            Stroke = sfs.Outline != null ? new SolidColorBrush(ToWpfColor(sfs.Outline.Color)) : Brushes.White,
                            StrokeThickness = sfs.Outline != null ? Math.Max(0.5, sfs.Outline.Width) : 1
                        };
                        canvas.Children.Add(rect);
                    }
                    else if (sym is PictureFillSymbol pfs)
                    {
                        string url = pfs.Uri != null ? pfs.Uri.ToString() : "";
                        if (!string.IsNullOrEmpty(url))
                        {
                            var bi = new BitmapImage(new Uri(url));
                            var brush = new ImageBrush(bi)
                            {
                                TileMode = TileMode.Tile,
                                Viewport = new Rect(0, 0, 0.25, 0.4),
                                ViewportUnits = BrushMappingMode.RelativeToBoundingBox
                            };
                            var rect = new System.Windows.Shapes.Rectangle
                            {
                                Width = 100, Height = 40,
                                Fill = brush,
                                Stroke = pfs.Outline != null ? new SolidColorBrush(ToWpfColor(pfs.Outline.Color)) : Brushes.White,
                                StrokeThickness = pfs.Outline != null ? Math.Max(0.5, pfs.Outline.Width) : 1
                            };
                            canvas.Children.Add(rect);
                        }
                    }
                }
            }
            catch { }
            BrdFillSymbolPreview.Child = canvas;
        }

        private async void BtnEditFillSymbol_Click(object sender, RoutedEventArgs e)
        {
            var cd = new ClassDefinition
            {
                Color = _fillColor,
                FillColor = _fillColor,
                OutlineColor = _outlineColor,
                OutlineWidth = _outlineWidth,
                FillMode = "Pattern"
            };
            foreach (var l in _fillLayers)
                cd.FillLayers.Add(l.Clone());
            var dlg = new SymbolEditorDialog(cd, _geomType, _fieldNames);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                _fillLayers.Clear();
                foreach (var l in cd.FillLayers)
                    _fillLayers.Add(l.Clone());
                _fillColor = cd.FillColor;
                _outlineColor = cd.OutlineColor;
                _outlineWidth = Math.Max(0, Math.Min(12, cd.OutlineWidth));
                DrawSingleFillPreview();
                if (_symbologyEditable)
                    await LiveApplyToMapAsync();
            }
        }

        private async void BtnLithoAutoFill_Click(object sender, RoutedEventArgs e)
        {
            var fl = _layer as FeatureLayer;
            if (fl == null)
            {
                MessageBox.Show("当前图层不是要素图层，无法应用岩性花纹填充。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var mv = (Owner as MainWindow)?.CurrentMapView;
            if (mv == null)
            {
                MessageBox.Show("无法获取地图视图。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var dlg = new LithologyAutoAssignWindow(mv, _classes, CboClassField?.SelectedItem?.ToString() ?? "")
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (dlg.ShowDialog() == true)
            {
                RefreshClassPreviews();
                if (_symbologyEditable)
                    await LiveApplyToMapAsync();
            }
        }

        private async void BtnLineAutoMatch_Click(object sender, RoutedEventArgs e)
        {
            var fl = _layer as FeatureLayer;
            if (fl == null || (fl.FeatureTable?.GeometryType != GeometryType.Polyline))
            {
                MessageBox.Show("当前图层不是线要素图层。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var mv = (Owner as MainWindow)?.CurrentMapView;
            if (mv == null) { MessageBox.Show("无法获取地图视图。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            string classField = CboClassField?.SelectedItem?.ToString() ?? "";
            var dlg = new LineSymbolAutoAssignWindow(mv, _classes, classField) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            if (dlg.ShowDialog() == true)
            {
                RefreshClassPreviews();
                if (_symbologyEditable) await LiveApplyToMapAsync();
            }
        }

        private async void BtnPointAutoMatch_Click(object sender, RoutedEventArgs e)
        {
            var fl = _layer as FeatureLayer;
            if (fl == null || (fl.FeatureTable?.GeometryType != GeometryType.Point && fl.FeatureTable?.GeometryType != GeometryType.Multipoint))
            {
                MessageBox.Show("当前图层不是点要素图层。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var mv = (Owner as MainWindow)?.CurrentMapView;
            if (mv == null) { MessageBox.Show("无法获取地图视图。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            string classField = CboClassField?.SelectedItem?.ToString() ?? "";
            var dlg = new PointSymbolAutoAssignWindow(mv, _classes, classField) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            if (dlg.ShowDialog() == true)
            {
                RefreshClassPreviews();
                if (_symbologyEditable) await LiveApplyToMapAsync();
            }
        }

        private async void BtnEditLineSymbol_Click(object sender, RoutedEventArgs e)
        {
            var cd = new ClassDefinition
            {
                Color = _singleSymLineLayers.FirstOrDefault()?.Color ?? _lineColor,
            };
            if (_singleSymLineLayers.Count > 0)
            {
                foreach (var l in _singleSymLineLayers)
                    cd.LineLayers.Add(l.Clone());
            }
            var dlg = new SymbolEditorDialog(cd, _geomType, _fieldNames);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                _singleSymLineLayers.Clear();
                foreach (var l in cd.LineLayers)
                    _singleSymLineLayers.Add(l.Clone());
                _lineColor = cd.LineLayers.FirstOrDefault()?.Color ?? cd.Color;
                DrawSingleLinePreview();
                if (!_loading && _symbologyEditable)
                    await LiveApplyToMapAsync();
            }
        }

        private async void BtnEditPointSymbol_Click(object sender, RoutedEventArgs e)
        {
            var cd = new ClassDefinition
            {
                Color = _singleSymPointLayers.FirstOrDefault()?.Color ?? _pointColor,
                OutlineColor = _singleSymPointLayers.FirstOrDefault()?.OutlineColor ?? _outlineColor,
                OutlineWidth = _singleSymPointLayers.FirstOrDefault()?.OutlineWidth ?? 1
            };
            if (_singleSymPointLayers.Count > 0)
            {
                foreach (var l in _singleSymPointLayers)
                    cd.PointLayers.Add(l.Clone());
            }
            var dlg = new SymbolEditorDialog(cd, _geomType, _fieldNames);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                _singleSymPointLayers.Clear();
                foreach (var l in cd.PointLayers)
                    _singleSymPointLayers.Add(l.Clone());
                _pointColor = cd.PointLayers.FirstOrDefault()?.Color ?? cd.Color;
                DrawSinglePointPreview();
                if (!_loading && _symbologyEditable)
                    await LiveApplyToMapAsync();
            }
        }

        private void DrawRasterPreview()
        { SymbolPreviewCanvas.Children.Clear(); for (int i = 0; i < 8; i++) { byte g = (byte)(30 + i * 25); var r = new System.Windows.Shapes.Rectangle { Width = 20, Height = 56, Fill = new SolidColorBrush(MediaColor.FromRgb(g, (byte)(g + 20), (byte)(g + 40))) }; Canvas.SetLeft(r, 30 + i * 22); Canvas.SetTop(r, 17); SymbolPreviewCanvas.Children.Add(r); } }

        private async Task LiveApplyToMapAsync() { int seq = ++_liveApplySeq; await Task.Delay(80); if (seq != _liveApplySeq) return; try { await ApplySymbologyToLayerAsync(); Applied = true; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[LiveApply] " + ex.Message); } }

        private async Task ApplySymbologyToLayerAsync()
        {
            if (!_symbologyEditable) return;
            if (IsClassifiedMode()) { await ApplyClassifiedRendererAsync(); return; }
            var symbol = await BuildSymbolFromUiAsync(); if (symbol == null) return; var renderer = new SimpleRenderer(symbol);
            if (_layer is FeatureLayer fl) { fl.Renderer = renderer; }
            else if (_layer is FeatureCollectionLayer fcl) { if (fcl.FeatureCollection?.Tables != null) { foreach (var t in fcl.FeatureCollection.Tables.OfType<FeatureCollectionTable>()) { if (_geomType == GeometryType.Unknown || t.GeometryType == _geomType) t.Renderer = renderer; } } if (fcl.Layers != null) { foreach (var sub in fcl.Layers.OfType<FeatureLayer>()) { if (sub.FeatureTable != null && sub.FeatureTable.GeometryType == _geomType) sub.Renderer = renderer; else if (_geomType == GeometryType.Unknown) sub.Renderer = renderer; } } }
            // 线几何覆盖层（波浪/锯齿/标记沿线/偏移）
            if ((_geomType == GeometryType.Polyline) && _singleSymLineLayers.Count > 0 && Owner is MainWindow mw && mw.CurrentMapView != null)
            {
                var mv = mw.CurrentMapView;
                if (_layer is FeatureLayer fl2)
                    await LineGeometryGenerator.ApplyOverlay(_singleSymLineLayers, fl2, mv, _layerItem?.Name ?? "");
                else if (_layer is FeatureCollectionLayer fcl2 && fcl2.Layers != null)
                {
                    foreach (var sub in fcl2.Layers.OfType<FeatureLayer>())
                        await LineGeometryGenerator.ApplyOverlay(_singleSymLineLayers, sub, mv, (sub.Name ?? _layerItem?.Name ?? ""));
                }
            }
            // 持久化单符号的多层定义，以便重新打开时恢复
            if (!string.IsNullOrEmpty(_layerItem?.Name))
            {
                if ((_geomType == GeometryType.Point || _geomType == GeometryType.Multipoint) && _singleSymPointLayers.Count > 0)
                    _savedSinglePoint[_layerItem.Name] = _singleSymPointLayers.Select(l => l.Clone()).ToList();
                else if (_geomType == GeometryType.Polyline && _singleSymLineLayers.Count > 0)
                    _savedSingleLine[_layerItem.Name] = _singleSymLineLayers.Select(l => l.Clone()).ToList();
                else if ((_geomType == GeometryType.Polygon || _geomType == GeometryType.Envelope) && _fillLayers.Count > 0)
                    _savedSingleFill[_layerItem.Name] = _fillLayers.Select(l => l.Clone()).ToList();
            }
            ApplyLabelSettings();
        }

        private async Task ApplyClassifiedRendererAsync()
        {
            string fieldName = CboClassField?.SelectedItem?.ToString(); if (string.IsNullOrEmpty(fieldName) || _classes.Count == 0) return;
            var uvr = new UniqueValueRenderer(); uvr.FieldNames.Add(fieldName);
            DrawingColor outlineColor = _geomType == GeometryType.Polygon || _geomType == GeometryType.Envelope ? _outlineColor : DrawingColor.Transparent;
            double outlineWidth = _outlineWidth;
            foreach (var c in _classes)
            {
                Symbol sym;
                if (c.FillLayers.Count > 0 && c.FillMode != "Solid" && c.FillMode != "Simple")
                {
                    var layers = c.FillLayers.Where(l => l.Enabled).ToList();
                    string[] patternTypes = { "Pattern", "LinePattern", "PointPattern", "RandomPoint", "Svg", "Gradient" };
                    if (Array.IndexOf(patternTypes, c.FillMode) >= 0)
                        sym = await PatternFillHelper.BuildFillSymbolAsync(layers, c.OutlineColor, c.OutlineWidth, 64);
                    else
                    {
                        SimpleLineSymbol ol = c.OutlineWidth > 0.01 ? new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, c.OutlineColor, c.OutlineWidth) : null;
                        sym = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, c.FillColor, ol);
                    }
                }
                else if (_geomType == GeometryType.Point || _geomType == GeometryType.Multipoint)
                    sym = BuildPointSymbolFromLayers(c.PointLayers, c.Color);
                else if (_geomType == GeometryType.Polyline)
                    sym = BuildLineSymbolFromLayers(c.LineLayers, c.Color);
                else
                {
                    SimpleLineSymbol ol = c.OutlineWidth > 0.01 ? new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, c.OutlineColor, c.OutlineWidth) : null;
                    sym = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, c.Color, ol);
                }
                var uv = new UniqueValue { Label = c.Label, Symbol = sym }; uv.Values.Add(c.Value); uvr.UniqueValues.Add(uv);
            }
            Symbol defaultSym;
            if (_geomType == GeometryType.Point || _geomType == GeometryType.Multipoint)
                defaultSym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, DrawingColor.FromArgb(255, 160, 160, 160), 10);
            else if (_geomType == GeometryType.Polyline)
                defaultSym = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, DrawingColor.FromArgb(255, 160, 160, 160), 2);
            else
            {
                SimpleLineSymbol defaultOutline = outlineWidth > 0.01 ? new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outlineColor, outlineWidth) : null;
                defaultSym = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, DrawingColor.FromArgb(255, 160, 160, 160), defaultOutline);
            }
            uvr.DefaultSymbol = defaultSym; uvr.DefaultLabel = "其他";
            // 持久化分类的自定义符号层数据
            if (!string.IsNullOrEmpty(_layerItem?.Name))
            {
                _savedClassData[_layerItem.Name] = _classes.Select(c => new ClassDefSymData
                {
                    Value = c.Value,
                    PointLayers = c.PointLayers?.Select(l => l.Clone()).ToList(),
                    FillLayers = c.FillLayers?.Select(l => l.Clone()).ToList(),
                    LineLayers = c.LineLayers?.Select(l => l.Clone()).ToList(),
                    FillMode = c.FillMode,
                    FillColor = c.FillColor,
                    OutlineColor = c.OutlineColor,
                    OutlineWidth = c.OutlineWidth
                }).ToList();
            }
            if (_layer is FeatureLayer fl) { fl.Renderer = uvr; }
            else if (_layer is FeatureCollectionLayer fcl) { if (fcl.FeatureCollection?.Tables != null) { foreach (var t in fcl.FeatureCollection.Tables.OfType<FeatureCollectionTable>()) { if (_geomType == GeometryType.Unknown || t.GeometryType == _geomType) t.Renderer = uvr; } } if (fcl.Layers != null) { foreach (var sub in fcl.Layers.OfType<FeatureLayer>()) { if (sub.FeatureTable != null && sub.FeatureTable.GeometryType == _geomType) sub.Renderer = uvr; else if (_geomType == GeometryType.Unknown) sub.Renderer = uvr; } } }
            ApplyLabelSettings();
        }

        private static string ResolveLineStrokeStyle(SimpleLineSymbol sls)
        {
            if (sls == null) return "Solid";
            switch (sls.Style)
            {
                case SimpleLineSymbolStyle.Dash: return "Dash";
                case SimpleLineSymbolStyle.Dot: return "Dot";
                case SimpleLineSymbolStyle.DashDot: return "DashDot";
                case SimpleLineSymbolStyle.DashDotDot: return "DashDotDot";
                default: return "Solid";
            }
        }

        private static Symbol BuildLineSymbolFromLayers(IList<LineSymbolLayerDef> layers, DrawingColor fallbackColor)
        {
            // 波浪/锯齿/标记沿线/偏移等需要几何生成的层不参与 FeatureLayer 渲染器，
            // 它们由 LineGeometryGenerator.ApplyOverlay 单独绘制；否则渲染器会画一条普通直线
            // 叠在生成的波浪线下面，导致地图显示与预览不一致。
            var enabled = layers?.Where(l => l.Enabled && !LineGeometryGenerator.NeedsGeometryGeneration(l)).ToList()
                ?? new List<LineSymbolLayerDef>();
            if (enabled.Count == 0)
            {
                // 全部都是几何生成层：渲染器画一条透明线占位，实际显示完全由覆盖层负责
                return new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Transparent, 1);
            }
            if (enabled.Count == 1)
                return BuildSingleLineSymbol(enabled[0]);
            var composite = new CompositeSymbol();
            foreach (var layer in enabled)
                composite.Symbols.Add(BuildSingleLineSymbol(layer));
            return composite;
        }

        private static Symbol BuildSingleLineSymbol(LineSymbolLayerDef def)
        {
            var useColor = PatternFillHelper.WithOpacity(def.Color, def.Opacity);
            double effWidth = def.Width * Math.Max(0.1, def.Scale);
            switch (def.Kind)
            {
                case LineSymbolLayerKind.SimpleStroke:
                {
                    SimpleLineSymbolStyle style = SimpleLineSymbolStyle.Solid;
                    switch (def.StrokeStyle)
                    {
                        case "Dash": style = SimpleLineSymbolStyle.Dash; break;
                        case "Dot": style = SimpleLineSymbolStyle.Dot; break;
                        case "DashDot": style = SimpleLineSymbolStyle.DashDot; break;
                        case "DashDotDot": style = SimpleLineSymbolStyle.DashDotDot; break;
                    }
                    var sls = new SimpleLineSymbol(style, useColor, effWidth);
                    return sls;
                }
                case LineSymbolLayerKind.MarkerLine:
                {
                    SimpleLineSymbolStyle baseStyle = SimpleLineSymbolStyle.Solid;
                    switch (def.StrokeStyle) { case "Dash": baseStyle = SimpleLineSymbolStyle.Dash; break; case "Dot": baseStyle = SimpleLineSymbolStyle.Dot; break; case "DashDot": baseStyle = SimpleLineSymbolStyle.DashDot; break; case "DashDotDot": baseStyle = SimpleLineSymbolStyle.DashDotDot; break; case "Hash": baseStyle = SimpleLineSymbolStyle.Dash; break; }
                    var sls = new SimpleLineSymbol(baseStyle, useColor, effWidth);
                    sls.MarkerStyle = SimpleLineSymbolMarkerStyle.Arrow;
                    string plc = def.MarkerPlacement;
                    if (plc == "Begin") sls.MarkerPlacement = SimpleLineSymbolMarkerPlacement.Begin;
                    else if (plc == "End" || plc == "Along") sls.MarkerPlacement = SimpleLineSymbolMarkerPlacement.End;
                    else if (plc == "BeginAndEnd") sls.MarkerPlacement = SimpleLineSymbolMarkerPlacement.BeginAndEnd;
                    return sls;
                }
                case LineSymbolLayerKind.ArrowLine:
                {
                    var sls = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, useColor, effWidth);
                    sls.MarkerStyle = SimpleLineSymbolMarkerStyle.Arrow;
                    switch (def.ArrowPlacement) { case "Begin": sls.MarkerPlacement = SimpleLineSymbolMarkerPlacement.Begin; break; case "End": sls.MarkerPlacement = SimpleLineSymbolMarkerPlacement.End; break; case "BeginAndEnd": sls.MarkerPlacement = SimpleLineSymbolMarkerPlacement.BeginAndEnd; break; }
                    return sls;
                }
                case LineSymbolLayerKind.WaveLine:
                case LineSymbolLayerKind.SawtoothLine:
                    return new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, useColor, effWidth);
                default:
                    return new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, useColor, effWidth);
            }
        }

        private Symbol BuildPointSymbolFromLayers(ObservableCollection<PointSymbolLayerDef> layers, DrawingColor fallbackColor)
        {
            var enabled = layers?.Where(l => l.Enabled).ToList() ?? new List<PointSymbolLayerDef>();
            if (enabled.Count == 0)
                return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, fallbackColor, 10);
            if (enabled.Count == 1)
                return BuildSinglePointSymbol(enabled[0]);
            var composite = new CompositeSymbol();
            foreach (var layer in enabled)
                composite.Symbols.Add(BuildSinglePointSymbol(layer));
            return composite;
        }
        private static Symbol BuildSinglePointSymbol(PointSymbolLayerDef def)
        {
            double effSize = def.Size * Math.Max(0.1, def.Scale);
            var useColor = PatternFillHelper.WithOpacity(def.Color, def.Opacity);
            var useOutlineColor = PatternFillHelper.WithOpacity(def.OutlineColor, def.Opacity);
            switch (def.Kind)
            {
                case PointSymbolLayerKind.SimpleMarker:
                {
                    string tag = def.MarkerStyle;
                    if (tag == "Hexagon")
                    {
                        int sz2 = Math.Max(12, (int)effSize * 2);
                        using (var bmp2 = PatternFillHelper.GenerateHexagonBitmap(sz2, useColor, useOutlineColor, def.OutlineWidth))
                        {
                            string tmp2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"geohex_{Guid.NewGuid():N}.png");
                            bmp2.Save(tmp2, System.Drawing.Imaging.ImageFormat.Png);
                            return new PictureMarkerSymbol(new Uri(tmp2))
                            { Width = sz2, Height = sz2, Angle = def.Angle, Opacity = def.Opacity };
                        }
                    }
                    if (tag == "Star")
                    {
                        int sz2 = Math.Max(12, (int)effSize * 2);
                        using (var bmp2 = PatternFillHelper.GenerateStarBitmap(sz2, useColor, useOutlineColor, def.OutlineWidth))
                        {
                            string tmp2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"geostar_{Guid.NewGuid():N}.png");
                            bmp2.Save(tmp2, System.Drawing.Imaging.ImageFormat.Png);
                            return new PictureMarkerSymbol(new Uri(tmp2))
                            { Width = sz2, Height = sz2, Angle = def.Angle, Opacity = def.Opacity };
                        }
                    }
                    if (tag == "ShortLine")
                    {
                        int sz = Math.Max(16, (int)effSize * 2);
                        using (var bmp = PatternFillHelper.GenerateShortLineBitmap(sz, useColor, def.LineLength, def.AnchorX, def.AnchorY, def.OffsetX, def.OffsetY))
                        {
                            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"geoline_{Guid.NewGuid():N}.png");
                            bmp.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                            return new PictureMarkerSymbol(new Uri(tmp))
                            { Width = sz, Height = sz, Angle = def.Angle, Opacity = def.Opacity };
                        }
                    }
                    if (tag == "Arrow")
                    {
                        int sz = Math.Max(16, (int)effSize * 2);
                        using (var bmp = PatternFillHelper.GenerateArrowBitmap(sz, useColor, def.LineLength, def.AnchorX, def.AnchorY, def.OffsetX, def.OffsetY))
                        {
                            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"geoarrow_{Guid.NewGuid():N}.png");
                            bmp.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                            return new PictureMarkerSymbol(new Uri(tmp))
                            { Width = sz, Height = sz, Angle = def.Angle, Opacity = def.Opacity };
                        }
                    }
                    // Circle/Triangle/Square/Cross/X/Diamond: use bitmap for offset/scale support
                    string[] bitmapStyles = { "Circle", "Triangle", "Square", "Cross", "X", "Diamond", "HollowCircle", "HollowTriangle" };
                    if (Array.IndexOf(bitmapStyles, tag) >= 0)
                    {
                        int sz = Math.Max(12, (int)effSize * 2);
                        using (var bmp = PatternFillHelper.GenerateSimpleMarkerBitmap(sz, useColor, tag,
                            def.AnchorX, def.AnchorY, def.OffsetX, def.OffsetY, def.OutlineWidth, useOutlineColor))
                        {
                            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"geosym_{Guid.NewGuid():N}.png");
                            bmp.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                            return new PictureMarkerSymbol(new Uri(tmp))
                            { Width = sz, Height = sz, Angle = def.Angle, Opacity = def.Opacity };
                        }
                    }
                    SimpleMarkerSymbolStyle style = SimpleMarkerSymbolStyle.Circle;
                    return new SimpleMarkerSymbol(style, useColor, effSize)
                    { Angle = def.Angle, Outline = def.OutlineWidth > 0 ? new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, useOutlineColor, def.OutlineWidth) : null };
                }
                case PointSymbolLayerKind.SvgMarker:
                {
                    if (!string.IsNullOrEmpty(def.FilePath) && File.Exists(def.FilePath))
                        return new PictureMarkerSymbol(new Uri(def.FilePath)) { Width = effSize, Height = effSize, Angle = def.Angle, Opacity = def.Opacity };
                    return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Diamond, useColor, effSize) { Angle = def.Angle };
                }
                case PointSymbolLayerKind.VectorField:
                {
                    int sz = Math.Max(12, (int)effSize * 2);
                    using (var bmp = PatternFillHelper.GenerateStrikeDipBitmap(sz, useColor, 45))
                    {
                        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"geostrike_{Guid.NewGuid():N}.png");
                        bmp.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                        var pic = new PictureMarkerSymbol(new Uri(tmp))
                        {
                            Width = sz, Height = sz, Angle = def.Angle, Opacity = def.Opacity
                        };
                        return pic;
                    }
                }
                case PointSymbolLayerKind.FontMarker:
                {
                    var text = new TextSymbol(def.CharString, useColor, effSize, Esri.ArcGISRuntime.Symbology.HorizontalAlignment.Center, Esri.ArcGISRuntime.Symbology.VerticalAlignment.Middle);
                    if (!string.IsNullOrEmpty(def.FontFamily)) text.FontFamily = def.FontFamily;
                    if (def.Angle != 0) text.Angle = def.Angle;
                    return text;
                }
                default:
                    return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, def.Color, def.Size);
            }
        }
        private void ApplyLabelSettings()
        {
            bool labelsEnabled = ChkLabelVisible?.IsChecked == true; string fieldName = CboLabelField?.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(fieldName)) labelsEnabled = false;
            _fontFamily = CboFontFamily?.SelectedItem?.ToString() ?? "";
            var textSymbol = new TextSymbol { Size = SldLabelFontSize?.Value ?? 12, Color = _labelColor, OffsetX = SldLabelOffsetX?.Value ?? 0, OffsetY = SldLabelOffsetY?.Value ?? 0 };
            if (!string.IsNullOrEmpty(_fontFamily)) textSymbol.FontFamily = _fontFamily;
            var labelDef = new LabelDefinition(new SimpleLabelExpression($"[{fieldName}]"), textSymbol);
            void applyToFl(FeatureLayer layer) { if (layer == null) return; layer.LabelsEnabled = labelsEnabled; layer.LabelDefinitions.Clear(); if (labelsEnabled && !string.IsNullOrEmpty(fieldName)) layer.LabelDefinitions.Add(labelDef); }
            if (_layer is FeatureLayer fl) { applyToFl(fl); }
            else if (_layer is FeatureCollectionLayer fcl) { if (fcl.Layers != null) { foreach (var sub in fcl.Layers.OfType<FeatureLayer>()) applyToFl(sub); } }
        }

        private void SldOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (TxtOpacityVal != null) TxtOpacityVal.Text = $"{e.NewValue:F0}%"; }
        private async Task<bool> ApplyChangesAsync()
        {
            try { _layer.Opacity = Math.Max(0, Math.Min(1.0, SldOpacity.Value / 100.0)); _layer.IsVisible = ChkVisible.IsChecked == true; _layerItem.IsVisible = _layer.IsVisible; if (double.TryParse(TxtMaxScale.Text.Trim(), out double ms) && ms >= 0) _layer.MaxScale = ms; if (double.TryParse(TxtMinScale.Text.Trim(), out double mins) && mins >= 0) _layer.MinScale = mins; await ApplySymbologyToLayerAsync(); Applied = true; return true; }
            catch (Exception ex) { MessageBox.Show($"应用失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
        }
        private async void BtnApply_Click(object sender, RoutedEventArgs e) { if (await ApplyChangesAsync()) MessageBox.Show("属性已应用（含符号系统和标注）", "成功", MessageBoxButton.OK, MessageBoxImage.Information); }
        private async void BtnOk_Click(object sender, RoutedEventArgs e) { if (await ApplyChangesAsync()) { DialogResult = true; Close(); } }
        private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    }

    internal static class LineGeometryGenerator
    {
        public static Esri.ArcGISRuntime.Geometry.Polyline GenerateWave(Esri.ArcGISRuntime.Geometry.Polyline original, double amplitude, double wavelength)
        {
            var sr = original.SpatialReference;
            var builder = new Esri.ArcGISRuntime.Geometry.PolylineBuilder(sr);
            foreach (var part in original.Parts)
            {
                var pts = new List<Esri.ArcGISRuntime.Geometry.MapPoint>(part.Points);
                if (pts.Count < 2) continue;
                var np = new List<Esri.ArcGISRuntime.Geometry.MapPoint>();
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var a = pts[i]; var b = pts[i + 1];
                    double dx = b.X - a.X; double dy = b.Y - a.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < 0.0001) continue;
                    double nx = -dy / len, ny = dx / len;
                    int steps = Math.Max(2, (int)(len / 2.0));
                    for (int s = 0; s <= steps; s++)
                    {
                        double t = (double)s / steps;
                        double wx = a.X + t * dx, wy = a.Y + t * dy;
                        double off = amplitude * Math.Sin(t * len / (wavelength * 0.5) * Math.PI);
                        np.Add(new Esri.ArcGISRuntime.Geometry.MapPoint(wx + nx * off, wy + ny * off, sr));
                    }
                }
                foreach (var pt in np)
                    builder.AddPoint(pt);
            }
            return builder.ToGeometry();
        }

        public static Esri.ArcGISRuntime.Geometry.Polyline GenerateSawtooth(Esri.ArcGISRuntime.Geometry.Polyline original, double amplitude, double wavelength)
        {
            var sr = original.SpatialReference;
            var builder = new Esri.ArcGISRuntime.Geometry.PolylineBuilder(sr);
            foreach (var part in original.Parts)
            {
                var pts = new List<Esri.ArcGISRuntime.Geometry.MapPoint>(part.Points);
                if (pts.Count < 2) continue;
                var np = new List<Esri.ArcGISRuntime.Geometry.MapPoint>();
                double accum = 0;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var a = pts[i]; var b = pts[i + 1];
                    double dx = b.X - a.X; double dy = b.Y - a.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < 0.0001) continue;
                    double nx = -dy / len, ny = dx / len;
                    int steps = Math.Max(2, (int)(len / (wavelength * 0.5)));
                    for (int s = 0; s <= steps; s++)
                    {
                        double t = (double)s / steps;
                        double wx = a.X + t * dx, wy = a.Y + t * dy;
                        double localDist = accum + t * len;
                        int cycle = (int)(localDist / (wavelength * 0.5));
                        double off = (cycle % 2 == 0) ? amplitude : -amplitude;
                        np.Add(new Esri.ArcGISRuntime.Geometry.MapPoint(wx + nx * off, wy + ny * off, sr));
                    }
                    accum += len;
                }
                foreach (var pt in np)
                    builder.AddPoint(pt);
            }
            return builder.ToGeometry();
        }

        public static bool NeedsGeometryGeneration(LineSymbolLayerDef def)
        {
            if (def == null || !def.Enabled) return false;
            return def.Kind == LineSymbolLayerKind.WaveLine
                || def.Kind == LineSymbolLayerKind.SawtoothLine
                || (def.Kind == LineSymbolLayerKind.MarkerLine && def.MarkerPlacement == "Along")
                || Math.Abs(def.OffsetY) > 0.01;
        }

        public static async Task ApplyOverlay(IList<LineSymbolLayerDef> layers, Esri.ArcGISRuntime.Mapping.FeatureLayer featureLayer, Esri.ArcGISRuntime.UI.Controls.MapView mapView, string layerName)
        {
            string overlayKey = "__GeometryOverlay_" + (layerName ?? "line");
            var existing = mapView.GraphicsOverlays.FirstOrDefault(o => o.Id == overlayKey);
            if (existing != null) mapView.GraphicsOverlays.Remove(existing);

            var needsGen = layers?.Where(NeedsGeometryGeneration).ToList();
            if (needsGen == null || needsGen.Count == 0) return;

            var overlay = new Esri.ArcGISRuntime.UI.GraphicsOverlay { Id = overlayKey };
            var table = featureLayer.FeatureTable;
            if (table == null) return;

            try
            {
                // 预览中波浪振幅/波长/偏移以像素为单位，需换算为地图单位以保持一致
                double mapUnitsPerPixel = EstimateMapUnitsPerPixel(mapView);

                var queryResult = await table.QueryFeaturesAsync(new Esri.ArcGISRuntime.Data.QueryParameters { WhereClause = "1=1" });
                foreach (var feature in queryResult)
                {
                    var pline = feature.Geometry as Esri.ArcGISRuntime.Geometry.Polyline;
                    if (pline == null) continue;
                    // 仅绘制需要几何生成的层（波浪/锯齿等）；普通线层由 FeatureLayer 渲染器负责，
                    // 避免同一几何被画两次导致显示与预览不一致
                    foreach (var def in layers.Where(l => l.Enabled && NeedsGeometryGeneration(l)))
                    {
                        Esri.ArcGISRuntime.Geometry.Polyline geo = pline;
                        if (def.Kind == LineSymbolLayerKind.WaveLine)
                        {
                            double amp = Math.Max(1, def.WaveAmplitude) * 0.3 * mapUnitsPerPixel;
                            double wlen = Math.Max(4, def.WaveLength) * mapUnitsPerPixel;
                            geo = GenerateWave(pline, amp, wlen);
                        }
                        else if (def.Kind == LineSymbolLayerKind.SawtoothLine)
                        {
                            double amp = Math.Max(1, def.WaveAmplitude) * 0.3 * mapUnitsPerPixel;
                            double wlen = Math.Max(4, def.WaveLength) * mapUnitsPerPixel;
                            geo = GenerateSawtooth(pline, amp, wlen);
                        }
                        else if (def.Kind == LineSymbolLayerKind.MarkerLine && def.MarkerPlacement == "Along")
                        {
                            // 沿线等距标记点符号：与预览一致（沿折线按像素间隔放点符号 + 底纹线）
                            AddMarkerLineAlong(overlay, pline, def, mapUnitsPerPixel, mapView);
                            continue;
                        }
                        else
                        {
                            // 仅带偏移的普通线：沿法向平移几何
                            if (Math.Abs(def.OffsetY) > 0.01)
                                geo = OffsetLine(pline, def.OffsetY * mapUnitsPerPixel);
                        }

                        var sym = BuildSimpleStrokeSymbol(def);
                        overlay.Graphics.Add(new Esri.ArcGISRuntime.UI.Graphic(geo, sym));
                    }
                }
            }
            catch { return; }
            if (overlay.Graphics.Count > 0) mapView.GraphicsOverlays.Add(overlay);
        }

        /// <summary>估算当前视图每像素对应的地图单位数（按屏幕坐标换算，兼容地理坐标/投影坐标）。</summary>
        private static double EstimateMapUnitsPerPixel(Esri.ArcGISRuntime.UI.Controls.MapView mapView)
        {
            try
            {
                if (mapView == null || mapView.ActualWidth <= 0 || mapView.ActualHeight <= 0) return 1e-7;
                double cx = mapView.ActualWidth / 2.0, cy = mapView.ActualHeight / 2.0;
                var p0 = mapView.ScreenToLocation(new System.Windows.Point(cx, cy));
                var p1 = mapView.ScreenToLocation(new System.Windows.Point(cx + 1, cy));
                if (p0 == null || p1 == null) return 1e-7;
                double d = Esri.ArcGISRuntime.Geometry.GeometryEngine.Distance(p0, p1);
                if (d <= 0) return 1e-7;
                return d;
            }
            catch { return 1e-7; }
        }

        private static Esri.ArcGISRuntime.Geometry.Polyline OffsetLine(Esri.ArcGISRuntime.Geometry.Polyline line, double offsetMapUnits)
        {
            var sr = line.SpatialReference;
            var builder = new Esri.ArcGISRuntime.Geometry.PolylineBuilder(sr);
            foreach (var part in line.Parts)
            {
                var pts = part.Points.ToList();
                if (pts.Count < 2) continue;
                var np = new List<Esri.ArcGISRuntime.Geometry.MapPoint>();
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var a = pts[i]; var b = pts[i + 1];
                    double dx = b.X - a.X, dy = b.Y - a.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-9) continue;
                    double nx = -dy / len, ny = dx / len;
                    np.Add(new Esri.ArcGISRuntime.Geometry.MapPoint(a.X + nx * offsetMapUnits, a.Y + ny * offsetMapUnits, sr));
                    if (i == pts.Count - 2)
                        np.Add(new Esri.ArcGISRuntime.Geometry.MapPoint(b.X + nx * offsetMapUnits, b.Y + ny * offsetMapUnits, sr));
                }
                foreach (var pt in np) builder.AddPoint(pt);
            }
            return builder.ToGeometry();
        }

        /// <summary>沿线等距放置标记点符号（与编辑器预览一致）：先画底纹线，再按像素间隔放点符号。</summary>
        private static void AddMarkerLineAlong(Esri.ArcGISRuntime.UI.GraphicsOverlay overlay, Esri.ArcGISRuntime.Geometry.Polyline pline, LineSymbolLayerDef def, double mapUnitsPerPixel, Esri.ArcGISRuntime.UI.Controls.MapView mapView)
        {
            // 底纹线（预览 DrawSingleLineLayer 对 MarkerLine 也画一条普通线）
            overlay.Graphics.Add(new Esri.ArcGISRuntime.UI.Graphic(pline, BuildSimpleStrokeSymbol(def)));

            var mColor = def.MarkerColor;

            string shape = def.MarkerShape;
            if (def.MarkerPointLayers.Count > 0)
            {
                var first = def.MarkerPointLayers[0];
                if (first.Kind == PointSymbolLayerKind.SimpleMarker && !string.IsNullOrEmpty(first.MarkerStyle))
                    shape = first.MarkerStyle;
            }

            // 按像素间隔在屏幕空间等距采样，再转回地图坐标（兼容地理/投影坐标，与预览像素一致）
            double intervalPx = Math.Max(4, def.MarkerInterval);
            var sampled = SampleAlongPolylineScreen(pline, intervalPx, mapView);
            if (sampled.Count == 0) return;

            var marker = BuildMarkerSymbol(shape, mColor, Math.Max(2, Math.Min(16, def.MarkerSize)));
            foreach (var p in sampled)
                overlay.Graphics.Add(new Esri.ArcGISRuntime.UI.Graphic(p, marker));
        }

        /// <summary>把折线顶点转成屏幕点，按固定像素间隔采样，再转回地图坐标。</summary>
        private static List<Esri.ArcGISRuntime.Geometry.MapPoint> SampleAlongPolylineScreen(Esri.ArcGISRuntime.Geometry.Polyline pline, double intervalPx, Esri.ArcGISRuntime.UI.Controls.MapView mapView)
        {
            var result = new List<Esri.ArcGISRuntime.Geometry.MapPoint>();
            try
            {
                foreach (var part in pline.Parts)
                {
                    var pts = part.Points.ToList();
                    if (pts.Count < 2) continue;

                    var screen = new List<System.Windows.Point>();
                    bool ok = true;
                    foreach (var mp in pts)
                    {
                        var sp = mapView.LocationToScreen(mp);
                        if (double.IsNaN(sp.X) || double.IsNaN(sp.Y)) { ok = false; break; }
                        screen.Add(sp);
                    }
                    if (!ok) continue;

                    double travelled = 0, nextDist = intervalPx;
                    var a = screen[0];
                    result.Add(mapView.ScreenToLocation(a));
                    for (int i = 0; i < screen.Count - 1; i++)
                    {
                        var b = screen[i + 1];
                        double dx = b.X - a.X, dy = b.Y - a.Y;
                        double segLen = Math.Sqrt(dx * dx + dy * dy);
                        double segStart = travelled;
                        while (segStart + segLen >= nextDist)
                        {
                            double t = (nextDist - segStart) / segLen;
                            var sp = new System.Windows.Point(a.X + t * dx, a.Y + t * dy);
                            result.Add(mapView.ScreenToLocation(sp));
                            nextDist += intervalPx;
                        }
                        travelled += segLen;
                        a = b;
                    }
                }
            }
            catch { }
            return result;
        }

        private static Esri.ArcGISRuntime.Symbology.Symbol BuildMarkerSymbol(string shape, System.Drawing.Color color, double sizeMap)
        {
            var c = PatternFillHelper.WithOpacity(color, 1.0);
            Esri.ArcGISRuntime.Symbology.SimpleMarkerSymbolStyle style;
            switch (shape)
            {
                case "Triangle": style = Esri.ArcGISRuntime.Symbology.SimpleMarkerSymbolStyle.Triangle; break;
                case "Diamond": style = Esri.ArcGISRuntime.Symbology.SimpleMarkerSymbolStyle.Diamond; break;
                case "Cross": style = Esri.ArcGISRuntime.Symbology.SimpleMarkerSymbolStyle.Cross; break;
                case "X": style = Esri.ArcGISRuntime.Symbology.SimpleMarkerSymbolStyle.X; break;
                case "Square": style = Esri.ArcGISRuntime.Symbology.SimpleMarkerSymbolStyle.Square; break;
                default: style = Esri.ArcGISRuntime.Symbology.SimpleMarkerSymbolStyle.Circle; break;
            }
            return new Esri.ArcGISRuntime.Symbology.SimpleMarkerSymbol(style, c, sizeMap);
        }

        private static Esri.ArcGISRuntime.Symbology.Symbol BuildSimpleStrokeSymbol(LineSymbolLayerDef def)
        {
            var useColor = PatternFillHelper.WithOpacity(def.Color, def.Opacity);
            double effW = def.Width * Math.Max(0.1, def.Scale);
            Esri.ArcGISRuntime.Symbology.SimpleLineSymbolStyle style = Esri.ArcGISRuntime.Symbology.SimpleLineSymbolStyle.Solid;
            switch (def.StrokeStyle)
            {
                case "Dash": style = Esri.ArcGISRuntime.Symbology.SimpleLineSymbolStyle.Dash; break;
                case "Dot": style = Esri.ArcGISRuntime.Symbology.SimpleLineSymbolStyle.Dot; break;
                case "DashDot": style = Esri.ArcGISRuntime.Symbology.SimpleLineSymbolStyle.DashDot; break;
                case "DashDotDot": style = Esri.ArcGISRuntime.Symbology.SimpleLineSymbolStyle.DashDotDot; break;
            }
            return new Esri.ArcGISRuntime.Symbology.SimpleLineSymbol(style, useColor, effW);
        }
    }
}
