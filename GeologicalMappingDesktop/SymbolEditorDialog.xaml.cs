using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace GeologicalMappingDesktop
{
    public partial class SymbolEditorDialog : Window
    {
        public ClassDefinition ClassDef { get; private set; }
        private GeometryType _geomType;
        private List<string> _fieldNames;
        private bool _loading;
        private bool _syncing;
        private DrawingColor _lineColor = DrawingColor.FromArgb(255, 88, 166, 255);
        private readonly ObservableCollection<FillSymbolLayerDef> _fillLayers = new ObservableCollection<FillSymbolLayerDef>();
        private readonly ObservableCollection<PointSymbolLayerDef> _pointLayers = new ObservableCollection<PointSymbolLayerDef>();
        private readonly ObservableCollection<LineSymbolLayerDef> _lineLayers = new ObservableCollection<LineSymbolLayerDef>();
        private FillSymbolLayerDef SelectedFillLayer => LstFillLayers?.SelectedItem as FillSymbolLayerDef;
        private PointSymbolLayerDef SelectedPointLayer => LstPointLayers?.SelectedItem as PointSymbolLayerDef;
        private LineSymbolLayerDef SelectedLineLayer
        { get { int i = LstLineLayers?.SelectedIndex ?? -1; if (i < 0 || i >= _lineLayers.Count) return null; return _lineLayers[i]; } }

        public SymbolEditorDialog(ClassDefinition cd, GeometryType geomType, List<string> fieldNames = null)
        {
            InitializeComponent();
            ClassDef = cd;
            _geomType = geomType;
            _fieldNames = fieldNames ?? new List<string>();
            TxtClassTitle.Text = $"类别: [{cd.Value}] {cd.Label}";

            TabPointSym.Visibility = (geomType == GeometryType.Point || geomType == GeometryType.Multipoint)
                ? Visibility.Visible : Visibility.Collapsed;
            TabLineSym.Visibility = (geomType == GeometryType.Polyline)
                ? Visibility.Visible : Visibility.Collapsed;
            TabFillSym.Visibility = (geomType == GeometryType.Polygon || geomType == GeometryType.Envelope)
                ? Visibility.Visible : Visibility.Collapsed;

            LstPointLayers.ItemsSource = _pointLayers;
            LstLineLayers.ItemsSource = _lineLayers;
            LstFillLayers.ItemsSource = _fillLayers;
            Loaded += (s, e) => LoadFromClassDef();
        }

        private void LoadFromClassDef()
        {
            _loading = true;
            try
            {
                InitMarkerStyles();
                InitFontFamilies();

                if (_geomType == GeometryType.Point || _geomType == GeometryType.Multipoint)
                    { SymTabs.SelectedIndex = 0; LoadPointFromClassDef(); }
                else if (_geomType == GeometryType.Polyline)
                    { SymTabs.SelectedIndex = 1; LoadLineFromClassDef(); }
                else
                    { SymTabs.SelectedIndex = 2; LoadFillFromClassDef(); }
            }
            finally
            {
                _loading = false;
                RefreshPreview();
            }
        }

        private void InitMarkerStyles()
        {
            if (CboMarkerStyle.Items.Count > 0) return;
            var styles = new (string, string)[]
            {
                ("实心圆", "Circle"), ("空心圆", "HollowCircle"), ("实心三角", "Triangle"),
                ("空心三角", "HollowTriangle"), ("十字", "Cross"), ("叉", "X"),
                ("星形", "Star"), ("菱形", "Diamond"), ("正方形", "Square"), ("六边形", "Hexagon"),
                ("短直线", "ShortLine"), ("箭头", "Arrow")
            };
            foreach (var (name, tag) in styles)
                CboMarkerStyle.Items.Add(new ComboBoxItem { Content = name, Tag = tag });
            CboMarkerStyle.SelectedIndex = 0;
        }

        private void InitFontFamilies()
        {
            if (CboPtFontFamily.Items.Count > 0) return;
            foreach (var ff in System.Windows.Media.Fonts.SystemFontFamilies.OrderBy(f => f.Source))
                CboPtFontFamily.Items.Add(ff.Source);
            CboPtFontFamily.SelectedItem = "Arial";
            if (CboPtFontFamily.SelectedIndex < 0 && CboPtFontFamily.Items.Count > 0)
                CboPtFontFamily.SelectedIndex = 0;
        }

        // ============== Point Layer Management ==============

        private void LoadPointFromClassDef()
        {
            _pointLayers.Clear();
            if (ClassDef.PointLayers.Count > 0)
            {
                foreach (var l in ClassDef.PointLayers)
                    _pointLayers.Add(l.Clone());
            }
            else
            {
                var def = new PointSymbolLayerDef
                {
                    Kind = PointSymbolLayerKind.SimpleMarker,
                    Name = "点符号层",
                    Color = ClassDef.Color,
                    OutlineColor = ClassDef.OutlineColor,
                    OutlineWidth = Math.Max(0, ClassDef.OutlineWidth)
                };
                _pointLayers.Add(def);
            }
            if (_pointLayers.Count > 0)
                LstPointLayers.SelectedIndex = 0;
            RefreshPointLayerList();
        }

        private void LstPointLayers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is PointSymbolLayerDef oldDef)
                SavePointLayerToDef(oldDef);
            LoadPointLayerToUi();
        }

        private void SavePointLayerToDef(PointSymbolLayerDef def)
        {
            if (def == null) return;
            def.Enabled = ChkPtLayerEnabled.IsChecked == true;
            def.Color = GetBorderColor(BrdPtLayerColor);
            def.Size = SldPtLayerSize.Value;
            def.Angle = SldPtLayerAngle.Value;
            if (def.Kind == PointSymbolLayerKind.SimpleMarker)
            {
                var sel = CboMarkerStyle?.SelectedItem as ComboBoxItem;
                def.MarkerStyle = sel?.Tag?.ToString() ?? "Circle";
                def.OutlineColor = GetBorderColor(BrdPtLayerOutline);
                def.OutlineWidth = SldPtLayerOutlineWidth.Value;
            }
            else if (def.Kind == PointSymbolLayerKind.SvgMarker)
                def.FilePath = TxtSvgPath.Text?.Trim() ?? "";
            else if (def.Kind == PointSymbolLayerKind.VectorField)
            {
                def.DirectionField = CboVectorDirField?.SelectedItem?.ToString() ?? "";
                def.MagnitudeField = CboVectorMagField?.SelectedItem?.ToString() ?? "";
            }
            else if (def.Kind == PointSymbolLayerKind.FontMarker)
            {
                def.FontFamily = CboPtFontFamily?.SelectedItem?.ToString() ?? "Arial";
                def.CharString = TxtPtFontChar.Text?.Trim() ?? "";
            }
            // 通用锚点/偏移/缩放
            if (double.TryParse(TxtAnchorX.Text, out double ax)) def.AnchorX = Math.Max(0, Math.Min(1, ax));
            if (double.TryParse(TxtAnchorY.Text, out double ay)) def.AnchorY = Math.Max(0, Math.Min(1, ay));
            if (double.TryParse(TxtOffsetX.Text, out double ox)) def.OffsetX = ox;
            if (double.TryParse(TxtOffsetY.Text, out double oy)) def.OffsetY = oy;
            def.Scale = Math.Max(0.1, Math.Min(5.0, SldPtScale.Value));
        }

        private void BtnAddPointLayer_Click(object sender, RoutedEventArgs e)
        {
            if (CboAddPointLayer?.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string tag = item.Tag.ToString();
                PointSymbolLayerKind kind = PointSymbolLayerKind.SimpleMarker;
                switch (tag)
                {
                    case "SimpleMarker": kind = PointSymbolLayerKind.SimpleMarker; break;
                    case "SvgMarker": kind = PointSymbolLayerKind.SvgMarker; break;
                    case "VectorField": kind = PointSymbolLayerKind.VectorField; break;
                    case "FontMarker": kind = PointSymbolLayerKind.FontMarker; break;
                    case "ShortLine": kind = PointSymbolLayerKind.SimpleMarker; break;
                    case "Arrow": kind = PointSymbolLayerKind.SimpleMarker; break;
                }
                string prefix = "";
                switch (tag)
                {
                    case "SimpleMarker": prefix = "简单点"; break;
                    case "SvgMarker": prefix = "SVG"; break;
                    case "VectorField": prefix = "向量场"; break;
                    case "FontMarker": prefix = "字体"; break;
                    case "ShortLine": prefix = "短直线"; break;
                    case "Arrow": prefix = "箭头"; break;
                }
                var def = new PointSymbolLayerDef
                {
                    Kind = kind,
                    Name = prefix + (_pointLayers.Count + 1),
                    Color = DrawingColor.FromArgb(255, 88, 166, 255),
                    Size = 10,
                    Enabled = true
                };
                if (tag == "ShortLine" || tag == "Arrow")
                    def.MarkerStyle = tag;
                _pointLayers.Add(def);
                RefreshPointLayerList();
                LstPointLayers.SelectedIndex = _pointLayers.Count - 1;
                RefreshPreview();
            }
        }

        private void BtnRemovePointLayer_Click(object sender, RoutedEventArgs e)
        {
            int i = LstPointLayers.SelectedIndex;
            if (i < 0) return;
            _pointLayers.RemoveAt(i);
            RefreshPointLayerList();
            if (_pointLayers.Count > 0)
                LstPointLayers.SelectedIndex = Math.Min(i, _pointLayers.Count - 1);
            RefreshPreview();
        }

        private void BtnPointLayerUp_Click(object sender, RoutedEventArgs e)
        {
            int i = LstPointLayers.SelectedIndex;
            if (i <= 0) return;
            SaveCurrentPointLayerToDef();
            var item = _pointLayers[i];
            _pointLayers.RemoveAt(i);
            _pointLayers.Insert(i - 1, item);
            RefreshPointLayerList();
            LstPointLayers.SelectedIndex = i - 1;
            RefreshPreview();
        }

        private void BtnPointLayerDown_Click(object sender, RoutedEventArgs e)
        {
            int i = LstPointLayers.SelectedIndex;
            if (i < 0 || i >= _pointLayers.Count - 1) return;
            SaveCurrentPointLayerToDef();
            var item = _pointLayers[i];
            _pointLayers.RemoveAt(i);
            _pointLayers.Insert(i + 1, item);
            RefreshPointLayerList();
            LstPointLayers.SelectedIndex = i + 1;
            RefreshPreview();
        }

        private void RefreshPointLayerList()
        {
            int sel = LstPointLayers?.SelectedIndex ?? -1;
            LstPointLayers.ItemsSource = null;
            LstPointLayers.ItemsSource = _pointLayers;
            if (_pointLayers.Count > 0)
                LstPointLayers.SelectedIndex = Math.Max(0, Math.Min(sel, _pointLayers.Count - 1));
            LoadPointLayerToUi();
        }

        private void LoadPointLayerToUi()
        {
            var def = SelectedPointLayer;
            if (def == null)
            {
                ChkPtLayerEnabled.IsChecked = false;
                PanelPtSimpleMarker.Visibility = Visibility.Collapsed;
                PanelPtSvgMarker.Visibility = Visibility.Collapsed;
                PanelPtVectorField.Visibility = Visibility.Collapsed;
                PanelPtFontMarker.Visibility = Visibility.Collapsed;
                return;
            }
            ChkPtLayerEnabled.IsChecked = def.Enabled;
            SetBorderColor(BrdPtLayerColor, def.Color);
            SldPtLayerSize.Value = Math.Max(2, Math.Min(48, def.Size));
            TxtPtLayerSize.Text = def.Size.ToString("F0");
            SldPtLayerAngle.Value = Math.Max(0, Math.Min(360, def.Angle));
            if (TxtPtLayerAngle != null) TxtPtLayerAngle.Text = def.Angle.ToString("F0");

            PanelPtSimpleMarker.Visibility = def.Kind == PointSymbolLayerKind.SimpleMarker ? Visibility.Visible : Visibility.Collapsed;
            PanelPtSvgMarker.Visibility = def.Kind == PointSymbolLayerKind.SvgMarker ? Visibility.Visible : Visibility.Collapsed;
            PanelPtVectorField.Visibility = def.Kind == PointSymbolLayerKind.VectorField ? Visibility.Visible : Visibility.Collapsed;
            PanelPtFontMarker.Visibility = def.Kind == PointSymbolLayerKind.FontMarker ? Visibility.Visible : Visibility.Collapsed;
            PanelPtLineArrow.Visibility = (def.Kind == PointSymbolLayerKind.SimpleMarker && (def.MarkerStyle == "ShortLine" || def.MarkerStyle == "Arrow")) ? Visibility.Visible : Visibility.Collapsed;

            if (def.Kind == PointSymbolLayerKind.SimpleMarker)
            {
                SelectComboByTag(CboMarkerStyle, def.MarkerStyle);
                SetBorderColor(BrdPtLayerOutline, def.OutlineColor);
                SldPtLayerOutlineWidth.Value = Math.Max(0, Math.Min(6, def.OutlineWidth));
            }
            else if (def.Kind == PointSymbolLayerKind.SvgMarker)
            {
                TxtSvgPath.Text = def.FilePath ?? "";
            }
            else if (def.Kind == PointSymbolLayerKind.VectorField)
            {
                InitFieldCombo(CboVectorDirField, def.DirectionField);
                InitFieldCombo(CboVectorMagField, def.MagnitudeField);
            }
            else if (def.Kind == PointSymbolLayerKind.FontMarker)
            {
                if (!string.IsNullOrEmpty(def.FontFamily))
                    CboPtFontFamily.SelectedItem = def.FontFamily;
                TxtPtFontChar.Text = def.CharString ?? "";
            }
            else if (def.Kind == PointSymbolLayerKind.SimpleMarker && (def.MarkerStyle == "ShortLine" || def.MarkerStyle == "Arrow"))
            {
                SldPtLineLength.Value = Math.Max(4, Math.Min(64, def.LineLength));
                TxtPtLineLength.Text = def.LineLength.ToString("F0");
            }
            // 通用锚点/偏移/缩放
            TxtAnchorX.Text = def.AnchorX.ToString("F2");
            TxtAnchorY.Text = def.AnchorY.ToString("F2");
            TxtOffsetX.Text = def.OffsetX.ToString("F0");
            TxtOffsetY.Text = def.OffsetY.ToString("F0");
            SldPtScale.Value = Math.Max(0.1, Math.Min(5.0, def.Scale));
            TxtPtScale.Text = def.Scale.ToString("F1");
        }

        private void SaveCurrentPointLayerToDef()
        {
            SavePointLayerToDef(SelectedPointLayer);
        }

        private void PtLayerControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SaveCurrentPointLayerToDef();
            if (sender == SldPtLayerSize && TxtPtLayerSize != null)
                TxtPtLayerSize.Text = SldPtLayerSize.Value.ToString("F0");
            if (sender == SldPtLayerAngle && TxtPtLayerAngle != null)
                TxtPtLayerAngle.Text = SldPtLayerAngle.Value.ToString("F0");
            if (sender == SldPtLineLength && TxtPtLineLength != null)
                TxtPtLineLength.Text = SldPtLineLength.Value.ToString("F0");
            if (sender == SldPtScale && TxtPtScale != null)
                TxtPtScale.Text = SldPtScale.Value.ToString("F1");
            RefreshPreview();
        }

        private void AngleText_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading || TxtPtLayerAngle == null) return;
            if (double.TryParse(TxtPtLayerAngle.Text, out double ang))
            {
                ang = Math.Max(0, Math.Min(360, ang));
                if (Math.Abs(SldPtLayerAngle.Value - ang) > 0.5)
                {
                    _loading = true;
                    SldPtLayerAngle.Value = ang;
                    _loading = false;
                    SaveCurrentPointLayerToDef();
                    RefreshPreview();
                }
            }
        }

        private void BtnPtLayerColor_Click(object sender, RoutedEventArgs e)
        {
            var def = SelectedPointLayer;
            if (def == null) return;
            def.Color = PickColor(def.Color);
            SetBorderColor(BrdPtLayerColor, def.Color);
            RefreshPreview();
        }

        private void BtnPtLayerOutline_Click(object sender, RoutedEventArgs e)
        {
            var def = SelectedPointLayer;
            if (def == null) return;
            def.OutlineColor = PickColor(def.OutlineColor);
            SetBorderColor(BrdPtLayerOutline, def.OutlineColor);
            RefreshPreview();
        }

        private void BtnBrowseSvg_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SVG 文件|*.svg|所有文件|*.*",
                Title = "选择 SVG 符号文件"
            };
            if (dlg.ShowDialog() == true)
            {
                TxtSvgPath.Text = dlg.FileName;
                SaveCurrentPointLayerToDef();
                RefreshPreview();
            }
        }

        private void InitFieldCombo(ComboBox cbo, string selectValue)
        {
            if (cbo == null) return;
            cbo.Items.Clear();
            cbo.Items.Add("(无)");
            foreach (var fn in _fieldNames)
                cbo.Items.Add(fn);
            if (!string.IsNullOrEmpty(selectValue) && _fieldNames.Contains(selectValue))
                cbo.SelectedItem = selectValue;
            else
                cbo.SelectedIndex = 0;
        }

        // ============== Line Layer Management ==============

        private void LoadLineFromClassDef()
        {
            _lineColor = ClassDef.Color;
            _lineLayers.Clear();
            if (ClassDef.LineLayers.Count > 0)
            {
                foreach (var l in ClassDef.LineLayers)
                    _lineLayers.Add(l.Clone());
            }
            else
            {
                // 默认一个简单线层
                _lineLayers.Add(new LineSymbolLayerDef
                {
                    Kind = LineSymbolLayerKind.SimpleStroke,
                    Color = ClassDef.Color,
                    Width = 2,
                    StrokeStyle = "Solid",
                    Enabled = true
                });
            }
            RefreshLineLayerList();
        }

        private void RefreshLineLayerList()
        {
            if (LstLineLayers == null) return;
            int sel = LstLineLayers.SelectedIndex;
            LstLineLayers.ItemsSource = null;
            LstLineLayers.ItemsSource = _lineLayers;
            if (_lineLayers.Count > 0)
                LstLineLayers.SelectedIndex = Math.Max(0, Math.Min(sel, _lineLayers.Count - 1));
            LoadLineLayerToUi();
        }

        private void LstLineLayers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is LineSymbolLayerDef oldDef)
                SaveLineLayerToDef(oldDef);
            LoadLineLayerToUi();
            RefreshPreview();
        }

        private void LoadLineLayerToUi()
        {
            var def = SelectedLineLayer;
            if (def == null)
            {
                if (PanelLineSimpleStroke != null) PanelLineSimpleStroke.Visibility = Visibility.Collapsed;
                if (PanelLineMarkerLine != null) PanelLineMarkerLine.Visibility = Visibility.Collapsed;
                if (PanelLineArrowLine != null) PanelLineArrowLine.Visibility = Visibility.Collapsed;
                if (PanelLineWave != null) PanelLineWave.Visibility = Visibility.Collapsed;
                return;
            }
            // 懒加载保护：线标签页内容可能在首次打开前未被加载，此时各面板为 null
            if (ChkLineLayerEnabled == null) return;
            _syncing = true;
            ChkLineLayerEnabled.IsChecked = def.Enabled;
            SetBorderColor(BrdLineLayerColor, def.Color);
            SldLineLayerWidth.Value = Math.Max(0.5, Math.Min(20, def.Width));
            TxtLineLayerWidth.Text = def.Width.ToString("F1");
            SldLineDashSpacing.Value = Math.Max(1, Math.Min(20, def.DashSpacing));
            TxtLineDashSpacing.Text = def.DashSpacing.ToString("F0");
            TxtLineAnchorX.Text = def.AnchorX.ToString("F2");
            TxtLineAnchorY.Text = def.AnchorY.ToString("F2");
            TxtLineOffsetX.Text = def.OffsetX.ToString("F0");
            TxtLineOffsetY.Text = def.OffsetY.ToString("F0");
            SldLineScale.Value = Math.Max(0.1, Math.Min(5.0, def.Scale));
            TxtLineScale.Text = def.Scale.ToString("F1");

            // Show/hide panels (含 null 保护：首次打开线标签页前面板可能未加载)
            if (PanelLineSimpleStroke != null)
                PanelLineSimpleStroke.Visibility = def.Kind == LineSymbolLayerKind.SimpleStroke ? Visibility.Visible : Visibility.Collapsed;
            if (PanelLineMarkerLine != null)
                PanelLineMarkerLine.Visibility = def.Kind == LineSymbolLayerKind.MarkerLine ? Visibility.Visible : Visibility.Collapsed;
            if (PanelLineArrowLine != null)
                PanelLineArrowLine.Visibility = def.Kind == LineSymbolLayerKind.ArrowLine ? Visibility.Visible : Visibility.Collapsed;
            if (PanelLineWave != null)
                PanelLineWave.Visibility = (def.Kind == LineSymbolLayerKind.WaveLine || def.Kind == LineSymbolLayerKind.SawtoothLine) ? Visibility.Visible : Visibility.Collapsed;

            if (def.Kind == LineSymbolLayerKind.SimpleStroke)
                SelectComboByTag(CboLineStrokeStyle, def.StrokeStyle);
            else if (def.Kind == LineSymbolLayerKind.MarkerLine)
            {
                SldLineMarkerSize.Value = Math.Max(2, Math.Min(24, def.MarkerSize));
                SldLineMarkerInterval.Value = Math.Max(4, Math.Min(80, def.MarkerInterval));
                SelectComboByTag(CboLineMarkerPlacement, def.MarkerPlacement);
                SetBorderColor(BrdLineMarkerColor, def.MarkerColor);
            }
            else if (def.Kind == LineSymbolLayerKind.ArrowLine)
            {
                SelectComboByTag(CboLineArrowPlacement, def.ArrowPlacement);
                SldLineArrowSize.Value = Math.Max(4, Math.Min(32, def.ArrowSize));
                TxtLineArrowSize.Text = def.ArrowSize.ToString("F0");
            }
            else if (def.Kind == LineSymbolLayerKind.WaveLine || def.Kind == LineSymbolLayerKind.SawtoothLine)
            {
                SldLineWaveAmplitude.Value = Math.Max(2, Math.Min(40, def.WaveAmplitude));
                TxtLineWaveAmplitude.Text = def.WaveAmplitude.ToString("F0");
                SldLineWaveLength.Value = Math.Max(4, Math.Min(60, def.WaveLength));
                TxtLineWaveLength.Text = def.WaveLength.ToString("F0");
            }
            _syncing = false;
        }

        private void SaveCurrentLineLayerToDef()
        {
            SaveLineLayerToDef(SelectedLineLayer);
        }

        private void SaveLineLayerToDef(LineSymbolLayerDef def)
        {
            if (def == null) return;
            def.Enabled = ChkLineLayerEnabled.IsChecked == true;
            def.Color = GetBorderColor(BrdLineLayerColor);
            def.Width = SldLineLayerWidth.Value;
            def.DashSpacing = SldLineDashSpacing.Value;
            if (TxtLineAnchorX.Text != null && double.TryParse(TxtLineAnchorX.Text, out double anx)) def.AnchorX = anx;
            if (TxtLineAnchorY.Text != null && double.TryParse(TxtLineAnchorY.Text, out double any)) def.AnchorY = any;
            if (TxtLineOffsetX.Text != null && double.TryParse(TxtLineOffsetX.Text, out double offx)) def.OffsetX = offx;
            if (TxtLineOffsetY.Text != null && double.TryParse(TxtLineOffsetY.Text, out double offy)) def.OffsetY = offy;
            def.Scale = SldLineScale.Value;
            switch (def.Kind)
            {
                case LineSymbolLayerKind.SimpleStroke:
                    if (CboLineStrokeStyle.SelectedItem is ComboBoxItem si && si.Tag != null)
                        def.StrokeStyle = si.Tag.ToString();
                    break;
                case LineSymbolLayerKind.MarkerLine:
                    def.MarkerSize = SldLineMarkerSize.Value;
                    def.MarkerInterval = SldLineMarkerInterval.Value;
                    if (CboLineMarkerPlacement.SelectedItem is ComboBoxItem mp && mp.Tag != null)
                        def.MarkerPlacement = mp.Tag.ToString();
                    def.MarkerColor = GetBorderColor(BrdLineMarkerColor);
                    break;
                case LineSymbolLayerKind.ArrowLine:
                    if (CboLineArrowPlacement.SelectedItem is ComboBoxItem ap && ap.Tag != null)
                        def.ArrowPlacement = ap.Tag.ToString();
                    def.ArrowSize = SldLineArrowSize.Value;
                    break;
                case LineSymbolLayerKind.WaveLine:
                case LineSymbolLayerKind.SawtoothLine:
                    def.WaveAmplitude = SldLineWaveAmplitude.Value;
                    def.WaveLength = SldLineWaveLength.Value;
                    break;
            }
        }

        private void LineLayerControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || _syncing) return;
            SaveLineLayerToDef(SelectedLineLayer);
            if (sender == SldLineLayerWidth && TxtLineLayerWidth != null)
                TxtLineLayerWidth.Text = SldLineLayerWidth.Value.ToString("F1");
            if (sender == SldLineDashSpacing && TxtLineDashSpacing != null)
                TxtLineDashSpacing.Text = SldLineDashSpacing.Value.ToString("F0");
            if (sender == SldLineArrowSize && TxtLineArrowSize != null)
                TxtLineArrowSize.Text = SldLineArrowSize.Value.ToString("F0");
            if (sender == SldLineWaveAmplitude && TxtLineWaveAmplitude != null)
                TxtLineWaveAmplitude.Text = SldLineWaveAmplitude.Value.ToString("F0");
            if (sender == SldLineWaveLength && TxtLineWaveLength != null)
                TxtLineWaveLength.Text = SldLineWaveLength.Value.ToString("F0");
            if (sender == SldLineScale && TxtLineScale != null)
                TxtLineScale.Text = SldLineScale.Value.ToString("F1");
            RefreshPreview();
        }

        private void BtnAddLineLayer_Click(object sender, RoutedEventArgs e)
        {
            SaveLineLayerToDef(SelectedLineLayer);
            string tag = "SimpleStroke";
            if (CboAddLineLayer?.SelectedItem is ComboBoxItem item && item.Tag != null)
                tag = item.Tag.ToString();
            LineSymbolLayerKind kind = LineSymbolLayerKind.SimpleStroke;
            switch (tag)
            {
                case "SimpleStroke": kind = LineSymbolLayerKind.SimpleStroke; break;
                case "MarkerLine": kind = LineSymbolLayerKind.MarkerLine; break;
                case "ArrowLine": kind = LineSymbolLayerKind.ArrowLine; break;
                case "WaveLine": kind = LineSymbolLayerKind.WaveLine; break;
                case "SawtoothLine": kind = LineSymbolLayerKind.SawtoothLine; break;
            }
            var def = new LineSymbolLayerDef
            {
                Kind = kind,
                Color = DrawingColor.FromArgb(255, 88, 166, 255),
                Width = 2,
                Enabled = true
            };
            _lineLayers.Add(def);
            RefreshLineLayerList();
            LstLineLayers.SelectedIndex = _lineLayers.Count - 1;
            RefreshPreview();
        }

        private void BtnRemoveLineLayer_Click(object sender, RoutedEventArgs e)
        {
            var def = SelectedLineLayer;
            if (def == null) return;
            int idx = _lineLayers.IndexOf(def);
            _lineLayers.RemoveAt(idx);
            RefreshLineLayerList();
            if (_lineLayers.Count > 0)
                LstLineLayers.SelectedIndex = Math.Min(idx, _lineLayers.Count - 1);
            RefreshPreview();
        }

        private void BtnLineLayerUp_Click(object sender, RoutedEventArgs e)
        {
            int i = LstLineLayers.SelectedIndex;
            if (i <= 0) return;
            SaveLineLayerToDef(SelectedLineLayer);
            var item = _lineLayers[i];
            _lineLayers.RemoveAt(i);
            _lineLayers.Insert(i - 1, item);
            RefreshLineLayerList();
            LstLineLayers.SelectedIndex = i - 1;
            RefreshPreview();
        }

        private void BtnLineLayerDown_Click(object sender, RoutedEventArgs e)
        {
            int i = LstLineLayers.SelectedIndex;
            if (i < 0 || i >= _lineLayers.Count - 1) return;
            SaveLineLayerToDef(SelectedLineLayer);
            var item = _lineLayers[i];
            _lineLayers.RemoveAt(i);
            _lineLayers.Insert(i + 1, item);
            RefreshLineLayerList();
            LstLineLayers.SelectedIndex = i + 1;
            RefreshPreview();
        }

        private void BtnLineLayerColor_Click(object sender, RoutedEventArgs e)
        {
            var def = SelectedLineLayer;
            if (def == null) return;
            def.Color = PickColor(def.Color);
            SetBorderColor(BrdLineLayerColor, def.Color);
            RefreshPreview();
        }

        private void BtnLineMarkerColor_Click(object sender, RoutedEventArgs e)
        {
            var def = SelectedLineLayer;
            if (def == null) return;
            if (def.Kind == LineSymbolLayerKind.MarkerLine)
            {
                def.MarkerColor = PickColor(def.MarkerColor);
                SetBorderColor(BrdLineMarkerColor, def.MarkerColor);
                RefreshPreview();
            }
        }

        private void BtnLineSelectPointSymbol_Click(object sender, RoutedEventArgs e)
        {
            var def = SelectedLineLayer;
            if (def == null || def.Kind != LineSymbolLayerKind.MarkerLine) return;
            SaveLineLayerToDef(SelectedLineLayer);
            // 用临时的 ClassDefinition 打开点符号编辑器
            var cd = new ClassDefinition
            {
                Color = def.MarkerPointLayers.FirstOrDefault()?.Color ?? def.MarkerColor,
            };
            foreach (var pl in def.MarkerPointLayers)
                cd.PointLayers.Add(pl.Clone());
            if (cd.PointLayers.Count == 0)
                cd.PointLayers.Add(new PointSymbolLayerDef { Kind = PointSymbolLayerKind.SimpleMarker, Color = def.MarkerColor, Size = def.MarkerSize, MarkerStyle = def.MarkerShape });
            var dlg = new SymbolEditorDialog(cd, GeometryType.Point, _fieldNames);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                def.MarkerPointLayers.Clear();
                foreach (var pl in cd.PointLayers)
                    def.MarkerPointLayers.Add(pl.Clone());
                // 从第一个点符号层的形状更新 MarkerShape
                if (def.MarkerPointLayers.Count > 0)
                {
                    var first = def.MarkerPointLayers[0];
                    def.MarkerColor = first.Color;
                    if (first.Kind == PointSymbolLayerKind.SimpleMarker)
                    {
                        string ms = first.MarkerStyle;
                        if (ms == "Circle" || ms == "HollowCircle" || ms == "Triangle" || ms == "HollowTriangle" ||
                            ms == "Cross" || ms == "X" || ms == "Star" || ms == "Diamond" || ms == "Square" ||
                            ms == "Hexagon" || ms == "ShortLine" || ms == "Arrow")
                            def.MarkerShape = ms;
                        else
                            def.MarkerShape = "Circle";
                    }
                    else
                        def.MarkerShape = "Circle";
                }
                SetBorderColor(BrdLineMarkerColor, def.MarkerColor);
                RefreshLineLayerList();
                RefreshPreview();
            }
        }

        // ============== Fill Layer Management (multi-layer, like point/line editor) ==============

        private void LoadFillFromClassDef()
        {
            SetBorderColor(BrdOutlineColor, ClassDef.OutlineColor);
            SldOutlineWidth.Value = Math.Max(0, Math.Min(12, ClassDef.OutlineWidth));
            _fillLayers.Clear();
            if (ClassDef.FillLayers.Count > 0)
            {
                foreach (var l in ClassDef.FillLayers)
                    _fillLayers.Add(l.Clone());
            }
            else
            {
                foreach (var d in PatternFillHelper.CreateDefaultLayers(ClassDef.FillColor))
                    _fillLayers.Add(d);
            }
            RefreshFillLayerList();
        }

        private void RefreshFillLayerList()
        {
            int sel = LstFillLayers?.SelectedIndex ?? -1;
            LstFillLayers.ItemsSource = null;
            LstFillLayers.ItemsSource = _fillLayers;
            if (_fillLayers.Count > 0)
                LstFillLayers.SelectedIndex = Math.Max(0, Math.Min(sel, _fillLayers.Count - 1));
            LoadFillLayerToUi();
        }

        private void LoadFillLayerToUi()
        {
            var def = SelectedFillLayer;
            if (def == null) return;
            ChkLayerEnabled.IsChecked = def.Enabled;
            SetBorderColor(BrdLayerColor, def.Color);
            SldLayerOpacity.Value = Math.Max(0, Math.Min(100, def.Opacity * 100));
            TxtLayerOpacity.Text = (int)SldLayerOpacity.Value + "%";
            SldLayerAngle.Value = Math.Max(0, Math.Min(180, def.Angle));
            TxtLayerAngle.Text = ((int)SldLayerAngle.Value).ToString();
            SldLayerSpacing.Value = Math.Max(2, Math.Min(48, def.Spacing));
            TxtLayerSpacing.Text = ((int)SldLayerSpacing.Value).ToString();
            if (def.Kind == FillSymbolLayerKind.Lines || def.Kind == FillSymbolLayerKind.CrossHatch || def.Kind == FillSymbolLayerKind.Arrows)
                SldLayerSize.Value = Math.Max(0.5, Math.Min(32, def.LineWidth));
            else
                SldLayerSize.Value = Math.Max(0.5, Math.Min(32, def.Size));
            TxtLayerSize.Text = SldLayerSize.Value.ToString("F1");
            SldLayerOffX.Value = Math.Max(-20, Math.Min(20, def.OffsetX));
            TxtLayerOffX.Text = ((int)SldLayerOffX.Value).ToString();
            SldLayerOffY.Value = Math.Max(-20, Math.Min(20, def.OffsetY));
            TxtLayerOffY.Text = ((int)SldLayerOffY.Value).ToString();
            SldLayerDensity.Value = Math.Max(0.2, Math.Min(3, def.Density));
            TxtLayerDensity.Text = SldLayerDensity.Value.ToString("F1");

            bool isLineKind = def.Kind == FillSymbolLayerKind.Lines || def.Kind == FillSymbolLayerKind.CrossHatch || def.Kind == FillSymbolLayerKind.Arrows;
            bool isPointKind = def.Kind == FillSymbolLayerKind.PointsGrid || def.Kind == FillSymbolLayerKind.PointsRandom;
            bool isSvg = def.Kind == FillSymbolLayerKind.SvgOrImage;
            bool isGradient = def.Kind == FillSymbolLayerKind.Gradient;

            if (PanelFillLineStyle != null) PanelFillLineStyle.Visibility = isLineKind ? Visibility.Visible : Visibility.Collapsed;
            if (PanelFillPointStyle != null) PanelFillPointStyle.Visibility = isPointKind ? Visibility.Visible : Visibility.Collapsed;
            if (PanelFillSvg != null) PanelFillSvg.Visibility = isSvg ? Visibility.Visible : Visibility.Collapsed;
            if (PanelFillGradient != null) PanelFillGradient.Visibility = isGradient ? Visibility.Visible : Visibility.Collapsed;
            // 密度仅 Arrows 类型可见
            if (PanelFillDensity != null) PanelFillDensity.Visibility = def.Kind == FillSymbolLayerKind.Arrows ? Visibility.Visible : Visibility.Collapsed;

            if (isSvg && TxtFillSvgPath != null)
                TxtFillSvgPath.Text = string.IsNullOrEmpty(def.FilePath) ? "(未选择)" : System.IO.Path.GetFileName(def.FilePath);
            if (isGradient)
            {
                if (CboGradientType != null) SelectComboByTag(CboGradientType, def.GradientType);
                if (BrdGradientColor1 != null) SetBorderColor(BrdGradientColor1, def.GradientColor1);
                if (BrdGradientColor2 != null) SetBorderColor(BrdGradientColor2, def.GradientColor2);
            }
        }

        private void SaveFillLayerToDef(FillSymbolLayerDef def)
        {
            if (def == null) return;
            def.Enabled = ChkLayerEnabled.IsChecked == true;
            def.Color = GetBorderColor(BrdLayerColor);
            def.Opacity = SldLayerOpacity.Value / 100.0;
            def.Angle = SldLayerAngle.Value;
            def.Spacing = SldLayerSpacing.Value;
            if (def.Kind == FillSymbolLayerKind.Lines || def.Kind == FillSymbolLayerKind.CrossHatch || def.Kind == FillSymbolLayerKind.Arrows)
                def.LineWidth = SldLayerSize.Value;
            else
                def.Size = SldLayerSize.Value;
            def.OffsetX = SldLayerOffX.Value;
            def.OffsetY = SldLayerOffY.Value;
            def.Density = SldLayerDensity.Value;
            if (def.Kind == FillSymbolLayerKind.SvgOrImage && TxtFillSvgPath != null)
                def.FilePath = TxtFillSvgPath.Text;
            if (def.Kind == FillSymbolLayerKind.Gradient)
            {
                if (CboGradientType?.SelectedItem is ComboBoxItem gi && gi.Tag != null)
                    def.GradientType = gi.Tag.ToString();
                def.GradientColor1 = GetBorderColor(BrdGradientColor1);
                def.GradientColor2 = GetBorderColor(BrdGradientColor2);
            }
        }

        private void LstFillLayers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is FillSymbolLayerDef oldDef)
                SaveFillLayerToDef(oldDef);
            LoadFillLayerToUi();
        }

        private void FillLayerControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SaveFillLayerToDef(SelectedFillLayer);
            RefreshFillLayerList();
            RefreshPreview();
        }

        private void BtnAddFillLayer_Click(object sender, RoutedEventArgs e)
        {
            if (CboAddFillLayer?.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string tag = item.Tag.ToString();
                FillSymbolLayerKind kind = FillSymbolLayerKind.Solid;
                switch (tag)
                {
                    case "Lines": kind = FillSymbolLayerKind.Lines; break;
                    case "CrossHatch": kind = FillSymbolLayerKind.CrossHatch; break;
                    case "PointsGrid": kind = FillSymbolLayerKind.PointsGrid; break;
                    case "PointsRandom": kind = FillSymbolLayerKind.PointsRandom; break;
                    case "Arrows": kind = FillSymbolLayerKind.Arrows; break;
                    case "SvgOrImage": kind = FillSymbolLayerKind.SvgOrImage; break;
                    case "Gradient": kind = FillSymbolLayerKind.Gradient; break;
                }
                double spacing = (kind == FillSymbolLayerKind.PointsGrid || kind == FillSymbolLayerKind.PointsRandom) ? 12 : 8;
                var def = new FillSymbolLayerDef
                {
                    Kind = kind, Name = item.Content?.ToString() ?? tag,
                    Color = kind == FillSymbolLayerKind.Solid ? DrawingColor.FromArgb(255, 200, 200, 200) : DrawingColor.FromArgb(200, 60, 60, 60),
                    Opacity = 0.9, Angle = 45, Spacing = spacing, Size = 4, LineWidth = 1.2, Enabled = true
                };
                _fillLayers.Add(def);
                RefreshFillLayerList();
                LstFillLayers.SelectedIndex = _fillLayers.Count - 1;
                if (kind == FillSymbolLayerKind.SvgOrImage) BtnFillBrowseSvg_Click(null, null);
                RefreshPreview();
            }
        }

        private void BtnRemoveFillLayer_Click(object sender, RoutedEventArgs e)
        {
            int i = LstFillLayers.SelectedIndex;
            if (i < 0 || i >= _fillLayers.Count) return;
            _fillLayers.RemoveAt(i);
            if (_fillLayers.Count == 0)
                _fillLayers.Add(PatternFillHelper.CreateDefaultLayers(DrawingColor.FromArgb(255, 200, 200, 200))[0]);
            RefreshFillLayerList();
            RefreshPreview();
        }

        private void BtnFillLayerUp_Click(object sender, RoutedEventArgs e)
        {
            int i = LstFillLayers.SelectedIndex;
            if (i <= 0) return;
            SaveFillLayerToDef(SelectedFillLayer);
            var item = _fillLayers[i];
            _fillLayers.RemoveAt(i);
            _fillLayers.Insert(i - 1, item);
            RefreshFillLayerList();
            LstFillLayers.SelectedIndex = i - 1;
            RefreshPreview();
        }

        private void BtnFillLayerDown_Click(object sender, RoutedEventArgs e)
        {
            int i = LstFillLayers.SelectedIndex;
            if (i < 0 || i >= _fillLayers.Count - 1) return;
            SaveFillLayerToDef(SelectedFillLayer);
            var item = _fillLayers[i];
            _fillLayers.RemoveAt(i);
            _fillLayers.Insert(i + 1, item);
            RefreshFillLayerList();
            LstFillLayers.SelectedIndex = i + 1;
            RefreshPreview();
        }

        private void BtnLayerColor_Click(object sender, RoutedEventArgs e)
        {
            var def = SelectedFillLayer;
            if (def == null) return;
            def.Color = PickColor(def.Color);
            SetBorderColor(BrdLayerColor, def.Color);
            RefreshPreview();
        }

        private void BtnOutlineColor_Click(object sender, RoutedEventArgs e)
        {
            SetBorderColor(BrdOutlineColor, PickColor(GetBorderColor(BrdOutlineColor)));
            RefreshPreview();
        }

        private void BtnFillSelectLineStyle_Click(object sender, RoutedEventArgs e)
        {
            var def = SelectedFillLayer;
            if (def == null || (def.Kind != FillSymbolLayerKind.Lines && def.Kind != FillSymbolLayerKind.CrossHatch && def.Kind != FillSymbolLayerKind.Arrows)) return;
            SaveFillLayerToDef(SelectedFillLayer);
            var cd = new ClassDefinition { Color = def.Color };
            foreach (var ll in def.PatternLineLayers)
                cd.LineLayers.Add(ll.Clone());
            if (cd.LineLayers.Count == 0)
                cd.LineLayers.Add(new LineSymbolLayerDef { Kind = LineSymbolLayerKind.SimpleStroke, Color = def.Color, Width = def.LineWidth, StrokeStyle = "Solid" });
            var dlg = new SymbolEditorDialog(cd, GeometryType.Polyline, _fieldNames);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                def.PatternLineLayers.Clear();
                foreach (var l in cd.LineLayers)
                    def.PatternLineLayers.Add(l.Clone());
                RefreshFillLayerList();
                RefreshPreview();
            }
        }

        private void BtnFillSelectPointStyle_Click(object sender, RoutedEventArgs e)
        {
            var def = SelectedFillLayer;
            if (def == null || (def.Kind != FillSymbolLayerKind.PointsGrid && def.Kind != FillSymbolLayerKind.PointsRandom)) return;
            SaveFillLayerToDef(SelectedFillLayer);
            var cd = new ClassDefinition { Color = def.Color };
            foreach (var pl in def.PatternPointLayers)
                cd.PointLayers.Add(pl.Clone());
            if (cd.PointLayers.Count == 0)
                cd.PointLayers.Add(new PointSymbolLayerDef { Kind = PointSymbolLayerKind.SimpleMarker, Color = def.Color, Size = def.Size, MarkerStyle = "Circle" });
            var dlg = new SymbolEditorDialog(cd, GeometryType.Point, _fieldNames);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                def.PatternPointLayers.Clear();
                foreach (var pl in cd.PointLayers)
                    def.PatternPointLayers.Add(pl.Clone());
                RefreshFillLayerList();
                RefreshPreview();
            }
        }

        private void BtnFillBrowseSvg_Click(object sender, RoutedEventArgs e)
        {
            var def = SelectedFillLayer;
            if (def == null || def.Kind != FillSymbolLayerKind.SvgOrImage) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "图片文件|*.svg;*.png;*.jpg;*.bmp|SVG 文件|*.svg|所有文件|*.*",
                Title = "选择填充图案"
            };
            if (dlg.ShowDialog() == true)
            {
                if (TxtFillSvgPath != null) TxtFillSvgPath.Text = dlg.FileName;
                SaveFillLayerToDef(SelectedFillLayer);
                def.FilePath = dlg.FileName;
                RefreshPreview();
            }
        }

        private void BtnGradientColor1_Click(object sender, RoutedEventArgs e)
        {
            var def = SelectedFillLayer;
            if (def == null || def.Kind != FillSymbolLayerKind.Gradient) return;
            def.GradientColor1 = PickColor(def.GradientColor1);
            SetBorderColor(BrdGradientColor1, def.GradientColor1);
            RefreshPreview();
        }

        private void BtnGradientColor2_Click(object sender, RoutedEventArgs e)
        {
            var def = SelectedFillLayer;
            if (def == null || def.Kind != FillSymbolLayerKind.Gradient) return;
            def.GradientColor2 = PickColor(def.GradientColor2);
            SetBorderColor(BrdGradientColor2, def.GradientColor2);
            RefreshPreview();
        }

        // ============== Build Symbols ==============

        /// <summary>将 SimpleMarkerSymbol 还原为 PointSymbolLayerDef（供图层属性对话框加载单符号点）</summary>
        public static PointSymbolLayerDef SimpleMarkerToLayerDef(SimpleMarkerSymbol sms, DrawingColor fallbackColor)
        {
            string style = "Circle";
            if (sms != null)
            {
                switch (sms.Style)
                {
                    case SimpleMarkerSymbolStyle.Square: style = "Square"; break;
                    case SimpleMarkerSymbolStyle.Triangle: style = "Triangle"; break;
                    case SimpleMarkerSymbolStyle.Diamond: style = "Diamond"; break;
                    case SimpleMarkerSymbolStyle.Cross: style = "Cross"; break;
                    case SimpleMarkerSymbolStyle.X: style = "X"; break;
                    default: style = "Circle"; break;
                }
            }
            var color = sms?.Color ?? fallbackColor;
            double outlineW = 0.5;
            var outlineColor = DrawingColor.White;
            if (sms?.Outline != null)
            {
                outlineW = sms.Outline.Width;
                outlineColor = sms.Outline.Color;
            }
            return new PointSymbolLayerDef
            {
                Kind = PointSymbolLayerKind.SimpleMarker,
                Enabled = true,
                Color = color,
                Size = sms != null && sms.Size > 0 ? sms.Size : 10,
                Angle = sms?.Angle ?? 0,
                Opacity = 1.0,
                MarkerStyle = style,
                OutlineColor = outlineColor,
                OutlineWidth = outlineW
            };
        }

        /// <summary>将 PictureMarkerSymbol 还原为 PointSymbolLayerDef（SVG/位图点符号）</summary>
        public static PointSymbolLayerDef PictureMarkerToLayerDef(PictureMarkerSymbol pms, DrawingColor fallbackColor)
        {
            string path = "";
            try
            {
                if (pms?.Uri != null)
                    path = pms.Uri.IsFile ? pms.Uri.LocalPath : pms.Uri.ToString();
            }
            catch { /* ignore */ }

            double size = 10;
            if (pms != null)
            {
                double w = pms.Width > 0 ? pms.Width : 10;
                double h = pms.Height > 0 ? pms.Height : w;
                size = Math.Max(w, h);
                // BuildSinglePointSymbol 常把位图尺寸设为 effSize*2，还原时除以 2 更接近编辑尺寸
                if (size >= 12) size = size / 2.0;
            }

            return new PointSymbolLayerDef
            {
                Kind = string.IsNullOrEmpty(path) ? PointSymbolLayerKind.SimpleMarker : PointSymbolLayerKind.SvgMarker,
                Enabled = true,
                Color = fallbackColor,
                Size = size > 0 ? size : 10,
                Angle = pms?.Angle ?? 0,
                Opacity = pms != null ? Math.Max(0, Math.Min(1.0, pms.Opacity)) : 1.0,
                MarkerStyle = "Circle",
                FilePath = path ?? ""
            };
        }

        private Symbol BuildPointSymbol()
        {
            SaveCurrentPointLayerToDef();
            var enabled = _pointLayers.Where(l => l.Enabled).ToList();
            if (enabled.Count == 0)
                return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, _pointLayers.FirstOrDefault()?.Color ?? DrawingColor.Gray, 10);
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
                    var text = new TextSymbol(def.CharString ?? "?", useColor, effSize, Esri.ArcGISRuntime.Symbology.HorizontalAlignment.Center, Esri.ArcGISRuntime.Symbology.VerticalAlignment.Middle);
                    if (!string.IsNullOrEmpty(def.FontFamily)) text.FontFamily = def.FontFamily;
                    if (def.Angle != 0) text.Angle = def.Angle;
                    return text;
                }
                default:
                    return new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, def.Color, def.Size);
            }
        }

        private Symbol BuildLineSymbol()
        {
            SaveLineLayerToDef(SelectedLineLayer);
            // 波浪/锯齿/标记沿线/偏移等需要几何生成的层由 LineGeometryGenerator.ApplyOverlay 绘制，
            // 不进入渲染器，避免地图画出一条普通直线叠在生成的波浪线下。
            var enabled = _lineLayers.Where(l => l.Enabled && !LineGeometryGenerator.NeedsGeometryGeneration(l)).ToList();
            if (enabled.Count == 0)
                return new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, _lineColor, 2);
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
                        case "Hash": style = SimpleLineSymbolStyle.Dash; break;
                    }
                    return new SimpleLineSymbol(style, useColor, effWidth);
                }
                case LineSymbolLayerKind.MarkerLine:
                {
                    SimpleLineSymbolStyle baseStyle = SimpleLineSymbolStyle.Solid;
                    switch (def.StrokeStyle)
                    {
                        case "Dash": baseStyle = SimpleLineSymbolStyle.Dash; break;
                        case "Dot": baseStyle = SimpleLineSymbolStyle.Dot; break;
                        case "DashDot": baseStyle = SimpleLineSymbolStyle.DashDot; break;
                        case "DashDotDot": baseStyle = SimpleLineSymbolStyle.DashDotDot; break;
                        case "Hash": baseStyle = SimpleLineSymbolStyle.Dash; break;
                    }
                    var sls = new SimpleLineSymbol(baseStyle, useColor, effWidth);
                    sls.MarkerStyle = SimpleLineSymbolMarkerStyle.Arrow;
                    string plc = def.MarkerPlacement;
                    if (plc == "Begin")
                        sls.MarkerPlacement = SimpleLineSymbolMarkerPlacement.Begin;
                    else if (plc == "End" || plc == "Along")
                        sls.MarkerPlacement = SimpleLineSymbolMarkerPlacement.End;
                    else if (plc == "BeginAndEnd")
                        sls.MarkerPlacement = SimpleLineSymbolMarkerPlacement.BeginAndEnd;
                    return sls;
                }
                case LineSymbolLayerKind.ArrowLine:
                {
                    var sls = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, useColor, effWidth);
                    sls.MarkerStyle = SimpleLineSymbolMarkerStyle.Arrow;
                    switch (def.ArrowPlacement)
                    {
                        case "Begin": sls.MarkerPlacement = SimpleLineSymbolMarkerPlacement.Begin; break;
                        case "End": sls.MarkerPlacement = SimpleLineSymbolMarkerPlacement.End; break;
                        case "BeginAndEnd": sls.MarkerPlacement = SimpleLineSymbolMarkerPlacement.BeginAndEnd; break;
                    }
                    return sls;
                }
                case LineSymbolLayerKind.WaveLine:
                case LineSymbolLayerKind.SawtoothLine:
                    return new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, useColor, effWidth);
                default:
                    return new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, useColor, effWidth);
            }
        }

        private Symbol BuildFillSymbol()
        {
            SaveFillLayerToDef(SelectedFillLayer);
            DrawingColor outlineColor = GetBorderColor(BrdOutlineColor);
            double outlineWidth = SldOutlineWidth.Value;
            SimpleLineSymbol ol = outlineWidth > 0.01 ? new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outlineColor, outlineWidth) : null;

            var enabled = _fillLayers.Where(l => l.Enabled).ToList();
            if (enabled.Count == 0)
                return new SimpleFillSymbol(SimpleFillSymbolStyle.Null, DrawingColor.Transparent, ol);
            return PatternFillHelper.BuildFillSymbolAsync(enabled, outlineColor, outlineWidth, 64).GetAwaiter().GetResult();
        }

        private void RefreshPreview()
        {
            if (SymbolPreviewCanvas == null) return;
            ImgPatternPreview.Visibility = Visibility.Collapsed;
            SymbolPreviewCanvas.Visibility = Visibility.Visible;
            SymbolPreviewCanvas.Children.Clear();
            try
            {
                Symbol sym = null;
                if (_geomType == GeometryType.Point || _geomType == GeometryType.Multipoint)
                    sym = BuildPointSymbol();
                else if (_geomType == GeometryType.Polyline)
                {
                    SaveLineLayerToDef(SelectedLineLayer);
                    DrawMultiLinePreview();
                    return;
                }
                else
                {
                    SaveFillLayerToDef(SelectedFillLayer);
                    var enabled = _fillLayers.Where(l => l.Enabled).ToList();
                    if (enabled.Count > 0 && !(enabled.Count == 1 && enabled[0].Kind == FillSymbolLayerKind.Solid))
                    {
                        using (var bmp = PatternFillHelper.GenerateCompositeTile(enabled, 64))
                        { ShowPatternPreview(bmp); return; }
                    }
                    sym = BuildFillSymbol();
                }
                if (sym != null) DrawSymbolPreview(sym);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[SymEditor Preview] " + ex.Message); }
        }

        private void DrawSymbolPreview(Symbol symbol)
        {
            SymbolPreviewCanvas.Children.Clear();
            double cx = 120, cy = 30;
            if (symbol is CompositeSymbol cs)
            {
                foreach (var sub in cs.Symbols)
                    DrawSingleSymbolPreview(sub, cx, cy, 0.7);
            }
            else if (symbol is TextSymbol)
            {
                DrawSingleSymbolPreview(symbol, cx, cy, 1.0);
            }
            else
            {
                DrawSingleSymbolPreview(symbol, cx, cy, 1.0);
            }
        }

        private void DrawSingleSymbolPreview(Symbol symbol, double cx, double cy, double scale)
        {
            if (symbol is SimpleMarkerSymbol sms)
            {
                double sz = Math.Max(6, Math.Min(48, sms.Size * 1.5 * scale));
                var fill = new SolidColorBrush(ToWpfColor(sms.Color));
                var stroke = sms.Outline != null ? new SolidColorBrush(ToWpfColor(sms.Outline.Color)) : WpfBrushes.White;
                double sw = sms.Outline?.Width ?? 1;
                switch (sms.Style)
                {
                    case SimpleMarkerSymbolStyle.Circle:
                    {
                        var e = new Ellipse { Width = sz, Height = sz, Fill = fill, Stroke = stroke, StrokeThickness = sw };
                        Canvas.SetLeft(e, cx - sz / 2); Canvas.SetTop(e, cy - sz / 2);
                        SymbolPreviewCanvas.Children.Add(e);
                        break;
                    }
                    case SimpleMarkerSymbolStyle.Square:
                    {
                        var r = new System.Windows.Shapes.Rectangle { Width = sz, Height = sz, Fill = fill, Stroke = stroke, StrokeThickness = sw };
                        Canvas.SetLeft(r, cx - sz / 2); Canvas.SetTop(r, cy - sz / 2);
                        SymbolPreviewCanvas.Children.Add(r);
                        break;
                    }
                    case SimpleMarkerSymbolStyle.Cross:
                    {
                        double hw = sz * 0.45;
                        var line1 = new System.Windows.Shapes.Line { X1 = cx - hw, Y1 = cy, X2 = cx + hw, Y2 = cy, Stroke = fill, StrokeThickness = Math.Max(2, sz / 6) };
                        var line2 = new System.Windows.Shapes.Line { X1 = cx, Y1 = cy - hw, X2 = cx, Y2 = cy + hw, Stroke = fill, StrokeThickness = Math.Max(2, sz / 6) };
                        SymbolPreviewCanvas.Children.Add(line1);
                        SymbolPreviewCanvas.Children.Add(line2);
                        break;
                    }
                    case SimpleMarkerSymbolStyle.X:
                    {
                        double hw = sz * 0.45;
                        double a = hw * 0.707;
                        var line1 = new System.Windows.Shapes.Line { X1 = cx - a, Y1 = cy - a, X2 = cx + a, Y2 = cy + a, Stroke = fill, StrokeThickness = Math.Max(2, sz / 6) };
                        var line2 = new System.Windows.Shapes.Line { X1 = cx + a, Y1 = cy - a, X2 = cx - a, Y2 = cy + a, Stroke = fill, StrokeThickness = Math.Max(2, sz / 6) };
                        SymbolPreviewCanvas.Children.Add(line1);
                        SymbolPreviewCanvas.Children.Add(line2);
                        break;
                    }
                    case SimpleMarkerSymbolStyle.Diamond:
                    {
                        var poly = new System.Windows.Shapes.Polygon
                        {
                            Points = new System.Windows.Media.PointCollection { new System.Windows.Point(cx, cy - sz / 2), new System.Windows.Point(cx + sz / 2, cy), new System.Windows.Point(cx, cy + sz / 2), new System.Windows.Point(cx - sz / 2, cy) },
                            Fill = fill, Stroke = stroke, StrokeThickness = sw
                        };
                        SymbolPreviewCanvas.Children.Add(poly);
                        break;
                    }
                    case SimpleMarkerSymbolStyle.Triangle:
                    {
                        var poly = new System.Windows.Shapes.Polygon
                        {
                            Points = new System.Windows.Media.PointCollection { new System.Windows.Point(cx, cy - sz / 2), new System.Windows.Point(cx + sz / 2, cy + sz / 2), new System.Windows.Point(cx - sz / 2, cy + sz / 2) },
                            Fill = fill, Stroke = stroke, StrokeThickness = sw
                        };
                        SymbolPreviewCanvas.Children.Add(poly);
                        break;
                    }
                    default:
                    {
                        var e = new Ellipse { Width = sz, Height = sz, Fill = fill, Stroke = stroke, StrokeThickness = sw };
                        Canvas.SetLeft(e, cx - sz / 2); Canvas.SetTop(e, cy - sz / 2);
                        SymbolPreviewCanvas.Children.Add(e);
                        break;
                    }
                }
            }
            else if (symbol is SimpleLineSymbol sls)
            {
                var brush = new SolidColorBrush(ToWpfColor(sls.Color));
                var line = new System.Windows.Shapes.Line
                {
                    X1 = 20, Y1 = cy, X2 = 220, Y2 = cy,
                    Stroke = brush,
                    StrokeThickness = Math.Max(1, Math.Min(16, sls.Width * scale)),
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                };
                switch (sls.Style)
                {
                    case SimpleLineSymbolStyle.Dash: line.StrokeDashArray = new DoubleCollection(new[] { 6.0, 3.0 }); break;
                    case SimpleLineSymbolStyle.Dot: line.StrokeDashArray = new DoubleCollection(new[] { 1.0, 4.0 }); break;
                    case SimpleLineSymbolStyle.DashDot: line.StrokeDashArray = new DoubleCollection(new[] { 6.0, 3.0, 1.5, 3.0 }); break;
                    case SimpleLineSymbolStyle.DashDotDot: line.StrokeDashArray = new DoubleCollection(new[] { 6.0, 3.0, 1.5, 3.0, 1.5, 3.0 }); break;
                }
                if (sls.MarkerStyle == SimpleLineSymbolMarkerStyle.Arrow)
                {
                    double arrowSz = 8;
                    if (sls.MarkerPlacement == SimpleLineSymbolMarkerPlacement.End || sls.MarkerPlacement == SimpleLineSymbolMarkerPlacement.BeginAndEnd)
                    {
                        var pts = new System.Windows.Media.PointCollection { new System.Windows.Point(220, cy), new System.Windows.Point(220 - arrowSz, cy - arrowSz / 2), new System.Windows.Point(220 - arrowSz, cy + arrowSz / 2) };
                        SymbolPreviewCanvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts, Fill = brush });
                    }
                    if (sls.MarkerPlacement == SimpleLineSymbolMarkerPlacement.Begin || sls.MarkerPlacement == SimpleLineSymbolMarkerPlacement.BeginAndEnd)
                    {
                        var pts2 = new System.Windows.Media.PointCollection { new System.Windows.Point(20, cy), new System.Windows.Point(20 + arrowSz, cy - arrowSz / 2), new System.Windows.Point(20 + arrowSz, cy + arrowSz / 2) };
                        SymbolPreviewCanvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts2, Fill = brush });
                    }
                }
                SymbolPreviewCanvas.Children.Add(line);
            }
            else if (symbol is SimpleFillSymbol sfs)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = 160, Height = 50,
                    Fill = new SolidColorBrush(ToWpfColor(sfs.Color)),
                    Stroke = sfs.Outline != null ? new SolidColorBrush(ToWpfColor(sfs.Outline.Color)) : WpfBrushes.White,
                    StrokeThickness = sfs.Outline != null ? Math.Max(1, sfs.Outline.Width) : 1
                };
                Canvas.SetLeft(rect, cx - 80); Canvas.SetTop(rect, cy - 25);
                SymbolPreviewCanvas.Children.Add(rect);
            }
            else if (symbol is TextSymbol ts)
            {
                var tb = new TextBlock
                {
                    Text = ts.Text ?? "?",
                    Foreground = new SolidColorBrush(ToWpfColor(ts.Color)),
                    FontSize = Math.Max(8, Math.Min(36, ts.Size * scale)),
                    FontFamily = !string.IsNullOrEmpty(ts.FontFamily) ? new System.Windows.Media.FontFamily(ts.FontFamily) : null
                };
                Canvas.SetLeft(tb, cx - tb.FontSize * 0.3); Canvas.SetTop(tb, cy - tb.FontSize * 0.4);
                SymbolPreviewCanvas.Children.Add(tb);
            }
            else if (symbol is PictureMarkerSymbol pms)
            {
                try
                {
                    string url = pms.Uri != null ? pms.Uri.ToString() : "";
                    if (string.IsNullOrEmpty(url)) return;
                    var bi = new BitmapImage(new Uri(url));
                    double imgSz = Math.Max(12, Math.Min(48, (pms.Width > 0 ? pms.Width : 20) * scale));
                    var img = new System.Windows.Controls.Image
                    {
                        Source = bi,
                        Width = imgSz,
                        Height = imgSz,
                        Stretch = Stretch.Uniform,
                        RenderTransform = new RotateTransform(pms.Angle),
                        RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
                    };
                    Canvas.SetLeft(img, cx - imgSz / 2); Canvas.SetTop(img, cy - imgSz / 2);
                    SymbolPreviewCanvas.Children.Add(img);
                }
                catch { }
            }
        }

        private void DrawMultiLinePreview()
        {
            SymbolPreviewCanvas.Children.Clear();
            var enabled = _lineLayers.Where(l => l.Enabled).ToList();
            if (enabled.Count == 0) return;
            double baseY = 30;
            foreach (var def in enabled)
            {
                double yOff = Math.Max(-25, Math.Min(25, def.OffsetY));
                double cy = baseY + yOff;
                DrawSingleLineLayer(def, 20, cy, 220);
            }
        }

        private void DrawSingleLineLayer(LineSymbolLayerDef def, double x1, double cy, double x2)
        {
            double effW = Math.Max(0.5, Math.Min(16, def.Width * Math.Max(0.1, def.Scale)));
            var brush = new SolidColorBrush(ToWpfColor(def.Color));

            if (def.Kind == LineSymbolLayerKind.WaveLine) { DrawWavePath(def, x1, cy, x2, brush, effW); return; }
            if (def.Kind == LineSymbolLayerKind.SawtoothLine) { DrawSawtoothPath(def, x1, cy, x2, brush, effW); return; }

            // 标准线
            SimpleLineSymbolStyle style = SimpleLineSymbolStyle.Solid;
            if (def.Kind == LineSymbolLayerKind.SimpleStroke || def.Kind == LineSymbolLayerKind.MarkerLine)
            {
                switch (def.StrokeStyle)
                {
                    case "Dash": style = SimpleLineSymbolStyle.Dash; break;
                    case "Dot": style = SimpleLineSymbolStyle.Dot; break;
                    case "DashDot": style = SimpleLineSymbolStyle.DashDot; break;
                    case "DashDotDot": style = SimpleLineSymbolStyle.DashDotDot; break;
                    case "Hash": style = SimpleLineSymbolStyle.Dash; break;
                }
            }
            var line = new System.Windows.Shapes.Line
            { X1 = x1, Y1 = cy, X2 = x2, Y2 = cy, Stroke = brush, StrokeThickness = effW,
              StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            double ds = Math.Max(2, def.DashSpacing);
            switch (style) { case SimpleLineSymbolStyle.Dash: line.StrokeDashArray = new DoubleCollection(new[] { ds * 2, ds }); break; case SimpleLineSymbolStyle.Dot: line.StrokeDashArray = new DoubleCollection(new[] { 1.0, ds }); break; case SimpleLineSymbolStyle.DashDot: line.StrokeDashArray = new DoubleCollection(new[] { ds * 2, ds, ds / 2, ds }); break; case SimpleLineSymbolStyle.DashDotDot: line.StrokeDashArray = new DoubleCollection(new[] { ds * 2, ds, ds / 2, ds, ds / 2, ds }); break; }
            SymbolPreviewCanvas.Children.Add(line);

            // 箭头/标记
            if (def.Kind == LineSymbolLayerKind.ArrowLine)
            {
                bool showL = def.ArrowPlacement == "Begin" || def.ArrowPlacement == "BeginAndEnd";
                bool showR = def.ArrowPlacement == "End" || def.ArrowPlacement == "BeginAndEnd";
                double asz = effW + 6;
                if (showR) { var pts = new System.Windows.Media.PointCollection { new System.Windows.Point(x2, cy), new System.Windows.Point(x2 - asz, cy - asz / 2), new System.Windows.Point(x2 - asz, cy + asz / 2) }; SymbolPreviewCanvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts, Fill = brush }); }
                if (showL) { var pts2 = new System.Windows.Media.PointCollection { new System.Windows.Point(x1, cy), new System.Windows.Point(x1 + asz, cy - asz / 2), new System.Windows.Point(x1 + asz, cy + asz / 2) }; SymbolPreviewCanvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts2, Fill = brush }); }
            }
            else if (def.Kind == LineSymbolLayerKind.MarkerLine)
            {
                // 沿线等距放置点符号
                string plc = def.MarkerPlacement;
                if (plc == "Along")
                {
                double interval = Math.Max(4, def.MarkerInterval);
                double sz = Math.Max(2, Math.Min(16, def.MarkerSize));
                var mColor = def.MarkerColor;
                var mBrush = new SolidColorBrush(ToWpfColor(mColor));
                string shape = def.MarkerShape;
                // 如果有点符号层，从第一层获取形状
                if (def.MarkerPointLayers.Count > 0)
                {
                    var first = def.MarkerPointLayers[0];
                    if (first.Kind == PointSymbolLayerKind.SimpleMarker && !string.IsNullOrEmpty(first.MarkerStyle))
                        shape = first.MarkerStyle;
                }
                for (double x = x1; x <= x2; x += interval)
                    DrawMarkerAt(x, cy, shape, sz, mBrush);
                }
                else
                {
                    bool showL = plc == "Begin" || plc == "BeginAndEnd";
                    bool showR = plc == "End" || plc == "BeginAndEnd";
                    double asz = effW + 4;
                    if (showR) { var pts = new System.Windows.Media.PointCollection { new System.Windows.Point(x2, cy), new System.Windows.Point(x2 - asz, cy - asz / 2), new System.Windows.Point(x2 - asz, cy + asz / 2) }; SymbolPreviewCanvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts, Fill = brush }); }
                    if (showL) { var pts2 = new System.Windows.Media.PointCollection { new System.Windows.Point(x1, cy), new System.Windows.Point(x1 + asz, cy - asz / 2), new System.Windows.Point(x1 + asz, cy + asz / 2) }; SymbolPreviewCanvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts2, Fill = brush }); }
                }
            }
        }

        private void DrawMarkerAt(double cx, double cy, string shape, double sz, SolidColorBrush brush)
        {
            double hs = sz / 2;
            switch (shape)
            {
                case "Circle":
                {
                    var e = new Ellipse { Width = sz, Height = sz, Fill = brush };
                    Canvas.SetLeft(e, cx - hs); Canvas.SetTop(e, cy - hs);
                    SymbolPreviewCanvas.Children.Add(e);
                    break;
                }
                case "Triangle":
                {
                    var poly = new System.Windows.Shapes.Polygon
                    { Points = new System.Windows.Media.PointCollection { new System.Windows.Point(cx, cy - hs), new System.Windows.Point(cx + hs, cy + hs), new System.Windows.Point(cx - hs, cy + hs) }, Fill = brush };
                    SymbolPreviewCanvas.Children.Add(poly);
                    break;
                }
                case "Diamond":
                {
                    var poly = new System.Windows.Shapes.Polygon
                    { Points = new System.Windows.Media.PointCollection { new System.Windows.Point(cx, cy - hs), new System.Windows.Point(cx + hs, cy), new System.Windows.Point(cx, cy + hs), new System.Windows.Point(cx - hs, cy) }, Fill = brush };
                    SymbolPreviewCanvas.Children.Add(poly);
                    break;
                }
                case "Cross":
                {
                    var l1 = new System.Windows.Shapes.Line { X1 = cx - hs, Y1 = cy, X2 = cx + hs, Y2 = cy, Stroke = brush, StrokeThickness = Math.Max(1.5, sz / 4) };
                    var l2 = new System.Windows.Shapes.Line { X1 = cx, Y1 = cy - hs, X2 = cx, Y2 = cy + hs, Stroke = brush, StrokeThickness = Math.Max(1.5, sz / 4) };
                    SymbolPreviewCanvas.Children.Add(l1); SymbolPreviewCanvas.Children.Add(l2);
                    break;
                }
                case "X":
                {
                    double a = hs * 0.707;
                    var l1 = new System.Windows.Shapes.Line { X1 = cx - a, Y1 = cy - a, X2 = cx + a, Y2 = cy + a, Stroke = brush, StrokeThickness = Math.Max(1.5, sz / 4) };
                    var l2 = new System.Windows.Shapes.Line { X1 = cx + a, Y1 = cy - a, X2 = cx - a, Y2 = cy + a, Stroke = brush, StrokeThickness = Math.Max(1.5, sz / 4) };
                    SymbolPreviewCanvas.Children.Add(l1); SymbolPreviewCanvas.Children.Add(l2);
                    break;
                }
                case "Square":
                {
                    var r = new System.Windows.Shapes.Rectangle { Width = sz, Height = sz, Fill = brush };
                    Canvas.SetLeft(r, cx - hs); Canvas.SetTop(r, cy - hs);
                    SymbolPreviewCanvas.Children.Add(r);
                    break;
                }
                case "Arrow":
                {
                    var pts = new System.Windows.Media.PointCollection { new System.Windows.Point(cx + hs, cy), new System.Windows.Point(cx - hs * 0.5, cy - hs * 0.6), new System.Windows.Point(cx - hs * 0.5, cy + hs * 0.6) };
                    SymbolPreviewCanvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts, Fill = brush });
                    break;
                }
                case "Star":
                {
                    double inner = hs * 0.4;
                    var pts = new System.Windows.Media.PointCollection();
                    for (int i = 0; i < 10; i++) { double a = Math.PI / 2 + i * Math.PI / 5; double r = i % 2 == 0 ? hs : inner; pts.Add(new System.Windows.Point(cx + r * Math.Cos(a), cy - r * Math.Sin(a))); }
                    SymbolPreviewCanvas.Children.Add(new System.Windows.Shapes.Polygon { Points = pts, Fill = brush });
                    break;
                }
                default:
                {
                    var e = new Ellipse { Width = sz, Height = sz, Fill = brush };
                    Canvas.SetLeft(e, cx - hs); Canvas.SetTop(e, cy - hs);
                    SymbolPreviewCanvas.Children.Add(e);
                    break;
                }
            }
        }

        private void DrawWavePath(LineSymbolLayerDef def, double x1, double cy, double x2, SolidColorBrush brush, double thickness)
        {
            double amp = Math.Max(1, def.WaveAmplitude) * 0.5;
            double wlen = Math.Max(4, def.WaveLength);
            var pg = new PathGeometry();
            var pf = new PathFigure { StartPoint = new System.Windows.Point(x1, cy) };
            for (double x = x1; x <= x2; x += 2)
            {
                double t = (x - x1) / wlen * Math.PI * 2;
                double y = cy + Math.Sin(t) * amp;
                pf.Segments.Add(new System.Windows.Media.LineSegment(new System.Windows.Point(x, y), true));
            }
            pg.Figures.Add(pf);
            var path = new System.Windows.Shapes.Path { Data = pg, Stroke = brush, StrokeThickness = thickness, Fill = null };
            SymbolPreviewCanvas.Children.Add(path);
        }

        private void DrawSawtoothPath(LineSymbolLayerDef def, double x1, double cy, double x2, SolidColorBrush brush, double thickness)
        {
            double amp = Math.Max(1, def.WaveAmplitude) * 0.5;
            double wlen = Math.Max(4, def.WaveLength);
            var pg = new PathGeometry();
            var pf = new PathFigure { StartPoint = new System.Windows.Point(x1, cy) };
            for (double x = x1; x <= x2; x += wlen * 0.5)
            {
                int seg = (int)((x - x1) / (wlen * 0.5));
                double y = cy + (seg % 2 == 0 ? -amp : amp);
                pf.Segments.Add(new System.Windows.Media.LineSegment(new System.Windows.Point(x, y), true));
            }
            pg.Figures.Add(pf);
            var path = new System.Windows.Shapes.Path { Data = pg, Stroke = brush, StrokeThickness = thickness, Fill = null };
            SymbolPreviewCanvas.Children.Add(path);
        }

        private void ShowPatternPreview(System.Drawing.Bitmap bmp)
        {
            SymbolPreviewCanvas.Visibility = Visibility.Collapsed;
            if (ImgPatternPreview == null) return;
            int tileW = bmp.Width, tileH = bmp.Height;
            int targetW = 200, targetH = 50;
            DrawingColor oc = GetBorderColor(BrdOutlineColor);
            double ow = Math.Max(1, SldOutlineWidth.Value);
            using (var full = new System.Drawing.Bitmap(targetW, targetH))
            using (var g = System.Drawing.Graphics.FromImage(full))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                for (int y = 0; y < targetH; y += tileH)
                    for (int x = 0; x < targetW; x += tileW)
                        g.DrawImage(bmp, x, y, tileW, tileH);
                if (ow > 0.5)
                {
                    using (var pen = new System.Drawing.Pen(oc, (float)ow))
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

        // ============== OK / Cancel ==============

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentPointLayerToDef();
            SaveLineLayerToDef(SelectedLineLayer);

            // 使用标签页可见性决定保存哪个类别（比 _geomType 枚举更可靠）
            if (TabPointSym.Visibility == Visibility.Visible)
            {
                ClassDef.Color = _pointLayers.FirstOrDefault()?.Color ?? DrawingColor.Gray;
                ClassDef.PointLayers.Clear();
                foreach (var l in _pointLayers)
                    ClassDef.PointLayers.Add(l.Clone());
            }
            else if (TabLineSym.Visibility == Visibility.Visible)
            {
                ClassDef.LineLayers.Clear();
                foreach (var l in _lineLayers)
                    ClassDef.LineLayers.Add(l.Clone());
                ClassDef.Color = ClassDef.LineLayers.FirstOrDefault()?.Color ?? _lineColor;
                ClassDef.FillColor = ClassDef.Color;
            }
            else if (TabFillSym.Visibility == Visibility.Visible)
            {
                SaveFillLayerToDef(SelectedFillLayer);
                ClassDef.FillMode = _fillLayers.Any(l => l.Kind != FillSymbolLayerKind.Solid) ? "Pattern" : "Solid";
                DrawingColor fc = _fillLayers.FirstOrDefault()?.Color ?? DrawingColor.FromArgb(128, 88, 166, 255);
                ClassDef.FillColor = fc;
                ClassDef.OutlineColor = GetBorderColor(BrdOutlineColor);
                ClassDef.OutlineWidth = SldOutlineWidth.Value;
                ClassDef.FillLayers.Clear();
                foreach (var l in _fillLayers) ClassDef.FillLayers.Add(l.Clone());
                ClassDef.Color = fc;
            }
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        // ============== Helpers ==============

        private static DrawingColor GetBorderColor(Border brd)
        {
            if (brd?.Background is SolidColorBrush scb)
                return DrawingColor.FromArgb(scb.Color.A, scb.Color.R, scb.Color.G, scb.Color.B);
            return DrawingColor.FromArgb(255, 88, 166, 255);
        }
        private static void SetBorderColor(Border brd, DrawingColor c) { if (brd != null) brd.Background = new SolidColorBrush(ToWpfColor(c)); }
        private static MediaColor ToWpfColor(DrawingColor c) => MediaColor.FromArgb(c.A, c.R, c.G, c.B);
        private DrawingColor PickColor(DrawingColor current)
        {
            try { var dlg = new System.Windows.Forms.ColorDialog { Color = current, FullOpen = true }; if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) return dlg.Color; } catch { }
            return current;
        }
        private static void SelectComboByTag(ComboBox cbo, string tag)
        {
            for (int i = 0; i < cbo.Items.Count; i++)
                if (cbo.Items[i] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                { cbo.SelectedIndex = i; return; }
        }
    }
}
