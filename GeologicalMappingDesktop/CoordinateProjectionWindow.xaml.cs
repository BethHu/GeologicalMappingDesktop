using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;

namespace GeologicalMappingDesktop
{
    public partial class CoordinateProjectionWindow : Window
    {
        public class SrItem
        {
            public int Wkid { get; set; }
            public string DisplayName { get; set; }
            public string SearchText { get; set; }
            public SpatialReference SpatialReference
            {
                get
                {
                    try { return Wkid == 0 ? null : SpatialReference.Create(Wkid); }
                    catch { return null; }
                }
            }
            public override string ToString() => DisplayName ?? $"EPSG:{Wkid}";
        }

        public class LayerOption
        {
            public string Display { get; set; }
            public string LayerName { get; set; }
            public int Wkid { get; set; }
            public override string ToString() => Display ?? LayerName ?? $"图层(WKID:{Wkid})";
        }

        private readonly ObservableCollection<SrItem> _allItems = new ObservableCollection<SrItem>();
        private readonly ObservableCollection<SrItem> _filteredItems = new ObservableCollection<SrItem>();
        private readonly List<LayerOption> _layerOptions;
        private readonly Func<string, Task<FeatureTable>> _getTableAsync;
        private readonly Func<string, SpatialReference, Task> _addLayerFromFileAsync;

        private FeatureTable _sourceTable;
        private SpatialReference _sourceSr;
        private bool _isFileSource;
        private string _sourceFilePath;

        /// <summary>目标坐标系</summary>
        public SpatialReference TargetSpatialReference { get; private set; }

        /// <summary>源图层名称（文件源时为null）</summary>
        public string SourceLayerName { get; private set; }

        /// <summary>源文件路径（图层源时为null）</summary>
        public string SourceFilePath => _sourceFilePath;

        public CoordinateProjectionWindow(
            List<LayerOption> layerOptions,
            Func<string, Task<FeatureTable>> getTableAsync,
            Func<string, SpatialReference, Task> addLayerFromFileAsync)
        {
            InitializeComponent();
            _layerOptions = layerOptions ?? new List<LayerOption>();
            _getTableAsync = getTableAsync;
            _addLayerFromFileAsync = addLayerFromFileAsync;

            LoadCommonSrs();
            CboTargetSr.ItemsSource = _filteredItems;
            CboTargetSr.SelectedIndex = 0;

            // 填充图层列表
            CboSourceLayer.ItemsSource = _layerOptions;
            CboSourceLayer.DisplayMemberPath = "Display";
            if (_layerOptions.Count > 0)
                CboSourceLayer.SelectedIndex = 0;
        }

