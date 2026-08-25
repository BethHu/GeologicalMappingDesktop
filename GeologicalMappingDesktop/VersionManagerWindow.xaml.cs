using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;

namespace GeologicalMappingDesktop
{
    public partial class VersionManagerWindow : Window
    {
        public ObservableCollection<VersionSnapshot> Versions { get; } = new ObservableCollection<VersionSnapshot>();
        public ObservableCollection<VersionSnapshot> CmpSource { get; } = new ObservableCollection<VersionSnapshot>();

        public event Action<VersionSnapshot> SaveVersionRequested;
        public event Action<VersionSnapshot> RollbackRequested;

        private MainWindow _main;

        public VersionManagerWindow(MainWindow main)
        {
            _main = main;
            InitializeComponent();
            RdoDetail.Checked += RdoDetail_Checked;
            RdoCompare.Checked += RdoCompare_Checked;
            VersionList.ItemsSource = Versions;
        }

        public void LoadVersions(System.Collections.Generic.List<VersionSnapshot> versions)
        {
            Versions.Clear();
            if (versions != null)
                foreach (var v in versions)
                    Versions.Add(v);
            RefreshCompareBoxes();
        }

        private void RefreshCompareBoxes()
        {
            CmpSource.Clear();
            foreach (var v in Versions)
                CmpSource.Add(v);
            if (CmpSource.Count >= 2)
            {
                CmpVersionA.SelectedIndex = CmpSource.Count - 2;
                CmpVersionB.SelectedIndex = CmpSource.Count - 1;
            }
            else if (CmpSource.Count == 1)
            {
                CmpVersionA.SelectedIndex = 0;
            }
        }

        private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ShowVersionDetail();
        }

        private void ShowVersionDetail()
        {
            var sel = VersionList.SelectedItem as VersionSnapshot;
            if (sel == null && Versions.Count > 0)
                sel = Versions.Last();
            if (sel == null)
            {
                TxtVersionName.Text = "";
                TxtVersionMeta.Text = "";
                TxtVersionDesc.Text = "";
                ResultList.ItemsSource = null;
                TxtResultHeader.Text = "";
                return;
            }
            TxtVersionName.Text = sel.Name;
            TxtVersionMeta.Text = $"创建时间: {sel.CreatedAt:yyyy-MM-dd HH:mm:ss}  |  创建人: {sel.Creator}  |  图层: {sel.LayerStates.Count}";
            TxtVersionDesc.Text = string.IsNullOrEmpty(sel.Description) ? "(无描述)" : sel.Description;

            // Show layer summary
            var items = new ObservableCollection<ResultItem>();
            foreach (var ls in sel.LayerStates)
            {
                items.Add(new ResultItem
                {
                    Label = $"📦 {ls.LayerName} ({ls.LayerType})",
                    Detail = $"要素: {ls.Features.Count} | 字段: {ls.Fields.Count} | WKID: {ls.Wkid}",
                    Color = "#58A6FF"
                });
            }
            ResultList.ItemsSource = items;
            TxtResultHeader.Text = $"版本内容 ({sel.LayerStates.Sum(l => l.Features.Count)} 要素, {sel.LayerStates.Count} 图层)";
        }

        private void RdoDetail_Checked(object sender, RoutedEventArgs e)
        {
            if (DetailPanel == null || ComparePanel == null) return;
            DetailPanel.Visibility = Visibility.Visible;
            ComparePanel.Visibility = Visibility.Collapsed;
            if (RdoDetail.IsChecked == true)
                ShowVersionDetail();
        }

        private void RdoCompare_Checked(object sender, RoutedEventArgs e)
        {
            if (DetailPanel == null || ComparePanel == null) return;
            DetailPanel.Visibility = Visibility.Collapsed;
            ComparePanel.Visibility = Visibility.Visible;
            TxtResultHeader.Text = "选择两个版本进行对比";
            ResultList.ItemsSource = null;
            RefreshCompareBoxes();
        }

