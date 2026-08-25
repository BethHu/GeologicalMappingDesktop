using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GeologicalMappingDesktop
{
    public partial class ImportDialog : Window
    {
        public string FilePath { get; private set; }
        public string LayerName { get; private set; }
        public bool PreviewOnly { get; private set; }
        public List<FeatureState> ParsedFeatures { get; private set; }

        public ImportDialog()
        {
            InitializeComponent();
            TxtPreview.Text = "请选择 GeoJSON 文件...";
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Filter = "GeoJSON 文件 (*.geojson;*.json)|*.geojson;*.json",
                Title = "选择 GeoJSON 数据文件"
            };

            if (openDialog.ShowDialog() == true)
            {
                TxtPath.Text = openDialog.FileName;
                PreviewFile(openDialog.FileName);
            }
        }

        private void PreviewFile(string filePath)
        {
            try
            {
                string content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                var features = GeoJsonHelper.ParseGeoJsonFeatures(content);

                ParsedFeatures = features;
                TxtPreview.Text = $"文件: {Path.GetFileName(filePath)}\n" +
                                  $"要素数量: {features.Count}\n\n";

                if (features.Count > 0)
                {
                    TxtPreview.Text += "字段列表:\n";
                    var fieldSet = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in features)
                    {
                        foreach (var k in f.Attributes.Keys)
                            fieldSet.Add(k);
                    }
                    TxtPreview.Text += string.Join(", ", fieldSet) + "\n\n";
                    TxtPreview.Text += $"几何类型: {DetectGeometryType(features)}";

                    if (string.IsNullOrEmpty(TxtLayerName.Text) || TxtLayerName.Text == "导入数据")
                    {
                        TxtLayerName.Text = Path.GetFileNameWithoutExtension(filePath);
                    }
                    BtnImport.IsEnabled = true;
                }
                else
                {
                    TxtPreview.Text += "未找到有效要素。";
                    BtnImport.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                TxtPreview.Text = $"读取文件失败: {ex.Message}";
                BtnImport.IsEnabled = false;
            }
        }

        private string DetectGeometryType(List<FeatureState> features)
        {
            bool hasPoint = false, hasLine = false, hasPolygon = false, hasOther = false;
            foreach (var f in features)
            {
                if (string.IsNullOrEmpty(f.GeometryJson)) continue;
                try
                {
                    var geom = Esri.ArcGISRuntime.Geometry.Geometry.FromJson(f.GeometryJson);
                    switch (geom.GeometryType)
                    {
                        case Esri.ArcGISRuntime.Geometry.GeometryType.Point: hasPoint = true; break;
                        case Esri.ArcGISRuntime.Geometry.GeometryType.Polyline: hasLine = true; break;
                        case Esri.ArcGISRuntime.Geometry.GeometryType.Polygon: hasPolygon = true; break;
                        default: hasOther = true; break;
                    }
                }
                catch { }
            }
            var types = new System.Collections.Generic.List<string>();
            if (hasPoint) types.Add("点");
            if (hasLine) types.Add("线");
            if (hasPolygon) types.Add("面");
            if (hasOther) types.Add("其他");
            return types.Count > 0 ? string.Join(", ", types) : "未知";
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtLayerName.Text))
            {
                MessageBox.Show("请输入图层名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FilePath = TxtPath.Text;
            LayerName = TxtLayerName.Text.Trim();
            PreviewOnly = ChkPreviewOnly.IsChecked ?? false;

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
