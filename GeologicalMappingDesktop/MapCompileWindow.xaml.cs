using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.Rasters;
using Esri.ArcGISRuntime.UI.Controls;
using WpfGrid = System.Windows.Controls.Grid;


namespace GeologicalMappingDesktop
{
    public partial class MapCompileWindow : Window
    {
        private MainWindow _mainWindow;
        private readonly LayoutDocument _doc = new LayoutDocument();
        private readonly Dictionary<string, LayoutItemData> _itemMap = new Dictionary<string, LayoutItemData>();
        private string _currentPath;
        private Border _selectedBorder;
        private bool _isDragging, _isResizing;
        private Point _dragStart, _resizeStartPt;
        private double _rsX, _rsY, _rsW, _rsH;
        private int _resizeIdx = -1;
        private bool _isUpdatingProps;
        private int _renderSeq;
        private System.Drawing.Color _gridLineColor = System.Drawing.Color.FromArgb(128, 100, 100, 100);
        private System.Drawing.Color _gridFontColor = System.Drawing.Color.FromArgb(180, 60, 60, 60);
        private static readonly string[] _gridFontNames = { "Times New Roman", "Arial", "Microsoft YaHei", "SimSun", "SimHei", "KaiTi", "FangSong" };

        // ========== Grid Settings Helpers ==========
        private static string SerializeGridSettings(GridSettings gs)
        {
            var dict = new Dictionary<string, object>
            {
                ["Enabled"] = gs.Enabled,
                ["DisplayMode"] = (int)gs.DisplayMode,
                ["IntervalMode"] = (int)gs.IntervalMode,
                ["FixedInterval"] = gs.FixedInterval,
                ["LineColorArgb"] = gs.LineColor.ToArgb(),
                ["LineWidth"] = gs.LineWidth,
                ["FontSize"] = gs.FontSize,
                ["FontColorArgb"] = gs.FontColor.ToArgb(),
                ["FontBold"] = gs.FontBold,
                ["FontItalic"] = gs.FontItalic,
                ["TopShow"] = gs.Top.ShowLabels,
                ["TopVert"] = gs.Top.VerticalOrientation,
                ["BottomShow"] = gs.Bottom.ShowLabels,
                ["BottomVert"] = gs.Bottom.VerticalOrientation,
                ["LeftShow"] = gs.Left.ShowLabels,
                ["LeftVert"] = gs.Left.VerticalOrientation,
                ["RightShow"] = gs.Right.ShowLabels,
                ["RightVert"] = gs.Right.VerticalOrientation,
            };
            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(dict);
        }

        private static GridSettings DeserializeGridSettings(string json)
        {
            var gs = new GridSettings();
            if (string.IsNullOrEmpty(json)) return gs;
            try
            {
                var dict = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (dict == null) return gs;
                if (dict.ContainsKey("Enabled")) gs.Enabled = (bool)dict["Enabled"];
                if (dict.ContainsKey("DisplayMode")) gs.DisplayMode = (GridDisplayMode)Convert.ToInt32(dict["DisplayMode"]);
                if (dict.ContainsKey("IntervalMode")) gs.IntervalMode = (IntervalMode)Convert.ToInt32(dict["IntervalMode"]);
                if (dict.ContainsKey("FixedInterval")) gs.FixedInterval = Convert.ToDouble(dict["FixedInterval"]);
                if (dict.ContainsKey("LineColorArgb")) gs.LineColor = System.Drawing.Color.FromArgb(Convert.ToInt32(dict["LineColorArgb"]));
                if (dict.ContainsKey("LineWidth")) gs.LineWidth = Convert.ToDouble(dict["LineWidth"]);
                if (dict.ContainsKey("FontSize")) gs.FontSize = Convert.ToDouble(dict["FontSize"]);
                if (dict.ContainsKey("FontColorArgb")) gs.FontColor = System.Drawing.Color.FromArgb(Convert.ToInt32(dict["FontColorArgb"]));
                if (dict.ContainsKey("FontBold")) gs.FontBold = (bool)dict["FontBold"];
                if (dict.ContainsKey("FontItalic")) gs.FontItalic = (bool)dict["FontItalic"];
                if (dict.ContainsKey("TopShow")) gs.Top.ShowLabels = (bool)dict["TopShow"];
                if (dict.ContainsKey("TopVert")) gs.Top.VerticalOrientation = (bool)dict["TopVert"];
                if (dict.ContainsKey("BottomShow")) gs.Bottom.ShowLabels = (bool)dict["BottomShow"];
                if (dict.ContainsKey("BottomVert")) gs.Bottom.VerticalOrientation = (bool)dict["BottomVert"];
                if (dict.ContainsKey("LeftShow")) gs.Left.ShowLabels = (bool)dict["LeftShow"];
                if (dict.ContainsKey("LeftVert")) gs.Left.VerticalOrientation = (bool)dict["LeftVert"];
                if (dict.ContainsKey("RightShow")) gs.Right.ShowLabels = (bool)dict["RightShow"];
                if (dict.ContainsKey("RightVert")) gs.Right.VerticalOrientation = (bool)dict["RightVert"];
            }
            catch { }
            return gs;
        }

        private static GridSettings GetFrameGridSettings(LayoutItemData data)
        {
            if (data.Props.TryGetValue("GridSettings", out var json))
                return DeserializeGridSettings(json);
            return new GridSettings { Enabled = false };
        }

        private static void SetFrameGridSettings(LayoutItemData data, GridSettings gs)
        {
            data.Props["GridSettings"] = SerializeGridSettings(gs);
        }

        private void LoadGridSettingsToUI(GridSettings gs)
        {
            if (gs == null) return;
            _isUpdatingProps = true;

            ChkGridEnabled.IsChecked = gs.Enabled;
            GridSettingsPanel.IsEnabled = gs.Enabled;
            CboGridSpacing.SelectedIndex = gs.IntervalMode == IntervalMode.Auto ? 0 : 1;
            GridFixedPanel.IsEnabled = gs.IntervalMode == IntervalMode.Fixed;
            TxtGridInterval.Text = gs.FixedInterval.ToString("F4");
            CboGridDisplayMode.SelectedIndex = (int)gs.DisplayMode;

            _gridLineColor = gs.LineColor;
            ClrGridLinePreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                gs.LineColor.A, gs.LineColor.R, gs.LineColor.G, gs.LineColor.B));
            TxtGridLineWidth.Text = gs.LineWidth.ToString("F2");

