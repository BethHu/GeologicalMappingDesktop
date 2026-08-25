using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI.Controls;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using EsriGeometry = Esri.ArcGISRuntime.Geometry.Geometry;

namespace GeologicalMappingDesktop
{
    public partial class AttributeTableWindow : Window
    {
        private MapView _mapView;
        private string _layerName;
        private FeatureTable _featureTable;
        private DataTable _dataTable;
        private MainWindow _mainWindow;
        private bool _isUpdatingSelection = false;
        private bool _isClosing = false;
        private bool _forceClose = false;
        private List<Feature> _loadedFeatures = new List<Feature>();
        private List<Feature> _filterFeatures; // 非空时仅显示这些要素（属性及编号修改用）
        private DataGridColumn _highlightedColumn;
        private Dictionary<string, string> _fieldColMap = new Dictionary<string, string>();

        public event Action<string> AddFieldRequested;
        public event Action<string> FieldCalculatorRequested;
        public event Action<string> ExportRequested;
        public event Action<string> BatchUpdateRequested;

        public DataGrid DataGrid => AttributeDataGrid;
        public string LayerName => _layerName;

        public AttributeTableWindow(MainWindow mainWindow, string layerName, MapView mapView)
            : this(mainWindow, layerName, mapView, null)
        {
        }

        public AttributeTableWindow(MainWindow mainWindow, string layerName, MapView mapView, List<Feature> selectedFeatures)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _layerName = layerName;
            _mapView = mapView;
            _filterFeatures = selectedFeatures;
            if (_filterFeatures != null && _filterFeatures.Count > 0)
                Title = "属性及编号修改（选中要素）";
            LayerNameText.Text = "图层：" + layerName;
            LoadAttributeTable();
            AttributeDataGrid.SelectionChanged += AttributeDataGrid_SelectionChanged;
            AttributeDataGrid.AutoGeneratingColumn += AttributeDataGrid_AutoGeneratingColumn;
            AttributeDataGrid.PreviewMouseLeftButtonDown += AttributeDataGrid_PreviewMouseLeftButtonDown;
            PreviewKeyDown += AttributeTableWindow_PreviewKeyDown;
        }

        private void SetStatus(string text)
        {
            if (StatusText != null)
                StatusText.Text = text;
        }

