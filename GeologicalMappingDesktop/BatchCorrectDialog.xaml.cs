using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Mapping;

namespace GeologicalMappingDesktop
{
    public partial class BatchCorrectDialog : Window
    {
        private MainWindow _main;
        private FeatureTable _currentTable;
        private List<Feature> _currentFeatures = new List<Feature>();
        private List<string> _fieldNames = new List<string>();

        public BatchCorrectDialog(MainWindow main)
        {
            _main = main;
            InitializeComponent();
            LoadLayers();
        }

        private void LoadLayers()
        {
            CboLayer.Items.Clear();
            foreach (var layerItem in _main.LayerManager.Layers)
            {
                if (layerItem.Layer is FeatureLayer fl &&
                    fl.FeatureTable != null)
                {
                    CboLayer.Items.Add(layerItem.Name);
                }
            }
            if (CboLayer.Items.Count > 0)
                CboLayer.SelectedIndex = 0;
        }

        private async void BtnLoadFeatures_Click(object sender, RoutedEventArgs e)
        {
            if (CboLayer.SelectedItem is string layerName)
            {
                await LoadLayerFeatures(layerName);
            }
        }

        private async Task LoadLayerFeatures(string layerName)
        {
            try
            {
                var layerItem = _main.LayerManager.Layers
                    .FirstOrDefault(l => l.Name == layerName);
                if (layerItem == null) return;

                _currentTable = _main.GetFeatureTableFromLayer(layerItem.Layer)
                    ?? layerItem.MemoryFeatureTable;
                if (_currentTable == null)
                {
                    TxtFeatureCount.Text = "无法获取要素表";
                    return;
                }

                var features = await _currentTable.QueryFeaturesAsync(new QueryParameters());
                _currentFeatures = features.ToList();
                TxtFeatureCount.Text = $"已加载 {_currentFeatures.Count} 个要素";

                // Populate field list
                _fieldNames.Clear();
                CboField.Items.Clear();
                foreach (var field in _currentTable.Fields)
                {
                    if (field.FieldType != FieldType.Geometry && field.FieldType != FieldType.OID)
                    {
                        _fieldNames.Add(field.Name);
                        CboField.Items.Add(field.Name);
                    }
                }
                if (CboField.Items.Count > 0)
                    CboField.SelectedIndex = 0;

                TxtResult.Text = $"图层: {layerName}\n字段: {_fieldNames.Count} 个\n要素: {_currentFeatures.Count} 个\n\n选择要修改的字段和输入新值后点击「应用」。";
            }
            catch (Exception ex)
            {
                TxtFeatureCount.Text = $"加载失败: {ex.Message}";
            }
        }

        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTable == null || _currentFeatures.Count == 0)
            {
                MessageBox.Show("请先加载要素", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!(CboField.SelectedItem is string fieldName) || string.IsNullOrEmpty(fieldName))
            {
                MessageBox.Show("请选择字段", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string newValue = TxtNewValue.Text.Trim();
            if (string.IsNullOrEmpty(newValue))
            {
                var confirm = MessageBox.Show("新值为空，这将清空该字段的所有值。确定继续？", "确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;
            }

            try
            {
                int modified = 0;
                foreach (var feature in _currentFeatures)
                {
                    if (feature.Attributes.ContainsKey(fieldName))
                    {
                        feature.Attributes[fieldName] = newValue;
                        await _currentTable.UpdateFeatureAsync(feature);
                        modified++;
                    }
                }

                TxtResult.Text = $"修改完成！\n字段: {fieldName}\n新值: {(string.IsNullOrEmpty(newValue) ? "(空)" : newValue)}\n修改要素数: {modified}";
                MessageBox.Show($"成功修改 {modified} 个要素", "批量修改完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"批量修改失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