        private async void CboSourceLayer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboSourceLayer.SelectedItem is LayerOption opt)
            {
                _isFileSource = false;
                _sourceFilePath = null;
                TxtLayerStatus.Text = "正在加载图层信息...";
                try
                {
                    _sourceSr = SpatialReference.Create(opt.Wkid);
                    _sourceTable = await _getTableAsync(opt.LayerName);
                    if (_sourceTable != null)
                    {
                        SourceLayerName = opt.LayerName;
                        TxtSourceSr.Text = $"EPSG:{_sourceSr.Wkid}  —  {opt.LayerName}";
                        TxtLayerStatus.Text = $"已加载图层，共 {(await _sourceTable.QueryFeaturesAsync(new QueryParameters())).Count()} 个要素";
                        BtnOK.IsEnabled = true;
                    }
                    else
                    {
                        TxtSourceSr.Text = "(无法获取要素表)";
                        TxtLayerStatus.Text = "错误：无法获取该图层的要素表";
                        BtnOK.IsEnabled = false;
                    }
                }
                catch (Exception ex)
                {
                    TxtSourceSr.Text = "(加载失败)";
                    TxtLayerStatus.Text = $"错误：{ex.Message}";
                    BtnOK.IsEnabled = false;
                }
            }
        }

        private async void BtnBrowseLayer_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "Shapefile (*.shp)|*.shp|All files (*.*)|*.*",
                Title = "选择要转换坐标系的图层文件"
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string filePath = dlg.FileName;
                TxtLayerStatus.Text = "正在加载文件...";
                BtnBrowseLayer.IsEnabled = false;
                try
                {
                    var table = new ShapefileFeatureTable(filePath);
                    await table.LoadAsync();
                    _sourceSr = table.SpatialReference;
                    _sourceTable = table;
                    _isFileSource = true;
                    _sourceFilePath = filePath;
                    SourceLayerName = null;

                    string srText = _sourceSr != null ? $"EPSG:{_sourceSr.Wkid}" : "(未知)";
                    TxtSourceSr.Text = $"{srText}  —  {System.IO.Path.GetFileName(filePath)}";
                    TxtLayerStatus.Text = $"已加载文件，共 {table.NumberOfFeatures} 个要素";
                    BtnOK.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    TxtSourceSr.Text = "(加载失败)";
                    TxtLayerStatus.Text = $"错误：无法加载文件 - {ex.Message}";
                    BtnOK.IsEnabled = false;
                }
                finally
                {
                    BtnBrowseLayer.IsEnabled = true;
                }
            }
        }

        public void SetSourceSr(SpatialReference sr)
        {
            // 保留以供外部调用，但内部由图层选择自动填充
        }

        private void LoadCommonSrs()
        {
            var list = new List<SrItem>
            {
                new SrItem { Wkid = 4326, DisplayName = "WGS84 (EPSG:4326)", SearchText = "4326 wgs84 wgs 84 geographic" },
                new SrItem { Wkid = 4490, DisplayName = "CGCS2000 (EPSG:4490)", SearchText = "4490 cgcs2000 cgcs 2000 geographic" },
                new SrItem { Wkid = 3857, DisplayName = "Web Mercator (EPSG:3857)", SearchText = "3857 web mercator pseudo mercator" },
                new SrItem { Wkid = 4269, DisplayName = "NAD83 (EPSG:4269)", SearchText = "4269 nad83 geographic" },
                new SrItem { Wkid = 4267, DisplayName = "NAD27 (EPSG:4267)", SearchText = "4267 nad27 geographic" },
                new SrItem { Wkid = 4610, DisplayName = "Xian 1980 (EPSG:4610)", SearchText = "4610 xian 1980 geographic" },
                new SrItem { Wkid = 4214, DisplayName = "Beijing 1954 (EPSG:4214)", SearchText = "4214 beijing 1954 geographic" },
                new SrItem { Wkid = 4555, DisplayName = "CGCS2000 3-degree Zone 38 (EPSG:4555)", SearchText = "4555 cgcs2000 3 degree zone 38" },
                new SrItem { Wkid = 4549, DisplayName = "CGCS2000 3-degree Zone 39 (EPSG:4549)", SearchText = "4549 cgcs2000 3 degree zone 39" },
                new SrItem { Wkid = 4548, DisplayName = "CGCS2000 3-degree Zone 40 (EPSG:4548)", SearchText = "4548 cgcs2000 3 degree zone 40" },
                new SrItem { Wkid = 4547, DisplayName = "CGCS2000 3-degree Zone 41 (EPSG:4547)", SearchText = "4547 cgcs2000 3 degree zone 41" },
                new SrItem { Wkid = 4546, DisplayName = "CGCS2000 3-degree Zone 42 (EPSG:4546)", SearchText = "4546 cgcs2000 3 degree zone 42" },
                new SrItem { Wkid = 4545, DisplayName = "CGCS2000 3-degree Zone 43 (EPSG:4545)", SearchText = "4545 cgcs2000 3 degree zone 43" },
                new SrItem { Wkid = 4544, DisplayName = "CGCS2000 3-degree Zone 44 (EPSG:4544)", SearchText = "4544 cgcs2000 3 degree zone 44" },
                new SrItem { Wkid = 4543, DisplayName = "CGCS2000 3-degree Zone 45 (EPSG:4543)", SearchText = "4543 cgcs2000 3 degree zone 45" },
                new SrItem { Wkid = 4542, DisplayName = "CGCS2000 3-degree Zone 46 (EPSG:4542)", SearchText = "4542 cgcs2000 3 degree zone 46" },
                new SrItem { Wkid = 4541, DisplayName = "CGCS2000 3-degree Zone 47 (EPSG:4541)", SearchText = "4541 cgcs2000 3 degree zone 47" },
                new SrItem { Wkid = 4540, DisplayName = "CGCS2000 3-degree Zone 48 (EPSG:4540)", SearchText = "4540 cgcs2000 3 degree zone 48" },
                new SrItem { Wkid = 4539, DisplayName = "CGCS2000 3-degree Zone 49 (EPSG:4539)", SearchText = "4539 cgcs2000 3 degree zone 49" },
                new SrItem { Wkid = 4538, DisplayName = "CGCS2000 3-degree Zone 50 (EPSG:4538)", SearchText = "4538 cgcs2000 3 degree zone 50" },
                new SrItem { Wkid = 4537, DisplayName = "CGCS2000 3-degree Zone 51 (EPSG:4537)", SearchText = "4537 cgcs2000 3 degree zone 51" },
                new SrItem { Wkid = 4536, DisplayName = "CGCS2000 3-degree Zone 52 (EPSG:4536)", SearchText = "4536 cgcs2000 3 degree zone 52" },
                new SrItem { Wkid = 4535, DisplayName = "CGCS2000 3-degree Zone 53 (EPSG:4535)", SearchText = "4535 cgcs2000 3 degree zone 53" },
                new SrItem { Wkid = 4534, DisplayName = "CGCS2000 3-degree Zone 54 (EPSG:4534)", SearchText = "4534 cgcs2000 3 degree zone 54" },
                new SrItem { Wkid = 4533, DisplayName = "CGCS2000 3-degree Zone 55 (EPSG:4533)", SearchText = "4533 cgcs2000 3 degree zone 55" },
                new SrItem { Wkid = 4532, DisplayName = "CGCS2000 3-degree Zone 56 (EPSG:4532)", SearchText = "4532 cgcs2000 3 degree zone 56" },
                new SrItem { Wkid = 4531, DisplayName = "CGCS2000 3-degree Zone 57 (EPSG:4531)", SearchText = "4531 cgcs2000 3 degree zone 57" },
                new SrItem { Wkid = 4530, DisplayName = "CGCS2000 3-degree Zone 58 (EPSG:4530)", SearchText = "4530 cgcs2000 3 degree zone 58" },
                new SrItem { Wkid = 4529, DisplayName = "CGCS2000 3-degree Zone 59 (EPSG:4529)", SearchText = "4529 cgcs2000 3 degree zone 59" },
                new SrItem { Wkid = 4528, DisplayName = "CGCS2000 3-degree Zone 60 (EPSG:4528)", SearchText = "4528 cgcs2000 3 degree zone 60" },
                new SrItem { Wkid = 32650, DisplayName = "UTM Zone 50N (EPSG:32650)", SearchText = "32650 utm zone 50 north" },
                new SrItem { Wkid = 32651, DisplayName = "UTM Zone 51N (EPSG:32651)", SearchText = "32651 utm zone 51 north" },
                new SrItem { Wkid = 32649, DisplayName = "UTM Zone 49N (EPSG:32649)", SearchText = "32649 utm zone 49 north" },
                new SrItem { Wkid = 32648, DisplayName = "UTM Zone 48N (EPSG:32648)", SearchText = "32648 utm zone 48 north" },
                new SrItem { Wkid = 32750, DisplayName = "UTM Zone 50S (EPSG:32750)", SearchText = "32750 utm zone 50 south" },
                new SrItem { Wkid = 32751, DisplayName = "UTM Zone 51S (EPSG:32751)", SearchText = "32751 utm zone 51 south" },
            };

            _allItems.Clear();
            foreach (var item in list.OrderBy(i => i.Wkid))
                _allItems.Add(item);

            ApplyFilter("");
        }

        private void ApplyFilter(string filter)
        {
            _filteredItems.Clear();
            var filtered = string.IsNullOrWhiteSpace(filter)
                ? _allItems
                : _allItems.Where(i => i.SearchText.Contains(filter.ToLowerInvariant()));
            foreach (var item in filtered)
                _filteredItems.Add(item);

            if (_filteredItems.Count > 0)
                CboTargetSr.SelectedIndex = 0;
        }

        private void TxtSearchWkid_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter(TxtSearchWkid.Text?.Trim() ?? "");
        }

        private async void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (_sourceTable == null || _sourceSr == null)
            {
                MessageBox.Show("请先选择输入图层", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!(CboTargetSr.SelectedItem is SrItem selected))
            {
                MessageBox.Show("请先选择目标坐标系", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 禁用按钮，显示进度
            BtnOK.IsEnabled = false;
            BtnCancel.IsEnabled = false;
            ProgressBorder.Visibility = Visibility.Visible;

            try
            {
                var targetSr = selected.SpatialReference;
                if (targetSr == null)
                {
                    MessageBox.Show($"无法创建目标坐标系 (WKID={selected.Wkid})", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    ProgressBorder.Visibility = Visibility.Collapsed;
                    BtnOK.IsEnabled = true;
                    BtnCancel.IsEnabled = true;
                    return;
                }
                System.Diagnostics.Debug.WriteLine($"[Proj] targetSr.Wkid={targetSr.Wkid}");

                if (_sourceSr.Wkid == targetSr.Wkid)
                {
                    MessageBox.Show("源与目标坐标系相同，无需转换", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    ProgressBorder.Visibility = Visibility.Collapsed;
                    BtnOK.IsEnabled = true;
                    BtnCancel.IsEnabled = true;
                    return;
                }
                bool applyToAll = RdoAllFeatures.IsChecked == true;
                bool useDatum = ChkUseDatumTransform.IsChecked == true;

                var features = (await _sourceTable.QueryFeaturesAsync(new QueryParameters())).ToList();

                int total = features.Count;
                int converted = 0;
                int errors = 0;
                string firstErrorMsg = null;

                // 收集投影后的要素（不修改原表，直接在新表中写入）
                var projectedFeatures = new List<(Feature feature, Geometry projectedGeom)>();

                for (int i = 0; i < total; i++)
                {
                    var feature = features[i];
                    var original = feature.Geometry;
                    if (original == null || original.IsEmpty)
                    {
                        errors++;
                        continue;
                    }

                    try
                    {
                        Geometry workGeom = original;
                        if (workGeom.SpatialReference == null && _sourceSr != null)
                            workGeom = Geometry.FromJson(workGeom.ToJson(), _sourceSr);
                        if (workGeom.SpatialReference == null) { errors++; continue; }
                        var projected = GeometryEngine.Project(workGeom, targetSr);
                        if (projected == null || projected.IsEmpty)
                        {
                            errors++;
                            continue;
                        }
                        projectedFeatures.Add((feature, projected));
                        converted++;
                    }
                    catch (Exception projEx)
                    {
                        string msg = $"[Proj] Feature {i}: {projEx.GetType().Name}: {projEx.Message}";
                        System.Diagnostics.Debug.WriteLine(msg);
                        if (firstErrorMsg == null) firstErrorMsg = msg;
                        errors++;
                    }

                    int pct = (int)((i + 1) * 100.0 / total);
                    ProgressBar.Value = pct;
                    TxtProgressStatus.Text = $"正在转换 ({i + 1}/{total})...";
                    TxtProgressCount.Text = $"成功: {converted}  失败: {errors}";
                    await Task.Delay(10);
                }

                TxtProgressStatus.Text = "转换完成";
                ProgressBar.Value = 100;
                TxtProgressCount.Text = $"成功: {converted}  失败: {errors}  总计: {total}";

                // 诊断：显示目标坐标系 WKID
                System.Diagnostics.Debug.WriteLine($"[Proj] Target SR WKID={targetSr.Wkid}");

                // 显示第一个错误信息
                if (firstErrorMsg != null)
                {
                    MessageBox.Show($"投影出错示例:\n{firstErrorMsg}", "投影错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 如果指定了导出路径，写出到文件
                string exportPath = TxtExportPath.Text?.Trim();
                if (!string.IsNullOrEmpty(exportPath) && converted > 0)
                {
                    TxtProgressStatus.Text = "正在导出文件...";
                    await Task.Delay(50);
                    try
                    {
                        bool isShp = CboExportFormat.SelectedIndex == 0;
                        if (isShp)
                        {
                            // 创建新表写入投影后的数据（避免原表 SR 不匹配问题）
                            var fields = _sourceTable.Fields.Where(f => f.FieldType != FieldType.OID && f.FieldType != FieldType.Geometry).ToList();
                            var newTable = new FeatureCollectionTable(fields, projectedFeatures[0].projectedGeom.GeometryType, targetSr);
                            foreach (var (feat, projGeom) in projectedFeatures)
                            {
                                var newFeat = newTable.CreateFeature();
                                newFeat.Geometry = projGeom;
                                foreach (var field in fields)
                                {
                                    if (field.FieldType == FieldType.OID || field.FieldType == FieldType.Geometry) continue;
                                    if (feat.Attributes.ContainsKey(field.Name))
                                        newFeat.Attributes[field.Name] = feat.Attributes[field.Name];
                                }
                                await newTable.AddFeatureAsync(newFeat);
                            }
                            await ExportHelper.ExportToShapefile(newTable, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(exportPath) ?? "", System.IO.Path.GetFileNameWithoutExtension(exportPath)), targetSr);
                        }
                        TxtProgressStatus.Text = $"已导出到 {exportPath}";
                        await Task.Delay(200);

                        // 诊断：检查 .prj 文件内容
                        string prjFile = System.IO.Path.ChangeExtension(exportPath, ".prj");
                        if (System.IO.File.Exists(prjFile))
                        {
                            string prjContent = System.IO.File.ReadAllText(prjFile);
                            if (prjContent.Contains("Unknown"))
                                MessageBox.Show($"警告：.prj 文件内容包含 'Unknown'！\n路径: {prjFile}\n内容: {prjContent.Substring(0, Math.Min(prjContent.Length, 200))}", "导出诊断", MessageBoxButton.OK, MessageBoxImage.Warning);
                            else
                                MessageBox.Show($"生成成功！.prj 文件内容正确。\n路径: {prjFile}\nWKID: {targetSr.Wkid}", "导出诊断", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                            MessageBox.Show($"警告：未生成 .prj 文件！\n路径: {prjFile}", "导出诊断", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch (Exception ex2)
                    {
                        string detail = $"导出失败: {ex2.Message}";
                        if (ex2.InnerException != null)
                            detail += $"\n内部异常: {ex2.InnerException.Message}";
                        detail += $"\n\n{ex2.GetType().FullName}";
                        MessageBox.Show(detail, "导出错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        System.Diagnostics.Debug.WriteLine($"Export error: {detail}");
                        TxtProgressStatus.Text = $"导出失败: {ex2.Message}";
                    }
                }
                else if (!string.IsNullOrEmpty(exportPath) && converted == 0)
                {
                    MessageBox.Show($"没有成功转换的要素（{errors} 个错误），跳过导出。", "无数据", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 如果是文件源，通知主窗口添加为图层
                if (_isFileSource && _addLayerFromFileAsync != null && converted > 0)
                {
                    TxtProgressStatus.Text = "正在添加图层...";
                    await Task.Delay(50);
                    await _addLayerFromFileAsync(_sourceFilePath, targetSr);
                }

                TargetSpatialReference = targetSr;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                string detail = $"转换失败: {ex.Message}";
                if (ex.InnerException != null)
                    detail += $"\n内部异常: {ex.InnerException.Message}";
                detail += $"\n\n{ex.GetType().FullName}";
                detail += $"\n{ex.StackTrace}";
                MessageBox.Show(detail, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine(detail);
                ProgressBorder.Visibility = Visibility.Collapsed;
                BtnOK.IsEnabled = true;
                BtnCancel.IsEnabled = true;
            }
        }

        private void BtnBrowseTargetSr_Click(object sender, RoutedEventArgs e)
        {
            var browser = new SpatialReferenceBrowser
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (browser.ShowDialog() == true)
            {
                // Try to match the selected SR in the combo
                foreach (var item in _filteredItems)
                {
                    if (item.Wkid == browser.SelectedWkid)
                    {
                        CboTargetSr.SelectedItem = item;
                        return;
                    }
                }
                // Not found in list — add as custom
                var custom = new SrItem
                {
                    Wkid = browser.SelectedWkid,
                    DisplayName = browser.SelectedDisplayName ?? $"EPSG:{browser.SelectedWkid}",
                    SearchText = browser.SelectedWkid.ToString()
                };
                CboTargetSr.ItemsSource = null;
                _filteredItems.Add(custom);
                CboTargetSr.ItemsSource = _filteredItems;
                CboTargetSr.SelectedItem = custom;
            }
        }

        private void BtnBrowseExportPath_Click(object sender, RoutedEventArgs e)
        {
            bool isShp = CboExportFormat.SelectedIndex == 0;
            var dlg = new System.Windows.Forms.SaveFileDialog
            {
                Filter = isShp ? "Shapefile (*.shp)|*.shp" : "GeoTIFF (*.tif)|*.tif",
                DefaultExt = isShp ? ".shp" : ".tif",
                FileName = "projected_output." + (isShp ? "shp" : "tif")
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtExportPath.Text = dlg.FileName;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