            _gridFontColor = gs.FontColor;
            ClrGridFontPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                gs.FontColor.A, gs.FontColor.R, gs.FontColor.G, gs.FontColor.B));
            TxtGridFontSize.Text = gs.FontSize.ToString("F0");
            ChkGridFontBold.IsChecked = gs.FontBold;
            ChkGridFontItalic.IsChecked = gs.FontItalic;
            // Match font name via index array
            int fi = Array.IndexOf(_gridFontNames, gs.FontName ?? "Microsoft YaHei");
            CboGridFontName.SelectedIndex = fi >= 0 ? fi : 0;

            ChkGridTop.IsChecked = gs.Top.ShowLabels;
            CboGridTopDir.SelectedIndex = gs.Top.VerticalOrientation ? 1 : 0;
            ChkGridBottom.IsChecked = gs.Bottom.ShowLabels;
            CboGridBottomDir.SelectedIndex = gs.Bottom.VerticalOrientation ? 1 : 0;
            ChkGridLeft.IsChecked = gs.Left.ShowLabels;
            CboGridLeftDir.SelectedIndex = gs.Left.VerticalOrientation ? 1 : 0;
            ChkGridRight.IsChecked = gs.Right.ShowLabels;
            CboGridRightDir.SelectedIndex = gs.Right.VerticalOrientation ? 1 : 0;

            _isUpdatingProps = false;
        }

        private GridSettings ReadGridSettingsFromUI()
        {
            var gs = new GridSettings();
            gs.Enabled = ChkGridEnabled.IsChecked == true;
            gs.IntervalMode = CboGridSpacing.SelectedIndex == 0 ? IntervalMode.Auto : IntervalMode.Fixed;
            double.TryParse(TxtGridInterval.Text, out double interval);
            gs.FixedInterval = interval > 0 ? interval : 1.0;
            gs.DisplayMode = (GridDisplayMode)CboGridDisplayMode.SelectedIndex;
            gs.LineColor = _gridLineColor;
            double.TryParse(TxtGridLineWidth.Text, out double lw);
            gs.LineWidth = lw > 0 ? lw : 0.5;
            gs.FontColor = _gridFontColor;
            double.TryParse(TxtGridFontSize.Text, out double fz);
            gs.FontSize = fz > 0 ? fz : 10;
            gs.FontBold = ChkGridFontBold.IsChecked == true;
            gs.FontItalic = ChkGridFontItalic.IsChecked == true;
            int fi2 = CboGridFontName.SelectedIndex;
            gs.FontName = (fi2 >= 0 && fi2 < _gridFontNames.Length) ? _gridFontNames[fi2] : "Microsoft YaHei";
            gs.Top.ShowLabels = ChkGridTop.IsChecked == true;
            gs.Top.VerticalOrientation = CboGridTopDir.SelectedIndex == 1;
            gs.Bottom.ShowLabels = ChkGridBottom.IsChecked == true;
            gs.Bottom.VerticalOrientation = CboGridBottomDir.SelectedIndex == 1;
            gs.Left.ShowLabels = ChkGridLeft.IsChecked == true;
            gs.Left.VerticalOrientation = CboGridLeftDir.SelectedIndex == 1;
            gs.Right.ShowLabels = ChkGridRight.IsChecked == true;
            gs.Right.VerticalOrientation = CboGridRightDir.SelectedIndex == 1;
            return gs;
        }

        public MapCompileWindow(MainWindow owner)
        {
            try
            {
                _mainWindow = owner;
                InitializeComponent();
                Owner = owner;
                Loaded += MapCompileWindow_Loaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"布局窗口构造失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MapCompileWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                CboPageSize.SelectionChanged += CboPageSize_SelectionChanged;
                CboOrientation.SelectionChanged += CboOrientation_SelectionChanged;
                SliderZoom.ValueChanged += SliderZoom_ValueChanged;
                InitPageSizes();
                ApplyPageSize(true);
                UpdateStatus("就绪");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"布局窗口初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        // ========== Init ==========
        private void InitPageSizes()
        {
            CboPageSize.SelectedIndex = 3;
            CboOrientation.SelectedIndex = 1;
            TxtDpi.Text = "300";
        }

        private void ApplyPageSize(bool resetZoom)
        {
            string sz = (CboPageSize.SelectedItem as ComboBoxItem)?.Content as string ?? "A3";
            string ori = (CboOrientation.SelectedItem as ComboBoxItem)?.Content as string ?? "横向";

            if (sz == "自定义")
            {
                double.TryParse(TxtPageW.Text, out double w);
                double.TryParse(TxtPageH.Text, out double h);
                if (w > 0 && h > 0) { _doc.PageWidthMm = w; _doc.PageHeightMm = h; }
            }
            else
            {
                LayoutMath.GetPageSize(sz, ori, out double w, out double h);
                _doc.PageWidthMm = w;
                _doc.PageHeightMm = h;
            }

            TxtPageW.Text = _doc.PageWidthMm.ToString("F1");
            TxtPageH.Text = _doc.PageHeightMm.ToString("F1");

            double pw = LayoutMath.MmToPx(_doc.PageWidthMm);
            double ph = LayoutMath.MmToPx(_doc.PageHeightMm);
            LayoutCanvas.Width = pw;
            LayoutCanvas.Height = ph;
            PaperBorder.Width = pw + 4;
            PaperBorder.Height = ph + 4;

            if (resetZoom)
            {
                SliderZoom.Value = 1.0;
                PaperBorder.LayoutTransform = new ScaleTransform(1, 1);
                TxtZoom.Text = "100%";
            }
        }

        // ========== Status ==========
        private void UpdateStatus(string msg)
        {
            string toolName = BtnSelectTool.IsChecked == true ? "选择" : "工具";
            TxtStatus.Text = msg;
            TxtStatusBar.Text = $"{msg} | 选中工具: {toolName}";
        }

        // ========== Menu: File ==========
        private void MenuItem_NewLayout_Click(object sender, RoutedEventArgs e)
        {
            if (_itemMap.Count > 0 && MessageBox.Show("当前布局未保存，是否新建?", "确认", MessageBoxButton.YesNo) == MessageBoxResult.No)
                return;
            NewLayout();
        }

        private void NewLayout()
        {
            LayoutCanvas.Children.Clear();
            _itemMap.Clear();
            _doc.Items.Clear();
            _selectedBorder = null;
            _currentPath = null;
            ItemPropPanel.Visibility = Visibility.Collapsed;
            PagePropPanel.Visibility = Visibility.Visible;
            TxtHint.Visibility = Visibility.Visible;
            ApplyPageSize(true);
            UpdateStatus("新建布局");
        }

        private async void MenuItem_OpenLayout_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "布局文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                DefaultExt = ".json"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                string json = File.ReadAllText(dlg.FileName, Encoding.UTF8);
                var doc = LayoutDocument.FromJson(json);
                if (doc == null) { MessageBox.Show("文件格式错误"); return; }
                LoadFromDocument(doc);
                _currentPath = dlg.FileName;
                UpdateStatus($"已打开: {Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex) { MessageBox.Show($"打开失败: {ex.Message}", "错误"); }
        }

        private void MenuItem_SaveLayout_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentPath)) { SaveToPath(_currentPath); return; }
            MenuItem_SaveAsLayout_Click(sender, e);
        }

        private void MenuItem_SaveAsLayout_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "布局文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                DefaultExt = ".json",
                FileName = _doc.Title + ".json"
            };
            if (dlg.ShowDialog() != true) return;
            SaveToPath(dlg.FileName);
        }

        private void SaveToPath(string path)
        {
            try
            {
                SyncDocFromCanvas();
                string json = _doc.ToJson();
                File.WriteAllText(path, json, Encoding.UTF8);
                _currentPath = path;
                _doc.Title = Path.GetFileNameWithoutExtension(path);
                UpdateStatus($"已保存: {Path.GetFileName(path)}");
            }
            catch (Exception ex) { MessageBox.Show($"保存失败: {ex.Message}", "错误"); }
        }

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e) => Close();

        // ========== Menu: Export ==========
        private void MenuItem_ExportImage_Click(object sender, RoutedEventArgs e)
        {
            double dpi = 300;
            double.TryParse(TxtDpi.Text, out dpi);
            if (dpi < 72) dpi = 300;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg)|*.jpg|BMP 图片 (*.bmp)|*.bmp",
                DefaultExt = ".png",
                FileName = _doc.Title + ".png"
            };
            if (dlg.ShowDialog() != true) return;

            // Temporary grid overlays added to LayoutCanvas before capture
            var tempOverlays = new List<Canvas>();

            try
            {
                SyncDocFromCanvas();
                UpdateStatus("正在导出图片...");

                double scale = dpi / LayoutMath.ScreenDpi;
                double canvasW = LayoutCanvas.ActualWidth;
                double canvasH = LayoutCanvas.ActualHeight;
                if (canvasW <= 0 || canvasH <= 0) { MessageBox.Show("布局画布为空", "错误"); return; }

                // Add grid overlays for map frames that have grid enabled
                foreach (var itemData in _doc.Items.Where(d => d.Type == LayoutItemType.MapFrame))
                {
                    var gs = GetFrameGridSettings(itemData);
                    if (!gs.Enabled) continue;
                    var overlay = CreateMapFrameGridOverlay(itemData, gs);
                    if (overlay != null)
                    {
                        LayoutCanvas.Children.Add(overlay);
                        tempOverlays.Add(overlay);
                    }
                }

                int screenW = (int)Math.Round(canvasW);
                int screenH = (int)Math.Round(canvasH);
                int renderW = (int)Math.Round(screenW * scale);
                int renderH = (int)Math.Round(screenH * scale);

                var screenShot = new RenderTargetBitmap(screenW, screenH, LayoutMath.ScreenDpi, LayoutMath.ScreenDpi, PixelFormats.Pbgra32);
                screenShot.Render(LayoutCanvas);

                BitmapSource finalBitmap = screenShot;
                if (scale > 1.001 || scale < 0.999)
                {
                    finalBitmap = new TransformedBitmap(screenShot, new ScaleTransform(scale, scale));
                    finalBitmap.Freeze();
                }

                BitmapEncoder encoder;
                string ext = Path.GetExtension(dlg.FileName).ToLower();
                if (ext == ".jpg" || ext == ".jpeg") encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                else if (ext == ".bmp") encoder = new BmpBitmapEncoder();
                else encoder = new PngBitmapEncoder();

                encoder.Frames.Add(BitmapFrame.Create(finalBitmap));
                using (var fs = new FileStream(dlg.FileName, FileMode.Create))
                    encoder.Save(fs);

                UpdateStatus($"已导出: {Path.GetFileName(dlg.FileName)} ({renderW}x{renderH} @ {dpi}DPI)");
                MessageBox.Show($"导出成功!\n尺寸: {renderW} × {renderH} 像素\nDPI: {dpi}\n文件: {dlg.FileName}", "导出成功");
            }
            catch (Exception ex) { MessageBox.Show($"导出失败: {ex.Message}", "错误"); }
            finally
            {
                foreach (var ov in tempOverlays) LayoutCanvas.Children.Remove(ov);
            }
        }

        private Canvas CreateMapFrameGridOverlay(LayoutItemData data, GridSettings gs)
        {
            double wPx = data.Width * LayoutMath.ScreenDpi / LayoutMath.MmPerInch;
            double hPx = data.Height * LayoutMath.ScreenDpi / LayoutMath.MmPerInch;
            double xPx = data.X * LayoutMath.ScreenDpi / LayoutMath.MmPerInch;
            double yPx = data.Y * LayoutMath.ScreenDpi / LayoutMath.MmPerInch;
            if (wPx <= 0 || hPx <= 0) return null;

            int mapSrWkid = data.MapWKID != 0 ? data.MapWKID : 4326;
            SpatialReference mapSr = null;
            try { mapSr = SpatialReference.Create(mapSrWkid); } catch { mapSr = SpatialReferences.WebMercator; }
            double extentXMin = data.MapExtentXMin, extentXMax = data.MapExtentXMax;
            double extentYMin = data.MapExtentYMin, extentYMax = data.MapExtentYMax;
            if (extentXMax <= extentXMin || extentYMax <= extentYMin) return null;

            // Map WGS84 lon/lat → pixel coordinates by projecting each point through the map's SR
            double SafeMapX(double lon, double lat)
            {
                try
                {
                    var pt = (MapPoint)GeometryEngine.Project(new MapPoint(lon, lat, SpatialReferences.Wgs84), mapSr);
                    return Math.Max(0, Math.Min(wPx, (pt.X - extentXMin) / (extentXMax - extentXMin) * wPx));
                }
                catch { return double.NaN; }
            }
            double SafeMapY(double lon, double lat)
            {
                try
                {
                    var pt = (MapPoint)GeometryEngine.Project(new MapPoint(lon, lat, SpatialReferences.Wgs84), mapSr);
                    return Math.Max(0, Math.Min(hPx, (extentYMax - pt.Y) / (extentYMax - extentYMin) * hPx));
                }
                catch { return double.NaN; }
            }

            double mapScale = data.MapScale > 0 ? data.MapScale : 50000;
            double interval = gs.IntervalMode == IntervalMode.Auto ? gs.AutoInterval(mapScale) : gs.FixedInterval;
            if (interval <= 0 || double.IsNaN(interval) || double.IsInfinity(interval)) interval = 1.0;
            if (interval < 0.01) interval = 0.01;

            // Get WGS84 extent
            Envelope wgsEnv = null;
            try
            {
                var env = new Envelope(extentXMin, extentYMin, extentXMax, extentYMax, mapSr);
                wgsEnv = GeometryEngine.Project(env, SpatialReferences.Wgs84) as Envelope;
            }
            catch { }
            if (wgsEnv == null) return null;
            double minLon = wgsEnv.XMin, maxLon = wgsEnv.XMax, minLat = wgsEnv.YMin, maxLat = wgsEnv.YMax;

            double gMinX = Math.Floor(minLon / interval) * interval;
            double gMaxX = Math.Ceiling(maxLon / interval) * interval;
            double gMinY = Math.Floor(minLat / interval) * interval;
            double gMaxY = Math.Ceiling(maxLat / interval) * interval;

            var ov = new Canvas { Width = wPx, Height = hPx, IsHitTestVisible = false };
            Canvas.SetLeft(ov, xPx); Canvas.SetTop(ov, yPx);
            Panel.SetZIndex(ov, 10000);

            var lineColor = System.Windows.Media.Color.FromArgb(gs.LineColor.A, gs.LineColor.R, gs.LineColor.G, gs.LineColor.B);
            var lineBrush = new SolidColorBrush(lineColor);
            double tickLen = 12; // tick length in screen pixels

            bool hasTicks = gs.DisplayMode != GridDisplayMode.LabelOnly;
            bool hasLabel = gs.DisplayMode != GridDisplayMode.GridOnly;

            if (hasTicks)
            {
                // Top edge: ticks extending downward from Y=0
                for (double lon = gMinX; lon <= gMaxX + interval * 0.5; lon += interval)
                {
                    double px = SafeMapX(lon, maxLat);
                    if (double.IsNaN(px) || px < 0 || px > wPx) continue;
                    ov.Children.Add(new System.Windows.Shapes.Line { X1 = px, Y1 = 0, X2 = px, Y2 = tickLen, Stroke = lineBrush, StrokeThickness = gs.LineWidth });
                }
                // Bottom edge: ticks extending upward from Y=hPx
                for (double lon = gMinX; lon <= gMaxX + interval * 0.5; lon += interval)
                {
                    double px = SafeMapX(lon, minLat);
                    if (double.IsNaN(px) || px < 0 || px > wPx) continue;
                    ov.Children.Add(new System.Windows.Shapes.Line { X1 = px, Y1 = hPx - tickLen, X2 = px, Y2 = hPx, Stroke = lineBrush, StrokeThickness = gs.LineWidth });
                }
                // Left edge: ticks extending rightward from X=0
                for (double lat = gMinY; lat <= gMaxY + interval * 0.5; lat += interval)
                {
                    double py = SafeMapY(minLon, lat);
                    if (double.IsNaN(py) || py < 0 || py > hPx) continue;
                    ov.Children.Add(new System.Windows.Shapes.Line { X1 = 0, Y1 = py, X2 = tickLen, Y2 = py, Stroke = lineBrush, StrokeThickness = gs.LineWidth });
                }
                // Right edge: ticks extending leftward from X=wPx
                for (double lat = gMinY; lat <= gMaxY + interval * 0.5; lat += interval)
                {
                    double py = SafeMapY(maxLon, lat);
                    if (double.IsNaN(py) || py < 0 || py > hPx) continue;
                    ov.Children.Add(new System.Windows.Shapes.Line { X1 = wPx - tickLen, Y1 = py, X2 = wPx, Y2 = py, Stroke = lineBrush, StrokeThickness = gs.LineWidth });
                }
            }

            if (hasLabel)
            {
                var fontColor = System.Windows.Media.Color.FromArgb(gs.FontColor.A, gs.FontColor.R, gs.FontColor.G, gs.FontColor.B);
                var fontBrush = new SolidColorBrush(fontColor);
                string fmt(double v) { double abs = Math.Abs(v); int d = (int)abs; double m = (abs - d) * 60; return m < 0.01 ? $"{d}°" : $"{d}°{m:F0}'"; }
                var fontFamily = new FontFamily(gs.FontName ?? "Microsoft YaHei");
                System.Windows.FontWeight fw = gs.FontBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
                System.Windows.FontStyle fst = gs.FontItalic ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;

                TextBlock MakeLabel(string text, bool vertical)
                {
                    var tb = new TextBlock { Text = text, FontSize = gs.FontSize, Foreground = fontBrush, FontFamily = fontFamily, FontWeight = fw, FontStyle = fst };
                    if (vertical) tb.LayoutTransform = new RotateTransform(90);
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    return tb;
                }

                if (gs.Top.ShowLabels)
                    for (double lon = gMinX; lon <= gMaxX + interval * 0.5; lon += interval)
                    {
                        double px = SafeMapX(lon, maxLat);
                        if (double.IsNaN(px)) continue;
                        var tb = MakeLabel(fmt(lon), gs.Top.VerticalOrientation);
                        Canvas.SetLeft(tb, px - tb.DesiredSize.Width / 2); Canvas.SetTop(tb, -tb.DesiredSize.Height);
                        ov.Children.Add(tb);
                    }
                if (gs.Bottom.ShowLabels)
                    for (double lon = gMinX; lon <= gMaxX + interval * 0.5; lon += interval)
                    {
                        double px = SafeMapX(lon, minLat);
                        if (double.IsNaN(px)) continue;
                        var tb = MakeLabel(fmt(lon), gs.Bottom.VerticalOrientation);
                        Canvas.SetLeft(tb, px - tb.DesiredSize.Width / 2); Canvas.SetTop(tb, hPx + 2);
                        ov.Children.Add(tb);
                    }
                if (gs.Left.ShowLabels)
                    for (double lat = gMinY; lat <= gMaxY + interval * 0.5; lat += interval)
                    {
                        double py = SafeMapY(minLon, lat);
                        if (double.IsNaN(py)) continue;
                        var tb = MakeLabel(fmt(lat), gs.Left.VerticalOrientation);
                        Canvas.SetLeft(tb, -tb.DesiredSize.Width - 2); Canvas.SetTop(tb, py - tb.DesiredSize.Height / 2);
                        ov.Children.Add(tb);
                    }
                if (gs.Right.ShowLabels)
                    for (double lat = gMinY; lat <= gMaxY + interval * 0.5; lat += interval)
                    {
                        double py = SafeMapY(maxLon, lat);
                        if (double.IsNaN(py)) continue;
                        var tb = MakeLabel(fmt(lat), gs.Right.VerticalOrientation);
                        Canvas.SetLeft(tb, wPx + 2); Canvas.SetTop(tb, py - tb.DesiredSize.Height / 2);
                        ov.Children.Add(tb);
                    }
            }

            return ov;
        }

        private void DrawLayoutItemToDC(DrawingContext dc, LayoutItemData data, double x, double y, double w, double h, double dpi)
        {
            switch (data.Type)
            {
                case LayoutItemType.TextItem:
                    string txt = data.Props.TryGetValue("Text", out var t) ? t : "文字";
                    double fs = 14;
                    if (data.Props.TryGetValue("FontSize", out var fss)) double.TryParse(fss, out fs);
                    fs = fs * dpi / LayoutMath.ScreenDpi;
                    string fn = "Times New Roman";
                    if (data.Props.TryGetValue("FontName", out var fnn)) fn = fnn;
                    bool bold = data.Props.TryGetValue("FontBold", out var fb) && fb == "true";
                    bool italic = data.Props.TryGetValue("FontItalic", out var fi) && fi == "true";
                    var fgBrush = Brushes.Black;
                    if (data.Props.TryGetValue("FontColor_R", out var cr) && byte.TryParse(cr, out byte r2) &&
                        data.Props.TryGetValue("FontColor_G", out var cg) && byte.TryParse(cg, out byte g2) &&
                        data.Props.TryGetValue("FontColor_B", out var cb) && byte.TryParse(cb, out byte b2))
                        fgBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r2, g2, b2));
                    var tf2 = new Typeface(fn);
                    var ft2 = new FormattedText(txt, System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, tf2, fs, fgBrush);
                    if (bold) ft2.SetFontWeight(FontWeights.Bold);
                    if (italic) ft2.SetFontStyle(FontStyles.Italic);
                    dc.DrawText(ft2, new System.Windows.Point(x, y));
                    break;

                case LayoutItemType.Rectangle:
                    string fill = data.Props.TryGetValue("Fill", out var f) ? f : "#E0E0E0";
                    string stroke = data.Props.TryGetValue("Stroke", out var s) ? s : "#333333";
                    double sw = 1;
                    if (data.Props.TryGetValue("StrokeThickness", out var st)) double.TryParse(st, out sw);
                    sw = sw * dpi / LayoutMath.ScreenDpi;
                    dc.DrawRectangle(ParseBrush(fill), new Pen(ParseBrush(stroke), sw), new Rect(x, y, w, h));
                    break;

                case LayoutItemType.Line:
                    stroke = data.Props.TryGetValue("Stroke", out s) ? s : "#333333";
                    sw = data.Props.TryGetValue("StrokeThickness", out st) ? (double.TryParse(st, out sw) ? sw : 2) : 2;
                    sw = sw * dpi / LayoutMath.ScreenDpi;
                    if (data.Props.TryGetValue("NormalizedPath", out var pathStr) && !string.IsNullOrEmpty(pathStr))
                    {
                        var parts = pathStr.Split(';');
                        var pts = new List<System.Windows.Point>();
                        foreach (var p in parts)
                        {
                            var xy = p.Split(',');
                            if (xy.Length == 2 && double.TryParse(xy[0], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double rx) &&
                                double.TryParse(xy[1], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double ry))
                            {
                                pts.Add(new System.Windows.Point(x + rx * w, y + (1 - ry) * h));
                            }
                        }
                        if (pts.Count >= 2)
                        {
                            var geometry = new StreamGeometry();
                            using (var ctx = geometry.Open())
                            {
                                ctx.BeginFigure(pts[0], false, false);
                                ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
                            }
                            dc.DrawGeometry(null, new Pen(ParseBrush(stroke), sw), geometry);
                        }
                    }
                    else
                    {
                        dc.DrawLine(new Pen(ParseBrush(stroke), sw), new System.Windows.Point(x, y + h / 2), new System.Windows.Point(x + w, y + h / 2));
                    }
                    break;

                case LayoutItemType.Legend:
                    try
                    {
                        var legendElem = CreateLegendContent();
                        double w_wpf = w * LayoutMath.ScreenDpi / dpi;
                        double h_wpf = h * LayoutMath.ScreenDpi / dpi;
                        legendElem.Measure(new Size(w_wpf, h_wpf));
                        legendElem.Arrange(new Rect(0, 0, w_wpf, h_wpf));
                        legendElem.UpdateLayout();
                        int pw = Math.Max(1, (int)Math.Round(w));
                        int ph = Math.Max(1, (int)Math.Round(h));
                        var rtb = new RenderTargetBitmap(pw, ph, dpi, dpi, PixelFormats.Pbgra32);
                        rtb.Render(legendElem);
                        dc.DrawImage(rtb, new Rect(x, y, w, h));
                    }
                    catch (Exception legendEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Legend render error: {legendEx.Message}");
                        dc.DrawRectangle(Brushes.White, new Pen(ParseBrush("#CCCCCC"), 1), new Rect(x, y, w, h));
                    }
                    break;

                case LayoutItemType.ScaleBar:
                    DrawScaleBarToDC(dc, data, x, y, w, h, dpi);
                    break;

                case LayoutItemType.NorthArrow:
                    DrawNorthArrowToDC(dc, x, y, w, h, dpi);
                    break;

                case LayoutItemType.ImageItem:
                    if (data.Props.TryGetValue("SourcePath", out var imgPath) && File.Exists(imgPath))
                    {
                        try
                        {
                            var bitmap = new BitmapImage(new Uri(imgPath));
                            dc.DrawImage(bitmap, new Rect(x, y, w, h));
                        }
                        catch { }
                    }
                    break;
            }
        }

        private void DrawScaleBarToDC(DrawingContext dc, LayoutItemData data, double x, double y, double w, double h, double dpi)
        {
            double mapScale = data.MapScale > 0 ? data.MapScale : 50000;
            double barMm = data.Width;
            double realDistM = barMm * mapScale / 1000.0;
            double nice = NiceRound(realDistM);
            double actualMm = nice * 1000.0 / mapScale;
            double actualPx = actualMm * dpi / LayoutMath.MmPerInch;

            double mid = h / 2;
            double lineWidth = 1.5 * dpi / LayoutMath.ScreenDpi;

            dc.DrawLine(new Pen(Brushes.Black, lineWidth), new System.Windows.Point(x, y + mid), new System.Windows.Point(x + actualPx, y + mid));
            dc.DrawLine(new Pen(Brushes.Black, lineWidth), new System.Windows.Point(x, y + mid - 4 * dpi / LayoutMath.ScreenDpi), new System.Windows.Point(x, y + mid + 4 * dpi / LayoutMath.ScreenDpi));
            dc.DrawLine(new Pen(Brushes.Black, lineWidth), new System.Windows.Point(x + actualPx, y + mid - 4 * dpi / LayoutMath.ScreenDpi), new System.Windows.Point(x + actualPx, y + mid + 4 * dpi / LayoutMath.ScreenDpi));

            double labelSize = 9 * dpi / LayoutMath.ScreenDpi;
            var tf = new Typeface("Microsoft YaHei");
            var ft0 = new FormattedText("0", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, labelSize, Brushes.Black);
            dc.DrawText(ft0, new System.Windows.Point(x - ft0.Width / 2, y + mid + 4 * dpi / LayoutMath.ScreenDpi));

            string label = nice >= 1000 ? $"{nice / 1000:F1}km" : $"{nice:F0}m";
            var ft1 = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, labelSize, Brushes.Black);
            dc.DrawText(ft1, new System.Windows.Point(x + actualPx - ft1.Width / 2, y + mid + 4 * dpi / LayoutMath.ScreenDpi));

            int segCount = 4;
            double segPx = actualPx / segCount;
            for (int i = 0; i < segCount; i++)
            {
                dc.DrawRectangle(i % 2 == 0 ? Brushes.Black : Brushes.White, new Pen(Brushes.Black, lineWidth),
                    new Rect(x + i * segPx, y + mid - 3 * dpi / LayoutMath.ScreenDpi, segPx, 6 * dpi / LayoutMath.ScreenDpi));
            }
        }

        private void DrawNorthArrowToDC(DrawingContext dc, double x, double y, double w, double h, double dpi)
        {
            double s = dpi / LayoutMath.ScreenDpi;
            // The screen north arrow (CreateNorthArrowContent) uses a 30×36 Canvas.
            // Center a copy scaled by s within the item area.
            double nw = 30 * s;
            double nh = 36 * s;
            double ox = x + (w - nw) / 2;
            double oy = y + (h - nh) / 2;

            // Arrow shaft
            var pts = new List<System.Windows.Point>
            {
                new System.Windows.Point(ox + 15 * s, oy + 2 * s),
                new System.Windows.Point(ox + 22 * s, oy + 28 * s),
                new System.Windows.Point(ox + 15 * s, oy + 22 * s),
                new System.Windows.Point(ox + 8 * s, oy + 28 * s)
            };
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(pts[0], true, true);
                ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
            }
            dc.DrawGeometry(Brushes.Black, null, geo);

            // Circle
            dc.DrawEllipse(null, new Pen(Brushes.Black, 1 * s),
                new System.Windows.Point(ox + 15 * s, oy + 16 * s), 14 * s, 14 * s);

            // N label — FormattedText fontSize is in WPF units (1/96″), same coordinate system as the
            // DrawingVisual.  Scale screen FontSize=12 by s to keep physical size constant.
            var tf = new Typeface("Microsoft YaHei");
            var ft = new FormattedText("N", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, 12 * s, Brushes.Red);
            dc.DrawText(ft, new System.Windows.Point(ox + 9 * s, oy + 28 * s));
        }

        private void MenuItem_ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF 文件 (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                FileName = _doc.Title + ".pdf"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                SyncDocFromCanvas();
                double canvasW = LayoutCanvas.ActualWidth;
                double canvasH = LayoutCanvas.ActualHeight;
                if (canvasW <= 0 || canvasH <= 0) { MessageBox.Show("布局画布为空", "错误"); return; }

                int dpi = 150;
                int wPx = (int)Math.Round(canvasW * dpi / LayoutMath.ScreenDpi);
                int hPx = (int)Math.Round(canvasH * dpi / LayoutMath.ScreenDpi);

                var rtb = new RenderTargetBitmap((int)canvasW, (int)canvasH, LayoutMath.ScreenDpi, LayoutMath.ScreenDpi, PixelFormats.Pbgra32);
                rtb.Render(LayoutCanvas);
                var scaled = new TransformedBitmap(rtb, new ScaleTransform(dpi / LayoutMath.ScreenDpi, dpi / LayoutMath.ScreenDpi));

                // Encode as JPEG
                var enc = new JpegBitmapEncoder { QualityLevel = 90 };
                enc.Frames.Add(BitmapFrame.Create(scaled));
                byte[] jpgData;
                using (var ms = new MemoryStream()) { enc.Save(ms); jpgData = ms.ToArray(); }

                // Build a minimal PDF
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("%PDF-1.4");
                sb.AppendLine("1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj");
                sb.AppendLine("2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj");
                sb.AppendLine($"3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 {wPx} {hPx}]/Contents 4 0 R/Resources<</XObject<</Im0 5 0 R>>>>>>endobj");
                string streamData = $"q\n{wPx} 0 0 {hPx} 0 0 cm\n/Im0 Do\nQ\n";
                sb.AppendLine($"4 0 obj<</Length {streamData.Length}>>stream\n{streamData}\nendstream\nendobj");
                sb.AppendLine($"5 0 obj<</Type/XObject/Subtype/Image/Width {wPx}/Height {hPx}/ColorSpace/DeviceRGB/BitsPerComponent 8/Filter/DCTDecode/Length {jpgData.Length}>>stream");

                using (var fs = new FileStream(dlg.FileName, FileMode.Create))
                using (var bw = new BinaryWriter(fs))
                {
                    byte[] prefix = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
                    bw.Write(prefix);
                    bw.Write(jpgData);
                    string suffix = $"\nendstream\nendobj\nxref\n0 6\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \n0000000115 00000 n \n0000000266 00000 n \n0000000391 00000 n \ntrailer\n<</Size 6/Root 1 0 R>>\nstartxref\n{prefix.Length + jpgData.Length + 1}\n%%EOF";
                    bw.Write(System.Text.Encoding.ASCII.GetBytes(suffix));
                }

                UpdateStatus($"已导出: {Path.GetFileName(dlg.FileName)} ({wPx}x{hPx} @ {dpi}DPI)");
                MessageBox.Show($"PDF 导出成功!\n文件: {dlg.FileName}", "导出成功");
            }
            catch (Exception ex) { MessageBox.Show($"导出失败: {ex.Message}", "错误"); }
        }

        private void MenuItem_Print_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() == true)
            {
                SyncDocFromCanvas();
                double pw = LayoutMath.MmToPx(_doc.PageWidthMm);
                double ph = LayoutMath.MmToPx(_doc.PageHeightMm);
                double scale = Math.Min(dlg.PrintableAreaWidth / pw, dlg.PrintableAreaHeight / ph);
                PaperBorder.LayoutTransform = new ScaleTransform(scale, scale);
                dlg.PrintVisual(PaperBorder, _doc.Title);
                PaperBorder.LayoutTransform = new ScaleTransform(SliderZoom.Value, SliderZoom.Value);
            }
        }

        // ========== Layer Data Loading ==========
        private async void MenuItem_LoadLayerData_Click(object sender, RoutedEventArgs e)
        {
            var layerItems = _mainWindow?.LayerManager?.Layers;
            if (layerItems == null || layerItems.Count == 0)
            {
                MessageBox.Show("数据视图中没有图层", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Build dialog programmatically
            var dlg = new Window
            {
                Title = "加载数据图层",
                Width = 480, Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3)),
                FontFamily = new FontFamily("Microsoft YaHei"),
                FontSize = 13
            };
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock
            {
                Text = "选择要加载到编绘器中的可编辑图层：",
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            var listBox = new ListBox
            {
                Height = 260,
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D)),
                BorderThickness = new Thickness(1),
                SelectionMode = System.Windows.Controls.SelectionMode.Multiple
            };
            var selLayers = new List<LayerItem>();
            foreach (var li in layerItems)
            {
                if (li.Layer is FeatureLayer)
                {
                    var cb = new CheckBox
                    {
                        Content = $"{li.Name}  ({li.Layer.GetType().Name})",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3)),
                        Margin = new Thickness(4),
                        FontSize = 12,
                        Tag = li,
                        IsChecked = true
                    };
                    var item = new ListBoxItem
                    {
                        Content = cb,
                        Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22)),
                        BorderThickness = new Thickness(0)
                    };
                    listBox.Items.Add(item);
                    selLayers.Add(li);
                }
            }

            if (selLayers.Count == 0)
            {
                MessageBox.Show("数据视图中没有要素图层（FeatureLayer）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            sp.Children.Add(listBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var btnOk = new Button { Content = "确定", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
            btnOk.Click += (s, args) => { dlg.DialogResult = true; dlg.Close(); };
            StyleButton(btnOk, "#238636");
            var btnCancel = new Button { Content = "取消", Width = 90, Height = 30, Cursor = Cursors.Hand };
            btnCancel.Click += (s, args) => { dlg.DialogResult = false; dlg.Close(); };
            StyleButton(btnCancel, "#21262D");
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            sp.Children.Add(btnPanel);
            dlg.Content = sp;

            if (dlg.ShowDialog() != true) return;

            var selectedLayers = listBox.Items.OfType<ListBoxItem>()
                .Select(item => item.Content as CheckBox)
                .Where(cb => cb != null && cb.IsChecked == true)
                .Select(cb => cb.Tag as LayerItem)
                .Where(li => li != null)
                .ToList();

            if (selectedLayers.Count == 0) return;

            try
            {
                // Get current map extent for positioning
                var mv = _mainWindow?.CurrentMapView;
                Envelope mapExtent = null;
                try { mapExtent = mv?.GetCurrentViewpoint(ViewpointType.BoundingGeometry).TargetGeometry as Envelope; } catch { }
                if (mapExtent == null || mapExtent.IsEmpty) mapExtent = new Envelope(-180, -90, 180, 90, SpatialReferences.Wgs84);

                // Default map frame position (matches BtnAddMap behavior)
                double mapFrameX = 20, mapFrameY = 20;
                double mapFrameW = Math.Min(_doc.PageWidthMm - 40, _doc.PageHeightMm - 60);
                double mapFrameH = mapFrameW;

                double extentW = mapExtent.Width;
                double extentH = mapExtent.Height;
                if (extentW <= 0 || extentH <= 0) { extentW = 360; extentH = 180; }

                int totalFeatures = 0;
                int colorIdx = 0;
                var colors = new[] { "#E74C3C", "#3498DB", "#2ECC71", "#F39C12", "#9B59B6", "#1ABC9C", "#E67E22", "#34495E", "#58A6FF", "#DA3633" };

                foreach (var layerItem in selectedLayers)
                {
                    var fl = layerItem.Layer as FeatureLayer;
                    if (fl == null) continue;
                    try
                    {
                        var qp = new QueryParameters { WhereClause = "1=1" };
                        var features = (await fl.FeatureTable.QueryFeaturesAsync(qp)).ToList();
                        if (features.Count == 0) continue;

                        string layerColor = colors[colorIdx++ % colors.Length];
                        string layerName = fl.Name;

                        // Add a section header item
                        var headerData = new LayoutItemData
                        {
                            Type = LayoutItemType.TextItem,
                            Name = $"【{layerName}】",
                            X = mapFrameX, Y = mapFrameY + mapFrameH + 10 + totalFeatures * 2,
                            Width = mapFrameW, Height = 8,
                            Props = { ["Text"] = $"◆ {layerName}  ({features.Count} 要素)", ["FontSize"] = "10", ["FontWeight"] = "Bold" }
                        };
                        AddItem(headerData, null);

                        double layerHeaderY = mapFrameY + mapFrameH + 10 + totalFeatures * 2;
                        double nextItemY = layerHeaderY + 12; // 起始位置在表头下方

                        foreach (var feature in features)
                        {
                            var geom = feature.Geometry;
                            if (geom == null) continue;

                            if (geom is Polyline polyline)
                            {
                                // 读取所有折点，归一化到地图范围
                                var pts = new List<System.Windows.Point>();
                                foreach (var part in polyline.Parts)
                                {
                                    foreach (var pt in part.Points)
                                    {
                                        double rx = extentW > 0 ? (pt.X - mapExtent.XMin) / extentW : 0.5;
                                        double ry = extentH > 0 ? (pt.Y - mapExtent.YMin) / extentH : 0.5;
                                        pts.Add(new System.Windows.Point(rx, ry));
                                    }
                                }

                                if (pts.Count >= 2)
                                {
                                    string ptStr = string.Join(";", pts.Select(p => $"{p.X:F6},{p.Y:F6}"));
                                    var ld = new LayoutItemData
                                    {
                                        Type = LayoutItemType.Line,
                                        Name = $"{layerName}_线{totalFeatures + 1}",
                                        X = mapFrameX, Y = mapFrameY,
                                        Width = mapFrameW, Height = mapFrameH,
                                        Props =
                                        {
                                            ["Stroke"] = layerColor,
                                            ["StrokeThickness"] = "2",
                                            ["NormalizedPath"] = ptStr,
                                            ["SourceLayer"] = layerName,
                                            ["OID"] = feature.Attributes["OBJECTID"]?.ToString() ?? ""
                                        }
                                    };
                                    AddItem(ld, null);
                                    totalFeatures++;
                                }
                            }
                            else if (geom is Polygon polygon)
                            {
                                var bbox = polygon.Extent;
                                if (bbox != null && !bbox.IsEmpty)
                                {
                                    double rx = extentW > 0 ? (bbox.XMin - mapExtent.XMin) / extentW : 0;
                                    double ry = extentH > 0 ? (bbox.YMin - mapExtent.YMin) / extentH : 0;
                                    double rw = extentW > 0 ? bbox.Width / extentW : 0.1;
                                    double rh = extentH > 0 ? bbox.Height / extentH : 0.1;

                                    var ld = new LayoutItemData
                                    {
                                        Type = LayoutItemType.Rectangle,
                                        Name = $"{layerName}_面{totalFeatures + 1}",
                                        X = mapFrameX + rx * mapFrameW,
                                        Y = mapFrameY + (1.0 - (ry + rh)) * mapFrameH,
                                        Width = Math.Max(2, rw * mapFrameW),
                                        Height = Math.Max(2, rh * mapFrameH),
                                        Props =
                                        {
                                            ["Fill"] = "#40" + layerColor.Substring(1),
                                            ["Stroke"] = layerColor,
                                            ["StrokeThickness"] = "1.5",
                                            ["SourceLayer"] = layerName,
                                            ["OID"] = feature.Attributes["OBJECTID"]?.ToString() ?? ""
                                        }
                                    };
                                    AddItem(ld, null);
                                    totalFeatures++;
                                }
                            }
                            else if (geom is MapPoint mp)
                            {
                                double rx = extentW > 0 ? (mp.X - mapExtent.XMin) / extentW : 0.5;
                                double ry = extentH > 0 ? (mp.Y - mapExtent.YMin) / extentH : 0.5;

                                var ld = new LayoutItemData
                                {
                                    Type = LayoutItemType.Rectangle,
                                    Name = $"{layerName}_点{totalFeatures + 1}",
                                    X = mapFrameX + rx * mapFrameW - 3,
                                    Y = mapFrameY + (1.0 - ry) * mapFrameH - 3,
                                    Width = 6, Height = 6,
                                    Props =
                                    {
                                        ["Fill"] = layerColor,
                                        ["Stroke"] = "#333333",
                                        ["StrokeThickness"] = "0.5",
                                        ["SourceLayer"] = layerName,
                                        ["OID"] = feature.Attributes["OBJECTID"]?.ToString() ?? ""
                                    }
                                };
                                AddItem(ld, null);
                                totalFeatures++;
                            }
                        }
                    }
                    catch (Exception exLayer)
                    {
                        MessageBox.Show($"加载图层 \"{layerItem.Name}\" 失败: {exLayer.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                UpdateStatus($"已加载 {totalFeatures} 个要素到布局");
                if (totalFeatures > 0)
                    MessageBox.Show($"成功加载 {totalFeatures} 个可编辑要素。\n\n提示：使用选择工具可移动/缩放各要素，右侧面板可修改颜色、线宽等属性。", "加载完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载数据图层失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void MenuItem_SaveToLayer_Click(object sender, RoutedEventArgs e)
        {
            var layerItems = _mainWindow?.LayerManager?.Layers;
            if (layerItems == null || layerItems.Count == 0)
            {
                MessageBox.Show("数据视图中没有图层", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Collect layout items with SourceLayer
            var itemsByLayer = _itemMap.Values
                .Where(d => d.Props.ContainsKey("SourceLayer") && d.Props.ContainsKey("OID"))
                .GroupBy(d => d.Props["SourceLayer"])
                .ToDictionary(g => g.Key, g => g.ToList());

            if (itemsByLayer.Count == 0)
            {
                MessageBox.Show("布局中没有已加载的数据图层要素", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var mv = _mainWindow?.CurrentMapView;
            Envelope mapExtent = null;
            try { mapExtent = mv?.GetCurrentViewpoint(ViewpointType.BoundingGeometry).TargetGeometry as Envelope; } catch { }
            if (mapExtent == null || mapExtent.IsEmpty)
            {
                MessageBox.Show("无法获取当前地图范围，请确保地图视图已初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            double extentW = mapExtent.Width;
            double extentH = mapExtent.Height;
            if (extentW <= 0 || extentH <= 0) return;

            double mapFrameX = 20, mapFrameY = 20;
            double mapFrameW = _doc.PageWidthMm - 40;
            double mapFrameH = (_doc.PageHeightMm - 60) * 0.6;

            int saved = 0;
            foreach (var kv in itemsByLayer)
            {
                var layerItem = layerItems.FirstOrDefault(li => li.Name == kv.Key);
                var fl = layerItem?.Layer as FeatureLayer;
                if (fl == null) continue;
                var fTable = fl.FeatureTable;
                if (fTable == null) continue;

                foreach (var layoutData in kv.Value)
                {
                    if (!long.TryParse(layoutData.Props["OID"], out long oid)) continue;
                    try
                    {
                        var qp = new QueryParameters { WhereClause = $"OBJECTID = {oid}" };
                        var existing = await fTable.QueryFeaturesAsync(qp);
                        var feature = existing.FirstOrDefault();
                        if (feature == null) continue;

                        // Compute new geometry from canvas position
                        Esri.ArcGISRuntime.Geometry.Geometry newGeom = null;
                        double canvasLeftMm = layoutData.X;
                        double canvasTopMm = layoutData.Y;
                        double canvasWidthMm = layoutData.Width;

                        double cx = mapExtent.XMin + ((canvasLeftMm - mapFrameX) / mapFrameW) * extentW;
                        double cy = mapExtent.YMin + (1 - (canvasTopMm - mapFrameY) / mapFrameH) * extentH;

                        if (feature.Geometry is MapPoint)
                            newGeom = new MapPoint(cx, cy, mapExtent.SpatialReference);
                        else if (feature.Geometry is Polyline || feature.Geometry is Polygon)
                            newGeom = feature.Geometry; // Keep original geometry

                        if (newGeom != null)
                        {
                            feature.Geometry = newGeom;
                            await fTable.UpdateFeatureAsync(feature);
                            saved++;
                        }
                    }
                    catch { }
                }
            }

            MessageBox.Show($"已保存 {saved} 个要素的变更到原始图层", "保存完成", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateStatus($"已保存 {saved} 个要素");
        }

        private static void StyleButton(Button btn, string bgHex)
        {
            btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
            btn.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3));
            btn.BorderThickness = new Thickness(0);
            // Use the existing ActBtn-style approach
            var oldTemplate = btn.Template;
            btn.Template = null; // Reset to default WPF button style
        }

        // ========== Menu stubs ==========
        private void MenuItem_Conformity_Click(object sender, RoutedEventArgs e) => MessageBox.Show("整合界线绘制 (功能开发中)");
        private void MenuItem_Unconformity_Click(object sender, RoutedEventArgs e) => MessageBox.Show("不整合界线绘制 (功能开发中)");
        private void MenuItem_FaultLine_Click(object sender, RoutedEventArgs e) => MessageBox.Show("断层线绘制 (功能开发中)");
        private void MenuItem_GenerateLegend_Click(object sender, RoutedEventArgs e) => BtnAddLegend_Click(sender, e);
        private void MenuItem_EditLegend_Click(object sender, RoutedEventArgs e) => MessageBox.Show("编辑图例 (功能开发中)");
        private void MenuItem_Help_Click(object sender, RoutedEventArgs e) => MessageBox.Show("图件编绘管理器帮助\n\n1. 选择纸张大小和方向\n2. 添加地图框架(自动截图当前地图)\n3. 添加图例/比例尺/指北针\n4. 加载数据图层进行编辑\n5. 导出为图片或打印", "帮助");
        private void MenuItem_About_Click(object sender, RoutedEventArgs e) => MessageBox.Show("地质图件编绘管理器 v1.0\n基于 ArcGIS Runtime 100.15", "关于");

        // ========== Tool Selection ==========
        private void BtnSelectTool_Click(object sender, RoutedEventArgs e) => UpdateStatus("已切换选择工具");

        // ========== Canvas Interaction ==========
        private void LayoutCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var pos = e.GetPosition(LayoutCanvas);

            // Check if hit a resize handle
            var rawHit = LayoutCanvas.InputHitTest(pos) as DependencyObject;
            while (rawHit != null && rawHit != LayoutCanvas)
            {
                if (rawHit is FrameworkElement fe && fe.Tag is string tag && tag.StartsWith("HANDLE_"))
                {
                    // Find the parent item border
                    var parent = VisualTreeHelper.GetParent(rawHit);
                    while (parent != null && !(parent is Border && ((Border)parent).Tag is string pid && !string.IsNullOrEmpty(pid)))
                        parent = VisualTreeHelper.GetParent(parent);
                    if (parent is Border hitBorder && hitBorder.Tag is string hitId && !string.IsNullOrEmpty(hitId))
                    {
                        SelectItem(hitBorder);
                        _isResizing = true;
                        _isDragging = false;
                        _resizeIdx = int.Parse(tag.Substring(7));
                        _resizeStartPt = pos;
                        _rsX = Canvas.GetLeft(hitBorder);
                        _rsY = Canvas.GetTop(hitBorder);
                        _rsW = hitBorder.Width;
                        _rsH = hitBorder.Height;
                        hitBorder.CaptureMouse();
                        e.Handled = true;
                        return;
                    }
                }
                rawHit = VisualTreeHelper.GetParent(rawHit);
            }

            // Check if hit an item border
            var hit = LayoutCanvas.InputHitTest(pos) as DependencyObject;
            while (hit != null && hit != LayoutCanvas)
            {
                if (hit is Border border && border.Tag is string id && !string.IsNullOrEmpty(id))
                {
                    SelectItem(border);
                    _isDragging = true;
                    _isResizing = false;
                    _dragStart = pos;
                    border.CaptureMouse();
                    e.Handled = true;
                    return;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }

            DeselectItem();
        }

        private void LayoutCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging && !_isResizing) return;
            var pos = e.GetPosition(LayoutCanvas);

            if (_isResizing && _selectedBorder != null)
            {
                double dx = pos.X - _resizeStartPt.X;
                double dy = pos.Y - _resizeStartPt.Y;
                double nx = _rsX, ny = _rsY, nw = _rsW, nh = _rsH;

                switch (_resizeIdx)
                {
                    case 0: nx = _rsX + dx; ny = _rsY + dy; nw = _rsW - dx; nh = _rsH - dy; break;
                    case 1: ny = _rsY + dy; nw = _rsW + dx; nh = _rsH - dy; break;
                    case 2: nx = _rsX + dx; nw = _rsW - dx; nh = _rsH + dy; break;
                    case 3: nw = _rsW + dx; nh = _rsH + dy; break;
                }
                if (nw < 20) nw = 20; if (nh < 20) nh = 20;
                _selectedBorder.Width = nw; _selectedBorder.Height = nh;
                Canvas.SetLeft(_selectedBorder, nx); Canvas.SetTop(_selectedBorder, ny);
                SyncItemFromElement(_selectedBorder);
                UpdatePropertyPanel();
            }
            else if (_isDragging && _selectedBorder != null)
            {
                double dx = pos.X - _dragStart.X;
                double dy = pos.Y - _dragStart.Y;
                double sx = ChkSnapToGrid.IsChecked == true ? SnapToGridX(Canvas.GetLeft(_selectedBorder) + dx) : Canvas.GetLeft(_selectedBorder) + dx;
                double sy = ChkSnapToGrid.IsChecked == true ? SnapToGridY(Canvas.GetTop(_selectedBorder) + dy) : Canvas.GetTop(_selectedBorder) + dy;
                Canvas.SetLeft(_selectedBorder, sx);
                Canvas.SetTop(_selectedBorder, sy);
                _dragStart = pos;
                SyncItemFromElement(_selectedBorder);
                UpdatePropertyPanel();
            }
        }

        private void LayoutCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging || _isResizing)
            {
                _isDragging = false;
                _isResizing = false;
                _selectedBorder?.ReleaseMouseCapture();
                if (_selectedBorder != null) SyncItemFromElement(_selectedBorder);
                UpdatePropertyPanel();
            }
        }

        private void SliderZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PaperBorder == null || TxtZoom == null) return;
            double z = SliderZoom.Value;
            PaperBorder.LayoutTransform = new ScaleTransform(z, z);
            TxtZoom.Text = $"{(z * 100):F0}%";
        }

        private void LayoutCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double delta = e.Delta > 0 ? 0.1 : -0.1;
            double newVal = Math.Max(0.1, Math.Min(4.0, SliderZoom.Value + delta * SliderZoom.Value));
            SliderZoom.Value = newVal;
            e.Handled = true;
        }

        // ========== Item Selection ==========
        private void SelectItem(Border border)
        {
            if (_selectedBorder == border) return;
            DeselectItem();
            _selectedBorder = border;
            _selectedBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF));
            _selectedBorder.BorderThickness = new Thickness(2);
            ShowHandles(border, true);
            PagePropPanel.Visibility = Visibility.Collapsed;
            ItemPropPanel.Visibility = Visibility.Visible;
            TxtHint.Visibility = Visibility.Collapsed;
            UpdatePropertyPanel();
            BringToFront(border);

            // (Map frame is static image; no overlay needed)
        }

        private void DeselectItem()
        {
            if (_selectedBorder != null)
            {
                _selectedBorder.BorderBrush = null;
                _selectedBorder.BorderThickness = new Thickness(0);
                ShowHandles(_selectedBorder, false);
            }
            _selectedBorder = null;
            _isDragging = false;
            _isResizing = false;
            PagePropPanel.Visibility = Visibility.Visible;
            ItemPropPanel.Visibility = Visibility.Collapsed;
            if (LayoutCanvas.Children.Count == 0) TxtHint.Visibility = Visibility.Visible;
        }

        private void BringToFront(Border border) { Panel.SetZIndex(border, LayoutCanvas.Children.Count + 10); }

        private void ShowHandles(Border border, bool show)
        {
            if (border?.Child is WpfGrid grid)
            {
                foreach (var child in grid.Children)
                    if (child is FrameworkElement fe && fe.Tag is string tag && tag.StartsWith("HANDLE_"))
                        fe.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private double SnapToGridX(double px) => Math.Round(px / 5) * 5;
        private double SnapToGridY(double px) => Math.Round(px / 5) * 5;

        // ========== Item Mouse Events ==========
        // ========== Property Panel ==========
        private void UpdatePropertyPanel()
        {
            if (_isUpdatingProps) return;
            if (_selectedBorder == null) return;
            string id = _selectedBorder.Tag as string;
            if (id == null || !_itemMap.TryGetValue(id, out var data)) return;

            _isUpdatingProps = true;
            TxtItemType.Text = $"选中: {ItemTypeName(data.Type)}";
            TxtItemName.Text = data.Name;
            TxtItemX.Text = data.X.ToString("F1");
            TxtItemY.Text = data.Y.ToString("F1");
            TxtItemW.Text = data.Width.ToString("F1");
            TxtItemH.Text = data.Height.ToString("F1");
            TxtItemRot.Text = data.Rotation.ToString("F1");

            TextPropPanel.Visibility = data.Type == LayoutItemType.TextItem ? Visibility.Visible : Visibility.Collapsed;
            ScalePropPanel.Visibility = data.Type == LayoutItemType.ScaleBar ? Visibility.Visible : Visibility.Collapsed;
            MapFramePropPanel.Visibility = data.Type == LayoutItemType.MapFrame ? Visibility.Visible : Visibility.Collapsed;
            RectPropPanel.Visibility = data.Type == LayoutItemType.Rectangle ? Visibility.Visible : Visibility.Collapsed;
            LegendPropPanel.Visibility = data.Type == LayoutItemType.Legend ? Visibility.Visible : Visibility.Collapsed;

            if (data.Props.TryGetValue("Text", out var txt)) TxtItemContent.Text = txt; else TxtItemContent.Text = "";
            if (data.Props.TryGetValue("FontSize", out var fs)) TxtItemFontSize.Text = fs; else TxtItemFontSize.Text = "14";
            if (data.Type == LayoutItemType.TextItem)
            {
                string fn = "Times New Roman";
                if (data.Props.TryGetValue("FontName", out var fnn)) fn = fnn;
                for (int i = 0; i < CboTextFontName.Items.Count; i++)
                    if ((CboTextFontName.Items[i] as ComboBoxItem)?.Content?.ToString() == fn) { CboTextFontName.SelectedIndex = i; break; }
                ChkTextBold.IsChecked = data.Props.TryGetValue("FontBold", out var fb) && fb == "true";
                ChkTextItalic.IsChecked = data.Props.TryGetValue("FontItalic", out var fi) && fi == "true";
                byte r = 0, g = 0, b = 0;
                if (data.Props.TryGetValue("FontColor_R", out var cr)) byte.TryParse(cr, out r);
                if (data.Props.TryGetValue("FontColor_G", out var cg)) byte.TryParse(cg, out g);
                if (data.Props.TryGetValue("FontColor_B", out var cb)) byte.TryParse(cb, out b);
                ClrTextPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            }
            if (data.Type == LayoutItemType.ScaleBar)
                TxtScaleBarScale.Text = data.MapScale > 0 ? data.MapScale.ToString("F0") : "50000";
            else if (data.Type == LayoutItemType.MapFrame)
                TxtMapScale.Text = data.MapScale > 0 ? data.MapScale.ToString("F0") : "50000";

            if (data.Type == LayoutItemType.Rectangle)
            {
                string rf = "#E0E0E0", rs = "#333333";
                if (data.Props.TryGetValue("Fill", out var rfv)) rf = rfv;
                if (data.Props.TryGetValue("Stroke", out var rsv)) rs = rsv;
                ClrRectFill.Background = ParseBrush(rf);
                ClrRectStroke.Background = ParseBrush(rs);
                TxtRectStrokeWidth.Text = "1";
                if (data.Props.TryGetValue("StrokeThickness", out var rst)) { var _ = double.TryParse(rst, out double rsw); TxtRectStrokeWidth.Text = rsw.ToString("F1"); }
            }
            if (data.Type == LayoutItemType.Legend)
                LoadLegendLayerList(data);

            if (data.Type == LayoutItemType.MapFrame)
            {
                // Load border properties
                string bc = "#808080"; double bw = 1;
                if (data.Props.TryGetValue("MapFrameBorderColor", out var bcv)) bc = bcv;
                if (data.Props.TryGetValue("MapFrameBorderWidth", out var bwv)) double.TryParse(bwv, out bw);
                ClrMapFrameBorder.Background = ParseBrush(bc);
                TxtMapFrameBorderWidth.Text = bw.ToString("F1");

                TxtMapExtentXMin.Text = data.MapExtentXMin.ToString("F4");
                TxtMapExtentYMin.Text = data.MapExtentYMin.ToString("F4");
                TxtMapExtentXMax.Text = data.MapExtentXMax.ToString("F4");
                TxtMapExtentYMax.Text = data.MapExtentYMax.ToString("F4");
                LoadGridSettingsToUI(GetFrameGridSettings(data));
            }

            _isUpdatingProps = false;
        }

        private void TxtItemProp_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingProps || _selectedBorder == null) return;
            string id = _selectedBorder.Tag as string;
            if (id == null || !_itemMap.TryGetValue(id, out var data)) return;

            double.TryParse(TxtItemX.Text, out double x);
            double.TryParse(TxtItemY.Text, out double y);
            double.TryParse(TxtItemW.Text, out double w);
            double.TryParse(TxtItemH.Text, out double h);
            double.TryParse(TxtItemRot.Text, out double rot);

            data.X = x; data.Y = y; data.Width = w; data.Height = h; data.Rotation = rot;
            data.Name = TxtItemName.Text;

            if (data.Type == LayoutItemType.TextItem)
            {
                data.Props["Text"] = TxtItemContent.Text;
                data.Props["FontSize"] = TxtItemFontSize.Text;
                if (CboTextFontName.SelectedItem is ComboBoxItem tfi) data.Props["FontName"] = tfi.Content?.ToString() ?? "Times New Roman";
                data.Props["FontBold"] = ChkTextBold.IsChecked == true ? "true" : "false";
                data.Props["FontItalic"] = ChkTextItalic.IsChecked == true ? "true" : "false";
                if (ClrTextPreview.Background is SolidColorBrush scb)
                {
                    data.Props["FontColor_R"] = scb.Color.R.ToString();
                    data.Props["FontColor_G"] = scb.Color.G.ToString();
                    data.Props["FontColor_B"] = scb.Color.B.ToString();
                }
            }
            if (data.Type == LayoutItemType.Rectangle)
            {
                if (ClrRectFill.Background is SolidColorBrush rfb)
                    data.Props["Fill"] = $"#{rfb.Color.R:X2}{rfb.Color.G:X2}{rfb.Color.B:X2}";
                if (ClrRectStroke.Background is SolidColorBrush rsb)
                    data.Props["Stroke"] = $"#{rsb.Color.R:X2}{rsb.Color.G:X2}{rsb.Color.B:X2}";
                data.Props["StrokeThickness"] = TxtRectStrokeWidth.Text;
            }
            if (data.Type == LayoutItemType.ScaleBar || data.Type == LayoutItemType.MapFrame)
            {
                if (data.Type == LayoutItemType.ScaleBar)
                {
                    if (double.TryParse(TxtScaleBarScale.Text, out double ms)) data.MapScale = ms;
                }
                else
                {
                    if (double.TryParse(TxtMapScale.Text, out double ms)) data.MapScale = ms;
                    // Also read extent fields
                    double.TryParse(TxtMapExtentXMin.Text, out double exMin);
                    double.TryParse(TxtMapExtentYMin.Text, out double eyMin);
                    double.TryParse(TxtMapExtentXMax.Text, out double exMax);
                    double.TryParse(TxtMapExtentYMax.Text, out double eyMax);
                    if (exMax > exMin && eyMax > eyMin)
                    {
                        data.MapExtentXMin = exMin;
                        data.MapExtentYMin = eyMin;
                        data.MapExtentXMax = exMax;
                        data.MapExtentYMax = eyMax;
                    }
                }
            }

            RebuildItem(data);

            // Re-render MapFrame when parameters change
            if (data.Type == LayoutItemType.MapFrame)
                _ = RenderAndUpdateMapFrameAsync(data);

            UpdatePropertyPanel();
        }

        // ========== Text Label Font Handlers ==========
        private void CboTextFontName_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isUpdatingProps || _selectedBorder == null) return;
            TxtItemProp_LostFocus(sender, e);
        }
        private void ChkTextStyle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingProps || _selectedBorder == null) return;
            TxtItemProp_LostFocus(sender, e);
        }
        private void BtnTextColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog();
            if (ClrTextPreview.Background is SolidColorBrush sb) dlg.Color = System.Drawing.Color.FromArgb(sb.Color.R, sb.Color.G, sb.Color.B);
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ClrTextPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
                if (_selectedBorder != null) TxtItemProp_LostFocus(sender, e);
            }
        }

        // ========== Rectangle Style Handlers ==========
        private void BtnRectFillColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog();
            if (ClrRectFill.Background is SolidColorBrush rfb) dlg.Color = System.Drawing.Color.FromArgb(rfb.Color.R, rfb.Color.G, rfb.Color.B);
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ClrRectFill.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
                if (_selectedBorder != null) TxtItemProp_LostFocus(sender, e);
            }
        }
        private void BtnRectStrokeColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog();
            if (ClrRectStroke.Background is SolidColorBrush rsb) dlg.Color = System.Drawing.Color.FromArgb(rsb.Color.R, rsb.Color.G, rsb.Color.B);
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ClrRectStroke.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
                if (_selectedBorder != null) TxtItemProp_LostFocus(sender, e);
            }
        }
        private void TxtRectProp_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingProps || _selectedBorder == null) return;
            TxtItemProp_LostFocus(sender, e);
        }

        // A wrapper to represent a legend list item (header or sub-item)
        private class LegendListItem
        {
            public string LayerName { get; set; }
            public string SubLabel { get; set; } // null for header
            public bool IsSubItem => SubLabel != null;
            public string DisplayName => IsSubItem ? $"   {SubLabel}" : LayerName;
            public string StorageKey => IsSubItem ? $"{LayerName}|{SubLabel}" : LayerName;
        }

        // ========== Legend Reorder Handlers ==========
        private void LoadLegendLayerList(LayoutItemData data)
        {
            LegendLayerList.Items.Clear();
            var layers = _mainWindow?.LayerManager?.Layers?.Where(l => l.IsVisible).ToList();
            if (layers == null || layers.Count == 0) return;

            // Filter hidden layers
            string hiddenStr = null;
            data.Props.TryGetValue("LegendHidden", out hiddenStr);
            var hidden = new HashSet<string>();
            if (!string.IsNullOrEmpty(hiddenStr))
                foreach (var h in hiddenStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    hidden.Add(h);
            if (hidden.Count > 0) layers = layers.Where(l => !hidden.Contains(l.Name)).ToList();

            data.Props.TryGetValue("LegendOrder", out var orderStr);
            var orderedNames = new List<string>();
            if (!string.IsNullOrEmpty(orderStr))
                orderedNames.AddRange(orderStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

            var sorted = layers.OrderBy(l => { int idx = orderedNames.IndexOf(l.Name); return idx >= 0 ? idx : 9999 + layers.IndexOf(l); }).ToList();
            foreach (var l in sorted)
            {
                LegendLayerList.Items.Add(new LegendListItem { LayerName = l.Name });
                // Add sub-items if classified
                var renderer = (l.Layer as FeatureLayer)?.Renderer;
                if (renderer is UniqueValueRenderer uvr && uvr.UniqueValues.Count > 0)
                {
                    string subKey = "LegendSub_" + l.Name;
                    data.Props.TryGetValue(subKey, out var subStr);
                    var subOrdered = new List<string>();
                    if (!string.IsNullOrEmpty(subStr)) subOrdered.AddRange(subStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                    var uvItems = uvr.UniqueValues.ToList();
                    if (subOrdered.Count > 0)
                        uvItems = uvItems.OrderBy(uv => { int idx2 = subOrdered.IndexOf(uv.Label ?? string.Join(",", uv.Values)); return idx2 >= 0 ? idx2 : 9999; }).ToList();
                    foreach (var uv in uvItems)
                        LegendLayerList.Items.Add(new LegendListItem { LayerName = l.Name, SubLabel = uv.Label ?? string.Join(",", uv.Values) });
                }
                else if (renderer is ClassBreaksRenderer cbr && cbr.ClassBreaks.Count > 0)
                {
                    string subKey = "LegendSub_" + l.Name;
                    data.Props.TryGetValue(subKey, out var subStr);
                    var subOrdered = new List<string>();
                    if (!string.IsNullOrEmpty(subStr)) subOrdered.AddRange(subStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                    var cbItems = cbr.ClassBreaks.ToList();
                    if (subOrdered.Count > 0)
                        cbItems = cbItems.OrderBy(cb => { int idx2 = subOrdered.IndexOf(cb.Label ?? $"{cb.MinValue}-{cb.MaxValue}"); return idx2 >= 0 ? idx2 : 9999; }).ToList();
                    foreach (var cb in cbItems)
                        LegendLayerList.Items.Add(new LegendListItem { LayerName = l.Name, SubLabel = cb.Label ?? $"{cb.MinValue}-{cb.MaxValue}" });
                }
            }
        }
        private void SaveLegendOrder(LayoutItemData data)
        {
            var mainOrder = new List<string>();
            var subDict = new Dictionary<string, List<string>>();
            foreach (var item in LegendLayerList.Items)
            {
                if (item is LegendListItem li)
                {
                    if (!li.IsSubItem)
                    {
                        mainOrder.Add(li.LayerName);
                        if (!subDict.ContainsKey(li.LayerName)) subDict[li.LayerName] = new List<string>();
                    }
                    else
                    {
                        if (!subDict.ContainsKey(li.LayerName)) subDict[li.LayerName] = new List<string>();
                        subDict[li.LayerName].Add(li.SubLabel);
                    }
                }
            }
            data.Props["LegendOrder"] = string.Join(";", mainOrder);
            foreach (var kv in subDict)
                data.Props["LegendSub_" + kv.Key] = string.Join(";", kv.Value);
        }
        private void BtnLegendMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (LegendLayerList.SelectedItem == null || _selectedBorder == null) return;
            int idx = LegendLayerList.SelectedIndex;
            if (idx <= 0) return;
            var item = LegendLayerList.SelectedItem;
            // Prevent moving a sub-item above its header
            if (item is LegendListItem li && li.IsSubItem)
            {
                var prev = LegendLayerList.Items[idx - 1] as LegendListItem;
                if (prev != null && !prev.IsSubItem && prev.LayerName == li.LayerName) return; // already at top of sub-list
                if (prev != null && prev.LayerName != li.LayerName) return; // can't move to another layer
            }
            LegendLayerList.Items.RemoveAt(idx);
            LegendLayerList.Items.Insert(idx - 1, item);
            LegendLayerList.SelectedIndex = idx - 1;
            string id = _selectedBorder.Tag as string;
            if (id != null && _itemMap.TryGetValue(id, out var data) && data.Type == LayoutItemType.Legend) SaveLegendOrder(data);
        }
        private void BtnLegendMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (LegendLayerList.SelectedItem == null || _selectedBorder == null) return;
            int idx = LegendLayerList.SelectedIndex;
            if (idx >= LegendLayerList.Items.Count - 1) return;
            var item = LegendLayerList.SelectedItem;
            // Prevent moving a sub-item below the last sub-item of its layer
            if (item is LegendListItem li && li.IsSubItem)
            {
                var next = LegendLayerList.Items[idx + 1] as LegendListItem;
                if (next != null && next.LayerName != li.LayerName) return; // can't move past the group end
            }
            LegendLayerList.Items.RemoveAt(idx);
            LegendLayerList.Items.Insert(idx + 1, item);
            LegendLayerList.SelectedIndex = idx + 1;
            string id = _selectedBorder.Tag as string;
            if (id != null && _itemMap.TryGetValue(id, out var data) && data.Type == LayoutItemType.Legend) SaveLegendOrder(data);
        }
        private void BtnLegendRefresh_Click(object sender, RoutedEventArgs e)
        {
            string id = _selectedBorder?.Tag as string;
            if (id != null && _itemMap.TryGetValue(id, out var data) && data.Type == LayoutItemType.Legend)
            {
                SaveLegendOrder(data);
                RebuildItem(data);
            }
        }
        private void BtnLegendDelete_Click(object sender, RoutedEventArgs e)
        {
            if (LegendLayerList.SelectedItem == null || _selectedBorder == null) return;
            string id = _selectedBorder.Tag as string;
            if (id == null || !_itemMap.TryGetValue(id, out var data) || data.Type != LayoutItemType.Legend) return;

            var sel = LegendLayerList.SelectedItem as LegendListItem;
            if (sel == null) return;

            // Get the blacklist
            string hiddenStr = null;
            data.Props.TryGetValue("LegendHidden", out hiddenStr);
            var hidden = new HashSet<string>();
            if (!string.IsNullOrEmpty(hiddenStr))
                foreach (var h in hiddenStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    hidden.Add(h);

            if (!sel.IsSubItem)
            {
                // Remove entire layer + all its sub-items
                hidden.Add(sel.LayerName);
                // Also remove from LegendOrder
                string orderStr = null;
                data.Props.TryGetValue("LegendOrder", out orderStr);
                if (!string.IsNullOrEmpty(orderStr))
                {
                    var names = new List<string>(orderStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                    names.RemoveAll(n => n == sel.LayerName);
                    data.Props["LegendOrder"] = string.Join(";", names);
                }
                // Remove sub-order for this layer
                data.Props.Remove("LegendSub_" + sel.LayerName);
            }
            else
            {
                // Remove just this sub-item
                string subKey = "LegendSub_" + sel.LayerName;
                string subStr = null;
                data.Props.TryGetValue(subKey, out subStr);
                if (!string.IsNullOrEmpty(subStr))
                {
                    var labels = new List<string>(subStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                    labels.RemoveAll(l => l == sel.SubLabel);
                    data.Props[subKey] = string.Join(";", labels);
                }
            }

            data.Props["LegendHidden"] = string.Join(";", hidden);
            RebuildItem(data);
            LoadLegendLayerList(data);
        }

        // ========== Grid Settings Event Handlers ==========
        private void GridSettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingProps || _selectedBorder == null) return;
            string id = _selectedBorder.Tag as string;
            if (id == null || !_itemMap.TryGetValue(id, out var data) || data.Type != LayoutItemType.MapFrame) return;

            if (sender == ChkGridEnabled)
                GridSettingsPanel.IsEnabled = ChkGridEnabled.IsChecked == true;
            else if (sender == CboGridSpacing)
                GridFixedPanel.IsEnabled = CboGridSpacing.SelectedIndex == 1;

            var gs = ReadGridSettingsFromUI();
            SetFrameGridSettings(data, gs);
            // Update the grid overlay on screen
            UpdateMapFrameGridDisplay(id);
            // Re-render map frame when display mode changes (clears baked-in grid lines)
            if (sender == CboGridDisplayMode)
                _ = RenderAndUpdateMapFrameAsync(data);
        }

        // Update the grid overlay Canvas for a specific map frame (add/remove/refresh)
        private void UpdateMapFrameGridDisplay(string mapFrameId)
        {
            var border = LayoutCanvas.Children.OfType<System.Windows.Controls.Border>()
                .FirstOrDefault(b => (b.Tag as string) == mapFrameId);
            if (border == null) return;
            var grid = border.Child as WpfGrid;
            if (grid == null) return;

            // Remove existing grid overlay from the map frame's internal Grid
            var existing = grid.Children.OfType<Canvas>().FirstOrDefault(c => c.Tag as string == "GRID_OVERLAY");
            if (existing != null) grid.Children.Remove(existing);

            if (!_itemMap.TryGetValue(mapFrameId, out var data) || data.Type != LayoutItemType.MapFrame) return;
            var gs = GetFrameGridSettings(data);
            if (!gs.Enabled) return;

            var overlay = CreateMapFrameGridOverlay(data, gs);
            if (overlay == null) return;
            overlay.Tag = "GRID_OVERLAY";
            // Disable clipping so tick labels outside the border are visible
            border.ClipToBounds = false;
            grid.Children.Add(overlay);
        }

        private void BtnGridLineColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog();
            dlg.Color = System.Drawing.Color.FromArgb(_gridLineColor.R, _gridLineColor.G, _gridLineColor.B);
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _gridLineColor = System.Drawing.Color.FromArgb(128, dlg.Color.R, dlg.Color.G, dlg.Color.B);
                ClrGridLinePreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                    _gridLineColor.A, _gridLineColor.R, _gridLineColor.G, _gridLineColor.B));
                GridSettingChanged(sender, e);
            }
        }

        private void BtnGridFontColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog();
            dlg.Color = System.Drawing.Color.FromArgb(_gridFontColor.R, _gridFontColor.G, _gridFontColor.B);
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _gridFontColor = System.Drawing.Color.FromArgb(180, dlg.Color.R, dlg.Color.G, dlg.Color.B);
                ClrGridFontPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                    _gridFontColor.A, _gridFontColor.R, _gridFontColor.G, _gridFontColor.B));
                GridSettingChanged(sender, e);
            }
        }

        private void BtnDeleteItem_Click(object sender, RoutedEventArgs e) => DeleteSelectedItem();

        private void DeleteSelectedItem()
        {
            if (_selectedBorder == null) return;
            string id = _selectedBorder.Tag as string;
            LayoutCanvas.Children.Remove(_selectedBorder);
            if (id != null) { _itemMap.Remove(id); _doc.Items.RemoveAll(i => i.Id == id); }
            _selectedBorder = null;
            DeselectItem();
            UpdateStatus("已删除选中项");
        }

        private void BtnBringToFront_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBorder != null)
            {
                BringToFront(_selectedBorder);
                int maxZ = 0;
                foreach (var child in LayoutCanvas.Children)
                    if (child is UIElement ue)
                        maxZ = Math.Max(maxZ, Panel.GetZIndex(ue));
                Canvas.SetZIndex(_selectedBorder, maxZ + 1);
            }
        }

        private void BtnSendToBack_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBorder != null)
            {
                int minZ = LayoutCanvas.Children.OfType<UIElement>().Min(c => Panel.GetZIndex(c));
                Canvas.SetZIndex(_selectedBorder, minZ - 1);
            }
        }

        // ========== Page Properties ==========
        private void CboPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PaperBorder == null) return;
            ApplyPageSize(true);
        }
        private void CboOrientation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PaperBorder == null) return;
            ApplyPageSize(true);
        }
        private void TxtPageDim_LostFocus(object sender, RoutedEventArgs e) => ApplyPageSize(false);

        // ========== Add Items ==========
        private async void BtnAddMap_Click(object sender, RoutedEventArgs e)
        {
            var mv = _mainWindow?.CurrentMapView;
            if (mv?.Map == null) { MessageBox.Show("主窗口地图未加载", "提示"); return; }

            var data = new LayoutItemData
            {
                Type = LayoutItemType.MapFrame,
                Name = GenerateDefaultName(LayoutItemType.MapFrame),
                X = 20, Y = 20,
                Width = Math.Min(_doc.PageWidthMm - 40, _doc.PageHeightMm - 60),
                Height = Math.Min(_doc.PageWidthMm - 40, _doc.PageHeightMm - 60),
                MapRef = "main",
                MapScale = GetMapScaleValue()
            };

            // Capture current view center, CRS, and extent
            try
            {
                var vp = mv.GetCurrentViewpoint(ViewpointType.CenterAndScale);
                if (vp?.TargetGeometry is MapPoint center)
                {
                    data.MapCenterX = center.X;
                    data.MapCenterY = center.Y;
                    data.MapWKID = center.SpatialReference?.Wkid ?? 0;
                }

                var extentVp = mv.GetCurrentViewpoint(ViewpointType.BoundingGeometry);
                if (extentVp?.TargetGeometry is Envelope extent)
                {
                    data.MapExtentXMin = extent.XMin;
                    data.MapExtentYMin = extent.YMin;
                    data.MapExtentXMax = extent.XMax;
                    data.MapExtentYMax = extent.YMax;
                }
            }
            catch { }

            // Render off-screen and add as Image
            var bitmap = await RenderMapFrameAsync(data);
            if (bitmap == null) { MessageBox.Show("地图渲染失败，请检查是否有可见图层", "提示"); return; }

            AddItem(data, bitmap);
            UpdateStatus("已添加地图框架");
        }

        private void BtnAddLegend_Click(object sender, RoutedEventArgs e)
        {
            var data = new LayoutItemData
            {
                Type = LayoutItemType.Legend,
                Name = GenerateDefaultName(LayoutItemType.Legend),
                X = 20, Y = _doc.PageHeightMm * 0.65,
                Width = 100, Height = 60
            };
            AddItem(data, null);
            UpdateStatus("已添加图例");
        }

        private void BtnAddScaleBar_Click(object sender, RoutedEventArgs e)
        {
            var data = new LayoutItemData
            {
                Type = LayoutItemType.ScaleBar,
                Name = GenerateDefaultName(LayoutItemType.ScaleBar),
                X = _doc.PageWidthMm / 2 - 50, Y = _doc.PageHeightMm - 30,
                Width = 100, Height = 16,
                Props = { ["MapScale"] = GetMapScaleString() }
            };
            AddItem(data, null);
            UpdateStatus("已添加比例尺");
        }

        private void BtnAddNorthArrow_Click(object sender, RoutedEventArgs e)
        {
            var data = new LayoutItemData
            {
                Type = LayoutItemType.NorthArrow,
                Name = GenerateDefaultName(LayoutItemType.NorthArrow),
                X = _doc.PageWidthMm - 50, Y = _doc.PageHeightMm * 0.65,
                Width = 30, Height = 36
            };
            AddItem(data, null);
            UpdateStatus("已添加指北针");
        }

        private void BtnAddText_Click(object sender, RoutedEventArgs e)
        {
            var data = new LayoutItemData
            {
                Type = LayoutItemType.TextItem,
                Name = GenerateDefaultName(LayoutItemType.TextItem),
                X = 50, Y = 50, Width = 80, Height = 20,
                Props = { ["Text"] = "双击编辑文字", ["FontSize"] = "14" }
            };
            AddItem(data, null);
            UpdateStatus("已添加文字");
        }

        private void BtnAddImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "图片 (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var bitmap = new BitmapImage(new Uri(dlg.FileName));
                var data = new LayoutItemData
                {
                    Type = LayoutItemType.ImageItem,
                    Name = Path.GetFileName(dlg.FileName),
                    X = 30, Y = 30,
                    Width = Math.Min(200, _doc.PageWidthMm - 60),
                    Height = Math.Min(150, _doc.PageHeightMm - 60),
                    Props = { ["SourcePath"] = dlg.FileName }
                };
                AddItem(data, bitmap);
                UpdateStatus("已添加图片");
            }
            catch (Exception ex) { MessageBox.Show($"加载图片失败: {ex.Message}", "错误"); }
        }

        private void BtnAddRectangle_Click(object sender, RoutedEventArgs e)
        {
            var data = new LayoutItemData
            {
                Type = LayoutItemType.Rectangle,
                Name = GenerateDefaultName(LayoutItemType.Rectangle),
                X = 30, Y = 30, Width = 80, Height = 50,
                Props = { ["Fill"] = "#E0E0E0", ["Stroke"] = "#333333", ["StrokeThickness"] = "1" }
            };
            AddItem(data, null);
            UpdateStatus("已添加矩形");
        }

        private void BtnAddLine_Click(object sender, RoutedEventArgs e)
        {
            var data = new LayoutItemData
            {
                Type = LayoutItemType.Line,
                Name = GenerateDefaultName(LayoutItemType.Line),
                X = 30, Y = 30, Width = 80, Height = 2,
                Props = { ["Stroke"] = "#333333", ["StrokeThickness"] = "2" }
            };
            AddItem(data, null);
            UpdateStatus("已添加线条");
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e) => DeleteSelectedItem();
        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutCanvas.Children.Count == 0) return;
            if (MessageBox.Show("确定清空所有布局元素?", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                LayoutCanvas.Children.Clear();
                _itemMap.Clear();
                _doc.Items.Clear();
                _selectedBorder = null;
                DeselectItem();
                UpdateStatus("已清空布局");
            }
        }

        // ========== Add Item Helper ==========
        private void AddItem(LayoutItemData data, BitmapSource bitmap)
        {
            // Add to document
            _itemMap[data.Id] = data;
            _doc.Items.Add(data);

            // Create UI element
            var border = CreateItemBorder(data, bitmap);
            LayoutCanvas.Children.Add(border);
            TxtHint.Visibility = Visibility.Collapsed;
            SelectItem(border);
            SyncItemFromElement(border);
        }

        private string GenerateDefaultName(LayoutItemType type)
        {
            int count = _itemMap.Values.Count(d => d.Type == type) + 1;
            string prefix;
            switch (type)
            {
                case LayoutItemType.MapFrame: prefix = "地图"; break;
                case LayoutItemType.Legend: prefix = "图例"; break;
                case LayoutItemType.ScaleBar: prefix = "比例尺"; break;
                case LayoutItemType.NorthArrow: prefix = "指北针"; break;
                case LayoutItemType.TextItem: prefix = "文字"; break;
                case LayoutItemType.ImageItem: prefix = "图片"; break;
                case LayoutItemType.Rectangle: prefix = "矩形"; break;
                case LayoutItemType.Line: prefix = "线条"; break;
                default: prefix = "元素"; break;
            }
            return $"{prefix}{count}";
        }

        private string GetMapScaleString()
        {
            try { return _mainWindow?.CurrentMapView?.MapScale.ToString("F0") ?? "50000"; }
            catch { return "50000"; }
        }

        private double GetMapScaleValue()
        {
            try { return _mainWindow?.CurrentMapView?.MapScale ?? 50000; }
            catch { return 50000; }
        }

        private string ItemTypeName(LayoutItemType t)
        {
            switch (t)
            {
                case LayoutItemType.MapFrame: return "地图框架";
                case LayoutItemType.Legend: return "图例";
                case LayoutItemType.ScaleBar: return "比例尺";
                case LayoutItemType.NorthArrow: return "指北针";
                case LayoutItemType.TextItem: return "文字标签";
                case LayoutItemType.ImageItem: return "图片";
                case LayoutItemType.Rectangle: return "矩形";
                case LayoutItemType.Line: return "线条";
                default: return "未知";
            }
        }

        // ========== Create Item Border ==========
        private Border CreateItemBorder(LayoutItemData data, BitmapSource bitmap)
        {
            var grid = new WpfGrid();

            var content = CreateItemContent(data, bitmap);
            if (content != null) grid.Children.Add(content);

            // Corner resize handles
            for (int i = 0; i < 4; i++)
                grid.Children.Add(MakeHandle(i));

            var border = new Border
            {
                Tag = data.Id,
                Width = LayoutMath.MmToPx(data.Width),
                Height = LayoutMath.MmToPx(data.Height),
                Background = Brushes.Transparent,
                BorderBrush = null,
                BorderThickness = new Thickness(0),
                ClipToBounds = true,
                Child = grid,
                Cursor = Cursors.SizeAll
            };
            Canvas.SetLeft(border, LayoutMath.MmToPx(data.X));
            Canvas.SetTop(border, LayoutMath.MmToPx(data.Y));
            Panel.SetZIndex(border, 1);
            return border;
        }

        private Border MakeHandle(int idx)
        {
            System.Windows.HorizontalAlignment ha;
            System.Windows.VerticalAlignment va;
            switch (idx)
            {
                case 0: ha = System.Windows.HorizontalAlignment.Left; va = System.Windows.VerticalAlignment.Top; break;
                case 1: ha = System.Windows.HorizontalAlignment.Right; va = System.Windows.VerticalAlignment.Top; break;
                case 2: ha = System.Windows.HorizontalAlignment.Left; va = System.Windows.VerticalAlignment.Bottom; break;
                default: ha = System.Windows.HorizontalAlignment.Right; va = System.Windows.VerticalAlignment.Bottom; break;
            }
            Cursor cur = (idx == 0 || idx == 3) ? Cursors.SizeNWSE : Cursors.SizeNESW;
            return new Border
            {
                Width = 8, Height = 8,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF)),
                BorderThickness = new Thickness(1.5),
                Visibility = Visibility.Collapsed,
                Cursor = cur,
                Tag = "HANDLE_" + idx,
                HorizontalAlignment = ha,
                VerticalAlignment = va,
                Margin = new Thickness(-4, -4, -4, -4),
                IsHitTestVisible = true
            };
        }

        // ========== Create Content ==========
        private UIElement CreateItemContent(LayoutItemData data, BitmapSource bitmap)
        {
            switch (data.Type)
            {
                case LayoutItemType.MapFrame: return CreateMapContent(bitmap, data);
                case LayoutItemType.Legend: return CreateLegendContent(data);
                case LayoutItemType.ScaleBar: return CreateScaleBarContent(data);
                case LayoutItemType.NorthArrow: return CreateNorthArrowContent();
                case LayoutItemType.TextItem: return CreateTextContent(data);
                case LayoutItemType.ImageItem: return CreateImageContent(bitmap);
                case LayoutItemType.Rectangle: return CreateRectangleContent(data);
                case LayoutItemType.Line: return CreateLineContent(data);
                default: return new TextBlock { Text = "?", Foreground = Brushes.Gray };
            }
        }

        private UIElement CreateMapContent(BitmapSource bitmap, LayoutItemData data = null)
        {
            var (br, bw) = data != null ? GetMapFrameBorder(data) : (new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), 1.0);
            var border = new Border { BorderBrush = br, BorderThickness = new Thickness(bw) };
            if (bitmap != null)
            {
                border.Background = Brushes.White;
                var img = new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(2) };
                border.Child = img;
            }
            else
            {
                border.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
                border.Child = new TextBlock
                {
                    Text = "地图\n(需刷新)",
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    FontSize = 14,
                    TextAlignment = TextAlignment.Center
                };
            }
            return border;
        }

        private UIElement CreateLegendContent(LayoutItemData legendDataHint = null)
        {
            var sp = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };
            sp.Children.Add(new TextBlock
            {
                Text = "图例",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = Brushes.Black,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var layers = _mainWindow?.LayerManager?.Layers;
            var visibleLayers = layers?.Where(l => l.IsVisible).ToList();
            if (visibleLayers != null && visibleLayers.Count > 0)
            {
                // Apply stored reorder if available
                if (legendDataHint == null)
                {
                    // Try to find the first legend item in the document
                    var firstLegend = _itemMap.Values.FirstOrDefault(d => d.Type == LayoutItemType.Legend);
                    if (firstLegend != null && firstLegend.Props.TryGetValue("LegendOrder", out var lo) && !string.IsNullOrEmpty(lo))
                        legendDataHint = firstLegend;
                }
                // Read hidden layers
                HashSet<string> hidden = null;
                string hiddenStr = null;
                if (legendDataHint != null && legendDataHint.Props.TryGetValue("LegendHidden", out hiddenStr) && !string.IsNullOrEmpty(hiddenStr))
                    hidden = new HashSet<string>(hiddenStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                if (hidden != null && hidden.Count > 0)
                    visibleLayers = visibleLayers.Where(l => !hidden.Contains(l.Name)).ToList();

                List<string> orderedNames = null;
                if (legendDataHint != null && legendDataHint.Props.TryGetValue("LegendOrder", out var orderStr) && !string.IsNullOrEmpty(orderStr))
                    orderedNames = new List<string>(orderStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                if (orderedNames != null && orderedNames.Count > 0)
                    visibleLayers = visibleLayers.OrderBy(l => { int idx = orderedNames.IndexOf(l.Name); return idx >= 0 ? idx : 9999; }).ToList();
                foreach (var layer in visibleLayers)
                {
                    try
                    {
                        Renderer renderer = null;
                        List<string> subOrder = null;
                        if (layer.Layer is FeatureLayer fl && fl.Renderer != null)
                        {
                            renderer = fl.Renderer;
                            if (legendDataHint != null)
                            {
                                string subKey = "LegendSub_" + layer.Name;
                                if (legendDataHint.Props.TryGetValue(subKey, out var so) && !string.IsNullOrEmpty(so))
                                    subOrder = new List<string>(so.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                            }
                        }

                        // 分类渲染（唯一值/分级）时不显示总图层名，只显示分类类别
                        bool isClassified = renderer is UniqueValueRenderer || renderer is ClassBreaksRenderer;
                        if (!isClassified)
                        {
                            // Always show layer header
                            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                            Symbol layerSymbol = null;
                            if (layer.Layer is FeatureLayer fl0)
                            {
                                if (fl0.Renderer is SimpleRenderer sr0) layerSymbol = sr0.Symbol;
                                else if (fl0.Renderer is UniqueValueRenderer uvr0) layerSymbol = uvr0.UniqueValues.FirstOrDefault()?.Symbol;
                                else if (fl0.Renderer is ClassBreaksRenderer cbr0) layerSymbol = cbr0.ClassBreaks.FirstOrDefault()?.Symbol;
                            }
                            var swatch = layerSymbol != null ? CreateSymbolSwatch(layerSymbol, 16) : (UIElement)new Border
                            {
                                Width = 14, Height = 12,
                                Background = new SolidColorBrush(GetColorFromLayer(layer.Layer)),
                                BorderBrush = Brushes.Black,
                                BorderThickness = new Thickness(0.5),
                                VerticalAlignment = System.Windows.VerticalAlignment.Center
                            };
                            row.Children.Add(new Border
                            {
                                Child = swatch,
                                Margin = new Thickness(0, 2, 4, 2),
                                VerticalAlignment = System.Windows.VerticalAlignment.Center
                            });
                            row.Children.Add(new TextBlock { Text = layer.Name, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                            sp.Children.Add(row);
                        }

                        // Add classification sub-items if any
                        if (renderer != null)
                            AddLegendSubItems(sp, renderer, subOrder);
                    }
                    catch (Exception legendEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Legend item error for '{layer.Name}': {legendEx.Message}");
                    }
                }
            }
            else
            {
                sp.Children.Add(new TextBlock { Text = "无可见图层", FontSize = 10, Foreground = Brushes.Gray, FontStyle = FontStyles.Italic });
            }
            return new Border { Child = sp, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)), BorderThickness = new Thickness(0.5) };
        }

        private static UIElement CreateSymbolSwatch(Symbol symbol, double size = 14)
        {
            if (size <= 0 || double.IsNaN(size) || double.IsInfinity(size)) size = 14;
            var c = new Canvas { Width = size, Height = size };
            double half = size / 2;

            if (symbol is CompositeSymbol cs)
            {
                foreach (var sub in cs.Symbols)
                {
                    var subCanvas = CreateSymbolSwatch(sub, size) as Canvas;
                    if (subCanvas == null) continue;
                    while (subCanvas.Children.Count > 0)
                    {
                        var el = subCanvas.Children[0];
                        subCanvas.Children.Remove(el);
                        c.Children.Add(el);
                    }
                }
                return c;
            }
            if (symbol is SimpleMarkerSymbol sms)
            {
                var fillColor = System.Windows.Media.Color.FromArgb(sms.Color.A, sms.Color.R, sms.Color.G, sms.Color.B);
                var outlineColor = sms.Outline != null
                    ? System.Windows.Media.Color.FromArgb(sms.Outline.Color.A, sms.Outline.Color.R, sms.Outline.Color.G, sms.Outline.Color.B)
                    : fillColor;
                double outlineWidth = sms.Outline?.Width ?? 1;

                // Scale marker size to fit the swatch
                double rawSize = sms.Size;
                if (double.IsNaN(rawSize) || double.IsInfinity(rawSize) || rawSize <= 0) rawSize = 10;
                double ms = Math.Min(rawSize * 0.5, half - 2);
                if (ms < 2 || double.IsNaN(ms) || double.IsInfinity(ms)) ms = half - 2;

                switch (sms.Style)
                {
                    case SimpleMarkerSymbolStyle.Circle:
                        c.Children.Add(new System.Windows.Shapes.Ellipse
                        {
                            Width = ms * 2, Height = ms * 2,
                            Fill = new SolidColorBrush(fillColor),
                            Stroke = new SolidColorBrush(outlineColor),
                            StrokeThickness = outlineWidth
                        });
                        Canvas.SetLeft(c.Children[c.Children.Count - 1], half - ms);
                        Canvas.SetTop(c.Children[c.Children.Count - 1], half - ms);
                        break;
                    case SimpleMarkerSymbolStyle.Square:
                        c.Children.Add(new System.Windows.Shapes.Rectangle
                        {
                            Width = ms * 2, Height = ms * 2,
                            Fill = new SolidColorBrush(fillColor),
                            Stroke = new SolidColorBrush(outlineColor),
                            StrokeThickness = outlineWidth
                        });
                        Canvas.SetLeft(c.Children[c.Children.Count - 1], half - ms);
                        Canvas.SetTop(c.Children[c.Children.Count - 1], half - ms);
                        break;
                    case SimpleMarkerSymbolStyle.Diamond:
                        {
                            var pts = new System.Windows.Media.PointCollection
                            {
                                new System.Windows.Point(half, half - ms),
                                new System.Windows.Point(half + ms, half),
                                new System.Windows.Point(half, half + ms),
                                new System.Windows.Point(half - ms, half)
                            };
                            c.Children.Add(new System.Windows.Shapes.Polygon
                            {
                                Points = pts, Fill = new SolidColorBrush(fillColor),
                                Stroke = new SolidColorBrush(outlineColor), StrokeThickness = outlineWidth
                            });
                        }
                        break;
                    case SimpleMarkerSymbolStyle.Triangle:
                        {
                            var pts = new System.Windows.Media.PointCollection
                            {
                                new System.Windows.Point(half, half - ms),
                                new System.Windows.Point(half + ms, half + ms * 0.7),
                                new System.Windows.Point(half - ms, half + ms * 0.7)
                            };
                            c.Children.Add(new System.Windows.Shapes.Polygon
                            {
                                Points = pts, Fill = new SolidColorBrush(fillColor),
                                Stroke = new SolidColorBrush(outlineColor), StrokeThickness = outlineWidth
                            });
                        }
                        break;
                    case SimpleMarkerSymbolStyle.Cross:
                        {
                            double t = ms * 0.35;
                            double a = ms * 0.7;
                            var pts = new System.Windows.Media.PointCollection
                            {
                                new System.Windows.Point(half - t, half - ms),
                                new System.Windows.Point(half + t, half - ms),
                                new System.Windows.Point(half + t, half - a),
                                new System.Windows.Point(half + ms, half - a),
                                new System.Windows.Point(half + ms, half + a),
                                new System.Windows.Point(half + t, half + a),
                                new System.Windows.Point(half + t, half + ms),
                                new System.Windows.Point(half - t, half + ms),
                                new System.Windows.Point(half - t, half + a),
                                new System.Windows.Point(half - ms, half + a),
                                new System.Windows.Point(half - ms, half - a),
                                new System.Windows.Point(half - t, half - a)
                            };
                            c.Children.Add(new System.Windows.Shapes.Polygon
                            {
                                Points = pts, Fill = new SolidColorBrush(fillColor),
                                Stroke = new SolidColorBrush(outlineColor), StrokeThickness = outlineWidth
                            });
                        }
                        break;
                    case SimpleMarkerSymbolStyle.X:
                        {
                            double o = ms * 0.35;
                            var pts = new System.Windows.Media.PointCollection
                            {
                                new System.Windows.Point(half - o, half - ms),
                                new System.Windows.Point(half + o, half - ms),
                                new System.Windows.Point(half + ms, half - o),
                                new System.Windows.Point(half + ms, half + o),
                                new System.Windows.Point(half + o, half + ms),
                                new System.Windows.Point(half - o, half + ms),
                                new System.Windows.Point(half - ms, half + o),
                                new System.Windows.Point(half - ms, half - o)
                            };
                            c.Children.Add(new System.Windows.Shapes.Polygon
                            {
                                Points = pts, Fill = new SolidColorBrush(fillColor),
                                Stroke = new SolidColorBrush(outlineColor), StrokeThickness = outlineWidth
                            });
                        }
                        break;
                    default: // fallback: circle
                        c.Children.Add(new System.Windows.Shapes.Ellipse
                        {
                            Width = ms * 2, Height = ms * 2,
                            Fill = new SolidColorBrush(fillColor),
                            Stroke = new SolidColorBrush(outlineColor),
                            StrokeThickness = outlineWidth
                        });
                        Canvas.SetLeft(c.Children[c.Children.Count - 1], half - ms);
                        Canvas.SetTop(c.Children[c.Children.Count - 1], half - ms);
                        break;
                }
            }
            else if (symbol is SimpleLineSymbol sls)
            {
                var lineColor = System.Windows.Media.Color.FromArgb(sls.Color.A, sls.Color.R, sls.Color.G, sls.Color.B);
                System.Windows.Media.DashStyle dashStyle = null;
                switch (sls.Style)
                {
                    case SimpleLineSymbolStyle.Dash:
                        dashStyle = new System.Windows.Media.DashStyle(new[] { 4.0, 3.0 }, 0);
                        break;
                    case SimpleLineSymbolStyle.Dot:
                        dashStyle = new System.Windows.Media.DashStyle(new[] { 1.0, 2.0 }, 0);
                        break;
                    case SimpleLineSymbolStyle.DashDot:
                        dashStyle = new System.Windows.Media.DashStyle(new[] { 4.0, 2.0, 1.0, 2.0 }, 0);
                        break;
                    case SimpleLineSymbolStyle.DashDotDot:
                        dashStyle = new System.Windows.Media.DashStyle(new[] { 4.0, 2.0, 1.0, 2.0, 1.0, 2.0 }, 0);
                        break;
                }
                c.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = 0, Y1 = half, X2 = size, Y2 = half,
                    Stroke = new SolidColorBrush(lineColor),
                    StrokeThickness = Math.Max(1, Math.Min(sls.Width, 4)),
                    StrokeDashArray = dashStyle?.Dashes,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeStartLineCap = PenLineCap.Round
                });
                if (sls.MarkerStyle == SimpleLineSymbolMarkerStyle.Arrow)
                {
                    bool arrowBegin = sls.MarkerPlacement == SimpleLineSymbolMarkerPlacement.Begin || sls.MarkerPlacement == SimpleLineSymbolMarkerPlacement.BeginAndEnd;
                    bool arrowEnd = sls.MarkerPlacement == SimpleLineSymbolMarkerPlacement.End || sls.MarkerPlacement == SimpleLineSymbolMarkerPlacement.BeginAndEnd;
                    double asz = Math.Max(3, Math.Min(4, half - 2));
                    if (arrowEnd)
                    {
                        var pts = new System.Windows.Media.PointCollection
                        {
                            new System.Windows.Point(size - 4, half),
                            new System.Windows.Point(size - 4 - asz, half - asz * 0.7),
                            new System.Windows.Point(size - 4 - asz, half + asz * 0.7)
                        };
                        c.Children.Add(new System.Windows.Shapes.Polygon { Points = pts, Fill = new SolidColorBrush(lineColor), StrokeThickness = 0.5 });
                    }
                    if (arrowBegin)
                    {
                        var pts = new System.Windows.Media.PointCollection
                        {
                            new System.Windows.Point(4, half),
                            new System.Windows.Point(4 + asz, half - asz * 0.7),
                            new System.Windows.Point(4 + asz, half + asz * 0.7)
                        };
                        c.Children.Add(new System.Windows.Shapes.Polygon { Points = pts, Fill = new SolidColorBrush(lineColor), StrokeThickness = 0.5 });
                    }
                }
            }
            else if (symbol is SimpleFillSymbol sfs)
            {
                var fillColor = System.Windows.Media.Color.FromArgb(sfs.Color.A, sfs.Color.R, sfs.Color.G, sfs.Color.B);
                var outlineColor = sfs.Outline != null
                    ? System.Windows.Media.Color.FromArgb(sfs.Outline.Color.A, sfs.Outline.Color.R, sfs.Outline.Color.G, sfs.Outline.Color.B)
                    : fillColor;
                c.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width = size - 2, Height = size - 2,
                    Fill = new SolidColorBrush(fillColor),
                    Stroke = new SolidColorBrush(outlineColor),
                    StrokeThickness = 1
                });
                Canvas.SetLeft(c.Children[c.Children.Count - 1], 1);
                Canvas.SetTop(c.Children[c.Children.Count - 1], 1);
            }
            else if (symbol is PictureMarkerSymbol pms)
            {
                try
                {
                    var uri = pms.Uri;
                    if (uri != null && (uri.IsFile || uri.IsAbsoluteUri))
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = uri;
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        double w = Math.Min(pms.Width > 0 ? pms.Width : size, size);
                        double h = Math.Min(pms.Height > 0 ? pms.Height : size, size);
                        var img = new Image { Source = bi, Width = w, Height = h, Stretch = Stretch.Uniform };
                        Canvas.SetLeft(img, (size - w) / 2);
                        Canvas.SetTop(img, (size - h) / 2);
                        c.Children.Add(img);
                        return c;
                    }
                }
                catch { }
                // Fallback: draw a circle with "P" to indicate picture marker
                var fc = System.Windows.Media.Color.FromArgb(0xCC, 0x88, 0xA6, 0xFF);
                c.Children.Add(new System.Windows.Shapes.Ellipse { Width = size - 4, Height = size - 4, Fill = new SolidColorBrush(fc), Stroke = Brushes.Gray, StrokeThickness = 1 });
                Canvas.SetLeft(c.Children[c.Children.Count - 1], 2);
                Canvas.SetTop(c.Children[c.Children.Count - 1], 2);
            }
            else if (symbol is PictureFillSymbol pfs)
            {
                Brush fillBrush;
                try
                {
                    if (pfs.Uri != null)
                    {
                        var bi = new BitmapImage(pfs.Uri);
                        fillBrush = new ImageBrush(bi)
                        {
                            TileMode = TileMode.Tile,
                            Viewport = new Rect(0, 0, 1, 1),
                            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                            Stretch = Stretch.Uniform
                        };
                    }
                    else
                    {
                        var fillColor = System.Windows.Media.Color.FromArgb(pfs.Color.A, pfs.Color.R, pfs.Color.G, pfs.Color.B);
                        fillBrush = new SolidColorBrush(fillColor);
                    }
                }
                catch
                {
                    var fillColor = System.Windows.Media.Color.FromArgb(pfs.Color.A, pfs.Color.R, pfs.Color.G, pfs.Color.B);
                    fillBrush = new SolidColorBrush(fillColor);
                }
                c.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width = size - 2, Height = size - 2,
                    Fill = fillBrush,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.5
                });
                Canvas.SetLeft(c.Children[c.Children.Count - 1], 1);
                Canvas.SetTop(c.Children[c.Children.Count - 1], 1);
            }
            else // fallback: colored square
            {
                var color = ExtractColor(symbol);
                c.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width = size - 2, Height = size - 2,
                    Fill = new SolidColorBrush(color),
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.5
                });
                Canvas.SetLeft(c.Children[c.Children.Count - 1], 1);
                Canvas.SetTop(c.Children[c.Children.Count - 1], 1);
            }

            return c;
        }

        private static void AddLegendSubItems(StackPanel parent, Renderer renderer, List<string> subOrder = null)
        {
            if (renderer is UniqueValueRenderer uvr)
            {
                var items = uvr.UniqueValues.ToList();
                if (subOrder != null && subOrder.Count > 0)
                    items = items.OrderBy(uv => { int idx = subOrder.IndexOf(uv.Label ?? string.Join(",", uv.Values)); return idx >= 0 ? idx : 9999; }).ToList();
                foreach (var uv in items)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                    row.Children.Add(new Border
                    {
                        Child = CreateSymbolSwatch(uv.Symbol, 14),
                        Margin = new Thickness(4, 2, 4, 2),
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    });
                    row.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(uv.Label) ? string.Join(", ", uv.Values) : uv.Label, FontSize = 9, Foreground = Brushes.Black, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                    parent.Children.Add(row);
                }
                if (!string.IsNullOrEmpty(uvr.DefaultLabel))
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                    row.Children.Add(new Border
                    {
                        Child = CreateSymbolSwatch(uvr.DefaultSymbol, 14),
                        Margin = new Thickness(4, 2, 4, 2),
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    });
                    row.Children.Add(new TextBlock { Text = uvr.DefaultLabel, FontSize = 9, Foreground = Brushes.Gray, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                    parent.Children.Add(row);
                }
            }
            else if (renderer is ClassBreaksRenderer cbr)
            {
                var breaks = cbr.ClassBreaks.ToList();
                if (subOrder != null && subOrder.Count > 0)
                    breaks = breaks.OrderBy(cb => { int idx = subOrder.IndexOf(cb.Label ?? $"{cb.MinValue}-{cb.MaxValue}"); return idx >= 0 ? idx : 9999; }).ToList();
                foreach (var cb in breaks)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                    var swatch = CreateSymbolSwatch(cb.Symbol, 14);
                    row.Children.Add(new Border
                    {
                        Child = swatch,
                        Margin = new Thickness(4, 2, 4, 2),
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    });
                    row.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(cb.Label) ? $"{cb.MinValue:F2} - {cb.MaxValue:F2}" : cb.Label, FontSize = 9, Foreground = Brushes.Black, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                    parent.Children.Add(row);
                }
                if (!string.IsNullOrEmpty(cbr.DefaultLabel))
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                    row.Children.Add(new Border
                    {
                        Child = CreateSymbolSwatch(cbr.DefaultSymbol, 14),
                        Margin = new Thickness(4, 2, 4, 2),
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    });
                    row.Children.Add(new TextBlock { Text = cbr.DefaultLabel, FontSize = 9, Foreground = Brushes.Gray, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                    parent.Children.Add(row);
                }
            }
        }

        private static Color GetColorFromLayer(Layer layer)
        {
            if (layer is FeatureLayer fl)
            {
                var r = fl.Renderer;
                if (r is SimpleRenderer sr && sr.Symbol != null) return ExtractColor(sr.Symbol);
                if (r is UniqueValueRenderer uvr && uvr.UniqueValues.Count > 0) return ExtractColor(uvr.UniqueValues[0].Symbol);
                if (r is ClassBreaksRenderer cbr && cbr.ClassBreaks.Count > 0) return ExtractColor(cbr.ClassBreaks[0].Symbol);
            }
            return Color.FromRgb(0x88, 0xA6, 0xFF);
        }

        internal static Color ExtractColorStatic(Symbol symbol)
        {
            if (symbol is SimpleMarkerSymbol sms) return Color.FromArgb(sms.Color.A, sms.Color.R, sms.Color.G, sms.Color.B);
            if (symbol is SimpleLineSymbol sls) return Color.FromArgb(sls.Color.A, sls.Color.R, sls.Color.G, sls.Color.B);
            if (symbol is SimpleFillSymbol sfs) return Color.FromArgb(sfs.Color.A, sfs.Color.R, sfs.Color.G, sfs.Color.B);
            return Color.FromRgb(0x88, 0xA6, 0xFF);
        }
        private static Color ExtractColor(Symbol symbol) => ExtractColorStatic(symbol);

        private UIElement CreateScaleBarContent(LayoutItemData data)
        {
            double mapScale = data.MapScale;
            if (mapScale <= 0) mapScale = 50000;

            double wPx = LayoutMath.MmToPx(data.Width);
            double barMm = data.Width;
            double realDistM = barMm * mapScale / 1000.0;

            // Round to nice number
            double nice = NiceRound(realDistM);
            double actualMm = nice * 1000.0 / mapScale;
            double actualPx = LayoutMath.MmToPx(actualMm);

            var canvas = new Canvas { Width = wPx, Height = 20 };
            double mid = 10;

            // Top line
            canvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 0, Y1 = mid, X2 = actualPx, Y2 = mid,
                Stroke = Brushes.Black, StrokeThickness = 1.5
            });
            // Left tick
            canvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 0, Y1 = mid - 4, X2 = 0, Y2 = mid + 4,
                Stroke = Brushes.Black, StrokeThickness = 1.5
            });
            // Right tick
            canvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = actualPx, Y1 = mid - 4, X2 = actualPx, Y2 = mid + 4,
                Stroke = Brushes.Black, StrokeThickness = 1.5
            });

            // Left label
            canvas.Children.Add(new TextBlock
            {
                Text = "0",
                FontSize = 9,
                Foreground = Brushes.Black,
                Margin = new Thickness(-4, mid + 4, 0, 0)
            });
            // Right label
            string label = nice >= 1000 ? $"{nice / 1000:F1}km" : $"{nice:F0}m";
            var tb = new TextBlock { Text = label, FontSize = 9, Foreground = Brushes.Black };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            canvas.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = Brushes.Black,
                Margin = new Thickness(actualPx - tb.DesiredSize.Width / 2, mid + 4, 0, 0)
            });

            // Segments
            int segCount = 4;
            double segPx = actualPx / segCount;
            for (int i = 0; i < segCount; i++)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = segPx,
                    Height = 6,
                    Fill = i % 2 == 0 ? Brushes.Black : Brushes.White,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.5
                };
                Canvas.SetLeft(rect, i * segPx);
                Canvas.SetTop(rect, mid - 3);
                canvas.Children.Add(rect);
            }

            return canvas;
        }

        private static double NiceRound(double val)
        {
            if (val <= 0) return 1;
            double exp = Math.Floor(Math.Log10(val));
            double mant = val / Math.Pow(10, exp);
            double nice;
            if (mant <= 1) nice = 1;
            else if (mant <= 2) nice = 2;
            else if (mant <= 5) nice = 5;
            else nice = 10;
            return nice * Math.Pow(10, exp);
        }

        private UIElement CreateNorthArrowContent()
        {
            var c = new Canvas { Width = 30, Height = 36 };
            // Arrow shaft
            c.Children.Add(new System.Windows.Shapes.Polygon
            {
                Points = new System.Windows.Media.PointCollection { new Point(15, 2), new Point(22, 28), new Point(15, 22), new Point(8, 28) },
                Fill = Brushes.Black
            });
            // N label
            c.Children.Add(new TextBlock { Text = "N", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.Red, Margin = new Thickness(9, 28, 0, 0) });
            // Circle
            c.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 28, Height = 28,
                Stroke = Brushes.Black, StrokeThickness = 1.0,
                Fill = Brushes.Transparent,
                Margin = new Thickness(1, 2, 0, 0)
            });
            return c;
        }

        private UIElement CreateTextContent(LayoutItemData data)
        {
            double fs = 14;
            if (data.Props.TryGetValue("FontSize", out var fss)) double.TryParse(fss, out fs);
            string txt = "文字";
            if (data.Props.TryGetValue("Text", out var t)) txt = t;
            string fn = "Times New Roman";
            if (data.Props.TryGetValue("FontName", out var fnn)) fn = fnn;
            bool bold = data.Props.TryGetValue("FontBold", out var fb) && fb == "true";
            bool italic = data.Props.TryGetValue("FontItalic", out var fi) && fi == "true";
            var fg = Brushes.Black;
            if (data.Props.TryGetValue("FontColor_R", out var cr) && byte.TryParse(cr, out byte r) &&
                data.Props.TryGetValue("FontColor_G", out var cg) && byte.TryParse(cg, out byte g) &&
                data.Props.TryGetValue("FontColor_B", out var cb) && byte.TryParse(cb, out byte b))
                fg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            return new TextBlock
            {
                Text = txt,
                FontSize = fs,
                FontFamily = new FontFamily(fn),
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
                Foreground = fg,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2)
            };
        }

        private UIElement CreateImageContent(BitmapSource bitmap)
        {
            if (bitmap != null)
                return new Image { Source = bitmap, Stretch = Stretch.Uniform };
            return new TextBlock { Text = "图片", Foreground = Brushes.Gray, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        }

        private UIElement CreateRectangleContent(LayoutItemData data)
        {
            string fill = "#E0E0E0", stroke = "#333333";
            double sw = 1;
            if (data.Props.TryGetValue("Fill", out var f)) fill = f;
            if (data.Props.TryGetValue("Stroke", out var s)) stroke = s;
            if (data.Props.TryGetValue("StrokeThickness", out var st)) double.TryParse(st, out sw);
            return new System.Windows.Shapes.Rectangle
            {
                Fill = ParseBrush(fill),
                Stroke = ParseBrush(stroke),
                StrokeThickness = sw,
                RadiusX = 2, RadiusY = 2
            };
        }

        private UIElement CreateLineContent(LayoutItemData data)
        {
            string stroke = "#333333";
            double sw = 2;
            if (data.Props.TryGetValue("Stroke", out var s)) stroke = s;
            if (data.Props.TryGetValue("StrokeThickness", out var st)) double.TryParse(st, out sw);

            // 如果有 NormalizedPath 则绘制实际几何路径
            if (data.Props.TryGetValue("NormalizedPath", out var pathStr) && !string.IsNullOrEmpty(pathStr))
            {
                var parts = pathStr.Split(';');
                var pts = new List<System.Windows.Point>();
                foreach (var p in parts)
                {
                    var xy = p.Split(',');
                    if (xy.Length == 2 && double.TryParse(xy[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double rx) &&
                        double.TryParse(xy[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double ry))
                    {
                        pts.Add(new System.Windows.Point(rx, 1.0 - ry));
                    }
                }

                double wPx = Math.Max(1, LayoutMath.MmToPx(data.Width));
                double hPx = Math.Max(1, LayoutMath.MmToPx(data.Height));

                if (pts.Count >= 2)
                {
                    var polyline = new System.Windows.Shapes.Polyline();
                    foreach (var pt in pts)
                        polyline.Points.Add(new System.Windows.Point(pt.X * wPx, pt.Y * hPx));
                    polyline.Stroke = ParseBrush(stroke);
                    polyline.StrokeThickness = sw;
                    polyline.StrokeStartLineCap = PenLineCap.Round;
                    polyline.StrokeEndLineCap = PenLineCap.Round;
                    polyline.StrokeLineJoin = PenLineJoin.Round;
                    return polyline;
                }
            }

            // 回退：简单水平线
            return new System.Windows.Shapes.Line
            {
                X1 = 0, Y1 = 0, X2 = LayoutMath.MmToPx(data.Width), Y2 = 0,
                Stroke = ParseBrush(stroke),
                StrokeThickness = sw,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
        }

        private static readonly BrushConverter _brushConv = new BrushConverter();
        private static Brush ParseBrush(string hex)
        {
            try { return (Brush)_brushConv.ConvertFromString(hex); }
            catch { return Brushes.Black; }
        }

        /// Clones visible layers from MainWindow.LayerManager.Layers into targetMap (file-path based).
        /// Returns number of successfully cloned layers.
        private static async System.Threading.Tasks.Task<int> CloneLayersFromManagerAsync(MainWindow mainWin, Map targetMap)
        {
            int count = 0;
            if (mainWin?.LayerManager?.Layers == null) return 0;

            foreach (var li in mainWin.LayerManager.Layers)
            {
                if (!li.IsVisible || li.Layer == null) continue;

                try
                {
                    Layer cloned = null;

                    if (li.Layer is FeatureLayer fl)
                    {
                        await fl.LoadAsync();
                        var ft = fl.FeatureTable;

                        if (ft is ShapefileFeatureTable sft)
                        {
                            string path = !string.IsNullOrEmpty(li.FilePath) ? li.FilePath : sft.Path;
                            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                            {
                                var newTable = new ShapefileFeatureTable(path);
                                await newTable.LoadAsync();
                                cloned = new FeatureLayer(newTable);
                            }
                        }
                        else if (ft is GeodatabaseFeatureTable gft)
                        {
                            string gdbPath = gft.Geodatabase?.Path;
                            string tableName = gft.TableName;
                            if (!string.IsNullOrEmpty(gdbPath) && !string.IsNullOrEmpty(tableName))
                            {
                                var gdb = await Geodatabase.OpenAsync(gdbPath);
                                foreach (var t in gdb.GeodatabaseFeatureTables)
                                {
                                    if (t.TableName == tableName)
                                    {
                                        await t.LoadAsync();
                                        cloned = new FeatureLayer(t);
                                        break;
                                    }
                                }
                            }
                        }
                        else if (ft is FeatureCollectionTable fct)
                        {
                            await fct.LoadAsync();
                            var feats = (await fct.QueryFeaturesAsync(new QueryParameters())).ToList();
                            if (feats.Count > 0)
                            {
                                var fields = fct.Fields.ToList();
                                var newTable = new FeatureCollectionTable(fields, fct.GeometryType, fct.SpatialReference);
                                foreach (var f in feats)
                                {
                                    var nf = newTable.CreateFeature();
                                    nf.Geometry = f.Geometry;
                                    foreach (var kv in f.Attributes)
                                        if (kv.Value != null && kv.Value != DBNull.Value) nf.Attributes[kv.Key] = kv.Value;
                                    try { await newTable.AddFeatureAsync(nf); } catch { }
                                }
                                await newTable.LoadAsync();
                                cloned = new FeatureLayer(newTable);
                            }
                        }
                        else if (li.MemoryFeatureTable != null)
                        {
                            var mem = li.MemoryFeatureTable;
                            await mem.LoadAsync();
                            var feats = (await mem.QueryFeaturesAsync(new QueryParameters())).ToList();
                            if (feats.Count > 0)
                            {
                                var newTable = new FeatureCollectionTable(mem.Fields.ToList(), mem.GeometryType, mem.SpatialReference);
                                foreach (var f in feats)
                                {
                                    var nf = newTable.CreateFeature();
                                    nf.Geometry = f.Geometry;
                                    foreach (var kv in f.Attributes)
                                        if (kv.Value != null && kv.Value != DBNull.Value) nf.Attributes[kv.Key] = kv.Value;
                                    try { await newTable.AddFeatureAsync(nf); } catch { }
                                }
                                await newTable.LoadAsync();
                                cloned = new FeatureLayer(newTable);
                            }
                        }

                        if (cloned is FeatureLayer cfl)
                        {
                            cfl.Name = fl.Name;
                            CloneRendererToLayer(fl.Renderer, cfl);
                        }
                    }
                    else if (li.Layer is RasterLayer rl)
                    {
                        var raster = rl.Raster;
                        if (raster != null && !string.IsNullOrEmpty(raster.Path) && System.IO.File.Exists(raster.Path))
                        {
                            var newRaster = new Raster(raster.Path);
                            await newRaster.LoadAsync();
                            cloned = new RasterLayer(newRaster);
                        }
                    }

                    if (cloned != null)
                    {
                        cloned.IsVisible = true;
                        targetMap.OperationalLayers.Add(cloned);
                        count++;
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Clone layer '{li.Name}' fail: {ex.GetType().Name}: {ex.Message}");
                }
            }
            return count;
        }

        // ========== MapFrame Off-Screen Renderer ==========
        // Renders a MapFrame to BitmapSource using a hidden MapView.
        // No MapView is placed in the layout — only the resulting Image.
        // targetPixelWidth/targetPixelHeight: Target resolution for rendering. If 0, uses screen resolution.
        private bool IsLatestRender(int seq) => seq == _renderSeq;

        private async System.Threading.Tasks.Task<BitmapSource> RenderMapFrameAsync(LayoutItemData data, int targetPixelWidth = 0, int targetPixelHeight = 0)
        {
            var mainWin = _mainWindow;
            if (mainWin == null) return null;

            var srcMap = mainWin.CurrentMapView?.Map;
            if (srcMap == null) return null;

            int mySeq = ++_renderSeq;

            try
            {
                var mapSr = data.MapWKID != 0 ? SpatialReference.Create(data.MapWKID) : SpatialReferences.WebMercator;

                // White background layer
                var whiteLayer = await CreateWhiteBasemapLayerAsync(mapSr);

                var targetMap = new Map();
                targetMap.OperationalLayers.Add(whiteLayer);

                // Compute combined full-extent from cloned layers and use it as viewpoint
                await CloneLayersFromManagerAsync(mainWin, targetMap);
                if (!IsLatestRender(mySeq)) return null;

                Envelope layerExtent = null;
                foreach (var layer in targetMap.OperationalLayers)
                {
                    try
                    {
                        if (layer is FeatureLayer fl) { await fl.LoadAsync(); if (fl.FullExtent != null) layerExtent = layerExtent == null ? fl.FullExtent : (Envelope)GeometryEngine.Union(layerExtent, fl.FullExtent); }
                        else if (layer is RasterLayer rl) { await rl.LoadAsync(); if (rl.FullExtent != null) layerExtent = layerExtent == null ? rl.FullExtent : (Envelope)GeometryEngine.Union(layerExtent, rl.FullExtent); }
                    }
                    catch { }
                }

                if (layerExtent != null)
                {
                    targetMap.InitialViewpoint = new Viewpoint(layerExtent);
                    // Update data with the actual layer extent
                    data.MapExtentXMin = layerExtent.XMin;
                    data.MapExtentXMax = layerExtent.XMax;
                    data.MapExtentYMin = layerExtent.YMin;
                    data.MapExtentYMax = layerExtent.YMax;
                    data.MapWKID = layerExtent.SpatialReference?.Wkid ?? mapSr.Wkid;
                    data.MapCenterX = layerExtent.GetCenter().X;
                    data.MapCenterY = layerExtent.GetCenter().Y;
                    data.MapScale = 0;
                }
                else
                {
                    // Fallback to stored extent
                    if (data.MapExtentXMax > data.MapExtentXMin && data.MapExtentYMax > data.MapExtentYMin)
                        targetMap.InitialViewpoint = new Viewpoint(new Envelope(data.MapExtentXMin, data.MapExtentYMin, data.MapExtentXMax, data.MapExtentYMax, mapSr));
                    else if (data.MapScale > 0)
                        targetMap.InitialViewpoint = new Viewpoint(new MapPoint(data.MapCenterX, data.MapCenterY, mapSr), data.MapScale);
                }

                await targetMap.LoadAsync();
                if (!IsLatestRender(mySeq)) return null;

                // Target pixel dimensions
                int renderW = targetPixelWidth > 0 ? targetPixelWidth : (int)LayoutMath.MmToPx(data.Width);
                int renderH = targetPixelHeight > 0 ? targetPixelHeight : (int)LayoutMath.MmToPx(data.Height);
                if (renderW < 100) renderW = 400;
                if (renderH < 100) renderH = 300;

                if (!IsLatestRender(mySeq)) return null;

                // Hidden MapView — white background, no basemap
                var mv = new MapView();
                mv.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                mv.Map = targetMap;

                MapOverlayBorder.Visibility = Visibility.Visible;
                MapOverlayBorder.Child = mv;
                mv.Width = renderW;
                mv.Height = renderH;
                mv.UpdateLayout();

                // Force render
                await mv.Map.LoadAsync();
                if (!IsLatestRender(mySeq)) return null;
                await System.Threading.Tasks.Task.Delay(500);
                if (!IsLatestRender(mySeq)) return null;

                var ri = await mv.ExportImageAsync();
                if (ri == null) return null;
                var src = await RuntimeImageExtensions.ToImageSourceAsync(ri);
                var bs = src as BitmapSource;
                if (bs != null)
                {
                    bs = RemoveBottomWatermark(bs);
                    bs.Freeze();
                }
                return bs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RenderMapFrame: {ex.Message}");
                return null;
            }
            finally
            {
                if (IsLatestRender(mySeq))
                {
                    MapOverlayBorder.Child = null;
                    MapOverlayBorder.Visibility = Visibility.Collapsed;
                }
            }
        }

        // Clone all visible layers from source map into target map (empty basemap).
        // Uses file paths (Shapefile.Path, Geodatabase path+tableName) to create independent copies.
        // Returns true if at least one layer was cloned.
        private async System.Threading.Tasks.Task<bool> CloneLayersForExportAsync(Map srcMap, Map targetMap)
        {
            bool anyCloned = false;

            foreach (var layer in srcMap.OperationalLayers)
            {
                if (!layer.IsVisible) continue;

                try
                {
                    Layer newLayer = null;

                    if (layer is FeatureLayer fl)
                    {
                        await fl.LoadAsync();
                        var ft = fl.FeatureTable;
                        if (ft == null) continue;
                        await ft.LoadAsync();

                        if (ft is ShapefileFeatureTable sft)
                        {
                            // Clone shapefile by opening the same file
                            string path = sft.Path;
                            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                            {
                                var newTable = new ShapefileFeatureTable(path);
                                await newTable.LoadAsync();
                                newLayer = new FeatureLayer(newTable);
                            }
                        }
                        else if (ft is GeodatabaseFeatureTable gft)
                        {
                            // Clone geodatabase feature table by opening the GDB + matching table name
                            string gdbPath = gft.Geodatabase?.Path;
                            string tableName = gft.TableName;
                            if (!string.IsNullOrEmpty(gdbPath) && System.IO.File.Exists(gdbPath) && !string.IsNullOrEmpty(tableName))
                            {
                                var gdb = await Geodatabase.OpenAsync(gdbPath);
                                foreach (var t in gdb.GeodatabaseFeatureTables)
                                {
                                    if (t.TableName == tableName)
                                    {
                                        await t.LoadAsync();
                                        newLayer = new FeatureLayer(t);
                                        break;
                                    }
                                }
                            }
                        }
                        else if (ft is FeatureCollectionTable fct)
                        {
                            // Copy features into a new FeatureCollectionTable
                            var query = new QueryParameters();
                            var features = (await fct.QueryFeaturesAsync(query)).ToList();
                            if (features.Count > 0)
                            {
                                var fields = fct.Fields.ToList();
                                var geomType = fct.GeometryType;
                                var sr = fct.SpatialReference;
                                var newTable = new FeatureCollectionTable(fields, geomType, sr);
                                foreach (var f in features)
                                {
                                    var nf = newTable.CreateFeature();
                                    nf.Geometry = f.Geometry;
                                    foreach (var kv in f.Attributes)
                                    {
                                        if (kv.Value != null && kv.Value != DBNull.Value)
                                            nf.Attributes[kv.Key] = kv.Value;
                                    }
                                    try { await newTable.AddFeatureAsync(nf); } catch { }
                                }
                                await newTable.LoadAsync();
                                newLayer = new FeatureLayer(newTable);
                            }
                        }

                        if (newLayer is FeatureLayer newFl)
                        {
                            newFl.Name = fl.Name;
                            newFl.IsVisible = fl.IsVisible;
                            CloneRendererToLayer(fl.Renderer, newFl);
                            targetMap.OperationalLayers.Add(newFl);
                            anyCloned = true;
                        }
                    }
                    else if (layer is FeatureCollectionLayer fcl)
                    {
                        foreach (var t in fcl.FeatureCollection.Tables)
                        {
                            if (t is FeatureCollectionTable fct)
                            {
                                await fct.LoadAsync();
                                var query = new QueryParameters();
                                var features = (await fct.QueryFeaturesAsync(query)).ToList();
                                if (features.Count == 0) continue;

                                var fields = fct.Fields.ToList();
                                var geomType = fct.GeometryType;
                                var sr = fct.SpatialReference;
                                var newTable = new FeatureCollectionTable(fields, geomType, sr);
                                foreach (var f in features)
                                {
                                    var nf = newTable.CreateFeature();
                                    nf.Geometry = f.Geometry;
                                    foreach (var kv in f.Attributes)
                                    {
                                        if (kv.Value != null && kv.Value != DBNull.Value)
                                            nf.Attributes[kv.Key] = kv.Value;
                                    }
                                    try { await newTable.AddFeatureAsync(nf); } catch { }
                                }
                                await newTable.LoadAsync();
                                var newFl = new FeatureLayer(newTable);
                                newFl.Name = t.TableName ?? layer.Name;
                                newFl.IsVisible = true;
                                targetMap.OperationalLayers.Add(newFl);
                                anyCloned = true;
                            }
                        }
                    }
                    else if (layer is RasterLayer rl)
                    {
                        var raster = rl.Raster;
                        if (raster != null && !string.IsNullOrEmpty(raster.Path) && System.IO.File.Exists(raster.Path))
                        {
                            var newRaster = new Raster(raster.Path);
                            await newRaster.LoadAsync();
                            var newRl = new RasterLayer(newRaster);
                            newRl.Name = rl.Name;
                            newRl.IsVisible = true;
                            targetMap.OperationalLayers.Add(newRl);
                            anyCloned = true;
                        }
                    }
                }
                catch (Exception exLayer)
                {
                    System.Diagnostics.Debug.WriteLine($"Clone layer '{layer.Name}' failed: {exLayer.Message}");
                }
            }

            return anyCloned;
        }

        private static void AddGridLabels(GraphicsOverlay ov, double min, double max, double step, double lat, int dec, GridSettings gs, bool isTop)
        {
            for (double lon = min; lon <= max; lon += step)
            {
                var sym = new TextSymbol(FormatCoord(lon, dec), gs.FontColor, gs.FontSize, Esri.ArcGISRuntime.Symbology.HorizontalAlignment.Center, Esri.ArcGISRuntime.Symbology.VerticalAlignment.Middle);
                if (gs.FontBold) sym.FontWeight = Esri.ArcGISRuntime.Symbology.FontWeight.Bold;
                if (gs.FontItalic) sym.FontStyle = Esri.ArcGISRuntime.Symbology.FontStyle.Italic;
                ov.Graphics.Add(new Graphic(new MapPoint(lon, lat, SpatialReferences.Wgs84), sym));
            }
        }

        private static void AddGridLabelsV(GraphicsOverlay ov, double min, double max, double step, double lon, int dec, GridSettings gs, bool isLeft)
        {
            for (double lat = min; lat <= max; lat += step)
            {
                var sym = new TextSymbol(FormatCoord(lat, dec), gs.FontColor, gs.FontSize, Esri.ArcGISRuntime.Symbology.HorizontalAlignment.Center, Esri.ArcGISRuntime.Symbology.VerticalAlignment.Middle);
                if (gs.FontBold) sym.FontWeight = Esri.ArcGISRuntime.Symbology.FontWeight.Bold;
                if (gs.FontItalic) sym.FontStyle = Esri.ArcGISRuntime.Symbology.FontStyle.Italic;
                sym.Angle = 90;
                ov.Graphics.Add(new Graphic(new MapPoint(lon, lat, SpatialReferences.Wgs84), sym));
            }
        }

        private static string FormatCoord(double val, int dec)
        {
            double abs = Math.Abs(val);
            int d = (int)abs;
            double m = (abs - d) * 60;
            if (m < 0.01) return $"{d}°";
            return $"{d}°{m:F0}'";
        }

        private static void CloneRendererToLayer(Renderer renderer, FeatureLayer target)
        {
            if (renderer == null) return;
            if (renderer is SimpleRenderer sr && sr.Symbol != null)
                target.Renderer = new SimpleRenderer(sr.Symbol);
            else if (renderer is UniqueValueRenderer uvr)
            {
                var newUvr = new UniqueValueRenderer();
                foreach (var fn in uvr.FieldNames) newUvr.FieldNames.Add(fn);
                newUvr.DefaultSymbol = uvr.DefaultSymbol;
                newUvr.DefaultLabel = uvr.DefaultLabel;
                foreach (var uv in uvr.UniqueValues)
                    newUvr.UniqueValues.Add(new UniqueValue(uv.Description, uv.Label, uv.Symbol, uv.Values.ToArray()));
                target.Renderer = newUvr;
            }
            else if (renderer is ClassBreaksRenderer cbr)
            {
                var newCbr = new ClassBreaksRenderer();
                newCbr.FieldName = cbr.FieldName;
                newCbr.DefaultSymbol = cbr.DefaultSymbol;
                newCbr.DefaultLabel = cbr.DefaultLabel;
                foreach (var cb in cbr.ClassBreaks)
                {
                    var newCb = new ClassBreak(cb.Description, cb.Label, cb.MinValue, cb.MaxValue, cb.Symbol);
                    newCbr.ClassBreaks.Add(newCb);
                }
                target.Renderer = newCbr;
            }
        }

        private void RefreshLayoutMapView()
        {
            _ = RefreshAllMapFramesAsync();
        }

        private async System.Threading.Tasks.Task RenderAndUpdateMapFrameAsync(LayoutItemData data)
        {
            var bitmap = await RenderMapFrameAsync(data);
            if (bitmap == null) return;

            var border = LayoutCanvas.Children.OfType<Border>().FirstOrDefault(b => (b.Tag as string) == data.Id);
            if (border == null) return;

            var grid = border.Child as WpfGrid;
            if (grid != null)
            {
                var oldContent = grid.Children.OfType<UIElement>().FirstOrDefault(elem =>
                    !((elem as FrameworkElement)?.Tag is string s2 && s2.StartsWith("HANDLE_")));
                if (oldContent != null) grid.Children.Remove(oldContent);

                var (mfb, mfw) = GetMapFrameBorder(data);
                grid.Children.Insert(0, new Border
                {
                    Background = Brushes.White,
                    BorderBrush = mfb,
                    BorderThickness = new Thickness(mfw),
                    Child = new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(2) }
                });
            }
            // Update grid overlay to match the new layer extent
            UpdateMapFrameGridDisplay(data.Id);
        }

        private async System.Threading.Tasks.Task RefreshAllMapFramesAsync()
        {
            int updated = 0;
            var mapFrames = _itemMap.Values.Where(d => d.Type == LayoutItemType.MapFrame).ToList();
            foreach (var data in mapFrames)
            {
                // Re-render and update the Image.Source
                var bitmap = await RenderMapFrameAsync(data);
                var border = LayoutCanvas.Children.OfType<Border>().FirstOrDefault(b => (b.Tag as string) == data.Id);
                if (border == null || bitmap == null) continue;

                var grid = border.Child as WpfGrid;
                if (grid != null)
                {
                    var oldContent = grid.Children.OfType<UIElement>().FirstOrDefault(elem =>
                        !((elem as FrameworkElement)?.Tag is string s2 && s2.StartsWith("HANDLE_")));
                    if (oldContent != null) grid.Children.Remove(oldContent);

                    var (mfb2, mfw2) = GetMapFrameBorder(data);
                    var newContent = new Border
                    {
                        Background = Brushes.White,
                        BorderBrush = mfb2,
                        BorderThickness = new Thickness(mfw2),
                        Child = new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(2) }
                    };
                    grid.Children.Insert(0, newContent);
                    updated++;
                }
                // Update grid overlay to match the new layer extent
                UpdateMapFrameGridDisplay(data.Id);
            }
            UpdateStatus($"已刷新 {updated} 个地图框架");
        }

        /// Creates a white vector basemap layer covering the whole world.
        private static async System.Threading.Tasks.Task<FeatureCollectionLayer> CreateWhiteBasemapLayerAsync(SpatialReference sr)
        {
            var fields = new List<Field>
            {
                new Field(FieldType.Int32, "ID", "ID", 0, null, true, true)
            };
            var schema = new FeatureCollectionTable(fields, GeometryType.Polygon, sr);
            await schema.LoadAsync();

            var wmExtent = new Envelope(-20037508, -20037508, 20037508, 20037508, SpatialReferences.WebMercator);
            var extent = GeometryEngine.Project(wmExtent, sr) as Envelope ?? wmExtent;
            var polygon = new Polygon(new[] {
                new MapPoint(extent.XMin, extent.YMin, sr),
                new MapPoint(extent.XMax, extent.YMin, sr),
                new MapPoint(extent.XMax, extent.YMax, sr),
                new MapPoint(extent.XMin, extent.YMax, sr),
                new MapPoint(extent.XMin, extent.YMin, sr)
            }, sr);

            var feature = schema.CreateFeature();
            feature.Geometry = polygon;
            await schema.AddFeatureAsync(feature);

            // White fill on the table, no outline
            var fillSymbol = new Esri.ArcGISRuntime.Symbology.SimpleFillSymbol(Esri.ArcGISRuntime.Symbology.SimpleFillSymbolStyle.Solid, System.Drawing.Color.White, null);
            schema.Renderer = new Esri.ArcGISRuntime.Symbology.SimpleRenderer(fillSymbol);

            var fc = new FeatureCollection();
            fc.Tables.Add(schema);

            var layer = new FeatureCollectionLayer(fc);
            layer.Name = "White Basemap";

            await layer.LoadAsync();
            return layer;
        }

        /// Removes the "Licensed For Developer Use Only" watermark by painting
        /// the bottom 25px rows of the bitmap white.
        private static BitmapSource RemoveBottomWatermark(BitmapSource source)
        {
            if (source == null) return null;
            try
            {
                int watermarkHeight = 25;
                if (source.Height <= watermarkHeight) return source;

                var fmt = PixelFormats.Bgr32;
                int stride = (int)source.Width * (fmt.BitsPerPixel / 8);
                int height = (int)source.Height;
                byte[] pixels = new byte[stride * height];
                source.CopyPixels(pixels, stride, 0);

                // Paint bottom rows white
                int firstWatermarkRow = height - watermarkHeight;
                for (int row = firstWatermarkRow; row < height; row++)
                {
                    int rowStart = row * stride;
                    for (int col = 0; col < stride; col += 4)
                    {
                        pixels[rowStart + col] = 255;     // B
                        pixels[rowStart + col + 1] = 255; // G
                        pixels[rowStart + col + 2] = 255; // R
                        pixels[rowStart + col + 3] = 255; // A
                    }
                }

                var bs = BitmapSource.Create(
                    (int)source.Width, height,
                    source.DpiX, source.DpiY,
                    fmt, null, pixels, stride);
                return bs;
            }
            catch
            {
                return source;
            }
        }

        // ========== Sync Helpers ==========
        private void SyncItemFromElement(Border border)
        {
            string id = border.Tag as string;
            if (id == null || !_itemMap.TryGetValue(id, out var data)) return;
            data.X = LayoutMath.PxToMm(Canvas.GetLeft(border));
            data.Y = LayoutMath.PxToMm(Canvas.GetTop(border));
            data.Width = LayoutMath.PxToMm(border.Width);
            data.Height = LayoutMath.PxToMm(border.Height);
        }

        private void SyncDocFromCanvas()
        {
            _doc.Items.Clear();
            foreach (var child in LayoutCanvas.Children)
            {
                if (child is Border border)
                {
                    string id = border.Tag as string;
                    if (id != null && _itemMap.TryGetValue(id, out var data))
                    {
                        SyncItemFromElement(border);
                        _doc.Items.Add(data);
                    }
                }
            }
        }

        private void RebuildItem(LayoutItemData data)
        {
            // Find and replace the UI element for this data
            var existing = LayoutCanvas.Children.OfType<Border>().FirstOrDefault(b => (b.Tag as string) == data.Id);
            if (existing == null) return;

            int z = Panel.GetZIndex(existing);
            var newBorder = CreateItemBorder(data, null);
            // For map and image items, keep the old bitmap
            var oldGrid = existing.Child as WpfGrid;
            var oldContent = oldGrid?.Children.OfType<UIElement>().FirstOrDefault(e => !((e as FrameworkElement)?.Tag is string s && s.StartsWith("HANDLE_")));
            var newGrid = newBorder.Child as WpfGrid;
            if (newGrid != null && oldContent != null && (data.Type == LayoutItemType.MapFrame || data.Type == LayoutItemType.ImageItem))
            {
                newGrid.Children.Insert(0, oldContent);
            }

            int idx = LayoutCanvas.Children.IndexOf(existing);
            LayoutCanvas.Children.RemoveAt(idx);
            LayoutCanvas.Children.Insert(idx, newBorder);
            Panel.SetZIndex(newBorder, z);

            if (_selectedBorder == existing) SelectItem(newBorder);
        }

        private void RebuildAllItems()
        {
            foreach (var data in _doc.Items)
            {
                var existing = LayoutCanvas.Children.OfType<Border>().FirstOrDefault(b => (b.Tag as string) == data.Id);
                if (existing != null)
                {
                    Canvas.SetLeft(existing, LayoutMath.MmToPx(data.X));
                    Canvas.SetTop(existing, LayoutMath.MmToPx(data.Y));
                    existing.Width = LayoutMath.MmToPx(data.Width);
                    existing.Height = LayoutMath.MmToPx(data.Height);
                }
            }
        }

        private void LoadFromDocument(LayoutDocument doc)
        {
            NewLayout();
            _doc.PageWidthMm = doc.PageWidthMm;
            _doc.PageHeightMm = doc.PageHeightMm;
            _doc.Dpi = doc.Dpi;
            _doc.Title = doc.Title;
            ApplyPageSize(false);

            foreach (var itemData in doc.Items)
            {
                _itemMap[itemData.Id] = itemData;
                var border = CreateItemBorder(itemData, null);
                LayoutCanvas.Children.Add(border);
            }
            TxtHint.Visibility = LayoutCanvas.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ========== Refresh Buttons ==========
        private void BtnRefreshMap_Click(object sender, RoutedEventArgs e)
        {
            RefreshLayoutMapView();
            UpdateStatus("已刷新地图视图");
        }

        private void BtnUpdateLegend_Click(object sender, RoutedEventArgs e)
        {
            int updated = 0;
            foreach (var data in _itemMap.Values.Where(d => d.Type == LayoutItemType.Legend).ToList())
            {
                var border = LayoutCanvas.Children.OfType<Border>().FirstOrDefault(b => (b.Tag as string) == data.Id);
                if (border != null)
                {
                    var grid = border.Child as WpfGrid;
                    if (grid != null)
                    {
                        var oldContent = grid.Children.OfType<UIElement>().FirstOrDefault(elem =>
                            !((elem as FrameworkElement)?.Tag is string s2 && s2.StartsWith("HANDLE_")));
                        if (oldContent != null) grid.Children.Remove(oldContent);
                        grid.Children.Insert(0, CreateLegendContent(data));
                    }
                    updated++;
                }
            }
            UpdateStatus($"已更新 {updated} 个图例");
        }

        private void BtnRefreshAll_Click(object sender, RoutedEventArgs e)
        {
            BtnRefreshMap_Click(sender, e);
            BtnUpdateLegend_Click(sender, e);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _mainWindow = null;
        }

        // ========== MapFrame Property Panel Events ==========
        private void TxtMapFrameProp_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingProps || _selectedBorder == null) return;
            string id = _selectedBorder.Tag as string;
            if (id == null || !_itemMap.TryGetValue(id, out var data)) return;
            if (data.Type != LayoutItemType.MapFrame) return;

            if (double.TryParse(TxtMapScale.Text, out double ms)) data.MapScale = ms;
            if (double.TryParse(TxtMapExtentXMin.Text, out double xmin)) data.MapExtentXMin = xmin;
            if (double.TryParse(TxtMapExtentYMin.Text, out double ymin)) data.MapExtentYMin = ymin;
            if (double.TryParse(TxtMapExtentXMax.Text, out double xmax)) data.MapExtentXMax = xmax;
            if (double.TryParse(TxtMapExtentYMax.Text, out double ymax)) data.MapExtentYMax = ymax;
            // Border properties
            if (ClrMapFrameBorder.Background is SolidColorBrush mfb)
                data.Props["MapFrameBorderColor"] = $"#{mfb.Color.R:X2}{mfb.Color.G:X2}{mfb.Color.B:X2}";
            data.Props["MapFrameBorderWidth"] = TxtMapFrameBorderWidth.Text;

            UpdateExtentCenter(data);
            _ = RenderAndUpdateMapFrameAsync(data);
            UpdatePropertyPanel();
        }

        private void BtnMapFrameBorderColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog();
            if (ClrMapFrameBorder.Background is SolidColorBrush mfb) dlg.Color = System.Drawing.Color.FromArgb(mfb.Color.R, mfb.Color.G, mfb.Color.B);
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ClrMapFrameBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
                TxtMapFrameProp_LostFocus(sender, e);
            }
        }

        private static (Brush brush, double thickness) GetMapFrameBorder(LayoutItemData data)
        {
            string bc = "#808080"; double bw = 1;
            if (data.Props.TryGetValue("MapFrameBorderColor", out var bcv)) bc = bcv;
            if (data.Props.TryGetValue("MapFrameBorderWidth", out var bwv)) double.TryParse(bwv, out bw);
            return (ParseBrush(bc), bw);
        }

        private void BtnMapZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ZoomMapFrame(0.5);
        }

        private void BtnMapZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ZoomMapFrame(2.0);
        }

        private void BtnMapRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBorder != null)
            {
                string id = _selectedBorder.Tag as string;
                if (id != null && _itemMap.TryGetValue(id, out var data) && data.Type == LayoutItemType.MapFrame)
                {
                    _ = RenderAndUpdateMapFrameAsync(data);
                    UpdateStatus("已刷新地图框架");
                }
            }
        }

        private void BtnMapPanUp_Click(object sender, RoutedEventArgs e)
        {
            PanMapFrame(0, -0.1);
        }

        private void BtnMapPanDown_Click(object sender, RoutedEventArgs e)
        {
            PanMapFrame(0, 0.1);
        }

        private void BtnMapPanLeft_Click(object sender, RoutedEventArgs e)
        {
            PanMapFrame(-0.1, 0);
        }

        private void BtnMapPanRight_Click(object sender, RoutedEventArgs e)
        {
            PanMapFrame(0.1, 0);
        }

        private void ZoomMapFrame(double factor)
        {
            if (_selectedBorder == null) return;
            string id = _selectedBorder.Tag as string;
            if (id == null || !_itemMap.TryGetValue(id, out var data) || data.Type != LayoutItemType.MapFrame) return;

            if (data.MapExtentXMax > data.MapExtentXMin && data.MapExtentYMax > data.MapExtentYMin)
            {
                double cx = (data.MapExtentXMin + data.MapExtentXMax) / 2;
                double cy = (data.MapExtentYMin + data.MapExtentYMax) / 2;
                double w = (data.MapExtentXMax - data.MapExtentXMin) * factor;
                double h = (data.MapExtentYMax - data.MapExtentYMin) * factor;
                data.MapExtentXMin = cx - w / 2;
                data.MapExtentYMin = cy - h / 2;
                data.MapExtentXMax = cx + w / 2;
                data.MapExtentYMax = cy + h / 2;
                data.MapScale = data.MapScale / factor;
                UpdateExtentCenter(data);
                _ = RenderAndUpdateMapFrameAsync(data);
                UpdatePropertyPanel();
            }
        }

        private void PanMapFrame(double dxRatio, double dyRatio)
        {
            if (_selectedBorder == null) return;
            string id = _selectedBorder.Tag as string;
            if (id == null || !_itemMap.TryGetValue(id, out var data) || data.Type != LayoutItemType.MapFrame) return;

            if (data.MapExtentXMax > data.MapExtentXMin && data.MapExtentYMax > data.MapExtentYMin)
            {
                double dx = (data.MapExtentXMax - data.MapExtentXMin) * dxRatio;
                double dy = (data.MapExtentYMax - data.MapExtentYMin) * dyRatio;
                data.MapExtentXMin += dx;
                data.MapExtentYMin += dy;
                data.MapExtentXMax += dx;
                data.MapExtentYMax += dy;
                UpdateExtentCenter(data);
                _ = RenderAndUpdateMapFrameAsync(data);
                UpdatePropertyPanel();
            }
        }

        private void UpdateExtentCenter(LayoutItemData data)
        {
            if (data.MapExtentXMax > data.MapExtentXMin && data.MapExtentYMax > data.MapExtentYMin)
            {
                data.MapCenterX = (data.MapExtentXMin + data.MapExtentXMax) / 2;
                data.MapCenterY = (data.MapExtentYMin + data.MapExtentYMax) / 2;
            }
        }
    }
}
