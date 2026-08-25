using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace GeologicalMappingDesktop
{
    public partial class GeoreferenceToolbarWindow : Window
    {
        public class ControlPointItem : INotifyPropertyChanged
        {
            private double _residual;
            public int Index { get; set; }
            public double SourceX { get; set; }
            public double SourceY { get; set; }
            public double TargetX { get; set; }
            public double TargetY { get; set; }
            public double Col { get; set; }
            public double Row { get; set; }
            public double RefCol { get; set; }
            public double RefRow { get; set; }
            public bool HasTarget { get; set; }

            public string SourceXText => SourceX.ToString("F4");
            public string SourceYText => SourceY.ToString("F4");
            public string TargetXText => HasTarget ? TargetX.ToString("F4") : "—";
            public string TargetYText => HasTarget ? TargetY.ToString("F4") : "—";
            public double Residual
            {
                get => _residual;
                set { _residual = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResidualText)); }
            }
            public string ResidualText => HasTarget ? Residual.ToString("F4") : "—";
            public string RefColText => HasTarget && (RefCol != 0 || RefRow != 0) ? RefCol.ToString("F1") : "—";
            public string RefRowText => HasTarget && (RefCol != 0 || RefRow != 0) ? RefRow.ToString("F1") : "—";

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            public void NotifyAll()
            {
                OnPropertyChanged(nameof(SourceXText));
                OnPropertyChanged(nameof(SourceYText));
                OnPropertyChanged(nameof(TargetXText));
                OnPropertyChanged(nameof(TargetYText));
                OnPropertyChanged(nameof(RefColText));
                OnPropertyChanged(nameof(RefRowText));
                OnPropertyChanged(nameof(ResidualText));
                OnPropertyChanged(nameof(Index));
            }
        }

        public ObservableCollection<ControlPointItem> ControlPoints { get; } = new ObservableCollection<ControlPointItem>();
        public ObservableCollection<string> RasterLayerNames { get; } = new ObservableCollection<string>();

        public event Action AddControlPointToggled;
        public event Action DeleteSelectedPointRequested;
        public event Action ClearPointsRequested;
        public event Action ZoomToSelectedPointRequested;
        public event Action PrevPointRequested;
        public event Action NextPointRequested;
        public event Action TransformTypeChanged;
        public event Action ApplyRequested;
        public event Action AutoApplyChanged;
        public event Action ResetRequested;
        public event Action ImportRequested;
        public event Action ExportRequested;
        public event Action ExportDataRequested;
        public event Action BrowseRasterRequested;
        public event Action LayerSelectionChanged;
        public event Action ClosedByUser;

        public bool AutoApply
        {
            get => BtnAutoApply.IsChecked == true;
            set => BtnAutoApply.IsChecked = value;
        }

        public GeoreferenceToolbarWindow()
        {
            InitializeComponent();
            PointGrid.ItemsSource = ControlPoints;
            CboRasterLayer.ItemsSource = RasterLayerNames;
        }

        public string SelectedLayerName => CboRasterLayer.SelectedItem as string;

        public string TransformType
        {
            get
            {
                if (CboTransformType.SelectedItem is ComboBoxItem item && item.Tag != null)
                    return item.Tag.ToString();
                return "Similarity";
            }
        }

        public bool OverwriteOriginal => RdoOverwrite.IsChecked == true;
        public bool IsAddingControlPoint => BtnAddControlPoint.IsChecked == true;

        public ControlPointItem SelectedPoint => PointGrid.SelectedItem as ControlPointItem;

        public void SetRasterLayers(System.Collections.Generic.IEnumerable<string> names, string selected = null)
        {
            RasterLayerNames.Clear();
            foreach (var n in names)
                RasterLayerNames.Add(n);
            if (!string.IsNullOrEmpty(selected) && RasterLayerNames.Contains(selected))
                CboRasterLayer.SelectedItem = selected;
            else if (RasterLayerNames.Count > 0)
                CboRasterLayer.SelectedIndex = 0;
        }

        public void SetRmse(double? rmse)
        {
            TxtRmse.Text = rmse.HasValue ? rmse.Value.ToString("F6") : "—";
        }

        public void SetStatus(string text)
        {
            TxtStatus.Text = text ?? "";
        }

        public void SetAddPointMode(bool active)
        {
            BtnAddControlPoint.IsChecked = active;
        }

        public void SetGeorefModeEnabled(bool enabled)
        {
            // 非配准标签页时隐藏整个工具栏
            Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        public void RefreshPointIndices()
        {
            for (int i = 0; i < ControlPoints.Count; i++)
            {
                ControlPoints[i].Index = i + 1;
                ControlPoints[i].NotifyAll();
            }
            PointGrid.Items.Refresh();
        }

        private void BtnAddControlPoint_Click(object sender, RoutedEventArgs e)
        {
            AddControlPointToggled?.Invoke();
            SetStatus(IsAddingControlPoint
                ? "添加控制点：先在待校准图上点选源点，再在参考地图上点选目标点"
                : "已退出添加控制点模式");
        }

        private void BtnDeletePoint_Click(object sender, RoutedEventArgs e) => DeleteSelectedPointRequested?.Invoke();
        private void BtnClearPoints_Click(object sender, RoutedEventArgs e) => ClearPointsRequested?.Invoke();
        private void BtnPrevPoint_Click(object sender, RoutedEventArgs e) => PrevPointRequested?.Invoke();
        private void BtnNextPoint_Click(object sender, RoutedEventArgs e) => NextPointRequested?.Invoke();
        private void BtnZoomToPoint_Click(object sender, RoutedEventArgs e) => ZoomToSelectedPointRequested?.Invoke();
        private void BtnAutoApply_Click(object sender, RoutedEventArgs e) => AutoApplyChanged?.Invoke();
        private void CboTransformType_SelectionChanged(object sender, SelectionChangedEventArgs e) => TransformTypeChanged?.Invoke();
        private void BtnApply_Click(object sender, RoutedEventArgs e) => ApplyRequested?.Invoke();
        private void BtnReset_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke();
        private void BtnImport_Click(object sender, RoutedEventArgs e) => ImportRequested?.Invoke();
        private void BtnExport_Click(object sender, RoutedEventArgs e) => ExportRequested?.Invoke();
        private void BtnExportData_Click(object sender, RoutedEventArgs e) => ExportDataRequested?.Invoke();
        private void BtnBrowseRaster_Click(object sender, RoutedEventArgs e) => BrowseRasterRequested?.Invoke();
        private void CboRasterLayer_SelectionChanged(object sender, SelectionChangedEventArgs e) => LayerSelectionChanged?.Invoke();
        private void PointGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            ClosedByUser?.Invoke();
            Hide();
        }

        public void LoadControlPoints(IEnumerable<ControlPointItem> points)
        {
            ControlPoints.Clear();
            foreach (var p in points)
                ControlPoints.Add(p);
            RefreshPointIndices();
        }

        public List<ControlPointItem> SaveControlPoints()
        {
            return ControlPoints.ToList();
        }

        public string ExportToPts()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("控制点文件");
            sb.AppendLine($"变换类型: {TransformType}");
            sb.AppendLine($"点数: {ControlPoints.Count}");
            sb.AppendLine("#ID 源X 源Y 目标X 目标Y 像素Col 像素Row 残差");
            foreach (var cp in ControlPoints)
            {
                sb.AppendLine($"{cp.Index} {cp.SourceX:G8} {cp.SourceY:G8} {cp.TargetX:G8} {cp.TargetY:G8} {cp.Col:G8} {cp.Row:G8} {cp.ResidualText}");
            }
            return sb.ToString();
        }

        public string ExportToCsv()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ID,源X,源Y,目标X,目标Y,像素Col,像素Row,残差");
            foreach (var cp in ControlPoints)
            {
                sb.AppendLine($"{cp.Index},{cp.SourceX:G8},{cp.SourceY:G8},{cp.TargetX:G8},{cp.TargetY:G8},{cp.Col:G8},{cp.Row:G8},{cp.ResidualText}");
            }
            return sb.ToString();
        }

        public string ExportToTxt()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== 控制点列表 ===");
            sb.AppendLine($"变换类型: {TransformType}");
            sb.AppendLine($"点数: {ControlPoints.Count}");
            sb.AppendLine();
            sb.AppendLine($"{"ID",4} {"源X",14} {"源Y",14} {"目标X",14} {"目标Y",14} {"残差",10}");
            sb.AppendLine(new string('-', 70));
            foreach (var cp in ControlPoints)
            {
                sb.AppendLine($"{cp.Index,4} {cp.SourceX,14:G8} {cp.SourceY,14:G8} {cp.TargetX,14:G8} {cp.TargetY,14:G8} {cp.ResidualText,10}");
            }
            return sb.ToString();
        }

        public static List<ControlPointItem> ParsePts(string content)
        {
            var result = new List<ControlPointItem>();
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("#") || line.StartsWith("控制点") || line.StartsWith("变换") || line.StartsWith("点数") || line.StartsWith("ID")) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 7)
                {
                    if (double.TryParse(parts[1], out double sx) && double.TryParse(parts[2], out double sy) &&
                        double.TryParse(parts[3], out double tx) && double.TryParse(parts[4], out double ty) &&
                        double.TryParse(parts[5], out double col) && double.TryParse(parts[6], out double row))
                    {
                        result.Add(new ControlPointItem
                        {
                            SourceX = sx, SourceY = sy, TargetX = tx, TargetY = ty,
                            Col = col, Row = row, HasTarget = true
                        });
                    }
                }
            }
            return result;
        }

        public static List<ControlPointItem> ParseCsv(string content)
        {
            var result = new List<ControlPointItem>();
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool header = true;
            foreach (var line in lines)
            {
                if (header) { header = false; continue; }
                var parts = line.Split(',');
                if (parts.Length >= 7)
                {
                    if (double.TryParse(parts[1], out double sx) && double.TryParse(parts[2], out double sy) &&
                        double.TryParse(parts[3], out double tx) && double.TryParse(parts[4], out double ty) &&
                        double.TryParse(parts[5], out double col) && double.TryParse(parts[6], out double row))
                    {
                        result.Add(new ControlPointItem
                        {
                            SourceX = sx, SourceY = sy, TargetX = tx, TargetY = ty,
                            Col = col, Row = row, HasTarget = true
                        });
                    }
                }
            }
            return result;
        }

        public static List<ControlPointItem> ParseTxt(string content)
        {
            var result = new List<ControlPointItem>();
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool dataStarted = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("---")) { dataStarted = true; continue; }
                if (!dataStarted) continue;
                if (line.Trim().Length == 0) continue;
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 6)
                {
                    if (double.TryParse(parts[1], out double sx) && double.TryParse(parts[2], out double sy) &&
                        double.TryParse(parts[3], out double tx) && double.TryParse(parts[4], out double ty))
                    {
                        result.Add(new ControlPointItem
                        {
                            SourceX = sx, SourceY = sy, TargetX = tx, TargetY = ty,
                            HasTarget = true
                        });
                    }
                }
            }
            return result;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            ClosedByUser?.Invoke();
            Hide();
        }
    }
}
