using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;

namespace GeologicalMappingDesktop
{
    public partial class LayerManagerPanel : UserControl
    {
        public ObservableCollection<LayerItem> Layers { get; set; } = new ObservableCollection<LayerItem>();

        public event Action<string> LayerRemoved;
        public event Action<string, bool> LayerVisibilityChanged;
        public event Action<string> LayerSelected;
        public event Action<string, bool, string> LayerEditRequested;
        public event Action NewLayerRequested;
        public event Action<string> SaveLayerRequested;
        public event Action<string> ZoomToLayerRequested;
        public event Action<string> ShowAttributeTableRequested;
        public event Action<string> BasemapChanged;
        public event Action<string, string> LayerExportRequested;
        public event Action<string, bool> LayerMoveRequested;

        private readonly List<LayerFolderNode> _leafFolders = new List<LayerFolderNode>();
        private readonly Dictionary<string, LayerFolderNode> _folderMap = new Dictionary<string, LayerFolderNode>();

        public LayerManagerPanel()
        {
            InitializeComponent();
            BuildFolderTree();
            LayerTree.ItemsSource = FolderRoots;
            Layers.CollectionChanged += Layers_CollectionChanged;
        }

        /// <summary>文件夹树根节点（TreeView ItemsSource）。</summary>
        public ObservableCollection<LayerFolderNode> FolderRoots { get; } = new ObservableCollection<LayerFolderNode>();

        /// <summary>构建始终显示的文件夹结构（点→7类+未分类；线→路线状态+地质线+未分类；其他文件）。</summary>
        private void BuildFolderTree()
        {
            FolderRoots.Clear();
            _folderMap.Clear();
            _leafFolders.Clear();

            var pointRoot = new LayerFolderNode("点");
            for (int i = 0; i < FieldDataClassification.PointTypes.Length; i++)
            {
                var t = FieldDataClassification.PointTypes[i];
                var sub = new LayerFolderNode(t.DisplayName);
                pointRoot.Children.Add(sub);
                _folderMap["1 点|" + (i + 1) + " " + t.DisplayName] = sub;
                _leafFolders.Add(sub);
            }
            var unclP = new LayerFolderNode(FieldDataClassification.POINT_UNCLASSIFIED);
            pointRoot.Children.Add(unclP);
            _folderMap["1 点|8 " + FieldDataClassification.POINT_UNCLASSIFIED] = unclP;
            _leafFolders.Add(unclP);
            FolderRoots.Add(pointRoot);

            var lineRoot = new LayerFolderNode("线");
            for (int i = 0; i < FieldDataClassification.RouteStatuses.Length; i++)
            {
                var st = FieldDataClassification.RouteStatuses[i];
                var sub = new LayerFolderNode(st.DisplayName);
                lineRoot.Children.Add(sub);
                _folderMap["2 线|" + (i + 1) + " " + st.DisplayName] = sub;
                _leafFolders.Add(sub);
            }
            for (int i = 0; i < FieldDataClassification.GeologicalLineTypes.Length; i++)
            {
                var t = FieldDataClassification.GeologicalLineTypes[i];
                var sub = new LayerFolderNode(t.DisplayName);
                lineRoot.Children.Add(sub);
                _folderMap["2 线|" + (5 + i) + " " + t.DisplayName] = sub;
                _leafFolders.Add(sub);
            }
            var unclL = new LayerFolderNode(FieldDataClassification.LINE_UNCLASSIFIED);
            lineRoot.Children.Add(unclL);
            _folderMap["2 线|11 " + FieldDataClassification.LINE_UNCLASSIFIED] = unclL;
            _leafFolders.Add(unclL);
            FolderRoots.Add(lineRoot);

            var otherRoot = new LayerFolderNode("其他文件");
            FolderRoots.Add(otherRoot);
            _folderMap["3 其他文件|0 "] = otherRoot;
            _leafFolders.Add(otherRoot);
        }

