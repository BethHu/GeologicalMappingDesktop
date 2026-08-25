using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace GeologicalMappingDesktop
{
    public enum MediaCategory { Photo, Sketch, Video }

    public class PhotoInfo
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public string PointId { get; set; }
        public string RouteId { get; set; }
        public string DateGroup { get; set; }
        public string GroupKey { get; set; }
        public string DisplayText { get; set; }
    }

    public partial class PhotoManagerWindow : Window
    {
        private string _photoDir;
        private string _sketchDir;
        private string _videoDir;
        private MediaCategory _currentCategory = MediaCategory.Photo;
        private ObservableCollection<PhotoInfo> _items = new ObservableCollection<PhotoInfo>();
        private Dictionary<string, string> _pointRouteMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _knownPointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private MainWindow _main;

        private string CurrentDir
        {
            get
            {
                if (_currentCategory == MediaCategory.Sketch) return _sketchDir;
                if (_currentCategory == MediaCategory.Video) return _videoDir;
                return _photoDir;
            }
        }
        private string CategoryLabel
        {
            get
            {
                if (_currentCategory == MediaCategory.Sketch) return "草图";
                if (_currentCategory == MediaCategory.Video) return "视频";
                return "照片";
            }
        }

        public PhotoManagerWindow(string photoDir, string sketchDir, string videoDir = null)
        {
            _photoDir = photoDir;
            _sketchDir = sketchDir;
            _videoDir = videoDir ?? (string.IsNullOrEmpty(photoDir) ? "" :
                Path.Combine(Path.GetDirectoryName(photoDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "", "Videos"));
            InitializeComponent();
            PhotoList.ItemsSource = _items;
            CboGroupMode.Items.Add("按点位");
            CboGroupMode.Items.Add("按路线");
            CboGroupMode.Items.Add("按日期");
            CboGroupMode.SelectedIndex = 0;
            _main = Owner as MainWindow;
            UpdateTabStyle();
            TxtListTitle.Text = "照片列表";
            _ = LoadMediaAsync();
        }

        private void SetTabActive(Button active, params Button[] inactive)
        {
            active.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x23, 0x86, 0x36));
            active.Foreground = System.Windows.Media.Brushes.White;
            foreach (var b in inactive)
            {
                b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x21, 0x26, 0x2D));
                b.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x94, 0x9E));
            }
        }

        private void UpdateTabStyle()
        {
            if (_currentCategory == MediaCategory.Photo)
                SetTabActive(BtnTabPhoto, BtnTabSketch, BtnTabVideo);
            else if (_currentCategory == MediaCategory.Sketch)
                SetTabActive(BtnTabSketch, BtnTabPhoto, BtnTabVideo);
            else
                SetTabActive(BtnTabVideo, BtnTabPhoto, BtnTabSketch);

            Title = CategoryLabel + "管理器";
            TxtListTitle.Text = CategoryLabel + "列表";
            BtnPlayVideo.Visibility = _currentCategory == MediaCategory.Video ? Visibility.Visible : Visibility.Collapsed;
            BtnStopVideo.Visibility = _currentCategory == MediaCategory.Video ? Visibility.Visible : Visibility.Collapsed;
            if (_currentCategory != MediaCategory.Video)
                StopInternalVideo();
        }

        private async void BtnTabPhoto_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCategory == MediaCategory.Photo) return;
            _currentCategory = MediaCategory.Photo;
            UpdateTabStyle();
            await LoadMediaAsync();
        }

        private async void BtnTabSketch_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCategory == MediaCategory.Sketch) return;
            _currentCategory = MediaCategory.Sketch;
            UpdateTabStyle();
            await LoadMediaAsync();
        }

        private async void BtnTabVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCategory == MediaCategory.Video) return;
            _currentCategory = MediaCategory.Video;
            UpdateTabStyle();
            await LoadMediaAsync();
        }

        private async Task LoadMediaAsync()
        {
            _items.Clear();
            CboFilter.Items.Clear();
            CboFilter.Items.Add("全部");
            CboFilter.SelectedIndex = 0;

            string dir = CurrentDir;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                TxtSummary.Text = $"{CategoryLabel}目录不存在: {dir}";
                return;
            }

            var files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => _currentCategory == MediaCategory.Video ? IsVideo(f) : IsImage(f))
                .OrderBy(f => f)
                .ToList();

            // Build pointId→routeId map + known point IDs from feature layers (async)
            _pointRouteMap.Clear();
            _knownPointIds.Clear();
            if (_main != null)
            {
                try { await Task.Run(() => BuildPointRouteMap()); } catch { }
            }

            foreach (var f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                string fullName = Path.GetFileName(f);
                string pointId = "";
                string routeId = "";
                string dateGroup = "";

                // Try to match filename against known point IDs from feature table first
                // (e.g., "GP0001_xxx.jpg" matches point "GP0001", but "GP00010_xxx.jpg" does NOT)
                foreach (var knownId in _knownPointIds)
                {
                    if (name.Equals(knownId, StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith(knownId + "_", StringComparison.OrdinalIgnoreCase))
                    {
                        pointId = knownId;
                        break;
                    }
                }

                // Fallback: extract prefix before first '_'
                if (string.IsNullOrEmpty(pointId))
                {
                    int idx = name.IndexOf('_');
                    string prefix = idx > 0 ? name.Substring(0, idx) : name;
                    if (prefix.Length >= 2)
                        pointId = prefix;
                }

                if (!string.IsNullOrEmpty(pointId) && _pointRouteMap.TryGetValue(pointId, out var rid))
                    routeId = rid;

                try
                {
                    var lastWrite = File.GetLastWriteTime(f);
                    dateGroup = lastWrite.ToString("yyyy-MM");
                }
                catch { dateGroup = "未知"; }

                _items.Add(new PhotoInfo
                {
                    FileName = fullName,
                    FullPath = f,
                    PointId = pointId,
                    RouteId = routeId,
                    DateGroup = dateGroup,
                    GroupKey = pointId,
                    DisplayText = $"[{pointId}] {fullName}"
                });
            }

            UpdateFilterList();
            TxtSummary.Text = $"共 {_items.Count} 个{CategoryLabel}";
        }

        private void BuildPointRouteMap()
        {
            if (_main == null) return;
            try
            {
                foreach (var layerItem in _main.LayerManager.Layers)
                {
                    var table = _main.GetFeatureTableFromLayer(layerItem.Layer) ?? layerItem.MemoryFeatureTable;
                    if (table == null || table.GeometryType != Esri.ArcGISRuntime.Geometry.GeometryType.Point) continue;
                    var features = System.Threading.Tasks.Task.Run(async () =>
                        await table.QueryFeaturesAsync(new Esri.ArcGISRuntime.Data.QueryParameters())).GetAwaiter().GetResult();
                    foreach (var f in features)
                    {
                        string pid = null;
                        if (f.Attributes.TryGetValue("id", out var idv) && idv != null)
                            pid = idv.ToString();
                        else if (f.Attributes.TryGetValue("SourceID", out var sidv) && sidv != null)
                            pid = sidv.ToString();
                        if (string.IsNullOrEmpty(pid)) continue;
                        _knownPointIds.Add(pid);

                        string rid = null;
                        if (f.Attributes.TryGetValue("routeId", out var rv) && rv != null)
                            rid = rv.ToString();
                        if (!string.IsNullOrEmpty(rid) && !_pointRouteMap.ContainsKey(pid))
                            _pointRouteMap[pid] = rid;
                    }
                }
            }
            catch { }
        }

        private void UpdateFilterList()
        {
            string mode = CboGroupMode.SelectedItem as string ?? "";
            string currentFilter = CboFilter.SelectedItem as string;
            CboFilter.Items.Clear();
            CboFilter.Items.Add("全部");

            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _items)
            {
                if (mode == "按路线")
                    groups.Add(string.IsNullOrEmpty(p.RouteId) ? "未关联路线" : p.RouteId);
                else if (mode == "按日期")
                    groups.Add(string.IsNullOrEmpty(p.DateGroup) ? "未知" : p.DateGroup);
                else
                    groups.Add(string.IsNullOrEmpty(p.PointId) ? "未关联点位" : p.PointId);
            }

            foreach (var g in groups.OrderBy(g => g))
                CboFilter.Items.Add(g);

            if (currentFilter != null && CboFilter.Items.Contains(currentFilter))
                CboFilter.SelectedItem = currentFilter;
            else
                CboFilter.SelectedIndex = 0;
        }

        private void ApplyFilter()
        {
            string mode = CboGroupMode.SelectedItem as string ?? "";
            string filter = CboFilter.SelectedItem as string ?? "全部";

            foreach (var p in _items)
            {
                string groupLabel = "";
                if (mode == "按路线")
                    groupLabel = string.IsNullOrEmpty(p.RouteId) ? "未关联路线" : p.RouteId;
                else if (mode == "按日期")
                    groupLabel = string.IsNullOrEmpty(p.DateGroup) ? "未知" : p.DateGroup;
                else
                    groupLabel = string.IsNullOrEmpty(p.PointId) ? "未关联点位" : p.PointId;

                p.GroupKey = groupLabel;
                p.DisplayText = $"[{groupLabel}] {p.FileName}";
            }

            var filtered = filter == "全部"
                ? _items.ToList()
                : _items.Where(p => string.Equals(p.GroupKey, filter, StringComparison.OrdinalIgnoreCase)).ToList();

            _items.Clear();
            foreach (var p in filtered)
                _items.Add(p);

            int total = 0;
            try
            {
                string dir = CurrentDir;
                if (Directory.Exists(dir))
                    total = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                        .Count(f => _currentCategory == MediaCategory.Video ? IsVideo(f) : IsImage(f));
            }
            catch { }
            TxtSummary.Text = $"共 {total} 个{CategoryLabel}，当前显示 {_items.Count} 个";
        }

        private void CboGroupMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFilterList();
        }

        private void CboFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void PhotoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PhotoList.SelectedItem is PhotoInfo photo)
            {
                if (File.Exists(photo.FullPath))
                {
                    try
                    {
                        long sizeKb = new FileInfo(photo.FullPath).Length / 1024;
                        TxtPhotoInfo.Text = $"{photo.FileName}\n{sizeKb} KB";
                        if (!string.IsNullOrEmpty(photo.PointId))
                            TxtPhotoInfo.Text += $"\n点位: {photo.PointId}";
                        if (!string.IsNullOrEmpty(photo.RouteId))
                            TxtPhotoInfo.Text += $"\n路线: {photo.RouteId}";
                        TxtPhotoInfo.Text += $"\n日期: {photo.DateGroup}";

                        if (_currentCategory == MediaCategory.Video || IsVideo(photo.FullPath))
                        {
                            StopInternalVideo();
                            PhotoPreview.Source = null;
                            PhotoPreview.Visibility = Visibility.Collapsed;
                            VideoPlayer.Visibility = Visibility.Collapsed;
                            TxtVideoPlaceholder.Visibility = Visibility.Visible;
                            BtnPlayVideo.Visibility = Visibility.Visible;
                            BtnStopVideo.Visibility = Visibility.Visible;
                            BtnPlayVideo.Content = "播放";
                        }
                        else
                        {
                            StopInternalVideo();
                            PhotoPreview.Visibility = Visibility.Visible;
                            VideoPlayer.Visibility = Visibility.Collapsed;
                            TxtVideoPlaceholder.Visibility = Visibility.Collapsed;
                            BtnPlayVideo.Visibility = Visibility.Collapsed;
                            BtnStopVideo.Visibility = Visibility.Collapsed;
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(photo.FullPath);
                            bitmap.EndInit();
                            PhotoPreview.Source = bitmap;
                        }
                    }
                    catch
                    {
                        PhotoPreview.Source = null;
                        TxtPhotoInfo.Text = $"{photo.FileName} (无法预览)";
                    }
                }
            }
        }

        private bool _videoPlaying;

        private void BtnPlayVideo_Click(object sender, RoutedEventArgs e)
        {
            if (!(PhotoList.SelectedItem is PhotoInfo photo)) return;
            if (!File.Exists(photo.FullPath)) return;
            try
            {
                if (_videoPlaying)
                {
                    VideoPlayer.Pause();
                    _videoPlaying = false;
                    BtnPlayVideo.Content = "继续";
                    return;
                }

                // 内置播放器（不依赖系统默认关联程序）
                PhotoPreview.Visibility = Visibility.Collapsed;
                TxtVideoPlaceholder.Visibility = Visibility.Collapsed;
                VideoPlayer.Visibility = Visibility.Visible;
                if (VideoPlayer.Source == null ||
                    !string.Equals(VideoPlayer.Source.LocalPath, photo.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    VideoPlayer.Source = new Uri(photo.FullPath, UriKind.Absolute);
                    VideoPlayer.Position = TimeSpan.Zero;
                }
                VideoPlayer.Play();
                _videoPlaying = true;
                BtnPlayVideo.Content = "暂停";
            }
            catch (Exception ex)
            {
                // 回退：尝试用 cmd start / 资源管理器打开
                try
                {
                    System.Diagnostics.Process.Start("cmd.exe", $"/c start \"\" \"{photo.FullPath}\"");
                }
                catch
                {
                    MessageBox.Show($"无法播放视频: {ex.Message}\n\n请确认系统已安装能播放该格式的解码器（如 mp4）。",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnStopVideo_Click(object sender, RoutedEventArgs e)
        {
            StopInternalVideo();
            TxtVideoPlaceholder.Visibility = Visibility.Visible;
            VideoPlayer.Visibility = Visibility.Collapsed;
            BtnPlayVideo.Content = "播放";
        }

        private void StopInternalVideo()
        {
            try
            {
                VideoPlayer.Stop();
                VideoPlayer.Source = null;
            }
            catch { }
            _videoPlaying = false;
            if (BtnPlayVideo != null)
                BtnPlayVideo.Content = "播放";
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            _videoPlaying = false;
            BtnPlayVideo.Content = "播放";
            try { VideoPlayer.Position = TimeSpan.Zero; } catch { }
        }

        private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            _videoPlaying = false;
            BtnPlayVideo.Content = "播放";
            VideoPlayer.Visibility = Visibility.Collapsed;
            TxtVideoPlaceholder.Visibility = Visibility.Visible;
            MessageBox.Show($"视频播放失败: {e.ErrorException?.Message ?? "未知错误"}\n\nWPF MediaElement 依赖系统编解码器，可安装 K-Lite Codec 或将视频转为 H.264/MP4。",
                "播放失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        protected override void OnClosed(EventArgs e)
        {
            StopInternalVideo();
            base.OnClosed(e);
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            string prevFilter = CboFilter.SelectedItem as string;
            int prevMode = CboGroupMode.SelectedIndex;
            await LoadMediaAsync();
            CboGroupMode.SelectedIndex = prevMode;
            if (prevFilter != null && CboFilter.Items.Contains(prevFilter))
                CboFilter.SelectedItem = prevFilter;
            else
                CboFilter.SelectedIndex = 0;
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string dir = CurrentDir;
            if (Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", dir);
        }

        private string GetSelectedFileName()
        {
            if (PhotoList.SelectedItem is PhotoInfo photo)
                return photo.FileName;
            return null;
        }

        private void BtnRename_Click(object sender, RoutedEventArgs e)
        {
            string fileName = GetSelectedFileName();
            if (fileName == null) return;
            string dir = CurrentDir;
            string oldPath = Path.Combine(dir, fileName);

            var dialog = new Window
            {
                Title = $"重命名{CategoryLabel}",
                Width = 360, Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0D, 0x11, 0x17)),
                ResizeMode = ResizeMode.NoResize
            };
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var txtBox = new System.Windows.Controls.TextBox
            {
                Text = fileName,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x1B, 0x22)),
                Foreground = System.Windows.Media.Brushes.LightGray,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D)),
                Height = 28,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var okBtn = new System.Windows.Controls.Button
            {
                Content = "确定", Width = 70, Height = 28, Margin = new Thickness(0, 0, 6, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x23, 0x86, 0x36)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0xA0, 0x43))
            };
            var cancelBtn = new System.Windows.Controls.Button { Content = "取消", Width = 70, Height = 28 };
            btnPanel.Children.Add(okBtn); btnPanel.Children.Add(cancelBtn);

            System.Windows.Controls.Grid.SetRow(txtBox, 0);
            System.Windows.Controls.Grid.SetRow(btnPanel, 1);
            grid.Children.Add(txtBox); grid.Children.Add(btnPanel);
            dialog.Content = grid;

            cancelBtn.Click += (s, args) => dialog.Close();
            okBtn.Click += async (s, args) =>
            {
                string newName = txtBox.Text.Trim();
                if (string.IsNullOrEmpty(newName)) return;
                string newPath = Path.Combine(dir, newName);
                if (File.Exists(newPath) && !newName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("文件名已存在", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                try
                {
                    File.Move(oldPath, newPath);
                    await LoadMediaAsync();
                    dialog.Close();
                }
                catch (Exception ex) { MessageBox.Show($"重命名失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            dialog.ShowDialog();
        }

        private async void BtnLinkPoint_Click(object sender, RoutedEventArgs e)
        {
            if (!(PhotoList.SelectedItem is PhotoInfo photo)) return;

            // Query all point IDs from feature layers
            var allPointIds = new System.Collections.Generic.List<string>();
            if (_main != null)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        foreach (var layerItem in _main.LayerManager.Layers)
                        {
                            var table = _main.GetFeatureTableFromLayer(layerItem.Layer) ?? layerItem.MemoryFeatureTable;
                            if (table == null || table.GeometryType != Esri.ArcGISRuntime.Geometry.GeometryType.Point) continue;
                            var features = System.Threading.Tasks.Task.Run(async () =>
                                await table.QueryFeaturesAsync(new Esri.ArcGISRuntime.Data.QueryParameters())).GetAwaiter().GetResult();
                            foreach (var f in features)
                            {
                                string pid = null;
                                if (f.Attributes.TryGetValue("id", out var idv) && idv != null)
                                    pid = idv.ToString();
                                else if (f.Attributes.TryGetValue("SourceID", out var sidv) && sidv != null)
                                    pid = sidv.ToString();
                                if (!string.IsNullOrEmpty(pid) && !allPointIds.Contains(pid))
                                    allPointIds.Add(pid);
                            }
                        }
                    });
                }
                catch { }
            }

            if (allPointIds.Count == 0)
            {
                MessageBox.Show($"未找到任何点位ID。\n请先导入或同步点位数据。", "无点位数据", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Show selection dialog
            var picker = new Window
            {
                Title = $"选择关联点位 - {photo.FileName}",
                Width = 320, Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0D, 0x11, 0x17)),
                ResizeMode = ResizeMode.NoResize
            };
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var lbl = new System.Windows.Controls.TextBlock
            {
                Text = "选择要关联到的点位:",
                Foreground = System.Windows.Media.Brushes.LightGray,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var listBox = new System.Windows.Controls.ListBox
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x1B, 0x22)),
                Foreground = System.Windows.Media.Brushes.LightGray,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D)),
                Height = 260,
                Margin = new Thickness(0, 0, 0, 8)
            };
            foreach (var pid in allPointIds.OrderBy(p => p))
                listBox.Items.Add(pid);

            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var okBtn = new System.Windows.Controls.Button
            {
                Content = "关联", Width = 70, Height = 28, Margin = new Thickness(0, 0, 6, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x23, 0x86, 0x36)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0xA0, 0x43))
            };
            var cancelBtn = new System.Windows.Controls.Button { Content = "取消", Width = 70, Height = 28 };
            btnPanel.Children.Add(okBtn); btnPanel.Children.Add(cancelBtn);

            System.Windows.Controls.Grid.SetRow(lbl, 0);
            System.Windows.Controls.Grid.SetRow(listBox, 1);
            System.Windows.Controls.Grid.SetRow(btnPanel, 2);
            grid.Children.Add(lbl); grid.Children.Add(listBox); grid.Children.Add(btnPanel);
            picker.Content = grid;

            string selectedPointId = null;
            okBtn.Click += (s, args) =>
            {
                selectedPointId = listBox.SelectedItem as string;
                if (!string.IsNullOrEmpty(selectedPointId))
                    picker.DialogResult = true;
                else
                    MessageBox.Show("请先选择一个点位", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            cancelBtn.Click += (s, args) => picker.Close();

            if (picker.ShowDialog() == true && !string.IsNullOrEmpty(selectedPointId))
            {
                // Rename file to include point ID as prefix
                string dir = CurrentDir;
                string oldPath = photo.FullPath;
                string ext = Path.GetExtension(photo.FileName);
                string newName = $"{selectedPointId}_{photo.FileName}";
                string newPath = Path.Combine(dir, newName);
                try
                {
                    if (!File.Exists(newPath))
                    {
                        File.Move(oldPath, newPath);
                        await LoadMediaAsync();
                        MessageBox.Show($"已关联点位 {selectedPointId}\n\n文件已重命名为: {newName}", "关联成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"文件 {newName} 已存在，无法重命名", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"关联失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportPhoto_Click(object sender, RoutedEventArgs e)
        {
            string fileName = GetSelectedFileName();
            if (fileName == null) return;
            string dir = CurrentDir;
            string srcPath = Path.Combine(dir, fileName);
            if (!File.Exists(srcPath)) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = _currentCategory == MediaCategory.Video
                    ? "视频文件|*.mp4;*.mov;*.avi;*.mkv;*.3gp|所有文件|*.*"
                    : "图片文件|*.jpg;*.jpeg;*.png;*.bmp",
                FileName = fileName,
                Title = $"导出{CategoryLabel}"
            };
            if (dlg.ShowDialog() == true)
            {
                try { File.Copy(srcPath, dlg.FileName, true); MessageBox.Show("导出成功", "成功", MessageBoxButton.OK, MessageBoxImage.Information); }
                catch (Exception ex) { MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private static bool IsImage(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif";
        }

        private static bool IsVideo(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mkv" || ext == ".3gp" || ext == ".wmv";
        }
    }
}
