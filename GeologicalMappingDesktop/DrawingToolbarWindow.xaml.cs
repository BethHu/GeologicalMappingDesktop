using System;
using System.Windows;
using System.Windows.Controls;
using Esri.ArcGISRuntime.Geometry;

namespace GeologicalMappingDesktop
{
    public partial class DrawingToolbarWindow : Window
    {
        public event Action<bool> SnappingToggled;
        public event Action NextPointRequested;
        public event Action CompleteRequested;
        public event Action CancelRequested;
        public event Action UndoRequested;
        public event Action AzimuthDistanceRequested;
        public event Action<bool> FreehandToggled;

        private bool _snappingEnabled = true;

        public bool IsSnappingEnabled => _snappingEnabled;

        public DrawingToolbarWindow()
        {
            InitializeComponent();
            UpdateSnapButtonVisual();
        }

        private void UpdateSnapButtonVisual()
        {
            if (BtnSnapToggle.IsChecked == true)
            {
                SnapIcon.Text = "⏺";
                BtnSnapToggle.ToolTip = "捕捉已开启（绿色），自动吸附要素顶点/边";
            }
            else
            {
                SnapIcon.Text = "⏺";
                BtnSnapToggle.ToolTip = "捕捉已关闭（灰色），点击开启";
            }
        }

        private void BtnSnapToggle_Click(object sender, RoutedEventArgs e)
        {
            _snappingEnabled = BtnSnapToggle.IsChecked == true;
            UpdateSnapButtonVisual();
            SnappingToggled?.Invoke(_snappingEnabled);
        }

        private void BtnNextPoint_Click(object sender, RoutedEventArgs e)
        {
            NextPointRequested?.Invoke();
        }

        private void BtnComplete_Click(object sender, RoutedEventArgs e)
        {
            CompleteRequested?.Invoke();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            CancelRequested?.Invoke();
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            UndoRequested?.Invoke();
        }

        private void BtnAzimuthDistance_Click(object sender, RoutedEventArgs e)
        {
            AzimuthDistanceRequested?.Invoke();
        }

        private void BtnFreehand_Click(object sender, RoutedEventArgs e)
        {
            FreehandToggled?.Invoke(BtnFreehand.IsChecked == true);
        }

        public void SetFreehandMode(bool isFreehand)
        {
            BtnFreehand.IsChecked = isFreehand;
        }

        public void UpdateGeometryType(string geomType)
        {
            string label = "";
            if (geomType == "Point") label = "绘制：点";
            else if (geomType == "Line") label = "绘制：线";
            else if (geomType == "Polygon") label = "绘制：面";
            TxtGeometryType.Text = label;
        }

        public void UpdateVertexCount(int count)
        {
            TxtVertexCount.Text = count > 0 ? $"顶点：{count}" : "";
        }

        public void UpdateCoordinate(double x, double y)
        {
            TxtCoordinate.Text = $"坐标：{x:F4}, {y:F4}";
        }

        public void ClearCoordinate()
        {
            TxtCoordinate.Text = "";
        }

        public void SetDrawToolActive(string toolName, int vertexCount)
        {
            UpdateGeometryType(toolName);
            UpdateVertexCount(vertexCount);
            ClearCoordinate();

            BtnComplete.IsEnabled = true; // Point uses right-click or button to complete
            BtnCancel.IsEnabled = true;
            BtnUndo.IsEnabled = (toolName == "Line" || toolName == "Polygon") && vertexCount > 0;
            BtnNextPoint.IsEnabled = true;
            BtnAzimuthDistance.IsEnabled = true;
            BtnFreehand.IsEnabled = (toolName == "Line" || toolName == "Polygon");
        }

        public void SetIdle()
        {
            TxtGeometryType.Text = "";
            TxtVertexCount.Text = "";
            TxtCoordinate.Text = "";
            BtnComplete.IsEnabled = false;
            BtnCancel.IsEnabled = false;
            BtnUndo.IsEnabled = false;
            BtnNextPoint.IsEnabled = false;
            BtnAzimuthDistance.IsEnabled = false;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            // 用户点 X 关闭浮动工具栏时，退出当前绘制工具（取消绘制、清除十字丝/橡皮筋预览）
            CancelRequested?.Invoke();
        }
    }
}