        private bool EnsureLayerEditable()
        {
            if (!_mainWindow.CanEditLayer(_layerName))
            {
                MessageBox.Show("该图层已锁定或未进入编辑模式，请先在图层管理器中开启编辑", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            return true;
        }

        private void AttributeDataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            e.Column.HeaderStyle = CreateColumnHeaderStyle(false);
            string name = e.PropertyName ?? e.Column.Header?.ToString() ?? "";
            if (name.Equals("FID", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("OID", StringComparison.OrdinalIgnoreCase))
            {
                e.Column.IsReadOnly = true;
            }
        }

        /// <summary>
        /// 将属性表网格中的编辑写回要素表（添加字段/保存前必须调用）
        /// </summary>
        public async Task CommitAllEditsToFeaturesAsync()
        {
            try
            {
                AttributeDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                AttributeDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { }

            _featureTable = ResolveFeatureTable();
            if (_featureTable == null || _dataTable == null || _loadedFeatures == null)
                return;

            for (int i = 0; i < _dataTable.Rows.Count && i < _loadedFeatures.Count; i++)
            {
                try
                {
                    await WriteRowToFeatureAsync(_dataTable.Rows[i], _loadedFeatures[i], pushUndo: false);
                }
                catch { }
            }
        }

        private async void AttributeDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (!(_mainWindow?.CanEditLayer(_layerName) ?? false))
            {
                e.Cancel = true;
                MessageBox.Show("该图层已锁定，无法修改属性。请先开启编辑模式。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (!(e.Row?.Item is DataRowView rowView)) return;
                string columnName = e.Column?.Header?.ToString();
                if (string.IsNullOrEmpty(columnName)) return;
                if (columnName.Equals("FID", StringComparison.OrdinalIgnoreCase) ||
                    columnName.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase))
                    return;

                string newValue = "";
                if (e.EditingElement is TextBox tb)
                    newValue = tb.Text ?? "";
                else
                    newValue = rowView[columnName]?.ToString() ?? "";

                // 写回 DataTable
                rowView[columnName] = newValue;

                var feature = FindFeatureByRow(rowView);
                if (feature == null) return;

                await WriteCellToFeatureAsync(feature, columnName, newValue, pushUndo: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存单元格失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task WriteRowToFeatureAsync(DataRow row, Feature feature, bool pushUndo)
        {
            if (row == null || feature == null || _featureTable == null) return;

            bool changed = false;
            foreach (DataColumn col in row.Table.Columns)
            {
                string columnName = col.ColumnName;
                if (columnName.Equals("FID", StringComparison.OrdinalIgnoreCase) ||
                    columnName.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase) ||
                    columnName.Equals("OID", StringComparison.OrdinalIgnoreCase))
                    continue;

                var field = _featureTable.Fields.FirstOrDefault(f =>
                    f.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase) ||
                    (f.Alias != null && f.Alias.Equals(columnName, StringComparison.OrdinalIgnoreCase)));
                if (field == null || field.FieldType == FieldType.OID || field.FieldType == FieldType.Geometry)
                    continue;

                string newValue = row[columnName]?.ToString() ?? "";
                object oldVal = MainWindow.GetAttributeIgnoreCase(feature, field.Name);
                string oldStr = oldVal?.ToString() ?? "";
                if (string.Equals(oldStr, newValue, StringComparison.Ordinal))
                    continue;

                object typed = _mainWindow.ConvertAttributeValue(newValue, field.FieldType);
                try
                {
                    feature.Attributes[field.Name] = typed;
                    changed = true;
                }
                catch { }
            }

            if (changed)
            {
                await _featureTable.UpdateFeatureAsync(feature);
                _mainWindow.MarkLayerHasUnsavedChanges(_layerName);
            }
        }

        private async Task WriteCellToFeatureAsync(Feature feature, string columnName, string newValue, bool pushUndo)
        {
            _featureTable = ResolveFeatureTable();
            if (_featureTable == null || feature == null) return;

            var field = _featureTable.Fields.FirstOrDefault(f =>
                f.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase) ||
                (f.Alias != null && f.Alias.Equals(columnName, StringComparison.OrdinalIgnoreCase)));
            if (field == null || field.FieldType == FieldType.OID)
                return;

            object oldVal = MainWindow.GetAttributeIgnoreCase(feature, field.Name);
            object typed = _mainWindow.ConvertAttributeValue(newValue, field.FieldType);

            if (pushUndo)
            {
                _mainWindow.PushUndoOperation(new MainWindow.EditOperation
                {
                    OperationType = "修改属性",
                    LayerName = _layerName,
                    Feature = feature,
                    FieldName = field.Name,
                    OldAttributeValue = oldVal,
                    NewAttributeValue = typed
                });
            }

            feature.Attributes[field.Name] = typed;
            await _featureTable.UpdateFeatureAsync(feature);
            _mainWindow.MarkLayerHasUnsavedChanges(_layerName);
        }

        private Style CreateColumnHeaderStyle(bool selected)
        {
            var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            style.Setters.Add(new Setter(Control.BackgroundProperty,
                new System.Windows.Media.SolidColorBrush(selected
                    ? System.Windows.Media.Color.FromRgb(0x1F, 0x6F, 0xEB)
                    : System.Windows.Media.Color.FromRgb(0x21, 0x26, 0x2D))));
            style.Setters.Add(new Setter(Control.ForegroundProperty,
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty,
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D))));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            return style;
        }