        private async void BtnRunCompare_Click(object sender, RoutedEventArgs e)
        {
            var vA = CmpVersionA.SelectedItem as VersionSnapshot;
            var vB = CmpVersionB.SelectedItem as VersionSnapshot;
            if (vA == null || vB == null)
            {
                MessageBox.Show("请选择两个版本进行对比", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (vA == vB)
            {
                MessageBox.Show("请选择两个不同的版本", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var diffs = ComputeDiff(vA, vB);
                var items = new ObservableCollection<ResultItem>();

                int totalAdd = diffs.Sum(d => d.AddedCount);
                int totalMod = diffs.Sum(d => d.ModifiedCount);
                int totalDel = diffs.Sum(d => d.DeletedCount);
                TxtResultHeader.Text = $"{vA.Name}  →  {vB.Name}  |  +{totalAdd}  ~{totalMod}  -{totalDel}";

                foreach (var diff in diffs)
                {
                    items.Add(new ResultItem
                    {
                        Label = $"图层: {diff.LayerName}",
                        Detail = $"增加 {diff.AddedCount}  修改 {diff.ModifiedCount}  删除 {diff.DeletedCount}",
                        Color = "#E6EDF3"
                    });

                    foreach (var added in diff.Added)
                    {
                        string geomType = added.GeometryJson != null ? "几何" : "无几何";
                        items.Add(new ResultItem { Label = $"  ➕ 新增要素 ({geomType})", Detail = $"属性: {string.Join(", ", added.Attributes.Keys.Take(3))}", Color = "#3FB950" });
                    }
                    foreach (var mod in diff.Modified)
                    {
                        string changes = "";
                        if (mod.GeometryChanged) changes += "几何 ";
                        changes += string.Join(", ", mod.ChangedAttributes.Take(3));
                        if (mod.ChangedAttributes.Count > 3) changes += "...";
                        items.Add(new ResultItem { Label = $"  ✏️ 修改要素", Detail = $"变更: {changes}", Color = "#D29922" });
                    }
                    foreach (var del in diff.Deleted)
                    {
                        string geomType = del.GeometryJson != null ? "几何" : "无几何";
                        items.Add(new ResultItem { Label = $"  ❌ 删除要素 ({geomType})", Detail = $"属性: {string.Join(", ", del.Attributes.Keys.Take(3))}", Color = "#F85149" });
                    }
                }

                if (items.Count == 0)
                    items.Add(new ResultItem { Label = "两个版本之间没有差异", Detail = "", Color = "#8B949E" });

                ResultList.ItemsSource = items;
            }
            catch (Exception ex)
            {
                MessageBox.Show("对比失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static System.Collections.Generic.List<VersionDiff> ComputeDiff(VersionSnapshot vA, VersionSnapshot vB)
        {
            var diffs = new System.Collections.Generic.List<VersionDiff>();

            var allLayers = vA.LayerStates.Concat(vB.LayerStates)
                .Select(l => l.LayerName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var layerName in allLayers)
            {
                var lvA = vA.LayerStates.FirstOrDefault(l => string.Equals(l.LayerName, layerName, StringComparison.OrdinalIgnoreCase));
                var lvB = vB.LayerStates.FirstOrDefault(l => string.Equals(l.LayerName, layerName, StringComparison.OrdinalIgnoreCase));

                var diff = new VersionDiff { LayerName = layerName };

                var featuresA = lvA?.Features ?? new System.Collections.Generic.List<FeatureState>();
                var featuresB = lvB?.Features ?? new System.Collections.Generic.List<FeatureState>();

                // Match by geometry comparison (simple approach: match by geometry hash)
                var usedB = new System.Collections.Generic.HashSet<int>();

                foreach (var fa in featuresA)
                {
                    int matchIdx = -1;
                    for (int j = 0; j < featuresB.Count; j++)
                    {
                        if (usedB.Contains(j)) continue;
                        if (GeometriesEqual(fa.GeometryJson, featuresB[j].GeometryJson))
                        {
                            matchIdx = j;
                            break;
                        }
                    }

                    if (matchIdx >= 0)
                    {
                        usedB.Add(matchIdx);
                        var fb = featuresB[matchIdx];
                        var attrChanges = new System.Collections.Generic.List<string>();
                        bool geomChanged = false;

                        // Compare attributes
                        var allKeys = fa.Attributes.Keys.Union(fb.Attributes.Keys, StringComparer.OrdinalIgnoreCase);
                        foreach (var key in allKeys)
                        {
                            var va = fa.Attributes.ContainsKey(key) ? fa.Attributes[key] : null;
                            var vb = fb.Attributes.ContainsKey(key) ? fb.Attributes[key] : null;
                            if (!ObjectEquals(va, vb))
                                attrChanges.Add(key);
                        }

                        if (attrChanges.Count > 0 || geomChanged)
                        {
                            diff.Modified.Add(new FeatureDiffEntry
                            {
                                Before = fa,
                                After = fb,
                                ChangedAttributes = attrChanges,
                                GeometryChanged = geomChanged
                            });
                        }
                    }
                    else
                    {
                        // In A but not B = deleted
                        diff.Deleted.Add(fa);
                    }
                }

                // Remaining in B but not A = added
                for (int j = 0; j < featuresB.Count; j++)
                {
                    if (!usedB.Contains(j))
                        diff.Added.Add(featuresB[j]);
                }

                diffs.Add(diff);
            }

            return diffs;
        }

        private static bool GeometriesEqual(string jsonA, string jsonB)
        {
            if (string.IsNullOrEmpty(jsonA) && string.IsNullOrEmpty(jsonB)) return true;
            if (string.IsNullOrEmpty(jsonA) || string.IsNullOrEmpty(jsonB)) return false;
            try
            {
                var gA = Esri.ArcGISRuntime.Geometry.Geometry.FromJson(jsonA);
                var gB = Esri.ArcGISRuntime.Geometry.Geometry.FromJson(jsonB);
                return GeometryEngine.Equals(gA, gB);
            }
            catch { return jsonA == jsonB; }
        }

        private static bool ObjectEquals(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a is double da && b is double db) return Math.Abs(da - db) < 1e-6;
            if (a is float fa && b is float fb) return Math.Abs(fa - fb) < 1e-6;
            return a.Equals(b);
        }

        private void BtnSaveVersion_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Window
            {
                Title = "保存版本",
                Width = 380, Height = 240,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17)),
                Foreground = Brushes.White
            };
            var g = new Grid { Margin = new Thickness(12) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tbCreator = new TextBox { Text = Environment.UserName, Height = 24, Margin = new Thickness(0, 4, 0, 4) };
            var tbDesc = new TextBox { Height = 60, Margin = new Thickness(0, 4, 0, 4), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            int r = 0;
            g.Children.Add(CreateLabel("版本名:", 0, r)); g.Children.Add(CreateTb($"v{DateTime.Now:yyyyMMdd-HHmmss}", 1, r++));
            g.Children.Add(CreateLabel("创建人:", 0, r)); Grid.SetColumn(tbCreator, 1); Grid.SetRow(tbCreator, r); g.Children.Add(tbCreator); r++;
            g.Children.Add(CreateLabel("描述:", 0, r)); Grid.SetColumn(tbDesc, 1); Grid.SetRow(tbDesc, r); g.Children.Add(tbDesc); r++;

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(btnPanel, r); Grid.SetColumnSpan(btnPanel, 2);
            var okBtn = new Button { Content = "确定保存", Width = 80, Height = 28, Margin = new Thickness(0, 0, 6, 0), Background = new SolidColorBrush(Color.FromRgb(0x23, 0x86, 0x36)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43)), Cursor = System.Windows.Input.Cursors.Hand };
            var cancelBtn = new Button { Content = "取消", Width = 70, Height = 28, Background = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x48, 0x4F, 0x58)), Cursor = System.Windows.Input.Cursors.Hand };
            btnPanel.Children.Add(okBtn); btnPanel.Children.Add(cancelBtn);
            g.Children.Add(btnPanel);
            dlg.Content = g;

            cancelBtn.Click += (s2, e2) => dlg.Close();
            okBtn.Click += (s2, e2) =>
            {
                var nameTb = g.Children.OfType<TextBox>().FirstOrDefault(tb => tb.Text.StartsWith("v20"));
                string name = nameTb?.Text ?? $"v{DateTime.Now:yyyyMMdd-HHmmss}";
                var snap = new VersionSnapshot
                {
                    Name = name,
                    Creator = tbCreator.Text,
                    Description = tbDesc.Text
                };
                SaveVersionRequested?.Invoke(snap);
                dlg.Close();
            };
            dlg.ShowDialog();
        }

        private static Label CreateLabel(string text, int col, int row)
        {
            var lbl = new Label { Content = text, Foreground = Brushes.White, Margin = new Thickness(0, 4, 8, 4) };
            Grid.SetColumn(lbl, col); Grid.SetRow(lbl, row);
            return lbl;
        }
        private static TextBox CreateTb(string text, int col, int row)
        {
            var tb = new TextBox { Text = text, Height = 24, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetColumn(tb, col); Grid.SetRow(tb, row);
            return tb;
        }

        private void BtnRollback_Click(object sender, RoutedEventArgs e)
        {
            var sel = VersionList.SelectedItem as VersionSnapshot;
            if (sel == null && Versions.Count > 0)
                sel = Versions.Last();
            if (sel == null)
            {
                MessageBox.Show("请先选择一个版本", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"确定要回滚到版本「{sel.Name}」吗？\n\n当前数据状态将自动备份为一个新版本。\n该操作不可撤销！",
                "回滚确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                RollbackRequested?.Invoke(sel);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }

    public class ResultItem
    {
        public string Label { get; set; }
        public string Detail { get; set; }
        public string Color { get; set; }
        public Brush Foreground => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Color ?? "#E6EDF3"));
    }
}
