using System.Windows;
using System.Windows.Controls;

namespace GeologicalMappingDesktop
{
    public partial class NewFeatureLayerDialog : Window
    {
        public string LayerName { get; set; }
        public string GeometryType { get; set; }
        public int SpatialReferenceWKID { get; set; }
        public string UnitType { get; set; } = "";

        public NewFeatureLayerDialog()
        {
            InitializeComponent();
            CboGeometryType.SelectedIndex = 0;
            CboSpatialReference.SelectedIndex = 0;
            CboUnitType.SelectedIndex = 0;
            UpdateUnitTypeEnabled();
        }

        private void UpdateUnitTypeEnabled()
        {
            var selectedGeometry = CboGeometryType.SelectedItem as ComboBoxItem;
            bool isPolygon = selectedGeometry != null && selectedGeometry.Tag?.ToString() == "Polygon";
            CboUnitType.IsEnabled = isPolygon;
        }

        private void CboGeometryType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateUnitTypeEnabled();
        }

        private void CboUnitType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboUnitType.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag?.ToString();
                string defaultName = "新建图层";
                if (string.IsNullOrWhiteSpace(TxtLayerName.Text) || TxtLayerName.Text == defaultName)
                {
                    if (tag == "STRATUM") TxtLayerName.Text = "地层单元";
                    else if (tag == "ROCK") TxtLayerName.Text = "岩体单元";
                    else if (tag == "TECTONIC") TxtLayerName.Text = "构造单元";
                    else TxtLayerName.Text = defaultName;
                }
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtLayerName.Text))
            {
                MessageBox.Show("请输入图层名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedGeometry = CboGeometryType.SelectedItem as ComboBoxItem;
            var selectedSR = CboSpatialReference.SelectedItem as ComboBoxItem;

            if (selectedGeometry == null || selectedSR == null)
            {
                MessageBox.Show("请选择几何类型和坐标系", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LayerName = TxtLayerName.Text;
            GeometryType = selectedGeometry.Tag.ToString();
            SpatialReferenceWKID = int.Parse(selectedSR.Tag.ToString());

            if (CboUnitType.SelectedItem is ComboBoxItem unitItem && unitItem.Tag != null)
                UnitType = unitItem.Tag.ToString();
            else
                UnitType = "";

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