        private void AttributeDataGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is System.Windows.Controls.Primitives.DataGridColumnHeader))
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);

            if (dep is System.Windows.Controls.Primitives.DataGridColumnHeader header && header.Column != null)
            {
                HighlightColumn(header.Column);
            }
        }

        private void HighlightColumn(DataGridColumn column)
        {
            _highlightedColumn = column;
            foreach (var col in AttributeDataGrid.Columns)
            {
                col.HeaderStyle = CreateColumnHeaderStyle(col == column);
            }
            AttributeDataGrid.CurrentColumn = column;
        }

        private string GetSelectedColumnName()
        {
            if (_highlightedColumn?.Header != null)
                return _highlightedColumn.Header.ToString();
            if (AttributeDataGrid.CurrentColumn?.Header != null)
                return AttributeDataGrid.CurrentColumn.Header.ToString();
            return null;
        }

        private async void AttributeTableWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Z &&
                (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                await _mainWindow.UndoLastEditPublic();
                e.Handled = true;
            }
        }

        private FeatureTable ResolveFeatureTable()
        {
            if (_mapView?.Map == null) return null;

            var layerItem = _mainWindow?.LayerManager?.Layers?.FirstOrDefault(l => l.Name == _layerName);
            var layer = layerItem?.Layer ?? _mapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == _layerName);

            // 优先使用地图上当前图层的表，避免陈旧 MemoryFeatureTable
            if (layer is FeatureLayer featureLayer && featureLayer.FeatureTable != null)
                return featureLayer.FeatureTable;
            if (layer is FeatureCollectionLayer collectionLayer)
            {
                var t = collectionLayer.FeatureCollection?.Tables?.FirstOrDefault();
                if (t != null) return t;
            }
            return layerItem?.MemoryFeatureTable;
        }

        private async void LoadAttributeTable()
        {
            if (_mapView == null || _mapView.Map == null) return;

            _featureTable = ResolveFeatureTable();
            _loadedFeatures = new List<Feature>();

            if (_featureTable != null)
            {
                try
                {
                    var featureList = (await _featureTable.QueryFeaturesAsync(new QueryParameters())).ToList();

                    // 「属性及编号修改」模式：仅保留选中要素（按 OBJECTID/FID/引用/几何匹配）
                    if (_filterFeatures != null && _filterFeatures.Count > 0)
                    {
                        var filtered = new List<Feature>();
                        foreach (var f in featureList)
                        {
                            foreach (var sel in _filterFeatures)
                            {
                                if (FeatureIsSame(f, sel))
                                {
                                    filtered.Add(f);
                                    break;
                                }
                            }
                        }
                        featureList = filtered;
                    }

                    _dataTable = new DataTable();
                    bool hasFidColumn = _featureTable.Fields.Any(f => f.Name.Equals("FID", StringComparison.OrdinalIgnoreCase));
                    if (!hasFidColumn)
                    {
                        _dataTable.Columns.Add("FID", typeof(int));
                    }
                    foreach (var field in _featureTable.Fields)
                    {
                        if (field.FieldType == FieldType.Geometry)
                            continue;
                        string colName = !string.IsNullOrEmpty(field.Alias) ? field.Alias : field.Name;
                        _dataTable.Columns.Add(colName, typeof(string));
                        _fieldColMap[colName] = field.Name;
                    }

                    int fidCounter = 0;
                    foreach (var feature in featureList)
                    {
                        _loadedFeatures.Add(feature);
                        var row = _dataTable.NewRow();
                        if (!hasFidColumn)
                        {
                            row["FID"] = fidCounter++;
                        }
                        foreach (var field in _featureTable.Fields)
                        {
                            if (field.FieldType == FieldType.Geometry)
                                continue;
                            var value = feature.Attributes.ContainsKey(field.Name) ? feature.Attributes[field.Name] : null;
                            string colName = !string.IsNullOrEmpty(field.Alias) ? field.Alias : field.Name;
                            row[colName] = value != null ? value.ToString() : "";
                        }
                        _dataTable.Rows.Add(row);
                    }

                    AttributeDataGrid.ItemsSource = _dataTable.DefaultView;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("加载属性表失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void AttributeDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_mainWindow == null || _isUpdatingSelection) return;

            var selectedFeatures = new List<Feature>();

            foreach (var item in AttributeDataGrid.SelectedItems)
            {
                if (item is DataRowView row)
                {
                    var feature = FindFeatureByRow(row);
                    if (feature != null)
                    {
                        selectedFeatures.Add(feature);
                    }
                }
            }

            _mainWindow.SelectFeaturesFromAttributeTable(selectedFeatures);
        }

        private void AttributeDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (AttributeDataGrid.SelectedItem is DataRowView row)
            {
                var feature = FindFeatureByRow(row);
                if (feature != null && feature.Geometry != null)
                {
                    _mapView.SetViewpointGeometryAsync(feature.Geometry.Extent, 50);
                    StatusBar.Text = "已定位到选中要素";
                }
            }
        }

        private Feature FindFeatureByRow(DataRowView row)
        {
            if (row == null || _dataTable == null || _loadedFeatures == null) return null;

            try
            {
                int index = _dataTable.Rows.IndexOf(row.Row);
                if (index >= 0 && index < _loadedFeatures.Count)
                    return _loadedFeatures[index];

                // 按属性匹配（FID / OBJECTID）
                string oidKey = null;
                if (row.Row.Table.Columns.Contains("OBJECTID"))
                    oidKey = "OBJECTID";
                else if (row.Row.Table.Columns.Contains("FID"))
                    oidKey = "FID";

                if (oidKey != null)
                {
                    var rowOid = row[oidKey]?.ToString();
                    foreach (var feature in _loadedFeatures)
                    {
                        if (feature.Attributes.ContainsKey(oidKey) &&
                            feature.Attributes[oidKey]?.ToString() == rowOid)
                            return feature;
                    }
                }
            }
            catch { }

            return null;
        }

        public void UpdateSelectionFromMap(List<Feature> selectedFeatures)
        {
            if (AttributeDataGrid.ItemsSource == null) return;

            try
            {
                void Apply()
                {
                    _isUpdatingSelection = true;
                    try
                    {
                        AttributeDataGrid.SelectedItems.Clear();
                        DataRowView first = null;
                        if (selectedFeatures != null)
                        {
                            foreach (var feature in selectedFeatures)
                            {
                                var row = FindRowByFeature(feature);
                                if (row != null)
                                {
                                    AttributeDataGrid.SelectedItems.Add(row);
                                    if (first == null) first = row;
                                }
                            }
                        }
                        if (first != null)
                        {
                            AttributeDataGrid.ScrollIntoView(first);
                            AttributeDataGrid.CurrentItem = first;
                        }
                    }
                    finally
                    {
                        _isUpdatingSelection = false;
                    }
                }

                if (Dispatcher.CheckAccess())
                    Apply();
                else
                    Dispatcher.Invoke(Apply);
            }
            catch { }
        }

        private bool GeometryMatches(EsriGeometry g1, EsriGeometry g2)
        {
            if (g1 == null || g2 == null) return false;
            if (g1.GeometryType != g2.GeometryType) return false;
            try
            {
                if (string.Equals(g1.ToJson(), g2.ToJson(), StringComparison.Ordinal))
                    return true;
            }
            catch { }
            try
            {
                return GeometryEngine.Distance(g1, g2) < 0.001;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>判断两个要素是否为同一要素（OID 优先，其次几何匹配，最后引用相等）。</summary>
        private bool FeatureIsSame(Feature a, Feature b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            if (a.FeatureTable != b.FeatureTable) return false;

            foreach (var key in new[] { "OBJECTID", "FID", "OID" })
            {
                if (a.Attributes.ContainsKey(key) && b.Attributes.ContainsKey(key) &&
                    a.Attributes[key] != null && b.Attributes[key] != null &&
                    string.Equals(a.Attributes[key].ToString(), b.Attributes[key].ToString()))
                    return true;
            }
            return GeometryMatches(a.Geometry, b.Geometry);
        }

        private DataRowView FindRowByFeature(Feature feature)
        {
            if (_dataTable == null || feature == null || _loadedFeatures == null) return null;

            try
            {
                int index = -1;
                for (int i = 0; i < _loadedFeatures.Count; i++)
                {
                    var f = _loadedFeatures[i];
                    if (ReferenceEquals(f, feature))
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0 && feature.Geometry != null)
                {
                    for (int i = 0; i < _loadedFeatures.Count; i++)
                    {
                        if (GeometryMatches(_loadedFeatures[i].Geometry, feature.Geometry))
                        {
                            index = i;
                            break;
                        }
                    }
                }

                if (index < 0)
                {
                    foreach (var key in new[] { "OBJECTID", "FID", "OID" })
                    {
                        if (!feature.Attributes.ContainsKey(key) || feature.Attributes[key] == null)
                            continue;
                        string target = feature.Attributes[key].ToString();
                        for (int i = 0; i < _loadedFeatures.Count; i++)
                        {
                            var f = _loadedFeatures[i];
                            if (f.Attributes.ContainsKey(key) && f.Attributes[key]?.ToString() == target)
                            {
                                index = i;
                                break;
                            }
                        }
                        if (index >= 0) break;
                    }
                }

                if (index >= 0 && index < AttributeDataGrid.Items.Count)
                    return AttributeDataGrid.Items[index] as DataRowView;
            }
            catch { }

            return null;
        }

        public void RefreshTable()
        {
            LoadAttributeTable();
        }

        private void MenuItem_AddField(object sender, RoutedEventArgs e)
        {
            if (!EnsureLayerEditable()) return;
            AddFieldRequested?.Invoke(_layerName);
        }

        private async void MenuItem_DeleteField(object sender, RoutedEventArgs e)
        {
            if (!EnsureLayerEditable()) return;

            var columnName = GetSelectedColumnName();
            if (string.IsNullOrEmpty(columnName))
            {
                MessageBox.Show("请先点击列标题选中要删除的字段（选中后列标题会高亮）", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (columnName.Equals("FID", StringComparison.OrdinalIgnoreCase) ||
                columnName.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase) ||
                columnName.Equals("OID", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("系统字段（FID、OBJECTID）无法删除", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                await CommitAllEditsToFeaturesAsync();

                var table = await _mainWindow.EnsureEditableMemoryTableAsync(_layerName);
                if (table == null)
                {
                    MessageBox.Show("无法获取图层数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var matchField = table.Fields.FirstOrDefault(f =>
                    string.Equals(f.Name, columnName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f.Alias, columnName, StringComparison.OrdinalIgnoreCase));
                if (matchField == null)
                {
                    MessageBox.Show("未找到匹配的字段: " + columnName, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var remainFields = MainWindow.CloneEditableFields(
                    table.Fields.Where(f => !string.Equals(f.Name, matchField.Name, StringComparison.OrdinalIgnoreCase)));
                if (remainFields.Count == 0)
                {
                    MessageBox.Show("至少保留一个属性字段", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var snapshots = await _mainWindow.SnapshotTableAsync(table);
                var originalFields = MainWindow.CloneEditableFields(table.Fields);

                _mainWindow.PushUndoOperation(new MainWindow.EditOperation
                {
                    OperationType = "删除字段",
                    LayerName = _layerName,
                    OriginalFields = originalFields,
                    TableSnapshots = snapshots,
                    SnapshotGeometryType = table.GeometryType,
                    SnapshotSpatialReference = table.SpatialReference,
                    NewFields = null
                });

                var newMemoryTable = await _mainWindow.BuildMemoryTableFromSnapshotsAsync(
                    remainFields, table.GeometryType, table.SpatialReference, snapshots);
                var layerItem = _mainWindow.LayerManager.Layers.FirstOrDefault(l => l.Name == _layerName);
                await _mainWindow.ReplaceLayerWithMemoryTablePublicAsync(_layerName, newMemoryTable, layerItem);
                _featureTable = newMemoryTable;

                LoadAttributeTable();
                _mainWindow.MarkLayerHasUnsavedChanges(_layerName);
                MessageBox.Show($"字段 \"{columnName}\" 已删除", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除字段失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void MenuItem_DeleteSelected(object sender, RoutedEventArgs e)
        {
            if (!EnsureLayerEditable())
                return;

            if (AttributeDataGrid.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选中要删除的记录", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_featureTable == null)
            {
                MessageBox.Show("无法获取图层数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _featureTable = ResolveFeatureTable();
            if (_featureTable == null)
            {
                MessageBox.Show("无法获取图层数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var featuresToDelete = new List<Feature>();

            foreach (var item in AttributeDataGrid.SelectedItems.Cast<object>().ToList())
            {
                if (item is DataRowView row)
                {
                    var feature = FindFeatureByRow(row);
                    if (feature != null)
                    {
                        featuresToDelete.Add(feature);
                    }
                }
            }

            if (featuresToDelete.Count == 0)
            {
                MessageBox.Show("未能匹配到可删除的要素，请重新选择后重试", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var snapshots = new List<MainWindow.FeatureSnapshot>();
            foreach (var f in featuresToDelete)
            {
                var attrs = new Dictionary<string, object>();
                foreach (var kv in f.Attributes)
                    attrs[kv.Key] = kv.Value;
                snapshots.Add(new MainWindow.FeatureSnapshot
                {
                    Geometry = f.Geometry != null ? EsriGeometry.FromJson(f.Geometry.ToJson()) : null,
                    Attributes = attrs
                });
            }

            _mainWindow.PushUndoOperation(new MainWindow.EditOperation
            {
                OperationType = "删除",
                LayerName = _layerName,
                DeletedFeatures = featuresToDelete.ToList(),
                DeletedSnapshots = snapshots
            });

            int deletedCount = 0;
            foreach (var feature in featuresToDelete)
            {
                try
                {
                    await _featureTable.DeleteFeatureAsync(feature);
                    deletedCount++;
                }
                catch { }
            }

            // 同步 MemoryFeatureTable 引用
            var layerItem = _mainWindow.LayerManager.Layers.FirstOrDefault(l => l.Name == _layerName);
            if (layerItem != null && _featureTable is FeatureCollectionTable fct)
            {
                layerItem.MemoryFeatureTable = fct;
            }

            LoadAttributeTable();
            _mainWindow.ClearSelection();
            _mainWindow.RefreshLayerDisplay(_layerName);
            _mainWindow.MarkLayerHasUnsavedChanges(_layerName);
            MessageBox.Show($"已删除 {deletedCount} 条记录", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MenuItem_SelectAll(object sender, RoutedEventArgs e)
        {
            AttributeDataGrid.SelectAll();
            SetStatus("已全选");
        }

        private void MenuItem_ClearSelection(object sender, RoutedEventArgs e)
        {
            AttributeDataGrid.SelectedItems.Clear();
            _mainWindow.ClearSelection();
            SetStatus("已清除选择");
        }

        private void MenuItem_FieldCalculator(object sender, RoutedEventArgs e)
        {
            if (!EnsureLayerEditable()) return;
            FieldCalculatorRequested?.Invoke(_layerName);
        }

        private void MenuItem_BatchUpdate(object sender, RoutedEventArgs e)
        {
            if (!EnsureLayerEditable()) return;
            BatchUpdateRequested?.Invoke(_layerName);
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            ExportRequested?.Invoke(_layerName);
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await CommitAllEditsToFeaturesAsync();
                await _mainWindow.SaveLayerToShapefile(_layerName);
                SetStatus("已保存图层");
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 程序化关闭（切换属性表等），不弹出未保存提示
        /// </summary>
        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_forceClose || _isClosing)
            {
                base.OnClosing(e);
                return;
            }

            try
            {
                AttributeDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                AttributeDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
                // 关闭前尽量把网格改动写回要素（同步等待，保证提示前状态正确）
                CommitAllEditsToFeaturesAsync().ConfigureAwait(true).GetAwaiter().GetResult();
            }
            catch { }

            var layerItem = _mainWindow?.LayerManager?.Layers?.FirstOrDefault(l => l.Name == _layerName);
            bool hasChanges = layerItem != null && layerItem.HasUnsavedChanges;

            if (!hasChanges)
            {
                base.OnClosing(e);
                return;
            }

            var result = MessageBox.Show(
                $"属性表已修改，图层「{_layerName}」有未保存的更改。\n\n是否保存到文件？\n\n是 = 保存并关闭\n否 = 不保存并关闭\n取消 = 继续编辑",
                "保存确认",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                base.OnClosing(e);
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                e.Cancel = true;
                base.OnClosing(e);
                _isClosing = true;
                SaveThenClose();
                return;
            }

            // 否：不保存并关闭
            _forceClose = true;
            base.OnClosing(e);
        }

        private async void SaveThenClose()
        {
            try
            {
                await CommitAllEditsToFeaturesAsync();
                await _mainWindow.SaveLayerToShapefile(_layerName);
                var layerItem = _mainWindow?.LayerManager?.Layers?.FirstOrDefault(l => l.Name == _layerName);
                if (layerItem != null && layerItem.HasUnsavedChanges)
                {
                    _isClosing = false;
                    SetStatus("保存未完成，窗口保持打开");
                    return;
                }
                _forceClose = true;
                Close();
            }
            catch (Exception ex)
            {
                _isClosing = false;
                MessageBox.Show("保存失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private TextBlock StatusBar => _mainWindow.StatusBar;
    }
}