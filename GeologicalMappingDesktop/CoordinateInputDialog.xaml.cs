using System;
using System.Collections.Generic;
using System.Windows;
using Esri.ArcGISRuntime.Geometry;

namespace GeologicalMappingDesktop
{
    public partial class CoordinateInputDialog : Window
    {
        public MapPoint ResultPoint { get; private set; }

        private class SrOption
        {
            public int Wkid { get; set; }
            public string Display { get; set; }
            public override string ToString() => Display;
        }

        private readonly List<SrOption> _srOptions = new List<SrOption>();

        public CoordinateInputDialog()
        {
            InitializeComponent();
            LoadCommonSrs();
            CboSpatialReference.SelectedIndex = 0;
        }

        private void LoadCommonSrs()
        {
            _srOptions.Add(new SrOption { Wkid = 4326, Display = "WGS84 (EPSG:4326)" });
            _srOptions.Add(new SrOption { Wkid = 4490, Display = "CGCS2000 (EPSG:4490)" });
            _srOptions.Add(new SrOption { Wkid = 4610, Display = "Xian 1980 (EPSG:4610)" });
            _srOptions.Add(new SrOption { Wkid = 4214, Display = "Beijing 1954 (EPSG:4214)" });
            _srOptions.Add(new SrOption { Wkid = 3857, Display = "Web Mercator (EPSG:3857)" });
            _srOptions.Add(new SrOption { Wkid = 32648, Display = "UTM Zone 48N (EPSG:32648)" });
            _srOptions.Add(new SrOption { Wkid = 32649, Display = "UTM Zone 49N (EPSG:32649)" });
            _srOptions.Add(new SrOption { Wkid = 32650, Display = "UTM Zone 50N (EPSG:32650)" });
            _srOptions.Add(new SrOption { Wkid = 32651, Display = "UTM Zone 51N (EPSG:32651)" });
            _srOptions.Add(new SrOption { Wkid = 32750, Display = "UTM Zone 50S (EPSG:32750)" });
            _srOptions.Add(new SrOption { Wkid = 32751, Display = "UTM Zone 51S (EPSG:32751)" });
            _srOptions.Add(new SrOption { Wkid = 4555, Display = "CGCS2000 3° GK Zone 38 (EPSG:4555)" });
            _srOptions.Add(new SrOption { Wkid = 4547, Display = "CGCS2000 3° GK Zone 41 (EPSG:4547)" });
            _srOptions.Add(new SrOption { Wkid = 4544, Display = "CGCS2000 3° GK Zone 44 (EPSG:4544)" });
            _srOptions.Add(new SrOption { Wkid = 4539, Display = "CGCS2000 3° GK Zone 49 (EPSG:4539)" });
            _srOptions.Add(new SrOption { Wkid = 4534, Display = "CGCS2000 3° GK Zone 54 (EPSG:4534)" });
            _srOptions.Add(new SrOption { Wkid = 4528, Display = "CGCS2000 3° GK Zone 60 (EPSG:4528)" });
            _srOptions.Add(new SrOption { Wkid = 4269, Display = "NAD83 (EPSG:4269)" });
            _srOptions.Add(new SrOption { Wkid = 4267, Display = "NAD27 (EPSG:4267)" });

            CboSpatialReference.ItemsSource = _srOptions;
        }

        private void BtnBrowseSr_Click(object sender, RoutedEventArgs e)
        {
            int defaultWkid = 4326;
            if (CboSpatialReference.SelectedItem is SrOption current)
                defaultWkid = current.Wkid;

            var browser = new SpatialReferenceBrowser(defaultWkid) { Owner = this };
            if (browser.ShowDialog() == true)
            {
                int idx = _srOptions.FindIndex(o => o.Wkid == browser.SelectedWkid);
                if (idx >= 0)
                {
                    CboSpatialReference.SelectedIndex = idx;
                }
                else
                {
                    var opt = new SrOption { Wkid = browser.SelectedWkid, Display = $"EPSG:{browser.SelectedWkid}" };
                    _srOptions.Add(opt);
                    CboSpatialReference.SelectedItem = opt;
                }
            }
        }

        private void RdoDecimal_Click(object sender, RoutedEventArgs e)
        {
            GridDecimal.Visibility = Visibility.Visible;
            GridDMS.Visibility = Visibility.Collapsed;
            GridProjected.Visibility = Visibility.Collapsed;
        }

        private void RdoDMS_Click(object sender, RoutedEventArgs e)
        {
            GridDecimal.Visibility = Visibility.Collapsed;
            GridDMS.Visibility = Visibility.Visible;
            GridProjected.Visibility = Visibility.Collapsed;
        }

        private void RdoProjected_Click(object sender, RoutedEventArgs e)
        {
            GridDecimal.Visibility = Visibility.Collapsed;
            GridDMS.Visibility = Visibility.Collapsed;
            GridProjected.Visibility = Visibility.Visible;
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double x, y;

                if (RdoDecimal.IsChecked == true)
                {
                    if (!double.TryParse(TxtLonDecimal.Text, out x))
                        throw new Exception("经度/X 坐标格式不正确");
                    if (!double.TryParse(TxtLatDecimal.Text, out y))
                        throw new Exception("纬度/Y 坐标格式不正确");
                }
                else if (RdoDMS.IsChecked == true)
                {
                    int lonDeg = int.TryParse(TxtLonDeg.Text, out var ld) ? ld : 0;
                    int lonMin = int.TryParse(TxtLonMin.Text, out var lm) ? lm : 0;
                    double lonSec = double.TryParse(TxtLonSec.Text, out var ls) ? ls : 0;

                    int latDeg = int.TryParse(TxtLatDeg.Text, out var pd) ? pd : 0;
                    int latMin = int.TryParse(TxtLatMin.Text, out var pm) ? pm : 0;
                    double latSec = double.TryParse(TxtLatSec.Text, out var ps) ? ps : 0;

                    x = DMStoDecimal(lonDeg, lonMin, lonSec, CboLonHemisphere.SelectedIndex == 1);
                    y = DMStoDecimal(latDeg, latMin, latSec, CboLatHemisphere.SelectedIndex == 1);
                }
                else
                {
                    if (!double.TryParse(TxtProjectedX.Text, out x))
                        throw new Exception("X 坐标格式不正确");
                    if (!double.TryParse(TxtProjectedY.Text, out y))
                        throw new Exception("Y 坐标格式不正确");
                }

                var opt = CboSpatialReference.SelectedItem as SrOption;
                int epsgCode = opt?.Wkid ?? 4326;
                var sr = SpatialReference.Create(epsgCode);

                ResultPoint = new MapPoint(x, y, sr);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("输入错误: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static double DMStoDecimal(int degrees, int minutes, double seconds, bool isWestOrSouth)
        {
            double decimalDegrees = Math.Abs(degrees) + minutes / 60.0 + seconds / 3600.0;
            return isWestOrSouth ? -decimalDegrees : decimalDegrees;
        }
    }
}