        private void Layers_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (LayerItem it in e.NewItems)
                {
                    HookItem(it);
                    PlaceLayer(it);
                }
                RefreshAllCounts();
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                foreach (LayerItem it in e.OldItems)
                {
                    UnhookItem(it);
                    foreach (var f in _leafFolders) f.Children.Remove(it);
                }
                RefreshAllCounts();
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var f in _leafFolders)
                {
                    for (int i = f.Children.Count - 1; i >= 0; i--)
                    {
                        if (f.Children[i] is LayerItem li) { UnhookItem(li); f.Children.RemoveAt(i); }
                    }
                }
                foreach (var it in Layers) { HookItem(it); PlaceLayer(it); }
                RefreshAllCounts();
            }
            else if (e.Action == NotifyCollectionChangedAction.Move)
            {
                RefreshAllCounts();
            }
        }

        private void HookItem(LayerItem it) => it.PropertyChanged += LayerItem_PropertyChanged;
        private void UnhookItem(LayerItem it) => it.PropertyChanged -= LayerItem_PropertyChanged;

        private void LayerItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LayerItem.TopGroup) || e.PropertyName == nameof(LayerItem.SubGroup))
            {
                if (sender is LayerItem it) PlaceLayer(it);
            }
        }

        /// <summary>按图层的 TopGroup|SubGroup 放入对应文件夹（先从原文件夹移除）。</summary>
        private void PlaceLayer(LayerItem item)
        {
            foreach (var f in _leafFolders) f.Children.Remove(item);
            string key = item.TopGroup + "|" + item.SubGroup;
            if (!_folderMap.TryGetValue(key, out var target))
                target = _folderMap["3 其他文件|0 "];
            if (!target.Children.Contains(item)) target.Children.Add(item);
            RefreshAllCounts();
        }

        private void RefreshAllCounts()
        {
            foreach (var root in FolderRoots) root.UpdateCount();
        }

        public void AddLayer(Layer layer)
        {
            AddLayer(layer, false, string.Empty);
        }

        public void AddLayer(Layer layer, bool isEditable, string filePath)
        {
            if (layer == null) return;

            var existing = Layers.FirstOrDefault(l => !l.IsFolderPlaceholder && l.Name == layer.Name);
            if (existing == null)
            {
                Layers.Add(new LayerItem(layer, isEditable, filePath));
            }
        }

        public void AddLayer(Layer layer, bool isEditable, string filePath, FeatureCollectionTable memoryTable)
        {
            if (layer == null) return;

            var existing = Layers.FirstOrDefault(l => !l.IsFolderPlaceholder && l.Name == layer.Name);
            if (existing == null)
            {
                Layers.Add(new LayerItem(layer, isEditable, filePath, memoryTable));
            }
        }

        public void RemoveLayer(string layerName)
        {
            var layer = Layers.FirstOrDefault(l => l.Name == layerName);
            if (layer != null)
            {
                Layers.Remove(layer);
            }
        }

        public void ClearAll()
        {
            Layers.Clear();
        }

        public void UpdateLayerVisibility(string layerName, bool isVisible)
        {
            var layer = Layers.FirstOrDefault(l => l.Name == layerName);
            if (layer != null)
            {
                layer.IsVisible = isVisible;
            }
        }

        private void LayerVisible_Checked(object sender, RoutedEventArgs e)
        {
            var checkbox = sender as CheckBox;
            var layerItem = checkbox?.DataContext as LayerItem;
            if (layerItem != null)
            {
                LayerVisibilityChanged?.Invoke(layerItem.Name, true);
            }
        }

        private void LayerVisible_Unchecked(object sender, RoutedEventArgs e)
        {
            var checkbox = sender as CheckBox;
            var layerItem = checkbox?.DataContext as LayerItem;
            if (layerItem != null)
            {
                LayerVisibilityChanged?.Invoke(layerItem.Name, false);
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            string layerName = button?.Tag as string;
            if (!string.IsNullOrEmpty(layerName))
            {
                LayerRemoved?.Invoke(layerName);
            }
        }

        private void ZoomToLayer_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            string layerName = button?.Tag as string;
            if (!string.IsNullOrEmpty(layerName))
            {
                ZoomToLayerRequested?.Invoke(layerName);
            }
        }

        private void ShowAttributeTable_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            string layerName = button?.Tag as string;
            if (!string.IsNullOrEmpty(layerName))
            {
                ShowAttributeTableRequested?.Invoke(layerName);
            }
        }

        private void ToggleEdit_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var layerItem = button?.Tag as LayerItem;
            if (layerItem != null)
            {
                if (layerItem.IsEditable && layerItem.HasUnsavedChanges)
                {
                    var result = MessageBox.Show($"图层 '{layerItem.Name}' 有未保存的更改，是否先保存？", "提示",
                        MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        SaveLayerRequested?.Invoke(layerItem.FilePath);
                        layerItem.IsEditable = false;
                        LayerEditRequested?.Invoke(layerItem.Name, false, layerItem.FilePath);
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        layerItem.IsEditable = false;
                        LayerEditRequested?.Invoke(layerItem.Name, false, layerItem.FilePath);
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    layerItem.IsEditable = !layerItem.IsEditable;
                    LayerEditRequested?.Invoke(layerItem.Name, layerItem.IsEditable, layerItem.FilePath);
                }
            }
        }

        private void CboBasemap_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboBasemap.SelectedItem is ComboBoxItem item && item.Tag != null)
                BasemapChanged?.Invoke(item.Tag.ToString());
        }

        public string CurrentBasemapTag
        {
            get
            {
                if (CboBasemap.SelectedItem is ComboBoxItem item && item.Tag != null)
                    return item.Tag.ToString();
                return "Imagery";
            }
            set
            {
                foreach (ComboBoxItem item in CboBasemap.Items)
                {
                    if (item.Tag?.ToString() == value)
                    {
                        CboBasemap.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void BtnNewLayer_Click(object sender, RoutedEventArgs e)
        {
            NewLayerRequested?.Invoke();
        }

        private void BtnSaveLayer_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LayerTree.SelectedItem as LayerItem;
            if (selectedItem == null)
            {
                selectedItem = Layers.FirstOrDefault(l => l.HasUnsavedChanges && !l.IsFolderPlaceholder)
                    ?? Layers.FirstOrDefault(l => l.IsEditable && !l.IsFolderPlaceholder)
                    ?? Layers.FirstOrDefault(l => !l.IsFolderPlaceholder);
            }

            if (selectedItem == null)
            {
                MessageBox.Show("没有可保存的图层，请先加载或选中一个图层", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectInTree(selectedItem);
            SaveLayerRequested?.Invoke(selectedItem.Name);
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            var realLayers = Layers.Where(l => !l.IsFolderPlaceholder).ToList();
            if (realLayers.Count > 0)
            {
                var result = MessageBox.Show("确定要清空所有图层吗？", "确认", 
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    foreach (var li in realLayers)
                    {
                        LayerRemoved?.Invoke(li.Name);
                    }
                    while (Layers.Count > 0)
                    {
                        var li = Layers.FirstOrDefault(l => !l.IsFolderPlaceholder);
                        if (li == null) break;
                        Layers.Remove(li);
                    }
                }
            }
        }

        private void LayerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is LayerItem li)
            {
                LayerSelected?.Invoke(li.Name);
            }
        }

        private void ExportLayer_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var border = menuItem?.Parent as ContextMenu;
            var layerItem = border?.DataContext as LayerItem;
            if (layerItem != null && menuItem?.Tag is string format)
            {
                LayerExportRequested?.Invoke(layerItem.Name, format);
            }
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var item = btn?.Tag as LayerItem;
            if (item == null) return;
            MoveLayerInFolder(item, true);
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var item = btn?.Tag as LayerItem;
            if (item == null) return;
            MoveLayerInFolder(item, false);
        }

        /// <summary>在同一文件夹内上移/下移图层（同步 TreeView 与扁平 Layers 顺序）。</summary>
        private void MoveLayerInFolder(LayerItem item, bool up)
        {
            var folder = _leafFolders.FirstOrDefault(f => f.Children.Contains(item));
            if (folder == null) return;
            int idx = folder.Children.IndexOf(item);
            int target = up ? idx - 1 : idx + 1;
            if (target < 0 || target >= folder.Children.Count) return;
            if (!(folder.Children[target] is LayerItem other)) return;

            folder.Children.Move(idx, target);

            int li = Layers.IndexOf(item);
            int lo = Layers.IndexOf(other);
            if (li >= 0 && lo >= 0 && li != lo)
            {
                Layers.Move(li, lo);
            }
            LayerMoveRequested?.Invoke(item.Name, up);
            SelectInTree(item);
        }

        private async void ToggleExpand_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var item = btn?.Tag as LayerItem;
            if (item == null) return;
            if (item.Layer is FeatureLayer fl)
            {
                try { await fl.LoadAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Load layer failed: {ex.Message}"); }
            }
            item.PopulateClassification();
            item.IsExpanded = !item.IsExpanded;
        }

        private void LayerTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (LayerTree.SelectedItem is LayerItem selectedItem)
                ShowLayerProperties(selectedItem);
        }

        private void LayerProperties_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var ctx = menuItem?.Parent as ContextMenu;
            var layerItem = ctx?.DataContext as LayerItem
                ?? LayerTree.SelectedItem as LayerItem;
            if (layerItem != null)
                ShowLayerProperties(layerItem);
        }

        private void ShowLayerProperties(LayerItem layerItem)
        {
            try
            {
                var owner = Window.GetWindow(this);
                var dlg = new LayerPropertiesDialog(layerItem) { Owner = owner };
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开图层属性失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>将图层手动移动到指定分组。</summary>
        private void MoveToGroup_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is MenuItem child && child.DataContext is MoveDestination dest)) return;
            var parent = child.Parent as MenuItem;
            var item = parent?.Tag as LayerItem;
            if (item == null || dest == null) return;
            item.ManualGroup = dest.GroupKey;
            item.RefreshGroups();
        }

        /// <summary>清除手动分组，恢复按类型自动分类。</summary>
        private void ResetAutoClassification_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var item = menuItem?.DataContext as LayerItem;
            if (item != null)
            {
                item.ManualGroup = "";
                item.RefreshGroups();
            }
        }

        /// <summary>在 TreeView 中定位并选中指定数据项。</summary>
        private void SelectInTree(object data)
        {
            var container = FindTreeViewItem(LayerTree, data);
            if (container != null)
            {
                container.IsExpanded = true;
                container.IsSelected = true;
                container.BringIntoView();
            }
        }

        private TreeViewItem FindTreeViewItem(ItemsControl parent, object data)
        {
            for (int i = 0; i < parent.Items.Count; i++)
            {
                var container = parent.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (container == null) continue;
                if (ReferenceEquals(parent.Items[i], data)) return container;
                var child = FindTreeViewItem(container, data);
                if (child != null) return child;
            }
            return null;
        }
    }

    public class ClassificationItem
    {
        public string Label { get; set; }
        public System.Windows.Media.Brush ColorBrush { get; set; }
    }

    public class MoveDestination
    {
        public string Header { get; set; }
        public string GroupKey { get; set; }
    }

    /// <summary>文件夹树节点（Windows 资源管理器风格：可展开/收起，显示图层数）。</summary>
    public class LayerFolderNode : INotifyPropertyChanged
    {
        private bool _isExpanded = true;
        private int _layerCount;

        public string Name { get; set; }

        public bool IsFolder => true;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FolderIcon));
            }
        }

        public string FolderIcon => IsExpanded ? "📂" : "📁";

        public ObservableCollection<object> Children { get; } = new ObservableCollection<object>();

        public int LayerCount
        {
            get => _layerCount;
            private set
            {
                _layerCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CountText));
            }
        }

        public string CountText => LayerCount > 0 ? $" ({LayerCount})" : "";

        public LayerFolderNode(string name)
        {
            Name = name;
        }

        /// <summary>递归统计后代图层数。</summary>
        public void UpdateCount()
        {
            int c = 0;
            foreach (var child in Children)
            {
                if (child is LayerItem) c++;
                else if (child is LayerFolderNode f) { f.UpdateCount(); c += f.LayerCount; }
            }
            LayerCount = c;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LayerItem : INotifyPropertyChanged
    {
        private string _name;
        private bool _isVisible;
        private bool _isEditable;
        private string _filePath;
        private bool _hasUnsavedChanges;
        private bool _isExpanded;
        private string _topGroup = "3 其他文件";
        private string _subGroup = "0 ";

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
                RefreshGroups();
            }
        }

        /// <summary>手动分组覆盖（"点|地质观察点" / "线|断层线" / "其他文件|"），空表示自动分类。</summary>
        public string ManualGroup { get; set; } = "";

        /// <summary>一级分组键（"1 点" / "2 线" / "3 其他文件"），前缀数字保证排序。</summary>
        public string TopGroup
        {
            get => _topGroup;
            set
            {
                _topGroup = value;
                OnPropertyChanged();
            }
        }

        /// <summary>二级分组键（点：7 类+未分类点；线：路线状态+地质线+未分类线；其他文件："0 "）。</summary>
        public string SubGroup
        {
            get => _subGroup;
            set
            {
                _subGroup = value;
                OnPropertyChanged();
            }
        }

        /// <summary>可移动到的分组列表（右键菜单用）。</summary>
        public List<MoveDestination> MoveDestinations { get; set; }

        /// <summary>是否文件夹占位项（兼容旧字段，新版本不再使用）。</summary>
        public bool IsFolderPlaceholder { get; set; }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                OnPropertyChanged();
            }
        }

        public bool IsEditable
        {
            get => _isEditable;
            set
            {
                _isEditable = value;
                OnPropertyChanged();
            }
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                OnPropertyChanged();
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                _hasUnsavedChanges = value;
                OnPropertyChanged();
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        public bool HasClassification => ClassificationItems != null && ClassificationItems.Count > 0;

        public List<ClassificationItem> ClassificationItems { get; set; }

        public FeatureCollectionTable MemoryFeatureTable { get; set; }

        public Layer Layer { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public LayerItem(Layer layer)
        {
            Layer = layer;
            Name = layer.Name;
            IsVisible = layer.IsVisible;
            IsEditable = false;
            FilePath = string.Empty;
            HasUnsavedChanges = false;
            RefreshGroups();
            BuildMoveDestinations();
            PopulateClassification();
        }

        public LayerItem(Layer layer, bool isEditable, string filePath)
        {
            Layer = layer;
            Name = layer.Name;
            IsVisible = layer.IsVisible;
            IsEditable = isEditable;
            FilePath = filePath;
            HasUnsavedChanges = false;
            RefreshGroups();
            BuildMoveDestinations();
            PopulateClassification();
        }

        public LayerItem(Layer layer, bool isEditable, string filePath, FeatureCollectionTable memoryTable)
        {
            Layer = layer;
            Name = layer.Name;
            IsVisible = layer.IsVisible;
            IsEditable = isEditable;
            FilePath = filePath;
            MemoryFeatureTable = memoryTable;
            HasUnsavedChanges = false;
            RefreshGroups();
            BuildMoveDestinations();
            PopulateClassification();
        }

        /// <summary>重新计算分组（手动覆盖优先，否则按几何类型+图层名称自动分类）。</summary>
        public void RefreshGroups()
        {
            try
            {
                if (IsFolderPlaceholder) return;

                if (!string.IsNullOrEmpty(ManualGroup))
                {
                    string[] parts = ManualGroup.Split('|');
                    string top = parts[0];
                    string sub = parts.Length > 1 ? parts[1] : "";
                    TopGroup = top == "点" ? "1 点" : top == "线" ? "2 线" : "3 其他文件";
                    SubGroup = top == "点" ? GetPointSubKey(sub)
                             : top == "线" ? GetLineSubKey(sub)
                             : "0 ";
                    return;
                }

                var gt = GetLayerGeometryType(Layer);
                if (gt == GeometryType.Point || gt == GeometryType.Multipoint)
                {
                    TopGroup = "1 点";
                    SubGroup = GetPointSubKey(GetSuffixDisplayName(Name));
                }
                else if (gt == GeometryType.Polyline)
                {
                    TopGroup = "2 线";
                    SubGroup = GetLineSubKey(GetSuffixDisplayName(Name));
                }
                else
                {
                    TopGroup = "3 其他文件";
                    SubGroup = "0 ";
                }
            }
            catch
            {
                TopGroup = "3 其他文件";
                SubGroup = "0 ";
            }
        }

        private static string GetSuffixDisplayName(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return "";
            // 去掉 " · " 前缀（MainWindow 导入格式："图层名 · 显示名"）
            int idx = layerName.LastIndexOf(" · ", StringComparison.Ordinal);
            string name = idx >= 0 ? layerName.Substring(idx + 3).Trim() : layerName.Trim();
            // 去掉 "_HHmmss" 时间后缀（FieldDataPanel ZIP/GeoJSON 导入格式："显示名_HHmmss"）
            int us = name.LastIndexOf('_');
            if (us > 0)
            {
                string suffix = name.Substring(us + 1);
                if (suffix.Length == 6 && suffix.All(char.IsDigit))
                    name = name.Substring(0, us).Trim();
            }
            return name;
        }

        private static string GetPointSubKey(string displayName)
        {
            if (displayName == FieldDataClassification.POINT_UNCLASSIFIED) return "8 " + FieldDataClassification.POINT_UNCLASSIFIED;
            var def = FieldDataClassification.FindPointType(displayName);
            if (def == null) return "8 " + FieldDataClassification.POINT_UNCLASSIFIED;
            int idx = Array.FindIndex(FieldDataClassification.PointTypes, t => string.Equals(t.TypeCode, def.TypeCode, StringComparison.OrdinalIgnoreCase));
            return $"{idx + 1} {def.DisplayName}";
        }

        private static string GetLineSubKey(string displayName)
        {
            var def = FieldDataClassification.FindLineType(displayName);
            if (def != null)
            {
                int idx = Array.FindIndex(FieldDataClassification.GeologicalLineTypes, t => string.Equals(t.TypeCode, def.TypeCode, StringComparison.OrdinalIgnoreCase));
                return $"{5 + idx} {def.DisplayName}";
            }
            foreach (var st in FieldDataClassification.RouteStatuses)
            {
                if (st.DisplayName == displayName)
                {
                    int idx = Array.IndexOf(FieldDataClassification.RouteStatuses, st);
                    return $"{1 + idx} {st.DisplayName}";
                }
            }
            return "11 " + FieldDataClassification.LINE_UNCLASSIFIED;
        }

        private void BuildMoveDestinations()
        {
            var list = new List<MoveDestination>();
            foreach (var t in FieldDataClassification.PointTypes)
                list.Add(new MoveDestination { Header = "点 · " + t.DisplayName, GroupKey = "点|" + t.DisplayName });
            list.Add(new MoveDestination { Header = "点 · " + FieldDataClassification.POINT_UNCLASSIFIED, GroupKey = "点|" + FieldDataClassification.POINT_UNCLASSIFIED });
            foreach (var st in FieldDataClassification.RouteStatuses)
                list.Add(new MoveDestination { Header = "线 · " + st.DisplayName, GroupKey = "线|" + st.DisplayName });
            foreach (var t in FieldDataClassification.GeologicalLineTypes)
                list.Add(new MoveDestination { Header = "线 · " + t.DisplayName, GroupKey = "线|" + t.DisplayName });
            list.Add(new MoveDestination { Header = "线 · " + FieldDataClassification.LINE_UNCLASSIFIED, GroupKey = "线|" + FieldDataClassification.LINE_UNCLASSIFIED });
            list.Add(new MoveDestination { Header = "其他文件", GroupKey = "其他文件|" });
            MoveDestinations = list;
        }

        private static GeometryType? GetLayerGeometryType(Layer layer)
        {
            try
            {
                if (layer is FeatureLayer fl && fl.FeatureTable != null)
                    return fl.FeatureTable.GeometryType;
                if (layer is FeatureCollectionLayer fcl && fcl.FeatureCollection != null &&
                    fcl.FeatureCollection.Tables.Count > 0)
                    return fcl.FeatureCollection.Tables[0].GeometryType;
            }
            catch { }
            return null;
        }

        public void PopulateClassification()
        {
            try
            {
                if (!(Layer is FeatureLayer fl) || fl.Renderer == null) return;
                var items = new List<ClassificationItem>();
                if (fl.Renderer is UniqueValueRenderer uvr)
                {
                    foreach (var uv in uvr.UniqueValues)
                    {
                        var c = MapCompileWindow.ExtractColorStatic(uv.Symbol);
                        var vals = uv.Values;
                        items.Add(new ClassificationItem
                        {
                            Label = string.IsNullOrEmpty(uv.Label) ? (vals != null ? string.Join(", ", vals) : "") : uv.Label,
                            ColorBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B))
                        });
                    }
                }
                else if (fl.Renderer is ClassBreaksRenderer cbr)
                {
                    foreach (var cb in cbr.ClassBreaks)
                    {
                        var c = MapCompileWindow.ExtractColorStatic(cb.Symbol);
                        items.Add(new ClassificationItem
                        {
                            Label = string.IsNullOrEmpty(cb.Label) ? $"{cb.MinValue:F2} - {cb.MaxValue:F2}" : cb.Label,
                            ColorBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B))
                        });
                    }
                }
                ClassificationItems = items.Count > 0 ? items : new List<ClassificationItem>();
                OnPropertyChanged(nameof(ClassificationItems));
                OnPropertyChanged(nameof(HasClassification));
            }
            catch { }
        }
    }
}
