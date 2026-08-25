using System;
using System.Windows;
using System.Windows.Controls;
using Esri.ArcGISRuntime.Geometry;

namespace GeologicalMappingDesktop
{
    public partial class DataEditPanel : UserControl
    {
        public event Action<string> DrawToolChanged;
        public event Action<string> PointTypeChanged;
        public event Action<string> LineTypeChanged;
        public event Action OpenAttributeTableRequested;
        public event Action TopologyCheckRequested;
        public event Action CoordinateProjectionRequested;
        public event Action GeoreferenceRequested;

        private bool _initializing;

        public DataEditPanel()
        {
            InitializeComponent();
            InitializePointTypeSelector();
            InitializeLineTypeSelector();
            Loaded += (s, e) => EnsureTypeSelectorsPopulated();
        }

        private void EnsureTypeSelectorsPopulated()
        {
            if (CmbPointType.Items.Count == 0) InitializePointTypeSelector();
            if (CmbLineType.Items.Count == 0) InitializeLineTypeSelector();
        }

        private void InitializePointTypeSelector()
        {
            _initializing = true;
            CmbPointType.Items.Clear();
            CmbPointType.Items.Add(FieldDataClassification.POINT_UNCLASSIFIED);
            foreach (var t in FieldDataClassification.PointTypes)
            {
                CmbPointType.Items.Add(t.DisplayName);
            }
            CmbPointType.SelectedIndex = 0;
            _initializing = false;
        }

        private void InitializeLineTypeSelector()
        {
            _initializing = true;
            CmbLineType.Items.Clear();
            CmbLineType.Items.Add(FieldDataClassification.LINE_UNCLASSIFIED);
            foreach (var t in FieldDataClassification.GeologicalLineTypes)
            {
                CmbLineType.Items.Add(t.DisplayName);
            }
            CmbLineType.SelectedIndex = 0;
            _initializing = false;
        }

        /// <summary>当前选择的点类型 code（空表示未分类）。</summary>
        public string SelectedPointTypeCode
        {
            get
            {
                if (CmbPointType.SelectedIndex <= 0) return "";
                int idx = CmbPointType.SelectedIndex - 1;
                if (idx >= 0 && idx < FieldDataClassification.PointTypes.Length)
                    return FieldDataClassification.PointTypes[idx].TypeCode;
                return "";
            }
        }

        /// <summary>当前选择的线类型 code（空表示未分类）。</summary>
        public string SelectedLineTypeCode
        {
            get
            {
                if (CmbLineType.SelectedIndex <= 0) return "";
                int idx = CmbLineType.SelectedIndex - 1;
                if (idx >= 0 && idx < FieldDataClassification.GeologicalLineTypes.Length)
                    return FieldDataClassification.GeologicalLineTypes[idx].TypeCode;
                return "";
            }
        }

        public void SetPointTypeSelectorEnabled(bool enabled)
        {
            CmbPointType.IsEnabled = enabled;
        }

        public void SetLineTypeSelectorEnabled(bool enabled)
        {
            CmbLineType.IsEnabled = enabled;
        }

        private void CmbPointType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            PointTypeChanged?.Invoke(SelectedPointTypeCode);
        }

        private void CmbLineType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            LineTypeChanged?.Invoke(SelectedLineTypeCode);
        }

        private void OnDrawToolChanged(string toolName)
        {
            DrawToolChanged?.Invoke(toolName);
        }

        private void BtnDrawPoint_Click(object sender, RoutedEventArgs e) => OnDrawToolChanged("Point");
        private void BtnDrawLine_Click(object sender, RoutedEventArgs e) => OnDrawToolChanged("Line");
        private void BtnDrawPolygon_Click(object sender, RoutedEventArgs e) => OnDrawToolChanged("Polygon");
        private void BtnEditFeature_Click(object sender, RoutedEventArgs e) => OnDrawToolChanged("EditFeature");
        private void BtnDeleteFeature_Click(object sender, RoutedEventArgs e) => OnDrawToolChanged("DeleteFeature");
        private void BtnMoveFeature_Click(object sender, RoutedEventArgs e) => OnDrawToolChanged("MoveFeature");

        private void BtnOpenAttributeTable_Click(object sender, RoutedEventArgs e)
        {
            OpenAttributeTableRequested?.Invoke();
        }

        private void BtnTopologyCheck_Click(object sender, RoutedEventArgs e)
        {
            TopologyCheckRequested?.Invoke();
        }

        private void BtnCoordinateProjection_Click(object sender, RoutedEventArgs e)
        {
            CoordinateProjectionRequested?.Invoke();
        }

        private void BtnGeoreference_Click(object sender, RoutedEventArgs e)
        {
            GeoreferenceRequested?.Invoke();
        }

        public void ResetDrawTool()
        {
            OnDrawToolChanged("");
        }

        public void SetDrawToolEnabled(bool enabled)
        {
            BtnDrawPoint.IsEnabled = enabled;
            BtnDrawLine.IsEnabled = enabled;
            BtnDrawPolygon.IsEnabled = enabled;
        }

        public void SetEditToolsEnabled(bool enabled)
        {
            BtnEditFeature.IsEnabled = enabled;
            BtnDeleteFeature.IsEnabled = enabled;
            BtnMoveFeature.IsEnabled = enabled;
        }

        public void SetDrawToolEnabledByGeometryType(GeometryType geometryType)
        {
            BtnDrawPoint.IsEnabled = geometryType == GeometryType.Point;
            BtnDrawLine.IsEnabled = geometryType == GeometryType.Polyline;
            BtnDrawPolygon.IsEnabled = geometryType == GeometryType.Polygon;
        }
    }
}
