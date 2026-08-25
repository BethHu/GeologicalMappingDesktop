using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI.Controls;
using Esri.ArcGISRuntime.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GeologicalMappingDesktop
{
    public enum TopologyErrorType
    {
        DanglingNode,
        Overlap,
        Gap,
        SelfIntersection
    }

    public enum ErrorLevel
    {
        Warning,
        Error,
        Critical
    }

    public class TopologyError
    {
        public TopologyErrorType ErrorType { get; set; }
        public ErrorLevel Level { get; set; }
        public string LayerName { get; set; }
        public string Description { get; set; }
        public Esri.ArcGISRuntime.Geometry.Geometry Geometry { get; set; }
        public Esri.ArcGISRuntime.Geometry.MapPoint Location { get; set; }
        public Feature Feature1 { get; set; }
        public Feature Feature2 { get; set; }
        public bool IsIgnored { get; set; }
        public string IgnoreReason { get; set; }

        public string ErrorTypeText
        {
            get
            {
                switch (ErrorType)
                {
                    case TopologyErrorType.DanglingNode: return "悬挂点";
                    case TopologyErrorType.Overlap: return "重叠";
                    case TopologyErrorType.Gap: return "缝隙";
                    case TopologyErrorType.SelfIntersection: return "自相交";
                    default: return "未知";
                }
            }
        }

        public Brush ErrorLevelColor
        {
            get
            {
                switch (Level)
                {
                    case ErrorLevel.Warning: return new SolidColorBrush(Colors.Yellow);
                    case ErrorLevel.Error: return new SolidColorBrush(Colors.Orange);
                    case ErrorLevel.Critical: return new SolidColorBrush(Colors.Red);
                    default: return new SolidColorBrush(Colors.Gray);
                }
            }
        }

        public string GetErrorHash()
        {
            string hash = $"{ErrorType}_{LayerName}";
            if (Location != null)
            {
                hash += $"_{Location.X:F2}_{Location.Y:F2}";
            }
            return hash;
        }
    }

    public partial class TopologyCheckPanel : UserControl
    {
        private MapView _mapView;
        private GraphicsOverlay _errorOverlay;
        private ObservableCollection<TopologyError> _errors = new ObservableCollection<TopologyError>();
        private List<string> _ignoredErrors = new List<string>();
        private double _tolerance = 0.001;
        private string _ignoreFilePath;

        public TopologyCheckPanel()
        {
            InitializeComponent();
            ErrorDataGrid.ItemsSource = _errors;
            CboCheckScope.SelectedIndex = 0;
        }

        public void Initialize(MapView mapView1, MapView mapView2)
        {
            _mapView = mapView1;
            _errorOverlay = new GraphicsOverlay();
            mapView1.GraphicsOverlays.Add(_errorOverlay);
            // 第二个视图的叠加层：不能直接复用 _errorOverlay 的 Graphic 实例
            // （ArcGIS Runtime 中一个 Graphic 只能属于一个 owning collection，
            //  直接 Add 同一实例会抛 "Graphic cannot be added - it is already in an owning collection"）。
            // 改为：同步时为新叠加层创建副本 Graphic。
            var overlay2 = new GraphicsOverlay();
            mapView2.GraphicsOverlays.Add(overlay2);
            _errorOverlay.Graphics.CollectionChanged += (s, e) =>
            {
                overlay2.Graphics.Clear();
                foreach (var g in _errorOverlay.Graphics)
                {
                    overlay2.Graphics.Add(new Graphic(g.Geometry, g.Symbol));
                }
            };

            string projectDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _ignoreFilePath = Path.Combine(projectDir, "GeologicalMapping", "topology_ignores.txt");
            LoadIgnoredErrors();
        }

        private void LoadIgnoredErrors()
        {
            try
            {
                if (File.Exists(_ignoreFilePath))
                {
                    string content = File.ReadAllText(_ignoreFilePath);
                    _ignoredErrors = content.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            }
            catch
            {
                _ignoredErrors = new List<string>();
            }
        }

        private void SaveIgnoredErrors()
        {
            try
            {
                string dir = Path.GetDirectoryName(_ignoreFilePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllLines(_ignoreFilePath, _ignoredErrors);
            }
            catch
            {
            }
        }

        private bool IsErrorIgnored(TopologyError error)
        {
            return _ignoredErrors.Contains(error.GetErrorHash());
        }

        private void AddIgnoredError(TopologyError error)
        {
            string hash = error.GetErrorHash();
            if (!_ignoredErrors.Contains(hash))
            {
                _ignoredErrors.Add(hash);
                SaveIgnoredErrors();
            }
        }

        private void CboCheckScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private double GetTolerance()
        {
            if (double.TryParse(TxtTolerance.Text, out double value) && value > 0)
            {
                _tolerance = value;
                return value;
            }
            return 0.001;
        }

        private List<Feature> GetFeaturesToCheck()
        {
            List<Feature> features = new List<Feature>();
            ComboBoxItem selectedItem = CboCheckScope.SelectedItem as ComboBoxItem;
            string scope = selectedItem?.Tag?.ToString() ?? "AllLayers";

            foreach (var layer in _mapView.Map.OperationalLayers)
            {
                FeatureTable table = GetFeatureTableFromLayer(layer);
                if (table != null)
                {
                    try
                    {
                        QueryParameters qp = new QueryParameters();

                        if (scope == "SelectedArea" && _mapView.VisibleArea != null)
                        {
                            qp.Geometry = _mapView.VisibleArea;
                            qp.SpatialRelationship = SpatialRelationship.Intersects;
                        }

                        var queryResult = table.QueryFeaturesAsync(qp).Result;
                        foreach (var feature in queryResult)
                        {
                            features.Add(feature);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return features;
        }

        private FeatureTable GetFeatureTableFromLayer(Layer layer)
        {
            if (layer is FeatureLayer featureLayer)
                return featureLayer.FeatureTable;
            else if (layer is FeatureCollectionLayer collectionLayer && collectionLayer.FeatureCollection?.Tables.Count > 0)
                return collectionLayer.FeatureCollection.Tables[0];
            return null;
        }

        private void BtnCheckDangling_Click(object sender, RoutedEventArgs e)
        {
            CheckDanglingNodes();
        }

        private void BtnCheckOverlap_Click(object sender, RoutedEventArgs e)
        {
            CheckOverlaps();
        }

        private void BtnCheckGap_Click(object sender, RoutedEventArgs e)
        {
            CheckGaps();
        }

        private void BtnCheckSelfIntersect_Click(object sender, RoutedEventArgs e)
        {
            CheckSelfIntersections();
        }

        private void CheckDanglingNodes()
        {
            ClearHighlights();
            List<Feature> features = GetFeaturesToCheck();
            double tolerance = GetTolerance();
            List<Esri.ArcGISRuntime.Geometry.Polyline> allLines = new List<Esri.ArcGISRuntime.Geometry.Polyline>();
            List<TopologyError> newErrors = new List<TopologyError>();

            foreach (var feature in features)
            {
                if (feature.Geometry is Esri.ArcGISRuntime.Geometry.Polyline polyline)
                {
                    allLines.Add(polyline);
                }
            }

            Esri.ArcGISRuntime.Geometry.Envelope mapExtent = _mapView.VisibleArea?.Extent;
            double buffer = tolerance * 10;

            foreach (var feature in features)
            {
                if (feature.Geometry is Esri.ArcGISRuntime.Geometry.Polyline polyline)
                    {
                        foreach (var part in polyline.Parts)
                        {
                            if (part.Points.Count == 0) continue;

                            Esri.ArcGISRuntime.Geometry.MapPoint startPoint = part.Points[0];
                            Esri.ArcGISRuntime.Geometry.MapPoint endPoint = part.Points[part.Points.Count - 1];

                        CheckPointForDangling(startPoint, polyline, allLines, tolerance, buffer, mapExtent, feature, newErrors);
                        CheckPointForDangling(endPoint, polyline, allLines, tolerance, buffer, mapExtent, feature, newErrors);
                    }
                }
            }

            AddErrors(newErrors);
            ShowResults("悬挂点检查完成");
        }

        private void CheckPointForDangling(Esri.ArcGISRuntime.Geometry.MapPoint point, Esri.ArcGISRuntime.Geometry.Polyline line, List<Esri.ArcGISRuntime.Geometry.Polyline> allLines, 
            double tolerance, double buffer, Esri.ArcGISRuntime.Geometry.Envelope mapExtent, Feature feature, List<TopologyError> errors)
        {
            if (mapExtent != null)
            {
                Esri.ArcGISRuntime.Geometry.Geometry bufferedGeom = GeometryEngine.Buffer(mapExtent, -buffer);
                Esri.ArcGISRuntime.Geometry.Envelope bufferedExtent = bufferedGeom as Esri.ArcGISRuntime.Geometry.Envelope;
                if (bufferedExtent != null && !GeometryEngine.Contains(bufferedExtent, point))
                {
                    return;
                }
            }

            bool foundNearby = false;
            foreach (var otherLine in allLines)
            {
                if (otherLine == line) continue;

                double distance = GeometryEngine.Distance(point, otherLine);
                if (distance <= tolerance)
                {
                    foundNearby = true;
                    break;
                }
            }

            if (!foundNearby)
            {
                bool isFarAway = true;
                foreach (var otherLine in allLines)
                {
                    if (otherLine == line) continue;

                    double distance = GeometryEngine.Distance(point, otherLine);
                    if (distance <= buffer)
                    {
                        isFarAway = false;
                        break;
                    }
                }

                if (!isFarAway)
                {
                    TopologyError error = new TopologyError
                    {
                        ErrorType = TopologyErrorType.DanglingNode,
                        Level = ErrorLevel.Warning,
                        LayerName = feature.FeatureTable?.TableName ?? "未知",
                        Description = $"线端点未吸附，距最近线距离小于{buffer}米",
                        Geometry = point,
                        Location = point,
                        Feature1 = feature
                    };

                    if (!IsErrorIgnored(error))
                    {
                        errors.Add(error);
                    }
                }
            }
        }

        private void CheckOverlaps()
        {
            ClearHighlights();
            List<Feature> features = GetFeaturesToCheck();
            List<TopologyError> newErrors = new List<TopologyError>();

            List<Feature> lines = features.Where(f => f.Geometry is Esri.ArcGISRuntime.Geometry.Polyline).ToList();
            List<Feature> polygons = features.Where(f => f.Geometry is Esri.ArcGISRuntime.Geometry.Polygon).ToList();

            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    CheckLineOverlap(lines[i], lines[j], newErrors);
                }
            }

            for (int i = 0; i < polygons.Count; i++)
            {
                for (int j = i + 1; j < polygons.Count; j++)
                {
                    CheckPolygonOverlap(polygons[i], polygons[j], newErrors);
                }
            }

            AddErrors(newErrors);
            ShowResults("重叠检查完成");
        }

        private void CheckLineOverlap(Feature line1, Feature line2, List<TopologyError> errors)
        {
            try
            {
                Esri.ArcGISRuntime.Geometry.Geometry intersection = GeometryEngine.Intersection(line1.Geometry, line2.Geometry);
                if (intersection != null && !intersection.IsEmpty)
                {
                    if (intersection is Esri.ArcGISRuntime.Geometry.Polyline intersectLine)
                    {
                        double length = GeometryEngine.Length(intersectLine);
                        if (length > 0.0001)
                        {
                            TopologyError error = new TopologyError
                            {
                                ErrorType = TopologyErrorType.Overlap,
                                Level = ErrorLevel.Error,
                                LayerName = line1.FeatureTable?.TableName ?? "未知",
                                Description = $"线重叠，重叠长度: {length:F4}米",
                                Geometry = intersectLine,
                                Location = GetCentroid(intersectLine),
                                Feature1 = line1,
                                Feature2 = line2
                            };

                            if (!IsErrorIgnored(error))
                            {
                                errors.Add(error);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void CheckPolygonOverlap(Feature poly1, Feature poly2, List<TopologyError> errors)
        {
            try
            {
                Esri.ArcGISRuntime.Geometry.Geometry intersection = GeometryEngine.Intersection(poly1.Geometry, poly2.Geometry);
                if (intersection != null && !intersection.IsEmpty)
                {
                    if (intersection is Esri.ArcGISRuntime.Geometry.Polygon intersectPoly)
                    {
                        double area = Math.Abs(GeometryEngine.Area(intersectPoly));
                        if (area > 0.0001)
                        {
                            TopologyError error = new TopologyError
                            {
                                ErrorType = TopologyErrorType.Overlap,
                                Level = ErrorLevel.Error,
                                LayerName = poly1.FeatureTable?.TableName ?? "未知",
                                Description = $"面重叠，重叠面积: {area:F4}平方米",
                                Geometry = intersectPoly,
                                Location = GetCentroid(intersectPoly),
                                Feature1 = poly1,
                                Feature2 = poly2
                            };

                            if (!IsErrorIgnored(error))
                            {
                                errors.Add(error);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void CheckGaps()
        {
            ClearHighlights();
            List<Feature> features = GetFeaturesToCheck();
            List<Esri.ArcGISRuntime.Geometry.Polygon> polygons = new List<Esri.ArcGISRuntime.Geometry.Polygon>();
            Dictionary<string, Feature> layerFeatureMap = new Dictionary<string, Feature>();
            foreach (var f in features)
            {
                if (f.Geometry is Esri.ArcGISRuntime.Geometry.Polygon p)
                {
                    polygons.Add(p);
                    string key = $"{p.ToJson()}";
                    if (!layerFeatureMap.ContainsKey(key))
                        layerFeatureMap[key] = f;
                }
            }
            List<TopologyError> newErrors = new List<TopologyError>();

            if (polygons.Count < 2)
            {
                MessageBox.Show("需要至少2个面要素才能检查缝隙", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // 计算所有面的并集
                Esri.ArcGISRuntime.Geometry.Geometry union = polygons[0];
                for (int i = 1; i < polygons.Count; i++)
                {
                    union = GeometryEngine.Union(union, polygons[i]);
                }

                if (union is Esri.ArcGISRuntime.Geometry.Polygon unionPoly)
                {
                    // 计算所有面的总外接矩形
                    Esri.ArcGISRuntime.Geometry.Envelope extent = GeometryEngine.CombineExtents(polygons);

                    // 从外接矩形中减去并集 -> 得到缝隙区域
                    Esri.ArcGISRuntime.Geometry.Geometry gapGeometry = GeometryEngine.Difference(extent, unionPoly);
                    if (gapGeometry != null && !gapGeometry.IsEmpty)
                    {
                        // 缝隙可能由多个不相连的部分组成
                        List<Esri.ArcGISRuntime.Geometry.Polygon> gapParts = new List<Esri.ArcGISRuntime.Geometry.Polygon>();
                        if (gapGeometry is Esri.ArcGISRuntime.Geometry.Polygon singleGap)
                        {
                            gapParts.Add(singleGap);
                        }
                        else if (gapGeometry is Esri.ArcGISRuntime.Geometry.Multipart multipartGap)
                        {
                            foreach (var part in multipartGap.Parts)
                            {
                                if (part.Points.Count >= 3)
                                {
                                    try
                                    {
                                        var builder = new Esri.ArcGISRuntime.Geometry.PolygonBuilder(part);
                                        var gapPart = builder.ToGeometry();
                                        if (gapPart != null && !gapPart.IsEmpty)
                                            gapParts.Add(gapPart as Esri.ArcGISRuntime.Geometry.Polygon);
                                    }
                                    catch { }
                                }
                            }
                        }

                        double tolerance = GetTolerance();
                        foreach (var gap in gapParts)
                        {
                            if (gap == null) continue;
                            double area = Math.Abs(GeometryEngine.Area(gap));
                            if (area < 1e-10) continue; // 忽略太小无法显示的缝隙

                            TopologyError error = new TopologyError
                            {
                                ErrorType = TopologyErrorType.Gap,
                                Level = area < tolerance ? ErrorLevel.Warning : ErrorLevel.Error,
                                LayerName = features.FirstOrDefault()?.FeatureTable?.TableName ?? "未知",
                                Description = area < tolerance
                                    ? $"微小缝隙，面积: {area:F6}平方米"
                                    : $"间隙，面积: {area:F4}平方米",
                                Geometry = gap,
                                Location = GetCentroid(gap)
                            };

                            if (!IsErrorIgnored(error))
                            {
                                newErrors.Add(error);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"缝隙检查失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            AddErrors(newErrors);
            ShowResults("缝隙检查完成");
        }

        private void CheckSelfIntersections()
        {
            ClearHighlights();
            List<Feature> features = GetFeaturesToCheck();
            List<TopologyError> newErrors = new List<TopologyError>();

            foreach (var feature in features)
            {
                if (feature.Geometry is Esri.ArcGISRuntime.Geometry.Polyline polyline)
                {
                    if (!GeometryEngine.IsSimple(polyline))
                    {
                        TopologyError error = new TopologyError
                        {
                            ErrorType = TopologyErrorType.SelfIntersection,
                            Level = ErrorLevel.Critical,
                            LayerName = feature.FeatureTable?.TableName ?? "未知",
                            Description = "线自相交（打结）",
                            Geometry = polyline,
                            Location = GetCentroid(polyline),
                            Feature1 = feature
                        };

                        if (!IsErrorIgnored(error))
                        {
                            newErrors.Add(error);
                        }
                    }
                }
                else if (feature.Geometry is Esri.ArcGISRuntime.Geometry.Polygon polygon)
                {
                    if (!GeometryEngine.IsSimple(polygon))
                    {
                        TopologyError error = new TopologyError
                        {
                            ErrorType = TopologyErrorType.SelfIntersection,
                            Level = ErrorLevel.Critical,
                            LayerName = feature.FeatureTable?.TableName ?? "未知",
                            Description = "面边界自相交",
                            Geometry = polygon,
                            Location = GetCentroid(polygon),
                            Feature1 = feature
                        };

                        if (!IsErrorIgnored(error))
                        {
                            newErrors.Add(error);
                        }
                    }
                }
            }

            AddErrors(newErrors);
            ShowResults("自相交检查完成");
        }

        private Esri.ArcGISRuntime.Geometry.MapPoint GetCentroid(Esri.ArcGISRuntime.Geometry.Geometry geometry)
        {
            try
            {
                if (geometry is Esri.ArcGISRuntime.Geometry.MapPoint point) return point;
                if (geometry is Esri.ArcGISRuntime.Geometry.Polyline line)
                {
                    if (line.Parts.Count > 0 && line.Parts[0].Points.Count > 0)
                    {
                        int midIndex = line.Parts[0].Points.Count / 2;
                        return line.Parts[0].Points[midIndex];
                    }
                }
                if (geometry is Esri.ArcGISRuntime.Geometry.Polygon poly)
                {
                    return GeometryEngine.LabelPoint(poly) as Esri.ArcGISRuntime.Geometry.MapPoint;
                }
            }
            catch
            {
            }
            return null;
        }

        private void AddErrors(List<TopologyError> errors)
        {
            foreach (var error in errors)
            {
                _errors.Add(error);
            }
            UpdateErrorCount();
        }

        private void ShowResults(string message)
        {
            int count = _errors.Count;
            if (count > 0)
            {
                MessageBox.Show($"{message}，共发现 {count} 个问题", "拓扑检查结果", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"{message}，未发现问题", "拓扑检查结果", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateErrorCount()
        {
            TxtErrorCount.Text = $"({_errors.Count})";
        }

        private void ClearHighlights()
        {
            if (_errorOverlay != null)
            {
                _errorOverlay.Graphics.Clear();
            }
        }

        private void HighlightError(TopologyError error)
        {
            ClearHighlights();

            if (error.Geometry == null) return;

            Esri.ArcGISRuntime.Symbology.Symbol symbol = null;
            switch (error.ErrorType)
            {
                case TopologyErrorType.DanglingNode:
                    symbol = new Esri.ArcGISRuntime.Symbology.SimpleMarkerSymbol(
                        Esri.ArcGISRuntime.Symbology.SimpleMarkerSymbolStyle.Circle, 
                        System.Drawing.Color.Red, 15);
                    break;
                case TopologyErrorType.Overlap:
                    symbol = new Esri.ArcGISRuntime.Symbology.SimpleFillSymbol(
                        Esri.ArcGISRuntime.Symbology.SimpleFillSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(150, System.Drawing.Color.Yellow),
                        new Esri.ArcGISRuntime.Symbology.SimpleLineSymbol(
                            Esri.ArcGISRuntime.Symbology.SimpleLineSymbolStyle.Solid,
                            System.Drawing.Color.Yellow, 2));
                    break;
                case TopologyErrorType.Gap:
                    symbol = new Esri.ArcGISRuntime.Symbology.SimpleFillSymbol(
                        Esri.ArcGISRuntime.Symbology.SimpleFillSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(150, System.Drawing.Color.Blue),
                        new Esri.ArcGISRuntime.Symbology.SimpleLineSymbol(
                            Esri.ArcGISRuntime.Symbology.SimpleLineSymbolStyle.Solid,
                            System.Drawing.Color.Blue, 2));
                    break;
                case TopologyErrorType.SelfIntersection:
                    symbol = new Esri.ArcGISRuntime.Symbology.SimpleLineSymbol(
                        Esri.ArcGISRuntime.Symbology.SimpleLineSymbolStyle.Solid,
                        System.Drawing.Color.Red, 3);
                    break;
            }

            if (symbol != null)
            {
                _errorOverlay.Graphics.Add(new Graphic(error.Geometry, symbol));
            }
        }

        private void ErrorDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ErrorDataGrid.SelectedItem is TopologyError error)
            {
                HighlightError(error);
            }
        }

        private void BtnLocate_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn?.CommandParameter is TopologyError error)
            {
                if (error.Location != null)
                {
                    _mapView.SetViewpointAsync(new Viewpoint(error.Location, 50));
                    HighlightError(error);
                }
            }
        }

        private void BtnIgnore_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn?.CommandParameter is TopologyError error)
            {
                var result = MessageBox.Show("确定要忽略此错误吗？下次检查将跳过。", "确认忽略",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    AddIgnoredError(error);
                    error.IsIgnored = true;
                    _errors.Remove(error);
                    UpdateErrorCount();
                }
            }
        }

        private void BtnFix_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn?.CommandParameter is TopologyError error)
            {
                var result = MessageBox.Show("自动修复功能尚未实现，是否手动定位并修复？", "提示",
                    MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                {
                    BtnLocate_Click(sender, e);
                }
            }
        }

        private void BtnExportCSV_Click(object sender, RoutedEventArgs e)
        {
            if (_errors.Count == 0)
            {
                MessageBox.Show("没有可导出的错误数据", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Microsoft.Win32.SaveFileDialog saveDialog = new Microsoft.Win32.SaveFileDialog();
            saveDialog.Filter = "CSV文件 (*.csv)|*.csv";
            saveDialog.Title = "导出拓扑检查报告";

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using (StreamWriter writer = new StreamWriter(saveDialog.FileName, false, System.Text.Encoding.UTF8))
                    {
                        writer.WriteLine("错误类型,图层名称,描述,级别,X坐标,Y坐标");

                        foreach (var error in _errors)
                        {
                            string x = error.Location?.X.ToString("F6") ?? "";
                            string y = error.Location?.Y.ToString("F6") ?? "";
                            string level = error.Level.ToString();

                            writer.WriteLine($"{error.ErrorTypeText},{error.LayerName},{error.Description},{level},{x},{y}");
                        }
                    }

                    MessageBox.Show("报告导出成功！", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            _errors.Clear();
            ClearHighlights();
            UpdateErrorCount();
        }
    }
}