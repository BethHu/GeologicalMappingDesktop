using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Microsoft.Win32;

namespace GeologicalMappingDesktop
{
    public partial class FieldDataPanel : UserControl
    {
        private MainWindow Main => Window.GetWindow(this) as MainWindow;
        private string AttachDir => Main != null && Main.projectManager.IsProjectOpen
            ? Path.Combine(Main.projectManager.ProjectPath, "Attachments")
            : "";
        private string SketchDir => Main != null && Main.projectManager.IsProjectOpen
            ? Path.Combine(Main.projectManager.ProjectPath, "Sketches")
            : "";
        private string VideoDir => Main != null && Main.projectManager.IsProjectOpen
            ? Path.Combine(Main.projectManager.ProjectPath, "Videos")
            : "";

        public FieldDataPanel()
        {
            InitializeComponent();
        }

        // ======================== 数据导入 ========================

        private async void BtnImportZip_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null || !Main.projectManager.IsProjectOpen)
            {
                MessageBox.Show("请先打开或创建一个项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new OpenFileDialog
            {
                Filter = "ZIP 数据包|*.zip",
                Title = "选择移动端导出的 ZIP 数据包"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                TxtImportStatus.Text = "正在解压和解析...";
                string extractDir = Path.Combine(Path.GetTempPath(), "GeoImport_" + Guid.NewGuid().ToString("N"));
                var result = FieldDataHelper.ProcessImportZip(dlg.FileName, extractDir);

                // Build import summary
                string msg = $"解析完成：\n• 地质点: {result.TotalPoints} 个\n• 路线/线: {result.TotalLines} 条\n• 照片: {result.TotalPhotos} 张\n\n选择导入方式：";

                var importDlg = new Window
                {
                    Title = "导入选项",
                    Width = 400, Height = 220,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Main,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0D, 0x11, 0x17)),
                    ResizeMode = ResizeMode.NoResize
                };
                var grid = new System.Windows.Controls.Grid { Margin = new Thickness(14) };
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

                var txtMsg = new System.Windows.Controls.TextBlock { Text = msg, Foreground = System.Windows.Media.Brushes.LightGray, FontSize = 12, TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
                var btnNewLayer = new System.Windows.Controls.Button { Content = "拆分为新图层", Width = 160, Height = 30, Margin = new Thickness(0, 0, 10, 0), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x23, 0x86, 0x36)), Foreground = System.Windows.Media.Brushes.White, BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0xA0, 0x43)) };
                var btnMerge = new System.Windows.Controls.Button { Content = "合并到现有图层", Width = 160, Height = 30, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x21, 0x26, 0x2D)), Foreground = System.Windows.Media.Brushes.LightGray, BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D)) };
                var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                btnPanel.Children.Add(btnNewLayer); btnPanel.Children.Add(btnMerge);

                System.Windows.Controls.Grid.SetRow(txtMsg, 0);
                System.Windows.Controls.Grid.SetRow(btnPanel, 1);
                grid.Children.Add(txtMsg); grid.Children.Add(btnPanel);
                importDlg.Content = grid;

                btnNewLayer.Click += async (s2, e2) =>
                {
                    importDlg.Close();
                    await ImportAsNewLayers(result, extractDir);
                };
                btnMerge.Click += async (s2, e2) =>
                {
                    importDlg.Close();
                    await ImportMergeToLayers(result, extractDir);
                };
                importDlg.ShowDialog();
            }
            catch (Exception ex)
            {
                TxtImportStatus.Text = $"导入失败: {ex.Message}";
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ImportAsNewLayers(FieldDataHelper.ImportResult result, string extractDir)
        {
            try
            {
                int totalImported = 0;
                string ts = System.DateTime.Now.ToString("HHmmss");

                // 点图层：按移动端 7 类点类型拆分
                var pointGroups = FieldDataClassification.SplitPointsByType(result.PointFeatures);
                foreach (var kv in pointGroups)
                {
                    string layerName = $"{kv.Key}_{ts}";
                    var table = await FieldDataHelper.CreateLayerFromFeatures(
                        kv.Value, layerName, GeometryType.Point, SpatialReferences.Wgs84);
                    if (table != null)
                    {
                        table.Renderer = new SimpleRenderer(
                            new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle,
                                FieldDataClassification.GetPointColor(kv.Key), 8)
                            { Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.White, 1) });
                        var layer = await Main.CreateFeatureCollectionLayerAsync(table, layerName);
                        if (layer != null)
                        {
                            Main.CurrentMapView.Map.OperationalLayers.Add(layer);
                            Main.LayerManager.AddLayer(layer, true, "", table);
                            totalImported += kv.Value.Count;
                        }
                    }
                }

                // 线图层：按状态/地质类型拆分
                var lineGroups = FieldDataClassification.SplitLinesByCategory(result.LineFeatures);
                foreach (var kv in lineGroups)
                {
                    string layerName = $"{kv.Key}_{ts}";
                    var table = await FieldDataHelper.CreateLayerFromFeatures(
                        kv.Value, layerName, GeometryType.Polyline, SpatialReferences.Wgs84);
                    if (table != null)
                    {
                        table.Renderer = new SimpleRenderer(
                            new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
                                FieldDataClassification.GetLineColor(kv.Key), 2));
                        var layer = await Main.CreateFeatureCollectionLayerAsync(table, layerName);
                        if (layer != null)
                        {
                            Main.CurrentMapView.Map.OperationalLayers.Add(layer);
                            Main.LayerManager.AddLayer(layer, true, "", table);
                            totalImported += kv.Value.Count;
                        }
                    }
                }

                // Copy photos
                int photoCopied = 0;
                if (result.ExtractedPhotos.Count > 0)
                {
                    string prefix = FieldDataHelper.DetectIdPrefix(result.PointFeatures);
                    photoCopied = FieldDataHelper.CopyPhotosToAttachments(
                        result.ExtractedPhotos, Main.projectManager.ProjectPath, prefix);
                    if (!string.IsNullOrEmpty(AttachDir))
                    {
                        FieldDataHelper.MatchPhotosToPoints(
                            result.PointFeatures, result.PhotoSourceDir ?? extractDir, AttachDir);
                    }
                }

                // Copy sketches
                int sketchCopied = 0;
                if (result.ExtractedSketches.Count > 0)
                {
                    string prefix = FieldDataHelper.DetectIdPrefix(result.PointFeatures);
                    sketchCopied = FieldDataHelper.CopySketchesToSketches(
                        result.ExtractedSketches, Main.projectManager.ProjectPath, prefix);
                }

                // Copy videos
                int videoCopied = 0;
                if (result.ExtractedVideos.Count > 0)
                {
                    string prefix = FieldDataHelper.DetectIdPrefix(result.PointFeatures);
                    videoCopied = FieldDataHelper.CopyVideosToVideos(
                        result.ExtractedVideos, Main.projectManager.ProjectPath, prefix);
                }

                TxtImportStatus.Text = $"导入完成: {totalImported} 个要素, {photoCopied} 张照片, {sketchCopied} 张草图, {videoCopied} 个视频";
                MessageBox.Show($"导入完成！\n• 点要素: {result.PointFeatures.Count}\n• 线要素: {result.LineFeatures.Count}\n• 照片: {photoCopied} 张\n• 草图: {sketchCopied} 张\n• 视频: {videoCopied} 个",
                    "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TxtImportStatus.Text = $"导入失败: {ex.Message}";
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try { Directory.Delete(extractDir, true); } catch { }
            }
        }

        private async Task ImportMergeToLayers(FieldDataHelper.ImportResult result, string extractDir)
        {
            try
            {
                if (Main == null) return;
                var layerItems = Main.LayerManager.Layers.ToList();
                var pointLayers = layerItems.Where(l => l.Layer is FeatureLayer fl &&
                    fl.FeatureTable?.GeometryType == GeometryType.Point).ToList();
                var lineLayers = layerItems.Where(l => l.Layer is FeatureLayer fl &&
                    fl.FeatureTable?.GeometryType == GeometryType.Polyline).ToList();

                if (result.PointFeatures.Count > 0)
                {
                    if (pointLayers.Count == 0)
                    {
                        MessageBox.Show("没有找到点图层用于合并", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        var pick = new PickLayerDialog(pointLayers.Select(l => l.Name).ToList(), "选择目标点图层", Main);
                        if (pick.ShowDialog() == true && pick.SelectedLayer != null)
                        {
                            var target = pointLayers.FirstOrDefault(l => l.Name == pick.SelectedLayer);
                            var table = Main.GetFeatureTableFromLayer(target?.Layer) ?? target?.MemoryFeatureTable;
                            if (table != null)
                            {
                                foreach (var fs in result.PointFeatures)
                                {
                                    var feat = table.CreateFeature();
                                    if (!string.IsNullOrEmpty(fs.GeometryJson))
                                        try { feat.Geometry = Geometry.FromJson(fs.GeometryJson); } catch { }
                                    foreach (var kv in fs.Attributes)
                                    {
                                        if (feat.Attributes.ContainsKey(kv.Key))
                                            feat.Attributes[kv.Key] = kv.Value ?? DBNull.Value;
                                    }
                                    try { await table.AddFeatureAsync(feat); } catch { }
                                }
                            }
                        }
                    }
                }

                if (result.LineFeatures.Count > 0)
                {
                    if (lineLayers.Count == 0)
                    {
                        MessageBox.Show("没有找到线图层用于合并", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        var pick = new PickLayerDialog(lineLayers.Select(l => l.Name).ToList(), "选择目标线图层", Main);
                        if (pick.ShowDialog() == true && pick.SelectedLayer != null)
                        {
                            var target = lineLayers.FirstOrDefault(l => l.Name == pick.SelectedLayer);
                            var table = Main.GetFeatureTableFromLayer(target?.Layer) ?? target?.MemoryFeatureTable;
                            if (table != null)
                            {
                                foreach (var fs in result.LineFeatures)
                                {
                                    var feat = table.CreateFeature();
                                    if (!string.IsNullOrEmpty(fs.GeometryJson))
                                        try { feat.Geometry = Geometry.FromJson(fs.GeometryJson); } catch { }
                                    foreach (var kv in fs.Attributes)
                                    {
                                        if (feat.Attributes.ContainsKey(kv.Key))
                                            feat.Attributes[kv.Key] = kv.Value ?? DBNull.Value;
                                    }
                                    try { await table.AddFeatureAsync(feat); } catch { }
                                }
                            }
                        }
                    }
                }

                if (result.ExtractedPhotos.Count > 0)
                {
                    string prefix = FieldDataHelper.DetectIdPrefix(result.PointFeatures);
                    FieldDataHelper.CopyPhotosToAttachments(result.ExtractedPhotos, Main.projectManager.ProjectPath, prefix);
                }

                TxtImportStatus.Text = "合并完成";
                MessageBox.Show("数据已合并到现有图层", "合并完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TxtImportStatus.Text = $"合并失败: {ex.Message}";
                MessageBox.Show($"合并失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try { Directory.Delete(extractDir, true); } catch { }
            }
        }

        private async void BtnImportGeoJson_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;
            var dlg = new ImportDialog { Owner = Main };
            if (dlg.ShowDialog() != true) return;
            if (dlg.ParsedFeatures == null || dlg.ParsedFeatures.Count == 0) return;

            // Split into point/line
            var points = new List<FeatureState>();
            var lines = new List<FeatureState>();
            foreach (var f in dlg.ParsedFeatures)
            {
                if (string.IsNullOrEmpty(f.GeometryJson)) continue;
                try
                {
                    var geom = Geometry.FromJson(f.GeometryJson);
                    if (geom.GeometryType == GeometryType.Point) points.Add(f);
                    else if (geom.GeometryType == GeometryType.Polyline) lines.Add(f);
                }
                catch { }
            }

            int total = 0;
            string ts = System.DateTime.Now.ToString("HHmmss");

            // 点图层：按移动端 7 类点类型拆分（pointType/typeCode 属性 → 分类）
            if (points.Count > 0)
            {
                var pointGroups = FieldDataClassification.SplitPointsByType(points);
                foreach (var kv in pointGroups)
                {
                    string layerName = $"{kv.Key}_{ts}";
                    var table = await FieldDataHelper.CreateLayerFromFeatures(
                        kv.Value, layerName, GeometryType.Point, SpatialReferences.Wgs84);
                    if (table != null)
                    {
                        table.Renderer = new SimpleRenderer(
                            new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle,
                                FieldDataClassification.GetPointColor(kv.Key), 8)
                            { Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.White, 1) });
                        var layer = await Main.CreateFeatureCollectionLayerAsync(table, layerName);
                        if (layer != null)
                        {
                            Main.CurrentMapView.Map.OperationalLayers.Add(layer);
                            Main.LayerManager.AddLayer(layer, true, "", table);
                            total += kv.Value.Count;
                        }
                    }
                }
            }

            // 线图层：按路线状态/地质线类型拆分（status/lineType 属性 → 分类）
            if (lines.Count > 0)
            {
                var lineGroups = FieldDataClassification.SplitLinesByCategory(lines);
                foreach (var kv in lineGroups)
                {
                    string layerName = $"{kv.Key}_{ts}";
                    var table = await FieldDataHelper.CreateLayerFromFeatures(
                        kv.Value, layerName, GeometryType.Polyline, SpatialReferences.Wgs84);
                    if (table != null)
                    {
                        table.Renderer = new SimpleRenderer(
                            new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
                                FieldDataClassification.GetLineColor(kv.Key), 2));
                        var layer = await Main.CreateFeatureCollectionLayerAsync(table, layerName);
                        if (layer != null)
                        {
                            Main.CurrentMapView.Map.OperationalLayers.Add(layer);
                            Main.LayerManager.AddLayer(layer, true, "", table);
                            total += kv.Value.Count;
                        }
                    }
                }
            }

            TxtImportStatus.Text = $"导入 {total} 个要素（{points.Count} 点, {lines.Count} 线）";
            MessageBox.Show($"导入完成：{points.Count} 个点, {lines.Count} 条线", "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnImportPhotos_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null || !Main.projectManager.IsProjectOpen)
            {
                MessageBox.Show("请先打开项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff",
                Title = "选择照片（可多选）",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            int copied = FieldDataHelper.CopyPhotosToAttachments(
                dlg.FileNames.ToList(), Main.projectManager.ProjectPath);

            TxtImportStatus.Text = $"已导入 {copied} 张照片到 {AttachDir}";
            MessageBox.Show($"成功导入 {copied} 张照片\n（{dlg.FileNames.Length - copied} 张已存在跳过）", "导入照片", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnImportTracks_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;
            var dlg = new OpenFileDialog
            {
                Filter = "轨迹文件|*.geojson;*.json;*.gpx;*.csv",
                Title = "选择轨迹文件"
            };
            if (dlg.ShowDialog() != true) return;

            string ext = Path.GetExtension(dlg.FileName).ToLower();
            if (ext == ".geojson" || ext == ".json")
            {
                string content = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
                var features = GeoJsonHelper.ParseGeoJsonFeatures(content);
                var lineFeats = features.Where(f =>
                {
                    try { return !string.IsNullOrEmpty(f.GeometryJson) &&
                        Geometry.FromJson(f.GeometryJson).GeometryType == GeometryType.Polyline; }
                    catch { return false; }
                }).ToList();

                if (lineFeats.Count == 0)
                {
                    MessageBox.Show("未找到线要素", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var table = await FieldDataHelper.CreateLayerFromFeatures(lineFeats, "导入轨迹", GeometryType.Polyline, SpatialReferences.Wgs84);
                if (table != null)
                {
                    var layer = await Main.CreateFeatureCollectionLayerAsync(table, "导入轨迹");
                    if (layer != null)
                    {
                        Main.CurrentMapView.Map.OperationalLayers.Add(layer);
                        Main.LayerManager.AddLayer(layer, true, "", table);
                        TxtImportStatus.Text = $"导入 {lineFeats.Count} 条轨迹";
                        MessageBox.Show($"成功导入 {lineFeats.Count} 条轨迹", "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            else
            {
                MessageBox.Show("当前仅支持 GeoJSON 格式的轨迹导入。\nGPX/CSV 格式支持将在后续版本添加。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ======================== 云端同步 ========================

        private static string CloudConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GeologicalMappingDesktop", "cloudsync.json");

        private Dictionary<string, object> LoadCloudAuth()
        {
            try
            {
                if (File.Exists(CloudConfigPath))
                {
                    var raw = File.ReadAllText(CloudConfigPath);
                    var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                    return ser.Deserialize<Dictionary<string, object>>(raw);
                }
            }
            catch { }
            return null;
        }

        private async void BtnCloudPull_Click(object sender, RoutedEventArgs e)
        {
            var auth = LoadCloudAuth();
            if (auth == null)
            {
                MessageBox.Show("请先点击工具栏「登录」按钮登录账号", "未登录", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var token = Convert.ToString(auth["token"]);
            var serverUrl = Convert.ToString(auth["serverUrl"])?.TrimEnd('/');
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(serverUrl))
            {
                MessageBox.Show("登录信息无效，请重新登录", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ProgressCloudSync.Visibility = Visibility.Visible;
            TxtCloudSyncStatus.Text = "正在从云端下载...";

            try
            {
                using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                {
                    var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"{serverUrl}/api/sync/pull?type=geojson");
                    req.Headers.Add("Authorization", $"Bearer {token}");
                    var resp = await http.SendAsync(req);
                    var raw = await resp.Content.ReadAsStringAsync();
                    var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                    var result = ser.Deserialize<Dictionary<string, object>>(raw);

                    if (result != null && result.ContainsKey("ok") && Convert.ToBoolean(result["ok"]) && result.ContainsKey("data"))
                    {
                        var geojson = Convert.ToString(result["data"]);
                        System.Diagnostics.Debug.WriteLine($"[BtnCloudPull_Click] raw data length={(geojson?.Length ?? 0)}, preview={(geojson?.Length > 500 ? geojson.Substring(0, 500) + "..." : geojson)}");
                        if (!string.IsNullOrEmpty(geojson))
                        {
                            TxtCloudSyncStatus.Text = "正在导入云端数据...";
                            if (Main != null)
                                await Main.ImportFeaturesFromGeoJson(geojson);

                            // Download attachment files
                            var dlResult = await DownloadAttachments(geojson, serverUrl, token);
                            string dlMsg = "下载并导入完成";
                            if (dlResult.total > 0)
                                dlMsg = $"下载并导入完成（照片: {dlResult.photos} 张, 草图: {dlResult.sketches} 张, 视频: {dlResult.videos} 个）";
                            TxtCloudSyncStatus.Text = dlMsg;
                            TxtCloudSyncStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                        }
                        else
                        {
                            TxtCloudSyncStatus.Text = "云端无数据";
                            TxtCloudSyncStatus.Foreground = System.Windows.Media.Brushes.Gray;
                        }
                    }
                    else
                    {
                        var msg = result != null && result.ContainsKey("message") ? Convert.ToString(result["message"]) : "未知错误";
                        TxtCloudSyncStatus.Text = $"拉取失败: {msg}";
                        TxtCloudSyncStatus.Foreground = System.Windows.Media.Brushes.Orange;
                    }
                }
            }
            catch (Exception ex)
            {
                TxtCloudSyncStatus.Text = $"连接失败: {ex.Message}";
                TxtCloudSyncStatus.Foreground = System.Windows.Media.Brushes.Orange;
            }

            ProgressCloudSync.Visibility = Visibility.Collapsed;
        }

        private async void BtnCloudPush_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;

            var auth = LoadCloudAuth();
            if (auth == null)
            {
                MessageBox.Show("请先点击工具栏「登录」按钮登录账号", "未登录", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var token = Convert.ToString(auth["token"]);
            var serverUrl = Convert.ToString(auth["serverUrl"])?.TrimEnd('/');
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(serverUrl))
            {
                MessageBox.Show("登录信息无效，请重新登录", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ProgressCloudSync.Visibility = Visibility.Visible;
            TxtCloudSyncStatus.Text = "正在上传到云端...";

            try
            {
                var geojson = Main.GetCurrentFeaturesAsGeoJson();
                if (string.IsNullOrEmpty(geojson) || geojson.Contains("\"features\":[]"))
                {
                    TxtCloudSyncStatus.Text = "没有数据可上传";
                    TxtCloudSyncStatus.Foreground = System.Windows.Media.Brushes.Gray;
                    ProgressCloudSync.Visibility = Visibility.Collapsed;
                    return;
                }

                using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                {
                    var body = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(
                        new Dictionary<string, object> { { "content", geojson }, { "dataType", "geojson" } });
                    var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{serverUrl}/api/sync/push")
                    {
                        Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json")
                    };
                    req.Headers.Add("Authorization", $"Bearer {token}");
                    var resp = await http.SendAsync(req);
                    var raw = await resp.Content.ReadAsStringAsync();
                    var result = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);

                    if (result != null && result.ContainsKey("ok") && Convert.ToBoolean(result["ok"]))
                    {
                        TxtCloudSyncStatus.Text = "正在上传附件...";
                        await UploadAttachments(geojson, serverUrl, token);
                        TxtCloudSyncStatus.Text = "上传成功";
                        TxtCloudSyncStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                    }
                    else
                    {
                        var msg = result != null && result.ContainsKey("message") ? Convert.ToString(result["message"]) : "未知错误";
                        TxtCloudSyncStatus.Text = $"上传失败: {msg}";
                        TxtCloudSyncStatus.Foreground = System.Windows.Media.Brushes.Orange;
                    }
                }
            }
            catch (Exception ex)
            {
                TxtCloudSyncStatus.Text = $"连接失败: {ex.Message}";
                TxtCloudSyncStatus.Foreground = System.Windows.Media.Brushes.Orange;
            }

            ProgressCloudSync.Visibility = Visibility.Collapsed;
        }

        // ======================== 附件传输 ========================

        private class DownloadResult { public int total; public int photos; public int sketches; public int videos; }

        private async Task<DownloadResult> DownloadAttachments(string geojson, string serverUrl, string token)
        {
            var result = new DownloadResult();
            if (Main == null || !Main.projectManager.IsProjectOpen) return result;
            string projectPath = Main.projectManager.ProjectPath;
            if (string.IsNullOrEmpty(projectPath)) return result;
            string attachDir = System.IO.Path.Combine(projectPath, "Attachments");
            string sketchDir = System.IO.Path.Combine(projectPath, "Sketches");
            string videoDir = System.IO.Path.Combine(projectPath, "Videos");
            if (!Directory.Exists(attachDir)) Directory.CreateDirectory(attachDir);
            if (!Directory.Exists(sketchDir)) Directory.CreateDirectory(sketchDir);
            if (!Directory.Exists(videoDir)) Directory.CreateDirectory(videoDir);

            // Get list of available files on server
            var fileList = await GetServerFileList(serverUrl, token);
            if (fileList == null || fileList.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[DownloadAttachments] no files on server");
                return result;
            }

            // Parse feature IDs from geojson to match files
            var featureIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                int searchPos = 0;
                while (searchPos < geojson.Length)
                {
                    int idIdx = geojson.IndexOf("\"id\":", searchPos, StringComparison.OrdinalIgnoreCase);
                    if (idIdx < 0) break;
                    int colonIdx = geojson.IndexOf(':', idIdx);
                    if (colonIdx < 0) break;
                    int quoteStart = geojson.IndexOf('"', colonIdx + 1);
                    if (quoteStart < 0) break;
                    int quoteEnd = geojson.IndexOf('"', quoteStart + 1);
                    if (quoteEnd < 0) break;
                    string id = geojson.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                    if (!string.IsNullOrEmpty(id))
                        featureIds.Add(id);
                    searchPos = quoteEnd + 1;
                }
            }
            catch { }

            System.Diagnostics.Debug.WriteLine($"[DownloadAttachments] feature IDs ({featureIds.Count}): {string.Join(", ", featureIds)}, server files ({fileList.Count}): {string.Join(", ", fileList)}");

            using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(120) })
            {
                foreach (var serverFile in fileList)
                {
                    bool matched = false;
                    foreach (var fid in featureIds)
                    {
                        string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(serverFile);
                        if (nameNoExt.Equals(fid, StringComparison.OrdinalIgnoreCase) ||
                            nameNoExt.StartsWith(fid + "_", StringComparison.OrdinalIgnoreCase))
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched) continue;

                    // Route by filename / extension
                    string lower = serverFile.ToLowerInvariant();
                    bool isSketch = lower.Contains("sketch");
                    bool isVideo = IsVideoFile(serverFile) || lower.Contains("video");
                    string targetDir = isVideo ? videoDir : (isSketch ? sketchDir : attachDir);
                    string localPath = System.IO.Path.Combine(targetDir, serverFile);
                    if (File.Exists(localPath)) continue;

                    try
                    {
                        var dlReq = new System.Net.Http.HttpRequestMessage(
                            System.Net.Http.HttpMethod.Get, $"{serverUrl}/api/sync/download/{System.Net.WebUtility.UrlEncode(serverFile)}");
                        dlReq.Headers.Add("Authorization", $"Bearer {token}");
                        var dlResp = await http.SendAsync(dlReq);
                        if (dlResp.IsSuccessStatusCode)
                        {
                            var dlJson = await dlResp.Content.ReadAsStringAsync();
                            var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                            var dlResult = ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(dlJson);
                            if (dlResult != null && dlResult.ContainsKey("ok") && Convert.ToBoolean(dlResult["ok"]) && dlResult.ContainsKey("data"))
                            {
                                var base64Data = Convert.ToString(dlResult["data"]);
                                var fileBytes = System.Convert.FromBase64String(base64Data);
                                File.WriteAllBytes(localPath, fileBytes);
                                result.total++;
                                if (isVideo) result.videos++;
                                else if (isSketch) result.sketches++;
                                else result.photos++;
                                System.Diagnostics.Debug.WriteLine($"[DownloadAttachments] downloaded: {serverFile} -> {(isVideo ? "Videos" : isSketch ? "Sketches" : "Attachments")}");
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DownloadAttachments] failed for {serverFile}: {ex.Message}");
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[DownloadAttachments] total downloaded: {result.total} (photos: {result.photos}, sketches: {result.sketches}, videos: {result.videos})");
            return result;
        }

        private async Task<System.Collections.Generic.List<string>> GetServerFileList(string serverUrl, string token)
        {
            try
            {
                using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    var req = new System.Net.Http.HttpRequestMessage(
                        System.Net.Http.HttpMethod.Get, $"{serverUrl}/api/sync/files");
                    req.Headers.Add("Authorization", $"Bearer {token}");
                    var resp = await http.SendAsync(req);
                    var raw = await resp.Content.ReadAsStringAsync();
                    var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                    var result = ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(raw);
                    if (result != null && result.ContainsKey("ok") && Convert.ToBoolean(result["ok"]) && result.ContainsKey("files"))
                    {
                        var files = result["files"] as System.Collections.ArrayList;
                        if (files != null)
                            return files.Cast<object>().Select(f => f.ToString()).ToList();
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetServerFileList] error: {ex.Message}");
            }
            return null;
        }

        private async Task UploadAttachments(string geojson, string serverUrl, string token)
        {
            if (Main == null || !Main.projectManager.IsProjectOpen) return;
            string projectPath = Main.projectManager.ProjectPath;
            if (string.IsNullOrEmpty(projectPath)) return;
            string attachDir = System.IO.Path.Combine(projectPath, "Attachments");
            string sketchDir = System.IO.Path.Combine(projectPath, "Sketches");
            string videoDir = System.IO.Path.Combine(projectPath, "Videos");

            // Extract feature IDs from geojson
            var featureIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                int searchPos = 0;
                while (searchPos < geojson.Length)
                {
                    int idIdx = geojson.IndexOf("\"id\":", searchPos, StringComparison.OrdinalIgnoreCase);
                    if (idIdx < 0) break;
                    int colonIdx = geojson.IndexOf(':', idIdx);
                    if (colonIdx < 0) break;
                    int quoteStart = geojson.IndexOf('"', colonIdx + 1);
                    if (quoteStart < 0) break;
                    int quoteEnd = geojson.IndexOf('"', quoteStart + 1);
                    if (quoteEnd < 0) break;
                    string id = geojson.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                    if (!string.IsNullOrEmpty(id))
                        featureIds.Add(id);
                    searchPos = quoteEnd + 1;
                }
            }
            catch { }

            if (featureIds.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[UploadAttachments] no feature IDs found");
                return;
            }

            // Find matching files in Attachments / Sketches / Videos
            var allFiles = new System.Collections.Generic.List<string>();
            if (Directory.Exists(attachDir))
                allFiles.AddRange(Directory.GetFiles(attachDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => IsImage(f) || IsAttachmentFile(f)));
            if (Directory.Exists(sketchDir))
                allFiles.AddRange(Directory.GetFiles(sketchDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => IsImage(f) || IsAttachmentFile(f)));
            if (Directory.Exists(videoDir))
                allFiles.AddRange(Directory.GetFiles(videoDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => IsVideoFile(f)));

            var filesToUpload = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var filePath in allFiles)
            {
                string fileName = System.IO.Path.GetFileName(filePath);
                foreach (var fid in featureIds)
                {
                    string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
                    if (nameNoExt.Equals(fid, StringComparison.OrdinalIgnoreCase) ||
                        nameNoExt.StartsWith(fid + "_", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var fileBytes = File.ReadAllBytes(filePath);
                            filesToUpload[fileName] = System.Convert.ToBase64String(fileBytes);
                        }
                        catch { }
                        break;
                    }
                }
            }

            if (filesToUpload.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[UploadAttachments] no matching files to upload");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[UploadAttachments] uploading {filesToUpload.Count} files: {string.Join(", ", filesToUpload.Keys)}");

            using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(180) })
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var body = ser.Serialize(
                    new System.Collections.Generic.Dictionary<string, object> { { "files", filesToUpload } });
                var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{serverUrl}/api/sync/upload")
                {
                    Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json")
                };
                req.Headers.Add("Authorization", $"Bearer {token}");
                var resp = await http.SendAsync(req);
                var raw = await resp.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"[UploadAttachments] upload response: {raw}");
            }
        }

        private static bool IsAttachmentFile(string path)
        {
            var ext = System.IO.Path.GetExtension(path).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif"
                || ext == ".tif" || ext == ".tiff" || ext == ".pdf" || ext == ".txt" || ext == ".xml";
        }

        private static bool IsVideoFile(string path)
        {
            var ext = System.IO.Path.GetExtension(path).ToLower();
            return ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mkv" || ext == ".3gp" || ext == ".wmv";
        }

        // ======================== 数据质量检查 ========================

        private async void BtnRunQualityCheck_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;

            try
            {
                var allPoints = new List<FeatureState>();
                var allRoutes = new List<FeatureState>();
                foreach (var layerItem in Main.LayerManager.Layers)
                {
                    var table = Main.GetFeatureTableFromLayer(layerItem.Layer) ?? layerItem.MemoryFeatureTable;
                    if (table == null) continue;
                    var features = await table.QueryFeaturesAsync(new QueryParameters());
                    foreach (var f in features)
                    {
                        var fs = new FeatureState
                        {
                            GeometryJson = f.Geometry?.ToJson() ?? "",
                            Attributes = f.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                        };
                        if (table.GeometryType == GeometryType.Point)
                            allPoints.Add(fs);
                        else if (table.GeometryType == GeometryType.Polyline)
                            allRoutes.Add(fs);
                    }
                }

                // Check points: missing description, coordinates, photos
                int totalPts = allPoints.Count;
                int noDesc = allPoints.Count(f => !f.Attributes.TryGetValue("description", out object d) || d == null || string.IsNullOrEmpty(d.ToString()));
                int noCoord = allPoints.Count(f =>
                {
                    bool hasLat = f.Attributes.TryGetValue("latitude", out object lat) && lat != null && double.TryParse(lat.ToString(), out double lv) && lv != 0;
                    bool hasLon = f.Attributes.TryGetValue("longitude", out object lon) && lon != null && double.TryParse(lon.ToString(), out double lov) && lov != 0;
                    return !hasLat || !hasLon;
                });
                int noPhotos = allPoints.Count(f =>
                {
                    bool hasPhoto = f.Attributes.TryGetValue("photoPaths", out object pp) && pp != null && !string.IsNullOrEmpty(pp.ToString());
                    return !hasPhoto;
                });

                // Check routes
                int totalRoutes = allRoutes.Count;
                double totalDist = 0;
                foreach (var r in allRoutes)
                {
                    if (!string.IsNullOrEmpty(r.GeometryJson))
                    {
                        try
                        {
                            var geom = Geometry.FromJson(r.GeometryJson);
                            totalDist += GeometryEngine.Length(geom);
                        }
                        catch { }
                    }
                }

                // Photo and sketch directories
                int photoCount = !string.IsNullOrEmpty(AttachDir) && Directory.Exists(AttachDir)
                    ? Directory.GetFiles(AttachDir, "*.*", SearchOption.TopDirectoryOnly)
                        .Count(f => IsImage(f) || IsAttachmentFile(f))
                    : 0;
                int sketchCount = !string.IsNullOrEmpty(SketchDir) && Directory.Exists(SketchDir)
                    ? Directory.GetFiles(SketchDir, "*.*", SearchOption.TopDirectoryOnly)
                        .Count(f => IsImage(f) || IsAttachmentFile(f))
                    : 0;
                int videoCount = !string.IsNullOrEmpty(VideoDir) && Directory.Exists(VideoDir)
                    ? Directory.GetFiles(VideoDir, "*.*", SearchOption.TopDirectoryOnly)
                        .Count(f => IsVideoFile(f))
                    : 0;

                string msg = $"数据质量检查报告\n" +
                             $"━━━━━━━━━━━━━━━━━━\n" +
                             $"点位检查:\n" +
                             $"  总点数: {totalPts}\n" +
                             $"  缺描述: {noDesc}\n" +
                             $"  缺坐标: {noCoord}\n" +
                             $"  缺照片关联: {noPhotos}\n\n" +
                             $"路线检查:\n" +
                             $"  总路线数: {totalRoutes}\n" +
                             $"  总长度: {totalDist / 1000:F2} km\n" +
                             $"  覆盖密度: {(totalPts > 0 ? totalDist / totalPts : 0):F0} m/点\n\n" +
                             $"附件检查:\n" +
                             $"  Attachments 目录照片/文件: {photoCount} 个\n" +
                             $"  Sketches 目录草图/文件: {sketchCount} 个\n" +
                             $"  Videos 目录视频: {videoCount} 个";

                CheckResultBorder.Visibility = Visibility.Visible;
                TxtCheckResult.Text = msg;
            }
            catch (Exception ex)
            {
                CheckResultBorder.Visibility = Visibility.Visible;
                TxtCheckResult.Text = $"检查失败: {ex.Message}";
            }
        }

        // ======================== 照片归档 ========================

        private void BtnPhotoManager_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;
            string photoDir = AttachDir;
            string sketchDir = SketchDir;
            string videoDir = VideoDir;
            if (!string.IsNullOrEmpty(photoDir) && !Directory.Exists(photoDir)) Directory.CreateDirectory(photoDir);
            if (!string.IsNullOrEmpty(sketchDir) && !Directory.Exists(sketchDir)) Directory.CreateDirectory(sketchDir);
            if (!string.IsNullOrEmpty(videoDir) && !Directory.Exists(videoDir)) Directory.CreateDirectory(videoDir);
            if ((string.IsNullOrEmpty(photoDir) || !Directory.Exists(photoDir)) &&
                (string.IsNullOrEmpty(sketchDir) || !Directory.Exists(sketchDir)) &&
                (string.IsNullOrEmpty(videoDir) || !Directory.Exists(videoDir)))
            {
                MessageBox.Show("未找到媒体目录，请先打开工程并导入/同步数据。\n\n照片: Attachments\n草图: Sketches\n视频: Videos", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var win = new PhotoManagerWindow(photoDir, sketchDir, videoDir) { Owner = Main };
            win.Show();
        }

        // ======================== 样品清单 ========================
        private async void BtnGenerateList_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;

            // Collect all sample points
            var allSamples = new List<FeatureState>();
            foreach (var layerItem in Main.LayerManager.Layers)
            {
                var table = Main.GetFeatureTableFromLayer(layerItem.Layer) ?? layerItem.MemoryFeatureTable;
                if (table == null || table.GeometryType != GeometryType.Point) continue;
                try
                {
                    var features = await table.QueryFeaturesAsync(new QueryParameters());
                    foreach (var f in features)
                    {
                        var fs = new FeatureState
                        {
                            GeometryJson = f.Geometry?.ToJson() ?? "",
                            Attributes = f.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                        };
                        allSamples.Add(fs);
                    }
                }
                catch { }
            }

            var filtered = FieldDataHelper.FilterSamplePoints(allSamples);

            if (filtered.Count == 0)
            {
                MessageBox.Show("未找到样品点数据。\n\n提示：矢量图层的属性表中需有 pointType 或 typeCode 字段标识样品类型。",
                    "无数据", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var win = new SampleListWindow(filtered) { Owner = Main };
            win.Show();
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            BtnGenerateList_Click(sender, e);
        }

        private async void BtnImportLabData_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "CSV 文件|*.csv|Excel 文件|*.xlsx;*.xls",
                Title = "导入实验数据"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                string ext = Path.GetExtension(dlg.FileName).ToLower();
                if (ext == ".csv")
                {
                    var lines = File.ReadAllLines(dlg.FileName, System.Text.Encoding.UTF8);
                    if (lines.Length < 2)
                    {
                        MessageBox.Show("CSV 文件格式无效（需至少包含表头和一行数据）", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    string header = lines[0];
                    string preview = string.Join("\n", lines.Take(Math.Min(6, lines.Length)));
                    MessageBox.Show($"CSV 读取成功：{lines.Length - 1} 行数据\n\n表头: {header}\n\n预览:\n{preview}",
                        "导入数据预览", MessageBoxButton.OK, MessageBoxImage.Information);
                    TxtImportStatus.Text = $"已导入实验数据: {lines.Length - 1} 条记录";
                }
                else
                {
                    MessageBox.Show("Excel 导入需要安装 Office 组件。\n请先将 Excel 另存为 CSV 格式后导入。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ======================== 数据纠错 ========================

        private async void BtnModifyPoint_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;

            // Select a point on map
            if (Main._selectedFeatures.Count == 0)
            {
                MessageBox.Show("请先使用选择工具在地图上选择一个要素", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var feat = Main._selectedFeatures[0];
            var geom = feat.Geometry;
            if (geom == null || geom.GeometryType != GeometryType.Point)
            {
                MessageBox.Show("请选择一个点要素", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var pt = (MapPoint)geom;
            var coordDlg = new CoordinateInputDialog { Owner = Main };
            coordDlg.TxtLonDecimal.Text = pt.X.ToString("F6");
            coordDlg.TxtLatDecimal.Text = pt.Y.ToString("F6");
            coordDlg.RdoDecimal.IsChecked = true;
            if (coordDlg.ShowDialog() == true && coordDlg.ResultPoint != null)
            {
                try
                {
                    var pt2 = coordDlg.ResultPoint;
                    // 直接用要素自身的 FeatureTable，避免 GetFeatureTableByFeature 按 OBJECTID 全表扫描
                    // （多个点图层的 OBJECTID 都从 1 开始，会匹配到错误图层的同名 OID 要素，
                    //   导致"选中的点没动、另一个点跑到目标坐标"）
                    var table = feat.FeatureTable ?? Main.GetFeatureTableByFeature(feat);
                    if (table == null)
                    {
                        MessageBox.Show("找不到该要素所属图层，无法修改坐标", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    feat.Geometry = pt2;
                    await table.UpdateFeatureAsync(feat);
                    // 通过要素表定位图层名并强制重绘，让地图上的点立即移动到新坐标
                    var layerName = Main.GetLayerNameForTable(table);
                    if (!string.IsNullOrEmpty(layerName))
                        Main.RefreshLayerDisplay(layerName);
                    TxtCorrectStatus.Text = $"已修改点位坐标: ({pt2.X:F6}, {pt2.Y:F6})";
                    MessageBox.Show("坐标修改成功", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"修改失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>属性及编号修改：打开所选图层的属性表。</summary>
        private void BtnEditAttributes_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;
            Main.OpenAttributeTableForSelected();
        }

        /// <summary>附件修改：打开照片管理器。</summary>
        private void BtnEditAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;
            BtnPhotoManager_Click(sender, e);
        }

        private void BtnLinkAttribute_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;

            var layerItems = Main.LayerManager.Layers.ToList();
            if (layerItems.Count < 2)
            {
                MessageBox.Show("需要至少两个图层才能进行属性关联。\n请先加载包含关联数据的多个图层。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBox.Show("属性关联功能：\n\n1. 选择源图层和目标图层\n2. 选择关联字段（如 ID）\n3. 系统根据匹配字段自动关联属性\n\n功能待完善，当前仅支持属性表手动编辑。",
                "属性关联", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static bool IsImage(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif";
        }
    }

    // Helper dialog for picking a target layer
    public class PickLayerDialog : Window
    {
        public string SelectedLayer { get; private set; }
        private ComboBox _combo;

        public PickLayerDialog(List<string> layerNames, string title, Window owner)
        {
            Owner = owner;
            Title = title;
            Width = 360; Height = 160;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0D, 0x11, 0x17));
            ResizeMode = ResizeMode.NoResize;

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var lbl = new System.Windows.Controls.TextBlock { Text = "选择目标图层:", Foreground = System.Windows.Media.Brushes.LightGray, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
            _combo = new System.Windows.Controls.ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 12), Foreground = System.Windows.Media.Brushes.LightGray, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x1B, 0x22)), BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D)) };
            foreach (var n in layerNames) _combo.Items.Add(n);
            if (_combo.Items.Count > 0) _combo.SelectedIndex = 0;

            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var okBtn = new System.Windows.Controls.Button { Content = "确定", Width = 70, Height = 28, Margin = new Thickness(0, 0, 6, 0), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x23, 0x86, 0x36)), Foreground = System.Windows.Media.Brushes.White, BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0xA0, 0x43)) };
            var cancelBtn = new System.Windows.Controls.Button { Content = "取消", Width = 70, Height = 28 };
            btnPanel.Children.Add(okBtn); btnPanel.Children.Add(cancelBtn);

            System.Windows.Controls.Grid.SetRow(lbl, 0); System.Windows.Controls.Grid.SetRow(_combo, 1); System.Windows.Controls.Grid.SetRow(btnPanel, 2);
            grid.Children.Add(lbl); grid.Children.Add(_combo); grid.Children.Add(btnPanel);
            Content = grid;

            okBtn.Click += (s, e) => { SelectedLayer = _combo.SelectedItem?.ToString(); if (SelectedLayer != null) { DialogResult = true; Close(); } };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
        }
    }
}
