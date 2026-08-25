using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace GeologicalMappingDesktop
{
    public partial class SpatialReferenceBrowser : Window
    {
        public int SelectedWkid { get; private set; }
        public string SelectedWkt { get; private set; }
        public string SelectedDisplayName { get; private set; }

        private readonly ObservableCollection<SrTreeNode> _treeNodes = new ObservableCollection<SrTreeNode>();
        private readonly List<SrItem> _allItems = new List<SrItem>();
        private bool _suppressSelectionEvent;

        public class SrItem
        {
            public int Wkid { get; set; }
            public string Name { get; set; }
            public string Category { get; set; }
            public string DisplayName => $"{Name}  (EPSG:{Wkid})";
            public string Wkt { get; set; }
            public string SearchText => $"{Wkid} {Name}".ToLowerInvariant();
        }

        public class SrTreeNode : INotifyPropertyChanged
        {
            public string Header { get; set; }
            public bool IsCategory { get; set; }
            public bool IsLeaf => !IsCategory && SrItem != null;
            public SrItem SrItem { get; set; }
            public ObservableCollection<SrTreeNode> Children { get; set; } = new ObservableCollection<SrTreeNode>();
            public bool IsVisible
            {
                get => _isVisible;
                set { _isVisible = value; NotifyPropertyChanged(nameof(IsVisible)); NotifyPropertyChanged(nameof(Visibility)); }
            }
            public Visibility Visibility => IsVisible ? Visibility.Visible : Visibility.Collapsed;
            private bool _isVisible = true;

            public event PropertyChangedEventHandler PropertyChanged;
            public void NotifyPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public SpatialReferenceBrowser(int defaultWkid = 4326)
        {
            InitializeComponent();
            LoadSrsData();
            SelectWkid(defaultWkid);
            TreeSrs.ItemsSource = _treeNodes;
        }

        private void LoadSrsData()
        {
            _allItems.Clear();
            _treeNodes.Clear();

            var categories = new Dictionary<string, List<SrItem>>
            {
                ["地理坐标系 (Geographic)"] = new List<SrItem>(),
                ["投影坐标系 (Projected)"] = new List<SrItem>()
            };

            AddGeographicItems(categories["地理坐标系 (Geographic)"]);
            AddProjectedItems(categories["投影坐标系 (Projected)"]);

            foreach (var kv in categories)
            {
                var catNode = new SrTreeNode { Header = $"{kv.Key}  ({kv.Value.Count})", IsCategory = true };
                foreach (var item in kv.Value.OrderBy(i => i.Wkid))
                {
                    catNode.Children.Add(new SrTreeNode { Header = item.DisplayName, SrItem = item, IsCategory = false });
                    _allItems.Add(item);
                }
                _treeNodes.Add(catNode);
            }
        }

        private void AddGeographicItems(List<SrItem> list)
        {
            list.Add(new SrItem { Wkid = 4326, Name = "WGS84", Category = "Geographic" });
            list.Add(new SrItem { Wkid = 4490, Name = "CGCS2000", Category = "Geographic" });
            list.Add(new SrItem { Wkid = 4269, Name = "NAD83", Category = "Geographic" });
            list.Add(new SrItem { Wkid = 4267, Name = "NAD27", Category = "Geographic" });
            list.Add(new SrItem { Wkid = 4610, Name = "Xian 1980", Category = "Geographic" });
            list.Add(new SrItem { Wkid = 4214, Name = "Beijing 1954", Category = "Geographic" });
            list.Add(new SrItem { Wkid = 4258, Name = "ETRS89", Category = "Geographic" });
            list.Add(new SrItem { Wkid = 4322, Name = "WGS 72", Category = "Geographic" });
        }

        private void AddProjectedItems(List<SrItem> list)
        {
            list.Add(MakeItem(3857, "Web Mercator (Pseudo-Mercator)", "Projected"));
            list.Add(MakeItem(4547, "CGCS2000 3-Degree GK Zone 41", "Projected", 123));
            list.Add(MakeItem(4548, "CGCS2000 3-Degree GK Zone 40", "Projected", 120));
            list.Add(MakeItem(4549, "CGCS2000 3-Degree GK Zone 39", "Projected", 117));
            list.Add(MakeItem(4555, "CGCS2000 3-Degree GK Zone 38", "Projected", 114));
            list.Add(MakeItem(4554, "CGCS2000 3-Degree GK Zone 37", "Projected", 111));
            list.Add(MakeItem(4553, "CGCS2000 3-Degree GK Zone 36", "Projected", 108));
            list.Add(MakeItem(4552, "CGCS2000 3-Degree GK Zone 35", "Projected", 105));
            list.Add(MakeItem(4551, "CGCS2000 3-Degree GK Zone 34", "Projected", 102));
            list.Add(MakeItem(4550, "CGCS2000 3-Degree GK Zone 33", "Projected", 99));
            list.Add(MakeItem(4546, "CGCS2000 3-Degree GK Zone 42", "Projected", 126));
            list.Add(MakeItem(4545, "CGCS2000 3-Degree GK Zone 43", "Projected", 129));
            list.Add(MakeItem(4544, "CGCS2000 3-Degree GK Zone 44", "Projected", 132));
            list.Add(MakeItem(4543, "CGCS2000 3-Degree GK Zone 45", "Projected", 135));
            list.Add(MakeItem(4542, "CGCS2000 3-Degree GK Zone 46", "Projected", 138));
            list.Add(MakeItem(4541, "CGCS2000 3-Degree GK Zone 47", "Projected", 141));
            list.Add(MakeItem(4540, "CGCS2000 3-Degree GK Zone 48", "Projected", 144));
            list.Add(MakeItem(4539, "CGCS2000 3-Degree GK Zone 49", "Projected", 147));
            list.Add(MakeItem(4538, "CGCS2000 3-Degree GK Zone 50", "Projected", 150));
            list.Add(MakeItem(4537, "CGCS2000 3-Degree GK Zone 51", "Projected", 153));
            list.Add(MakeItem(4536, "CGCS2000 3-Degree GK Zone 52", "Projected", 156));
            list.Add(MakeItem(4535, "CGCS2000 3-Degree GK Zone 53", "Projected", 159));
            list.Add(MakeItem(4534, "CGCS2000 3-Degree GK Zone 54", "Projected", 162));
            list.Add(MakeItem(4533, "CGCS2000 3-Degree GK Zone 55", "Projected", 165));
            list.Add(MakeItem(4532, "CGCS2000 3-Degree GK Zone 56", "Projected", 168));
            list.Add(MakeItem(4531, "CGCS2000 3-Degree GK Zone 57", "Projected", 171));
            list.Add(MakeItem(4530, "CGCS2000 3-Degree GK Zone 58", "Projected", 174));
            list.Add(MakeItem(4529, "CGCS2000 3-Degree GK Zone 59", "Projected", 177));
            list.Add(MakeItem(4528, "CGCS2000 3-Degree GK Zone 60", "Projected", 180));
            list.Add(MakeUtmItem(32648, "WGS 84 / UTM Zone 48N", "Projected", 48, true));
            list.Add(MakeUtmItem(32649, "WGS 84 / UTM Zone 49N", "Projected", 49, true));
            list.Add(MakeUtmItem(32650, "WGS 84 / UTM Zone 50N", "Projected", 50, true));
            list.Add(MakeUtmItem(32651, "WGS 84 / UTM Zone 51N", "Projected", 51, true));
            list.Add(MakeUtmItem(32750, "WGS 84 / UTM Zone 50S", "Projected", 50, false));
            list.Add(MakeUtmItem(32751, "WGS 84 / UTM Zone 51S", "Projected", 51, false));
        }

        private static SrItem MakeItem(int wkid, string name, string cat, int? centralMeridian = null)
        {
            return new SrItem { Wkid = wkid, Name = name, Category = cat };
        }

        private static SrItem MakeUtmItem(int wkid, string name, string cat, int zone, bool north)
        {
            return new SrItem { Wkid = wkid, Name = name, Category = cat };
        }

        private void SelectWkid(int wkid)
        {
            foreach (var catNode in _treeNodes)
            {
                foreach (var child in catNode.Children)
                {
                    if (child.SrItem != null && child.SrItem.Wkid == wkid)
                    {
                        child.IsVisible = true;
                        catNode.IsVisible = true;
                        _suppressSelectionEvent = true;
                        if (TreeSrs.ItemContainerGenerator.ContainerFromItem(catNode) is TreeViewItem catContainer)
                            catContainer.IsExpanded = true;
                        if (TreeSrs.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem childContainer)
                            childContainer.IsSelected = true;
                        _suppressSelectionEvent = false;
                        UpdatePreview(child.SrItem);
                        return;
                    }
                }
            }
        }

        private void TreeSrs_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_suppressSelectionEvent) return;
            if (e.NewValue is SrTreeNode node && node.SrItem != null)
            {
                UpdatePreview(node.SrItem);
            }
        }

        private void UpdatePreview(SrItem item)
        {
            if (item == null) return;
            TxtSrName.Text = item.Name;
            TxtSrWkid.Text = $"EPSG:{item.Wkid}";
            string wkt = GetWktForItem(item);
            TxtWktPreview.Text = wkt;
            TxtSelectionSummary.Text = $"当前选择：{item.DisplayName}";
            BtnOK.IsEnabled = true;
        }

        private void ClearSelection()
        {
            TxtSrName.Text = "(未选择)";
            TxtSrWkid.Text = "";
            TxtWktPreview.Text = "";
            TxtSelectionSummary.Text = "请在列表中选择坐标系或输入自定义 EPSG";
            BtnOK.IsEnabled = false;
        }

        private static string GetWktForItem(SrItem item)
        {
            if (item == null) return "";
            return GeoreferenceHelper.GetEsriWkt(item.Wkid) ?? "";
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = TxtSearch.Text?.Trim().ToLowerInvariant() ?? "";
            bool hasFilter = !string.IsNullOrEmpty(filter);

            foreach (var catNode in _treeNodes)
            {
                bool catVisible = false;
                foreach (var child in catNode.Children)
                {
                    bool match = !hasFilter ||
                        (child.SrItem != null && child.SrItem.SearchText.Contains(filter));
                    child.IsVisible = match;
                    if (match) catVisible = true;
                }
                catNode.IsVisible = !hasFilter || catVisible;
                if (hasFilter && catVisible)
                {
                    catNode.IsVisible = true;
                }
            }

            if (!hasFilter && TreeSrs.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem firstCat)
            {
                firstCat.IsExpanded = false;
            }
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = "";
        }

        private void TxtCustomEpsg_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(TxtCustomEpsg.Text?.Trim(), out int wkid) && wkid > 0)
            {
                BtnApplyCustom.IsEnabled = true;
                TxtCustomStatus.Text = "";
            }
            else
            {
                BtnApplyCustom.IsEnabled = false;
            }
        }

        private void BtnApplyCustom_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtCustomEpsg.Text?.Trim(), out int wkid) || wkid <= 0)
            {
                TxtCustomStatus.Text = "请输入有效的 EPSG 编号";
                return;
            }
            try
            {
                var sr = Esri.ArcGISRuntime.Geometry.SpatialReference.Create(wkid);
                if (sr == null)
                {
                    TxtCustomStatus.Text = $"EPSG:{wkid} 不受支持";
                    return;
                }
                string name = $"EPSG:{wkid}";
                var item = new SrItem { Wkid = wkid, Name = name, Category = "Custom" };
                string wkt = GetWktForItem(item);
                if (string.IsNullOrEmpty(wkt))
                    wkt = GeoreferenceHelper.GetEsriWkt(wkid);
                item.Wkt = wkt;
                UpdatePreview(item);
                TxtCustomStatus.Text = $"已加载 EPSG:{wkid}";
                TxtCustomStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
            }
            catch (Exception ex)
            {
                TxtCustomStatus.Text = $"加载 EPSG:{wkid} 失败: {ex.Message}";
                TxtCustomStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xDA, 0x36, 0x33));
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (TreeSrs.SelectedItem is SrTreeNode node && node.SrItem != null)
            {
                SelectedWkid = node.SrItem.Wkid;
                SelectedWkt = GetWktForItem(node.SrItem);
                SelectedDisplayName = node.SrItem.DisplayName;
                DialogResult = true;
                Close();
            }
            else if (int.TryParse(TxtCustomEpsg.Text?.Trim(), out int customWkid) && customWkid > 0)
            {
                SelectedWkid = customWkid;
                var tempItem = new SrItem { Wkid = customWkid, Name = $"EPSG:{customWkid}", Category = "Custom" };
                SelectedWkt = GetWktForItem(tempItem);
                SelectedDisplayName = tempItem.DisplayName;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("请选择一个坐标系或输入自定义 EPSG 编号", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
