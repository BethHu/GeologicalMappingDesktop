using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Rasters;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Esri.ArcGISRuntime.UI.Controls;
using Microsoft.Win32;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeologicalMappingDesktop
{
    public partial class MainWindow : Window
    {
        private string currentDrawTool = "";
        private string _currentPointTypeCode = "";      // 当前选择的点类型 code（DataEditPanel 点类型下拉框）
        private string _currentLineTypeCode = "";       // 当前选择的线类型 code（DataEditPanel 线类型下拉框）
        internal ProjectManager projectManager = new ProjectManager();
        internal GraphicsOverlay _drawingOverlay;
        private List<Graphic> _unsavedGraphics = new List<Graphic>(); // 未保存的图形，等待写入文件

        // ----- 点拾取交互相关字段 -----
        private MapPoint _pendingPoint = null;          // 待确认的地理点（十字丝位置）
        private MapPoint _trackingPoint = null;         // 鼠标跟踪点（点模式下始终跟随鼠标，_pendingPoint为null时十字丝绘制此点）
        private GraphicsOverlay _crosshairOverlay;      // 亮蓝色十字丝（地图叠加层，两视图通用）
        private Graphic _crosshairGraphic;
        private SimpleMarkerSymbol _crosshairSymbol;
        private List<Graphic> _savedGraphics = new List<Graphic>(); // 已保存的图形（点和线），用于撤销

        // ----- 线绘制相关字段 -----
        private List<MapPoint> _lineVertices = new List<MapPoint>(); // 当前线的折点列表
        private GraphicsOverlay _linePreviewOverlay;   // 线预览图层
        private Graphic _previewLineGraphic;           // 预览线图形
        private Graphic _previewVerticesGraphic;       // 预览折点图形

        // ----- 面绘制相关字段 -----
        private List<MapPoint> _polygonVertices = new List<MapPoint>(); // 当前面的折点列表
        private GraphicsOverlay _polygonPreviewOverlay; // 面预览图层
        private Graphic _previewPolygonGraphic;        // 预览面图形
        private Graphic _previewPolygonVerticesGraphic; // 预览折点图形

        private bool _isLayerEditMode = false;              // 是否处于图层编辑模式
        private string _currentEditLayerName = string.Empty; // 当前编辑的图层名称
        private string _currentEditFilePath = string.Empty; // 当前编辑图层的文件路径

        // ----- 选择工具相关字段 -----
        internal List<Feature> _selectedFeatures = new List<Feature>(); // 选中的要素列表
        private GraphicsOverlay _selectionOverlay; // 选择高亮图层

        // ----- 顶点编辑相关字段 -----
        private bool _isVertexEditing = false;
        private Feature _currentEditingFeature;
        private List<MapPoint> _currentVertices = new List<MapPoint>();
        private List<MapPoint> _originalVertices;         // 保存原始顶点，用于取消编辑
        private Geometry _originalEditGeometry;           // 编辑前完整几何（含多部件/孔洞），用于安全保存/撤销
        private List<List<MapPoint>> _preservedHoleRings = new List<List<MapPoint>>(); // 面的孔洞环，编辑时不破坏
        private string _currentEditingLayerName;
        private GraphicsOverlay _vertexOverlay;           // 顶点圆圈
        private GraphicsOverlay _editPreviewOverlay;       // 编辑预览（线/面实时跟随）
        private int _selectedVertexIndex = -1;
        private bool _isDraggingVertex = false;
        private MapPoint _vertexDragStartPosition;
        private MapCompileWindow _mapCompileWindow;
       // ----- 移动要素相关字段 -----
        private Feature _movePendingFeature = null; // 待移动的要素
        private MapPoint _moveAnchorPoint = null;   // 移动起点（首次点击位置）
        private GraphicsOverlay _movePreviewOverlay; // 移动预览图层
        private Graphic _movePreviewGraphic;        // 移动预览图形

        // ----- 捕捉相关字段 -----
        private bool _snappingEnabled = true;
        private GraphicsOverlay _snappingOverlay;
        private MapPoint _nearestSnapPoint;
        private bool _isSnappingToVertex = true;
        private const double SnapTolerancePixels = 12;
        private SimpleMarkerSymbol _snapVertexSymbol;
        private SimpleMarkerSymbol _snapEdgeSymbol;
        private DrawingToolbarWindow _drawingToolbar;

        // ----- 绘制相关字段 -----
        private bool _isDrawing = false;
        private bool _streamDrawActive = false;
        // ----- 流式/手绘绘制相关字段 -----
        private bool _isStreamDrawing = false;
        private bool _isFreehandMode = false;
        private const double StreamVertexDistance = 8; // 屏幕像素距离阈值，每移动这么多像素自动添加一个折点
        private Point _lastStreamScreenPoint;
        private List<MapPoint> _streamPoints = new List<MapPoint>();

        // ----- 编辑要素工具栏 -----
        private EditFeatureToolbarWindow _editFeatureToolbar;
        private bool _topologyLockEnabled = false;
        private bool _isAddingVertexMode = false;

        // ----- 双视图管理 -----
        internal MapView CurrentMapView => MapTabControl != null && MapTabControl.SelectedIndex == 1 ? MapView2 : MapView1;

        // 无参考影像的固定画布范围（WebMercator 坐标系，100km x 100km 方框）
        private static readonly Envelope UnreferencedCanvasExtent = new Envelope(
            0, 0, 100000, 100000, SpatialReferences.WebMercator);
        private MapPoint UnreferencedCanvasCenter => new MapPoint(
            (UnreferencedCanvasExtent.XMin + UnreferencedCanvasExtent.XMax) / 2,
            (UnreferencedCanvasExtent.YMin + UnreferencedCanvasExtent.YMax) / 2,
            SpatialReferences.WebMercator);

        // ----- 无参考影像 WPF 覆盖层 -----
        private Envelope _overlayExtent;          // 影像在 map 中的范围
        private bool _hasOverlay = false;
        private string _overlayLayerName;

        // 跟随当前激活视图的叠加层列表（切标签页时移入新视图）
        private GraphicsOverlay[] _sharedOverlays => new GraphicsOverlay[]
        {
            _selectionOverlay, _drawingOverlay, _linePreviewOverlay,
            _polygonPreviewOverlay, _editPreviewOverlay, _vertexOverlay,
            _movePreviewOverlay, _snappingOverlay, _crosshairOverlay
        };

        // ----- 空间校准（地理配准） -----
        private GeoreferenceToolbarWindow _georefToolbar;
        private bool _isGeorefMode = false;
        private bool _isGeorefAddingPoint = false;
        private bool _georefWaitingTarget = false;
        private MapPoint _georefPendingSource;
        private double _georefPendingCol, _georefPendingRow;
        private GraphicsOverlay _georefOverlay;

        private string _georefLayerName;
        private string _georefFilePath;
        private string _georefWorldBackup;
        private Envelope _georefOriginalExtent;
        private Envelope _overlayOriginalExtent;
        private int _georefPixelWidth;
        private int _georefPixelHeight;
        private SpatialReference _georefMapSr;

        // ----- 地图网格 -----


        // ----- 撤销相关字段 -----
        private Stack<EditOperation> _undoStack = new Stack<EditOperation>();
        private AttributeTableWindow _attributeTableWindow;
        private VersionManagerWindow _versionManager;

        public class FeatureSnapshot
        {
            public Geometry Geometry { get; set; }
            public Dictionary<string, object> Attributes { get; set; }
        }

        public class EditOperation
        {
            public Feature Feature { get; set; }
            public Geometry OriginalGeometry { get; set; }
            public Geometry NewGeometry { get; set; }
            public string OperationType { get; set; }
            public Graphic DeletedGraphic { get; set; }
            public Dictionary<string, object> DeletedAttributes { get; set; }
            
            public string LayerName { get; set; }
            public List<Field> OriginalFields { get; set; }
            public List<Field> NewFields { get; set; }
            public List<Feature> DeletedFeatures { get; set; }
            public List<FeatureSnapshot> DeletedSnapshots { get; set; }
            public string FieldName { get; set; }
            public object OldAttributeValue { get; set; }
            public object NewAttributeValue { get; set; }
            /// <summary>添加字段前整表快照，用于可靠撤销并保留属性值</summary>
            public List<FeatureSnapshot> TableSnapshots { get; set; }
            public GeometryType SnapshotGeometryType { get; set; }
            public SpatialReference SnapshotSpatialReference { get; set; }
        }

        private Geometry CloneGeometry(Geometry geometry)
        {
            // 必须用 JSON 克隆，才能保留多部件/孔洞；手写 Builder 会把所有环压成一个导致反向填色盖住全图
            if (geometry == null) return null;
            try { return Geometry.FromJson(geometry.ToJson()); }
            catch { return geometry; }
        }

        public static MainWindow Instance { get; private set; }

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();
            UpdateLoginButton();
            InitializeMap();
            SubscribeToDataEditPanel();
            SubscribeToMapEvents();
            InitializeTopologyCheck();
            InitializeSnapping();

            RightColumn.Width = new GridLength(0);
        }

        private void InitializeTopologyCheck()
        {
            PanelTopologyCheck.Initialize(MapView1, MapView2);
        }

        private void InitializeSnapping()
        {
            _snapVertexSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Diamond, System.Drawing.Color.Lime, 16)
            {
                Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.White, 2)
            };
            _snapEdgeSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Yellow, 12)
            {
                Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.White, 2)
            };
            _snappingOverlay = new GraphicsOverlay();
            CurrentMapView.GraphicsOverlays.Add(_snappingOverlay);
        }

        private void SubscribeToDataEditPanel()
        {
            PanelDataEdit.DrawToolChanged += async (toolName) =>
            {
                // 切换工具时清理前一个工具的临时状态
                if (currentDrawTool == "MoveFeature" && toolName != "MoveFeature")
                    CancelMoveFeature();
                if (_isVertexEditing && toolName != "EditFeature")
                    await SaveVertexEditAndExit();

                _isFreehandMode = false; // 重置手绘模式
                _isStreamDrawing = false;

                currentDrawTool = toolName;

                ClearSnapIndicator();

                // 显示/隐藏浮动绘制工具栏

                bool isDrawTool = toolName == "Point" || toolName == "Line" || toolName == "Polygon";
                if (isDrawTool)
                {
                    ShowDrawingToolbar(toolName);
                    _drawingToolbar.SetFreehandMode(false);
                }
                else
                {
                    HideDrawingToolbar();
                }

                // 显示/隐藏编辑要素工具栏
                if (toolName == "EditFeature")
                    ShowEditFeatureToolbar();
                else
                    HideEditFeatureToolbar();

                if (toolName == "Point")
                {
                    StatusBar.Text = "点工具：移动鼠标显示十字丝预览，右键选择保存/删除，中键拖动地图，Esc 清除十字丝";
                }
                else if (toolName == "Line")
                {
                    ClearLinePreview();
                    StatusBar.Text = "线工具已激活，左键添加折点，右键完成线（至少2点），中键拖动地图，按 Esc 取消";
                }
                else if (toolName == "Polygon")
                {
                    ClearPolygonPreview();
                    StatusBar.Text = "面工具已激活，左键添加折点，右键完成面（至少3点），中键拖动地图，按 Esc 取消，Backspace 撤销上一点";
                }
                else if (toolName == "Select")
                {
                    ClearCrosshair();
                    ClearLinePreview();
                    ClearPolygonPreview();
                    StatusBar.Text = "选择工具已激活，点击选中要素，Shift+点击多选，拉框框选，点击空白处取消选中";
                }
                else if (toolName == "EditFeature")
                {
                    ClearCrosshair();
                    ClearLinePreview();
                    ClearPolygonPreview();
                    StatusBar.Text = "编辑要素：点击要素进入顶点编辑，拖拽顶点修改形状，Delete 删除选中顶点，Esc 退出";
                }
                else if (toolName == "DeleteFeature")
                {
                    ClearCrosshair();
                    ClearLinePreview();
                    ClearPolygonPreview();
                    StatusBar.Text = "删除要素：点击要删除的要素（将弹出确认对话框），Esc 取消";
                }
                else if (toolName == "MoveFeature")
                {
                    ClearCrosshair();
                    ClearLinePreview();
                    ClearPolygonPreview();
                    StatusBar.Text = "移动要素：点击要素选中，再次点击目标位置完成移动，Esc 取消";
                }
                else
                {
                    ClearCrosshair();
                    ClearLinePreview();
                    ClearPolygonPreview();
                    ClearSelection();
                    StatusBar.Text = "就绪";
                }
            };

            PanelDataEdit.TopologyCheckRequested += () =>
            {
                ShowTopologyCheckPanel();
            };

            PanelDataEdit.CoordinateProjectionRequested += () =>
            {
                ShowCoordinateProjectionDialog();
            };

            PanelDataEdit.GeoreferenceRequested += () =>
            {
                ShowGeoreferenceToolbar();
            };
            PanelDataEdit.OpenAttributeTableRequested += () =>
            {
                string name = !string.IsNullOrEmpty(_currentEditLayerName)
                    ? _currentEditLayerName
                    : LayerManager.Layers.FirstOrDefault(l => l.IsEditable && !l.IsFolderPlaceholder)?.Name
                      ?? LayerManager.Layers.FirstOrDefault(l => !l.IsFolderPlaceholder)?.Name;
                if (string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("请先加载并选择一个矢量图层", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                ShowAttributeTable(name);
            };

            PanelDataEdit.PointTypeChanged += (code) =>
            {
                _currentPointTypeCode = code;
                ApplyPointTypeToEditLayerRenderer();
            };

            PanelDataEdit.LineTypeChanged += (code) =>
            {
                _currentLineTypeCode = code;
                ApplyLineTypeToEditLayerRenderer();
            };

        }

        private void ShowDrawingToolbar(string toolName)
        {
            if (_drawingToolbar == null)
            {
                _drawingToolbar = new DrawingToolbarWindow();
                _drawingToolbar.Owner = this;

                _drawingToolbar.SnappingToggled += (enabled) =>
                {
                    _snappingEnabled = enabled;
                    if (!enabled) ClearSnapIndicator();
                };

                _drawingToolbar.NextPointRequested += () =>
                {
                    HandleCoordinateInputForCurrentTool(false);
                };

                _drawingToolbar.CompleteRequested += () =>
                {
                    CompleteCurrentDrawing();
                    UpdateDrawingToolbar();
                };

                _drawingToolbar.CancelRequested += () =>
                {
                    CancelCurrentDrawing();
                    HideDrawingToolbar();
                    PanelDataEdit.ResetDrawTool();
                    UpdateDrawingToolbar();
                };

                _drawingToolbar.UndoRequested += () =>
                {
                    UndoLastVertex();
                    UpdateDrawingToolbar();
                };

                _drawingToolbar.AzimuthDistanceRequested += async () =>
                {
                    await ShowAzimuthDistanceDialogAsync();
                };

                _drawingToolbar.FreehandToggled += (isFreehand) =>
                {
                    _isFreehandMode = isFreehand;
                    if (isFreehand)
                    {
                        _isStreamDrawing = false;
                        if (currentDrawTool == "Line")
                        {
                            ClearLinePreview();
                            StatusBar.Text = "手绘模式（线）：按住鼠标拖拽自由绘制，自动生成折点，松开完成";
                        }
                        else if (currentDrawTool == "Polygon")
                        {
                            ClearPolygonPreview();
                            StatusBar.Text = "手绘模式（面）：按住鼠标拖拽自由绘制，自动生成折点，松开完成";
                        }
                    }
                    else
                    {
                        string baseMsg;
                        if (currentDrawTool == "Line")
                            baseMsg = "线工具：左键添加折点，右键完成（至少2点）";
                        else if (currentDrawTool == "Point")
                            baseMsg = "点工具：移动鼠标预览位置，右键保存或删除";
                        else
                            baseMsg = "面工具：左键添加折点，右键完成（至少3点）";
                        StatusBar.Text = baseMsg;
                    }
                };
            }

            int vertexCount = toolName == "Line" ? _lineVertices.Count :
                              toolName == "Polygon" ? _polygonVertices.Count : 0;
            _drawingToolbar.SetDrawToolActive(toolName, vertexCount);

            // 点模式：初始化跟踪十字丝到视图中心
            if (toolName == "Point")
            {
                var vp = CurrentMapView.GetCurrentViewpoint(ViewpointType.CenterAndScale);
                if (vp?.TargetGeometry is MapPoint mp)
                {
                    _trackingPoint = mp;
                    UpdateCrosshair();
                    // 延迟重试，防止 LocationToScreen 因地图未渲染完成返回 NaN
                    Dispatcher.BeginInvoke(new Action(() => UpdateCrosshair()), System.Windows.Threading.DispatcherPriority.Render);
                }
            }

            if (!_drawingToolbar.IsVisible)
            {
                _drawingToolbar.Show();
                var mainWindowPos = new System.Windows.Point(Left, Top);
                _drawingToolbar.Left = mainWindowPos.X + (ActualWidth - _drawingToolbar.Width) / 2;
                _drawingToolbar.Top = mainWindowPos.Y + 50;
            }
        }

        private void HideDrawingToolbar()
        {
            if (_drawingToolbar != null && _drawingToolbar.IsVisible)
                _drawingToolbar.Hide();
            _isFreehandMode = false;
            _isStreamDrawing = false;
            ClearSnapIndicator();
        }

        private void ShowEditFeatureToolbar()
        {
            if (_editFeatureToolbar == null)
            {
                _editFeatureToolbar = new EditFeatureToolbarWindow();
                _editFeatureToolbar.Owner = this;

                _editFeatureToolbar.DeletePointRequested += () =>
                {
                    RemoveSelectedVertex();
                };

                _editFeatureToolbar.AddPointRequested += () =>
                {
                    if (_currentEditingFeature == null || _currentVertices.Count == 0)
                    {
                        StatusBar.Text = "请先点击要素进入顶点编辑模式";
                        return;
                    }
                    _isAddingVertexMode = true;
                    StatusBar.Text = "添加顶点：点击目标边附近位置插入顶点（自动投影到图层坐标系），右键取消";
                };

                _editFeatureToolbar.ModifyPointToggled += (active) =>
                {
                    // Toggle between vertex drag mode and select mode
                    if (active)
                    {
                        StatusBar.Text = "修改模式：点击拖拽顶点移动位置";
                    }
                    else
                    {
                        StatusBar.Text = "选择模式：点击顶点选中，Delete 删除";
                    }
                };

                _editFeatureToolbar.TopologyLockToggled += (locked) =>
                {
                    _topologyLockEnabled = locked;
                    StatusBar.Text = locked ? "拓扑锁定已开启：编辑时保持拓扑关系" : "拓扑锁定已关闭";
                };

                _editFeatureToolbar.ApplySmoothRequested += () =>
                {
                    ApplySmoothingToCurrentFeature();
                };
            }

            if (!_editFeatureToolbar.IsVisible)
            {
                _editFeatureToolbar.Show();
                var mainWindowPos = new System.Windows.Point(Left, Top);
                _editFeatureToolbar.Left = mainWindowPos.X + (ActualWidth - _editFeatureToolbar.Width) / 2;
                _editFeatureToolbar.Top = mainWindowPos.Y + 50;
            }
        }

        private void HideEditFeatureToolbar()
        {
            if (_editFeatureToolbar != null && _editFeatureToolbar.IsVisible)
                _editFeatureToolbar.Hide();
        }

        private void ShowTopologyCheckPanel()
        {
            RightPanelBorder.Visibility = Visibility.Visible;
            RightColumn.Width = new GridLength(360);
            PanelDataEdit.Visibility = Visibility.Collapsed;
            PanelFieldData.Visibility = Visibility.Collapsed;
            PanelTopologyCheck.Visibility = Visibility.Visible;
            ModuleTitle.Text = "拓扑检查";
            BtnBackModule.Visibility = Visibility.Visible;
            BtnDataEdit.IsChecked = false;
            BtnMapCompile.IsChecked = false;
            BtnFieldData.IsChecked = false;
        }

        private async void ShowCoordinateProjectionDialog()
        {
            try
            {
                // 构建图层选项列表
                var layerOptions = new List<CoordinateProjectionWindow.LayerOption>();
                foreach (var li in LayerManager.Layers)
                {
                    var ft = li.MemoryFeatureTable ?? GetFeatureTableFromLayer(li.Layer);
                    if (ft == null) continue;
                    int wkid = ft.SpatialReference?.Wkid ?? 0;
                    layerOptions.Add(new CoordinateProjectionWindow.LayerOption
                    {
                        Display = $"{li.Name}  (EPSG:{wkid})",
                        LayerName = li.Name,
                        Wkid = wkid
                    });
                }

                // 回调：通过图层名获取可编辑要素表
                async Task<FeatureTable> getTableAsync(string name)
                {
                    return await EnsureEditableMemoryTableAsync(name);
                }

                // 回调：添加文件为新图层
                async Task addLayerFromFileAsync(string filePath, SpatialReference sr)
                {
                    try
                    {
                        var table = new ShapefileFeatureTable(filePath);
                        await table.LoadAsync();
                        var layerName = System.IO.Path.GetFileNameWithoutExtension(filePath);

                        // 检查是否已存在同名图层
                        var existing = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
                        if (existing != null)
                            layerName = layerName + "_投影转换";

                        var featureLayer = new FeatureLayer(table) { Name = layerName };
                        CurrentMapView.Map.OperationalLayers.Add(featureLayer);
                        SyncMapToOtherView(CurrentMapView);

                        bool isEditable = true;
                        LayerManager.AddLayer(featureLayer, isEditable, filePath);
                        SetDefaultRenderer(featureLayer, table.GeometryType);
                        StatusBar.Text = $"已添加转换后的图层: {layerName}";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"添加文件图层失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                var dialog = new CoordinateProjectionWindow(layerOptions, getTableAsync, addLayerFromFileAsync)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    string layerName = dialog.SourceLayerName;
                    if (!string.IsNullOrEmpty(layerName))
                    {
                        // 图层源：刷新显示
                        MarkLayerHasUnsavedChanges(layerName);
                        RefreshLayerDisplay(layerName);
                        UpdateAttributeTable();
                        StatusBar.Text = $"坐标系转换完成：{layerName}";
                        MessageBox.Show("坐标系转换完成！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else if (!string.IsNullOrEmpty(dialog.SourceFilePath))
                    {
                        // 文件源：已由对话框处理并通知添加图层
                        StatusBar.Text = "文件坐标系转换完成";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"坐标系转换失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowGeoreferenceToolbar()
        {
            EnsureGeorefOverlay();
            if (_georefToolbar == null)
            {
                _georefToolbar = new GeoreferenceToolbarWindow { Owner = this };
                _georefToolbar.AddControlPointToggled += () =>
                {
                    _isGeorefAddingPoint = _georefToolbar.IsAddingControlPoint;
                    _georefWaitingTarget = false;
                    _georefPendingSource = null;
                    if (_isGeorefAddingPoint)
                    {
                        _isGeorefMode = true;
                        StatusBar.Text = "空间校准：在待校准图上点击源点，再在参考图/地图上点击目标点（可缩放平移精确放置）";
                    }
                    else
                    {
                        StatusBar.Text = "已退出添加控制点";
                    }
                };
                _georefToolbar.DeleteSelectedPointRequested += () => GeorefDeleteSelectedPoint();
                _georefToolbar.ClearPointsRequested += () => GeorefClearPoints();
                _georefToolbar.PrevPointRequested += () => GeorefPrevPoint();
                _georefToolbar.NextPointRequested += () => GeorefNextPoint();
                _georefToolbar.ZoomToSelectedPointRequested += () => GeorefZoomToSelected();
                _georefToolbar.TransformTypeChanged += () => GeorefUpdateResidualsAndPreview();
                _georefToolbar.AutoApplyChanged += () => GeorefAutoApplyPreview();
                _georefToolbar.ApplyRequested += async () => await GeorefApplyAsync();
                _georefToolbar.ResetRequested += async () => await GeorefResetAsync();
                _georefToolbar.ImportRequested += () => GeorefImport();
                _georefToolbar.ExportRequested += () => GeorefExport();
                _georefToolbar.ExportDataRequested += async () => await GeorefExportDataAsync();
                _georefToolbar.BrowseRasterRequested += async () => await GeorefBrowseRasterAsync();
                _georefToolbar.LayerSelectionChanged += async () => await GeorefOnLayerChangedAsync();
                _georefToolbar.ClosedByUser += () =>
                {
                    _isGeorefMode = false;
                    _isGeorefAddingPoint = false;
                    _georefWaitingTarget = false;
                    ClearGeorefOverlay();
                    StatusBar.Text = "空间校准已关闭";
                };
            }

            RefreshGeorefLayerList();
            if (!_georefToolbar.IsVisible)
            {
                _georefToolbar.Show();
                _georefToolbar.Left = Left + ActualWidth - _georefToolbar.Width - 40;
                _georefToolbar.Top = Top + 80;
            }
            _isGeorefMode = true;
            StatusBar.Text = "空间校准工具已打开";
        }

        private void RefreshGeorefLayerList()
        {
            if (_georefToolbar == null) return;
            var names = new List<string>();
            foreach (var li in LayerManager.Layers)
            {
                if (li.Layer is RasterLayer || li.Layer is GroupLayer)
                    names.Add(li.Name);
            }
            _georefToolbar.SetRasterLayers(names, _georefLayerName);
        }

        private async System.Threading.Tasks.Task GeorefOnLayerChangedAsync()
        {
            if (_georefToolbar == null) return;
            string name = _georefToolbar.SelectedLayerName;
            if (string.IsNullOrEmpty(name)) return;

            var li = LayerManager.Layers.FirstOrDefault(l => l.Name == name);
            if (li == null || li.Layer == null)
            {
                _georefToolbar.SetStatus("请选择栅格图层");
                return;
            }

            _georefLayerName = name;
            _georefFilePath = li.FilePath;

            // 支持无参考影像覆盖层（GroupLayer 占位 + WPF Image 覆盖）
            if (li.Layer is RasterLayer rl)
            {
                try
                {
                    if (rl.LoadStatus != LoadStatus.Loaded)
                        await rl.LoadAsync();
                    var raster = rl.Raster;
                    if (raster != null && raster.LoadStatus != LoadStatus.Loaded)
                        await raster.LoadAsync();

                    _georefMapSr = rl.SpatialReference ?? CurrentMapView.SpatialReference ?? SpatialReferences.WebMercator;
                    TryGetImageSize(_georefFilePath, out _georefPixelWidth, out _georefPixelHeight);

                    if (rl.FullExtent != null && !rl.FullExtent.IsEmpty)
                    {
                        _georefOriginalExtent = new Envelope(rl.FullExtent.XMin, rl.FullExtent.YMin, rl.FullExtent.XMax, rl.FullExtent.YMax, rl.FullExtent.SpatialReference);
                    }
                    else if (_georefPixelWidth > 0 && _georefPixelHeight > 0)
                    {
                        var mapSr = _georefMapSr;
                        MapPoint center = new MapPoint(0, 0, mapSr);
                        if (CurrentMapView.VisibleArea?.Extent != null)
                            center = CurrentMapView.VisibleArea.Extent.GetCenter();
                        double cellSize = 10;
                        if (CurrentMapView.VisibleArea?.Extent != null)
                        {
                            double visW = CurrentMapView.VisibleArea.Extent.Width;
                            double visH = CurrentMapView.VisibleArea.Extent.Height;
                            cellSize = Math.Min(visW / _georefPixelWidth, visH / _georefPixelHeight) * 0.3;
                            cellSize = Math.Max(1, cellSize);
                        }
                        double halfW = _georefPixelWidth * cellSize / 2;
                        double halfH = _georefPixelHeight * cellSize / 2;
                        _georefOriginalExtent = new Envelope(center.X - halfW, center.Y - halfH,
                                                             center.X + halfW, center.Y + halfH, mapSr);
                    }
                }
                catch (Exception ex)
                {
                    _georefToolbar.SetStatus("加载图层失败: " + ex.Message);
                    return;
                }
            }
            else if (_hasOverlay && li.Name == _overlayLayerName && _overlayExtent != null)
            {
                // 无参考影像覆盖层：从覆盖层缓存获取范围
                _georefMapSr = _overlayExtent.SpatialReference ?? CurrentMapView.SpatialReference ?? SpatialReferences.WebMercator;
                TryGetImageSize(_georefFilePath, out _georefPixelWidth, out _georefPixelHeight);
                _georefOriginalExtent = new Envelope(_overlayExtent.XMin, _overlayExtent.YMin,
                                                     _overlayExtent.XMax, _overlayExtent.YMax,
                                                     _overlayExtent.SpatialReference);
            }
            else
            {
                _georefToolbar.SetStatus("请选择栅格图层");
                return;
            }

            if (_georefPixelWidth <= 0 || _georefPixelHeight <= 0)
            {
                _georefToolbar.SetStatus("无法读取栅格像素尺寸");
                return;
            }

            GeorefClearPoints(false);
            _georefToolbar.SetStatus($"已选择 {name}，像素 {_georefPixelWidth}×{_georefPixelHeight}，点击「添加控制点」开始");
            if (_georefOriginalExtent != null && !_georefOriginalExtent.IsEmpty)
                await CurrentMapView.SetViewpointGeometryAsync(_georefOriginalExtent, 20);
        }

        private static void TryGetImageSize(string path, out int w, out int h)
        {
            w = 0; h = 0;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                using (var img = System.Drawing.Image.FromFile(path))
                {
                    w = img.Width;
                    h = img.Height;
                }
            }
            catch { }
        }

        private void EnsureGeorefOverlay()
        {
            if (_georefOverlay == null)
            {
                _georefOverlay = new GraphicsOverlay
                {
                    RenderingMode = GraphicsRenderingMode.Static
                };
                MapView2.GraphicsOverlays.Add(_georefOverlay);
            }
        }

        private void ClearGeorefOverlay()
        {
            GeorefCanvas?.Children.Clear();
            if (_georefOverlay != null)
                _georefOverlay.Graphics.Clear();
        }

        private async void GeorefHandleMapClick(MapPoint mapPoint, MapView sourceView)
        {
            if (!_isGeorefMode || !_isGeorefAddingPoint || mapPoint == null || _georefToolbar == null)
                return;
            if (string.IsNullOrEmpty(_georefLayerName))
            {
                StatusBar.Text = "请先选择待校准栅格图层";
                return;
            }

            var pt = mapPoint;
            // 启用捕捉时使用捕捉点
            if (_snappingEnabled && _nearestSnapPoint != null)
            {
                pt = _nearestSnapPoint;
                ClearSnapIndicator();
            }

            if (pt.SpatialReference != null && _georefMapSr != null && pt.SpatialReference.Wkid != _georefMapSr.Wkid)
            {
                try { pt = (MapPoint)GeometryEngine.Project(pt, _georefMapSr); } catch { }
            }

            if (!_georefWaitingTarget)
            {
                double col = 0, row = 0;
                bool pixelOk = false;

                // 对 WPF Image Overlay（JPG/PNG）从屏幕坐标反算像素坐标，不依赖 _georefOriginalExtent
                if (_hasOverlay)
                {
                    var screenPt = sourceView.LocationToScreen(pt);
                    if (!double.IsNaN(screenPt.X))
                        pixelOk = ScreenToPixel(screenPt, out col, out row);
                }
                else
                {
                    pixelOk = MapToPixel(pt, out col, out row);
                }

                if (!pixelOk)
                {
                    // 如果失败（RasterLayer/GeoTIFF），尝试通过 Identify 获取像素坐标
                    var screenPt = sourceView.LocationToScreen(pt);
                    if (!double.IsNaN(screenPt.X))
                    {
                        var results = await sourceView.IdentifyLayersAsync(screenPt, 5, false);
                        foreach (var result in results)
                        {
                            if (result.LayerContent is RasterLayer rl && rl.Name == _georefLayerName)
                            {
                                if (rl.FullExtent != null && !rl.FullExtent.IsEmpty)
                                {
                                    _georefOriginalExtent = new Envelope(rl.FullExtent.XMin, rl.FullExtent.YMin,
                                        rl.FullExtent.XMax, rl.FullExtent.YMax, rl.FullExtent.SpatialReference);
                                    if (MapToPixel(pt, out col, out row))
                                    { pixelOk = true; break; }
                                }
                                if (_georefPixelWidth > 0 && _georefPixelHeight > 0)
                                {
                                    var mapSr = _georefMapSr ?? sourceView.SpatialReference ?? SpatialReferences.WebMercator;
                                    double size = Math.Max(_georefPixelWidth, _georefPixelHeight) * 0.5;
                                    _georefOriginalExtent = new Envelope(pt.X - size / 2, pt.Y - size / 2,
                                        pt.X + size / 2, pt.Y + size / 2, mapSr);
                                    if (MapToPixel(pt, out col, out row))
                                    { pixelOk = true; break; }
                                }
                            }
                        }
                    }
                    if (!pixelOk)
                    {
                        StatusBar.Text = "源点不在栅格范围内，请点击待校准图";
                        return;
                    }
                }
                _georefPendingSource = pt;
                _georefPendingCol = col;
                _georefPendingRow = row;
                _georefWaitingTarget = true;
                var sp = ImagePixelToScreen(col, row);
                if (sp != null)
                {
                    double sx = sp.Value.X, sy = sp.Value.Y, half = 7;
                    var hLine = new System.Windows.Shapes.Line
                    { X1 = sx - half, Y1 = sy, X2 = sx + half, Y2 = sy,
                      Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Lime), StrokeThickness = 2 };
                    var vLine = new System.Windows.Shapes.Line
                    { X1 = sx, Y1 = sy - half, X2 = sx, Y2 = sy + half,
                      Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Lime), StrokeThickness = 2 };
                    GeorefCanvas.Children.Add(hLine);
                    GeorefCanvas.Children.Add(vLine);
                }
                StatusBar.Text = "已记录源点，请在参考图上点击目标点（可缩放平移）";
                _georefToolbar.SetStatus($"源点像素 ({col:F1}, {row:F1})，请点击目标位置");
            }
            else
            {
                // 计算参考图上的像素坐标（从图层管理器获取参考栅格文件信息）
                double refCol = 0, refRow = 0;
                try
                {
                    var li = LayerManager.Layers.FirstOrDefault(l => l.Layer != null &&
                        l.Layer is RasterLayer r && r.Name != _georefLayerName &&
                        r.FullExtent != null && !r.FullExtent.IsEmpty &&
                        File.Exists(l.FilePath));
                    if (li != null)
                    {
                        TryGetImageSize(li.FilePath, out int refW, out int refH);
                        if (refW > 0 && refH > 0)
                        {
                            MapToPixelExtent(pt,
                                ((RasterLayer)li.Layer).FullExtent,
                                refW, refH, out refCol, out refRow);
                        }
                    }
                }
                catch { }

                // 目标点（可在任一视图上点击）
                var item = new GeoreferenceToolbarWindow.ControlPointItem
                {
                    SourceX = _georefPendingSource.X,
                    SourceY = _georefPendingSource.Y,
                    TargetX = pt.X,
                    TargetY = pt.Y,
                    Col = _georefPendingCol,
                    Row = _georefPendingRow,
                    RefCol = refCol,
                    RefRow = refRow,
                    HasTarget = true
                };
                _georefToolbar.ControlPoints.Add(item);
                _georefToolbar.RefreshPointIndices();
                _georefWaitingTarget = false;
                _georefPendingSource = null;
                GeorefRedrawLinks();
                GeorefUpdateResidualsAndPreview();
                // ArcGIS 行为：第一个控制点添加后图片平移到目标点（无论自动应用是否开启）
                if (_georefToolbar.ControlPoints.Count == 1 && _hasOverlay && _overlayExtent != null)
                {
                    GeorefApplyTranslation(item);
                }
                StatusBar.Text = $"已添加控制点 #{_georefToolbar.ControlPoints.Count}，可继续添加或执行校正";
                _georefToolbar.SetStatus($"控制点 {_georefToolbar.ControlPoints.Count} 个");
            }
        }

        private bool MapToPixel(MapPoint mapPt, out double col, out double row)
        {
            col = 0; row = 0;
            if (_georefOriginalExtent == null || _georefPixelWidth <= 0 || _georefPixelHeight <= 0)
                return false;
            var e = _georefOriginalExtent;
            double w = e.Width;
            double h = e.Height;
            if (w < 1e-15 || h < 1e-15) return false;

            col = (mapPt.X - e.XMin) / w * _georefPixelWidth - 0.5;
            row = (e.YMax - mapPt.Y) / h * _georefPixelHeight - 0.5;
            if (col < -2.5 || row < -2.5 || col > _georefPixelWidth + 1.5 || row > _georefPixelHeight + 1.5)
                return false;
            col = Math.Max(-0.5, Math.Min(_georefPixelWidth - 0.5, col));
            row = Math.Max(-0.5, Math.Min(_georefPixelHeight - 0.5, row));
            return true;
        }

        private bool MapToPixelExtent(MapPoint mapPt, Envelope extent, int pixelW, int pixelH,
            out double col, out double row)
        {
            col = 0; row = 0;
            if (extent == null || extent.IsEmpty || pixelW <= 0 || pixelH <= 0)
                return false;
            double w = extent.Width;
            double h = extent.Height;
            if (w < 1e-15 || h < 1e-15) return false;
            col = (mapPt.X - extent.XMin) / w * pixelW - 0.5;
            row = (extent.YMax - mapPt.Y) / h * pixelH - 0.5;
            if (col < -2.5 || row < -2.5 || col > pixelW + 1.5 || row > pixelH + 1.5)
                return false;
            col = Math.Max(-0.5, Math.Min(pixelW - 0.5, col));
            row = Math.Max(-0.5, Math.Min(pixelH - 0.5, row));
            return true;
        }

        private System.Windows.Point? ImagePixelToScreen(double col, double row)
        {
            if (!_hasOverlay || _georefPixelWidth <= 0 || _georefPixelHeight <= 0) return null;
            double nx = (col + 0.5) / _georefPixelWidth;
            double ny = (row + 0.5) / _georefPixelHeight;
            double imgX = nx * UnreferencedImageOverlay.Width;
            double imgY = ny * UnreferencedImageOverlay.Height;
            var margin = UnreferencedImageOverlay.Margin;
            var xform = UnreferencedImageOverlay.RenderTransform as System.Windows.Media.MatrixTransform;
            if (xform != null)
            {
                var m = xform.Matrix;
                double sx = imgX * m.M11 + imgY * m.M21 + m.OffsetX + margin.Left;
                double sy = imgX * m.M12 + imgY * m.M22 + m.OffsetY + margin.Top;
                return new System.Windows.Point(sx, sy);
            }
            if (UnreferencedImageOverlay.Width > 0 && UnreferencedImageOverlay.Height > 0)
                return new System.Windows.Point(margin.Left + imgX, margin.Top + imgY);
            return null;
        }

        private bool ScreenToPixel(System.Windows.Point screenPt, out double col, out double row)
        {
            col = 0; row = 0;
            if (!_hasOverlay || UnreferencedImageOverlay.Source == null ||
                _georefPixelWidth <= 0 || _georefPixelHeight <= 0)
                return false;
            try
            {
                var margin = UnreferencedImageOverlay.Margin;
                double dispW = UnreferencedImageOverlay.Width;
                double dispH = UnreferencedImageOverlay.Height;
                if (dispW <= 0 || dispH <= 0) return false;

                double imgX, imgY;
                var xform = UnreferencedImageOverlay.RenderTransform as System.Windows.Media.MatrixTransform;
                if (xform != null)
                {
                    var m = xform.Matrix;
                    if (m.HasInverse)
                    {
                        m.Invert();
                        double sx = screenPt.X - margin.Left;
                        double sy = screenPt.Y - margin.Top;
                        imgX = sx * m.M11 + sy * m.M21 + m.OffsetX;
                        imgY = sx * m.M12 + sy * m.M22 + m.OffsetY;
                    }
                    else
                    {
                        imgX = screenPt.X - margin.Left;
                        imgY = screenPt.Y - margin.Top;
                    }
                }
                else
                {
                    imgX = screenPt.X - margin.Left;
                    imgY = screenPt.Y - margin.Top;
                }

                col = (imgX / dispW) * _georefPixelWidth - 0.5;
                row = (imgY / dispH) * _georefPixelHeight - 0.5;
                if (col < -2.5 || row < -2.5 || col > _georefPixelWidth + 1.5 || row > _georefPixelHeight + 1.5)
                    return false;
                col = Math.Max(-0.5, Math.Min(_georefPixelWidth - 0.5, col));
                row = Math.Max(-0.5, Math.Min(_georefPixelHeight - 0.5, row));
                return true;
            }
            catch { return false; }
        }

        private System.Windows.Media.Color ResidualColor(double residual, double threshold)
        {
            if (double.IsNaN(residual)) return System.Windows.Media.Colors.Cyan;
            return residual <= threshold ? System.Windows.Media.Colors.Lime : System.Windows.Media.Colors.Red;
        }

        private MapPoint PixelToMapPoint(double col, double row)
        {
            var sr = _georefMapSr ?? _overlayExtent?.SpatialReference ?? SpatialReferences.WebMercator;
            if (_georefAffineParams != null)
            {
                GeoreferenceHelper.TransformAffine(_georefAffineParams, col, row, out double mx0, out double my0);
                return new MapPoint(mx0, my0, sr);
            }
            var e = _georefOriginalExtent;
            if (e == null) return null;
            double mx = e.XMin + ((col + 0.5) / _georefPixelWidth) * e.Width;
            double my = e.YMax - ((row + 0.5) / _georefPixelHeight) * e.Height;
            return new MapPoint(mx, my, e.SpatialReference ?? sr);
        }

        private void GeorefRedrawGraphics()
        {
            if (!_isGeorefMode) return;
            if (_georefOverlay == null) return;
            _georefOverlay.Graphics.Clear();
            if (_georefToolbar == null) return;

            double threshold = 10.0;
            var valid = _georefToolbar.ControlPoints.Where(p => p.HasTarget && !double.IsNaN(p.Residual)).ToList();
            if (valid.Count > 0)
                threshold = valid.Average(p => p.Residual) * 1.5;

            foreach (var cp in _georefToolbar.ControlPoints)
            {
                if (!cp.HasTarget) continue;
                var mp = PixelToMapPoint(cp.Col, cp.Row);
                if (mp == null) continue;

                var color = ResidualColor(cp.Residual, threshold);
                var scolor = System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

                var cross = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Cross, scolor, 14)
                {
                    Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, scolor, 2)
                };
                _georefOverlay.Graphics.Add(new Graphic(mp, cross));

                // Draw line from source to target showing residual
                var lineColor = System.Drawing.Color.FromArgb(120, color.R, color.G, color.B);
                var line = new Polyline(new[] { mp, new MapPoint(cp.TargetX, cp.TargetY, mp.SpatialReference) });
                _georefOverlay.Graphics.Add(new Graphic(line, new SimpleLineSymbol(SimpleLineSymbolStyle.Dash, lineColor, 1)));

                // Draw target (reference) point marker
                var diamond = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Diamond, System.Drawing.Color.Orange, 10)
                {
                    Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Orange, 2)
                };
                _georefOverlay.Graphics.Add(new Graphic(new MapPoint(cp.TargetX, cp.TargetY, mp.SpatialReference), diamond));
            }

            if (_georefWaitingTarget && _georefPendingSource != null)
            {
                var mp = PixelToMapPoint(_georefPendingCol, _georefPendingRow);
                if (mp != null)
                {
                    var cyan = System.Drawing.Color.Cyan;
                    var pending = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Cross, cyan, 12)
                    {
                        Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, cyan, 2)
                    };
                    _georefOverlay.Graphics.Add(new Graphic(mp, pending));
                }
            }
        }

        private void GeorefRedrawLinks()
        {
            if (!_isGeorefMode) return;
            GeorefCanvas?.Children.Clear();
            if (_georefToolbar == null) return;

            // 对 WPF Image overlay 路径，清除图形覆盖上的菱形，避免重复累积
            if (_hasOverlay && _georefOverlay != null)
                _georefOverlay.Graphics.Clear();

            if (!_hasOverlay)
            {
                GeorefRedrawGraphics();
                return;
            }

            double threshold = 10.0;
            if (_georefToolbar.ControlPoints.Count > 0)
            {
                var valid = _georefToolbar.ControlPoints.Where(p => p.HasTarget && !double.IsNaN(p.Residual)).ToList();
                if (valid.Count > 0)
                    threshold = valid.Average(p => p.Residual) * 1.5;
            }

            foreach (var cp in _georefToolbar.ControlPoints)
            {
                if (!cp.HasTarget) continue;
                var sp = ImagePixelToScreen(cp.Col, cp.Row);
                if (sp == null) continue;
                double sx = sp.Value.X, sy = sp.Value.Y;

                bool good = !double.IsNaN(cp.Residual) && cp.Residual <= threshold;
                var color = good ? System.Windows.Media.Colors.Lime : System.Windows.Media.Colors.Red;
                double half = 8;

                var hLine = new System.Windows.Shapes.Line
                {
                    X1 = sx - half, Y1 = sy, X2 = sx + half, Y2 = sy,
                    Stroke = new System.Windows.Media.SolidColorBrush(color), StrokeThickness = 2
                };
                var vLine = new System.Windows.Shapes.Line
                {
                    X1 = sx, Y1 = sy - half, X2 = sx, Y2 = sy + half,
                    Stroke = new System.Windows.Media.SolidColorBrush(color), StrokeThickness = 2
                };
                GeorefCanvas.Children.Add(hLine);
                GeorefCanvas.Children.Add(vLine);

                // 在 _georefOverlay 上绘制参考点（橙色菱形），对所有图片类型可见
                var sr = _georefMapSr ?? SpatialReferences.WebMercator;
                var diamond = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Diamond,
                    System.Drawing.Color.Orange, 10)
                {
                    Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
                        System.Drawing.Color.Orange, 2)
                };
                _georefOverlay.Graphics.Add(new Graphic(
                    new MapPoint(cp.TargetX, cp.TargetY, sr), diamond));

                // 从参考点（源）到校准点（目标）画连线，显示距离差异
                var mpSrc = PixelToMapPoint(cp.Col, cp.Row);
                if (mpSrc != null)
                {
                    var connColor = System.Drawing.Color.FromArgb(120,
                        good ? System.Drawing.Color.Lime.R : System.Drawing.Color.Red.R,
                        good ? System.Drawing.Color.Lime.G : System.Drawing.Color.Red.G,
                        good ? System.Drawing.Color.Lime.B : System.Drawing.Color.Red.B);
                    var connLine = new Polyline(new[] { mpSrc, new MapPoint(cp.TargetX, cp.TargetY, sr) });
                    _georefOverlay.Graphics.Add(new Graphic(connLine,
                        new SimpleLineSymbol(SimpleLineSymbolStyle.Dash, connColor, 1)));
                }
            }

            if (_georefWaitingTarget && _georefPendingSource != null)
            {
                double col = _georefPendingCol, row = _georefPendingRow;
                var sp = ImagePixelToScreen(col, row);
                if (sp != null)
                {
                    double sx = sp.Value.X, sy = sp.Value.Y, half = 7;
                    var hLine = new System.Windows.Shapes.Line
                    {
                        X1 = sx - half, Y1 = sy, X2 = sx + half, Y2 = sy,
                        Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Cyan), StrokeThickness = 2
                    };
                    var vLine = new System.Windows.Shapes.Line
                    {
                        X1 = sx, Y1 = sy - half, X2 = sx, Y2 = sy + half,
                        Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Cyan), StrokeThickness = 2
                    };
                    GeorefCanvas.Children.Add(hLine);
                    GeorefCanvas.Children.Add(vLine);
                }
            }

        }

        private void GeorefInputTarget()
        {
            var dialog = new CoordinateInputDialog { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var targetPt = dialog.ResultPoint;
            if (targetPt == null) return;

            // 将输入的坐标投影到地图坐标系
            MapPoint mapPt = targetPt;
            if (targetPt.SpatialReference != null && _georefMapSr != null &&
                targetPt.SpatialReference.Wkid != _georefMapSr.Wkid)
            {
                try { mapPt = (MapPoint)GeometryEngine.Project(targetPt, _georefMapSr); } catch { }
            }

            var item = new GeoreferenceToolbarWindow.ControlPointItem
            {
                SourceX = _georefPendingSource?.X ?? mapPt.X,
                SourceY = _georefPendingSource?.Y ?? mapPt.Y,
                TargetX = mapPt.X,
                TargetY = mapPt.Y,
                Col = _georefPendingCol,
                Row = _georefPendingRow,
                HasTarget = true
            };
            _georefToolbar.ControlPoints.Add(item);
            _georefToolbar.RefreshPointIndices();
            _georefWaitingTarget = false;
            _georefPendingSource = null;
            GeorefRedrawLinks();
            GeorefUpdateResidualsAndPreview();
            if (_georefToolbar.ControlPoints.Count == 1 && _hasOverlay && _overlayExtent != null)
                GeorefApplyTranslation(item);
            StatusBar.Text = $"已添加控制点 #{_georefToolbar.ControlPoints.Count}（键盘输入）";
            _georefToolbar.SetStatus($"控制点 {_georefToolbar.ControlPoints.Count} 个");
        }

        private void GeorefInputSource()
        {
            var dialog = new CoordinateInputDialog { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var srcPt = dialog.ResultPoint;
            if (srcPt == null) return;

            // 投影到地图坐标系
            MapPoint mapPt = srcPt;
            if (srcPt.SpatialReference != null && _georefMapSr != null &&
                srcPt.SpatialReference.Wkid != _georefMapSr.Wkid)
            {
                try { mapPt = (MapPoint)GeometryEngine.Project(srcPt, _georefMapSr); } catch { }
            }

            // 从地图坐标反算像素坐标
            double col = 0, row = 0;
            if (_overlayExtent != null && _georefPixelWidth > 0 && _georefPixelHeight > 0)
            {
                var e = _overlayExtent;
                col = (mapPt.X - e.XMin) / e.Width * _georefPixelWidth - 0.5;
                row = (e.YMax - mapPt.Y) / e.Height * _georefPixelHeight - 0.5;
                col = Math.Max(-0.5, Math.Min(_georefPixelWidth - 0.5, col));
                row = Math.Max(-0.5, Math.Min(_georefPixelHeight - 0.5, row));
            }

            _georefPendingSource = mapPt;
            _georefPendingCol = col;
            _georefPendingRow = row;
            _georefWaitingTarget = true;
            GeorefRedrawLinks();
            StatusBar.Text = $"已设置源点坐标 ({mapPt.X:F2}, {mapPt.Y:F2})，请在参考图上点击目标点或右键输入坐标";
            _georefToolbar.SetStatus("源点已通过键盘设置，请点击目标位置或右键输入");
        }

        private bool GeorefTrySolve(out GeoreferenceHelper.AffineParams affine, out GeoreferenceHelper.Poly2Params poly2, out double rmse)
        {
            affine = null;
            poly2 = null;
            rmse = double.NaN;
            if (_georefToolbar == null) return false;

            var pts = _georefToolbar.ControlPoints
                .Where(p => p.HasTarget)
                .Select(p => new GeoreferenceHelper.Gcp { Col = p.Col, Row = p.Row, MapX = p.TargetX, MapY = p.TargetY })
                .ToList();

            string type = _georefToolbar.TransformType;
            int min = GeoreferenceHelper.MinPoints(type);
            if (pts.Count < min)
            {
                _georefToolbar.SetStatus($"{type} 至少需要 {min} 个控制点，当前 {pts.Count}");
                return false;
            }

            if (type == "Similarity")
                affine = GeoreferenceHelper.SolveSimilarity(pts);
            else if (type == "Polynomial2")
            {
                poly2 = GeoreferenceHelper.SolvePolynomial2(pts);
                if (poly2 != null)
                    affine = GeoreferenceHelper.ApproximateAffineFromPoly2(poly2, _georefPixelWidth, _georefPixelHeight);
            }
            else
                affine = GeoreferenceHelper.SolveAffine(pts);

            if (affine == null && poly2 == null) return false;

            rmse = GeoreferenceHelper.ComputeRmse(pts, type, affine, poly2);
            for (int i = 0; i < _georefToolbar.ControlPoints.Count; i++)
            {
                var cp = _georefToolbar.ControlPoints[i];
                if (!cp.HasTarget) continue;
                var g = new GeoreferenceHelper.Gcp { Col = cp.Col, Row = cp.Row, MapX = cp.TargetX, MapY = cp.TargetY };
                cp.Residual = GeoreferenceHelper.PointResidual(g, type, affine, poly2);
            }
            _georefToolbar.RefreshPointIndices();
            return true;
        }

        private void GeorefUpdateResidualsAndPreview()
        {
            if (!GeorefTrySolve(out var affine, out var poly2, out double rmse))
            {
                _georefToolbar?.SetRmse(null);
                if (_georefToolbar?.AutoApply != true)
                    GeorefRedrawLinks();
                return;
            }
            _georefToolbar.SetRmse(double.IsNaN(rmse) ? (double?)null : rmse);
            _georefToolbar.SetStatus($"RMSE={rmse:F6}（{ _georefToolbar.TransformType }），点击「执行校正」应用配准");
            if (_georefToolbar?.AutoApply == true && affine != null)
                GeorefApplyPreviewToOverlay(affine);
            else
                GeorefRedrawLinks();
        }

        private void GeorefAutoApplyPreview()
        {
            // 自动应用开关被触发时，立即刷新预览
            if (_georefToolbar?.AutoApply == true)
            {
                if (GeorefTrySolve(out var affine, out _, out _) && affine != null)
                    GeorefApplyPreviewToOverlay(affine);
            }
        }

        private void GeorefApplyPreviewToOverlay(GeoreferenceHelper.AffineParams affine)
        {
            if (_overlayExtent == null || _georefPixelWidth <= 0 || _georefPixelHeight <= 0)
                return;
            try
            {
                _georefAffineParams = affine;

                double w = _georefPixelWidth;
                double h = _georefPixelHeight;
                double half = 0.5;

                GeoreferenceHelper.TransformAffine(affine, -half, -half, out double x1, out double y1);
                GeoreferenceHelper.TransformAffine(affine, w - half, -half, out double x2, out double y2);
                GeoreferenceHelper.TransformAffine(affine, -half, h - half, out double x3, out double y3);
                GeoreferenceHelper.TransformAffine(affine, w - half, h - half, out double x4, out double y4);

                double xmin = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
                double ymin = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
                double xmax = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
                double ymax = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));

                _overlayExtent = new Envelope(xmin, ymin, xmax, ymax, _overlayExtent.SpatialReference);

                if (_hasOverlay)
                {
                    UpdateImageOverlayPosition();
                    // 缩放到求解范围，使图片始终可见（确保后续点击的 ScreenToPixel 定位正确）
                    try
                    {
                        if (_overlayExtent != null && !_overlayExtent.IsEmpty &&
                            _overlayExtent.Width > 0 && _overlayExtent.Height > 0)
                        {
                            var pad = new Envelope(
                                _overlayExtent.XMin - _overlayExtent.Width * 0.2,
                                _overlayExtent.YMin - _overlayExtent.Height * 0.2,
                                _overlayExtent.XMax + _overlayExtent.Width * 0.2,
                                _overlayExtent.YMax + _overlayExtent.Height * 0.2,
                                _overlayExtent.SpatialReference);
                            _ = MapView2.SetViewpointGeometryAsync(pad, 40);
                        }
                    }
                    catch { }
                }

                // 更新控制点源坐标为变换后位置（像素 Col/Row 经仿射变换后的地图坐标）
                foreach (var cp in _georefToolbar.ControlPoints)
                {
                    if (!cp.HasTarget) continue;
                    GeoreferenceHelper.TransformAffine(affine, cp.Col, cp.Row, out double newX, out double newY);
                    cp.SourceX = newX;
                    cp.SourceY = newY;
                }
                _georefToolbar.RefreshPointIndices();
                GeorefRedrawLinks();
            }
            catch { }
        }

        private GeoreferenceHelper.AffineParams _georefAffineParams;

        private void GeorefApplyTranslation(GeoreferenceToolbarWindow.ControlPointItem cp)
        {
            if (!_hasOverlay || _overlayExtent == null || cp == null || !cp.HasTarget) return;
            try
            {
                var e = _overlayExtent;
                double cellW = (e.XMax - e.XMin) / _georefPixelWidth;
                double cellH = (e.YMax - e.YMin) / _georefPixelHeight;

                // 像素中心 (col, row) 映射到目标点
                double col = (cp.SourceX - e.XMin) / e.Width * _georefPixelWidth - 0.5;
                double row = (e.YMax - cp.SourceY) / e.Height * _georefPixelHeight - 0.5;
                double C = cp.TargetX - cellW * col;
                double F = cp.TargetY + cellH * row;

                _georefAffineParams = new GeoreferenceHelper.AffineParams
                {
                    A = cellW, B = 0, C = C,
                    D = 0, E = -cellH, F = F
                };

                // 新范围 = 四角像素角点经仿射变换后的范围
                double half = 0.5;
                GeoreferenceHelper.TransformAffine(_georefAffineParams, -half, -half, out double x1, out double y1);
                GeoreferenceHelper.TransformAffine(_georefAffineParams, _georefPixelWidth - half, -half, out double x2, out double y2);
                GeoreferenceHelper.TransformAffine(_georefAffineParams, -half, _georefPixelHeight - half, out double x3, out double y3);
                GeoreferenceHelper.TransformAffine(_georefAffineParams, _georefPixelWidth - half, _georefPixelHeight - half, out double x4, out double y4);
                double xmin = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
                double ymin = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
                double xmax = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
                double ymax = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));
                var newExtent = new Envelope(xmin, ymin, xmax, ymax, e.SpatialReference);

                _overlayExtent = newExtent;
                _georefOriginalExtent = new Envelope(newExtent.XMin, newExtent.YMin, newExtent.XMax, newExtent.YMax, newExtent.SpatialReference);
                UpdateImageOverlayPosition();

                cp.SourceX = cp.TargetX;
                cp.SourceY = cp.TargetY;

                try
                {
                    if (MapView2 != null && !newExtent.IsEmpty && newExtent.Width > 0 && newExtent.Height > 0)
                    {
                        var expanded = new Envelope(newExtent.XMin - newExtent.Width * 0.2,
                            newExtent.YMin - newExtent.Height * 0.2,
                            newExtent.XMax + newExtent.Width * 0.2,
                            newExtent.YMax + newExtent.Height * 0.2,
                            newExtent.SpatialReference);
                        _ = MapView2.SetViewpointGeometryAsync(expanded, 40);
                    }
                }
                catch { }

                _georefToolbar.RefreshPointIndices();
                GeorefRedrawLinks();
            }
            catch { }
        }

        private void GeorefDeleteSelectedPoint()
        {
            if (_georefToolbar?.SelectedPoint == null) return;
            _georefToolbar.ControlPoints.Remove(_georefToolbar.SelectedPoint);
            _georefToolbar.RefreshPointIndices();
            if (_georefToolbar.ControlPoints.Count == 0)
            {
                _georefAffineParams = null;
                if (_overlayOriginalExtent != null)
                {
                    _overlayExtent = new Envelope(_overlayOriginalExtent.XMin, _overlayOriginalExtent.YMin,
                                                  _overlayOriginalExtent.XMax, _overlayOriginalExtent.YMax,
                                                  _overlayOriginalExtent.SpatialReference);
                    UpdateImageOverlayPosition();
                }
                ClearGeorefOverlay();
            }
            else
            {
                GeorefRedrawLinks();
                GeorefUpdateResidualsAndPreview();
            }
        }

        private void GeorefClearPoints(bool updateUi = true)
        {
            _georefToolbar?.ControlPoints.Clear();
            _georefWaitingTarget = false;
            _georefPendingSource = null;
            _georefAffineParams = null;
            ClearGeorefOverlay();
            _georefToolbar?.SetRmse(null);
            if (updateUi)
            {
                if (_overlayOriginalExtent != null)
                {
                    _overlayExtent = new Envelope(_overlayOriginalExtent.XMin, _overlayOriginalExtent.YMin,
                                                  _overlayOriginalExtent.XMax, _overlayOriginalExtent.YMax,
                                                  _overlayOriginalExtent.SpatialReference);
                    UpdateImageOverlayPosition();
                }
                _georefToolbar?.SetStatus("已清除全部控制点，图像已复位");
            }
        }

        private async void GeorefZoomToSelected()
        {
            var cp = _georefToolbar?.SelectedPoint;
            if (cp == null || !cp.HasTarget) return;
            var sr = _georefMapSr ?? CurrentMapView.SpatialReference;
            var t = new MapPoint(cp.TargetX, cp.TargetY, sr);
            try
            {
                await CurrentMapView.SetViewpointCenterAsync(t, CurrentMapView.MapScale > 0 ? CurrentMapView.MapScale : 5000);
            }
            catch { }
        }

        private void GeorefPrevPoint()
        {
            if (_georefToolbar == null || _georefToolbar.ControlPoints.Count == 0) return;
            int idx = -1;
            if (_georefToolbar.SelectedPoint != null)
                idx = _georefToolbar.ControlPoints.IndexOf(_georefToolbar.SelectedPoint);
            idx = (idx <= 0) ? _georefToolbar.ControlPoints.Count - 1 : idx - 1;
            if (idx >= 0 && idx < _georefToolbar.ControlPoints.Count)
                _georefToolbar.PointGrid.SelectedItem = _georefToolbar.ControlPoints[idx];
        }

        private void GeorefNextPoint()
        {
            if (_georefToolbar == null || _georefToolbar.ControlPoints.Count == 0) return;
            int idx = -1;
            if (_georefToolbar.SelectedPoint != null)
                idx = _georefToolbar.ControlPoints.IndexOf(_georefToolbar.SelectedPoint);
            idx = (idx < 0 || idx >= _georefToolbar.ControlPoints.Count - 1) ? 0 : idx + 1;
            if (idx >= 0 && idx < _georefToolbar.ControlPoints.Count)
                _georefToolbar.PointGrid.SelectedItem = _georefToolbar.ControlPoints[idx];
        }

        private void GeorefUndoLastPoint()
        {
            if (_georefToolbar == null || _georefToolbar.ControlPoints.Count == 0) return;
            var last = _georefToolbar.ControlPoints.Last();
            _georefToolbar.ControlPoints.Remove(last);
            _georefToolbar.RefreshPointIndices();
            GeorefRedrawLinks();
            GeorefUpdateResidualsAndPreview();
            StatusBar.Text = $"已撤销控制点 #{last.Index}，剩余 {_georefToolbar.ControlPoints.Count} 个";
        }

        // ---- 右键控制点编辑 ----
        private GeoreferenceToolbarWindow.ControlPointItem FindNearestControlPoint(MapPoint mapPt, double toleranceMeters)
        {
            if (_georefToolbar == null || mapPt == null) return null;
            var sr = _georefMapSr ?? CurrentMapView.SpatialReference;
            GeoreferenceToolbarWindow.ControlPointItem best = null;
            double bestDist = toleranceMeters;
            foreach (var cp in _georefToolbar.ControlPoints)
            {
                if (!cp.HasTarget) continue;
                var sPt = new MapPoint(cp.SourceX, cp.SourceY, sr);
                double d = GeometryEngine.Distance(mapPt, sPt);
                if (d < bestDist) { bestDist = d; best = cp; }
                var tPt = new MapPoint(cp.TargetX, cp.TargetY, sr);
                d = GeometryEngine.Distance(mapPt, tPt);
                if (d < bestDist) { bestDist = d; best = cp; }
            }
            return best;
        }

        private void GeorefShowEditDialog(GeoreferenceToolbarWindow.ControlPointItem cp)
        {
            // 保存原始投影坐标值
            double projSx = cp.SourceX, projSy = cp.SourceY, projTx = cp.TargetX, projTy = cp.TargetY;
            bool useGeo = false;
            var sr = _georefMapSr ?? SpatialReferences.WebMercator;
            var geoSr = SpatialReferences.Wgs84;

            var dlg = new System.Windows.Window
            {
                Title = "编辑控制点坐标",
                Width = 400, Height = 320,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.NoResize
            };
            var grid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(12) };
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            for (int i = 0; i < 7; i++)
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            System.Windows.Controls.TextBox tbSx = null, tbSy = null, tbTx = null, tbTy = null;

            // 第 0 行：坐标格式切换
            var fmtPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 0, 0, 6) };
            System.Windows.Controls.Grid.SetRow(fmtPanel, 0); System.Windows.Controls.Grid.SetColumnSpan(fmtPanel, 2);
            fmtPanel.Children.Add(new System.Windows.Controls.Label { Content = "坐标格式:", Foreground = System.Windows.Media.Brushes.White, Margin = new System.Windows.Thickness(0, 0, 6, 0) });
            var cboFmt = new System.Windows.Controls.ComboBox { Width = 180, Height = 24 };
            cboFmt.Items.Add("投影坐标 (Meters)");
            cboFmt.Items.Add("地理坐标 (经纬度)");
            cboFmt.SelectedIndex = 0;
            fmtPanel.Children.Add(cboFmt);
            grid.Children.Add(fmtPanel);

            int r = 1;
            AddLabelAndTextBox(grid, r++, "源点 X:", projSx.ToString("F6"), out tbSx);
            AddLabelAndTextBox(grid, r++, "源点 Y:", projSy.ToString("F6"), out tbSy);
            AddLabelAndTextBox(grid, r++, "目标 X:", projTx.ToString("F6"), out tbTx);
            AddLabelAndTextBox(grid, r++, "目标 Y:", projTy.ToString("F6"), out tbTy);

            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new System.Windows.Thickness(0, 8, 0, 0) };
            System.Windows.Controls.Grid.SetRow(btnPanel, r); System.Windows.Controls.Grid.SetColumnSpan(btnPanel, 2);
            var okBtn = new System.Windows.Controls.Button { Content = "确定", Width = 70, Height = 26, Margin = new System.Windows.Thickness(0, 0, 8, 0) };
            var cancelBtn = new System.Windows.Controls.Button { Content = "取消", Width = 70, Height = 26 };
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            grid.Children.Add(btnPanel);
            dlg.Content = grid;

            Func<double, double, string> convX = null, convY = null;
            convX = (x, y) => useGeo
                ? (GeometryEngine.Project(new MapPoint(x, y, sr), geoSr) as MapPoint)?.X.ToString("F8") ?? x.ToString("F6")
                : x.ToString("F6");
            convY = (x, y) => useGeo
                ? (GeometryEngine.Project(new MapPoint(x, y, sr), geoSr) as MapPoint)?.Y.ToString("F8") ?? y.ToString("F6")
                : y.ToString("F6");

            // 格式切换事件
            cboFmt.SelectionChanged += (sf, ef) =>
            {
                useGeo = cboFmt.SelectedIndex == 1;
                try
                {
                    tbSx.Text = convX(projSx, projSy);
                    tbSy.Text = convY(projSx, projSy);
                    tbTx.Text = convX(projTx, projTy);
                    tbTy.Text = convY(projTx, projTy);
                }
                catch { }
            };

            cancelBtn.Click += (s2, e2) => dlg.Close();
            okBtn.Click += (s2, e2) =>
            {
                if (!double.TryParse(tbSx.Text, out double sx) ||
                    !double.TryParse(tbSy.Text, out double sy) ||
                    !double.TryParse(tbTx.Text, out double tx) ||
                    !double.TryParse(tbTy.Text, out double ty))
                {
                    System.Windows.MessageBox.Show("请输入有效数值", "格式错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                // 如果当前是地理坐标，转换回投影坐标
                if (useGeo)
                {
                    try
                    {
                        var srcP = GeometryEngine.Project(new MapPoint(sx, sy, geoSr), sr) as MapPoint;
                        var tgtP = GeometryEngine.Project(new MapPoint(tx, ty, geoSr), sr) as MapPoint;
                        if (srcP == null || tgtP == null || double.IsNaN(srcP.X) || double.IsNaN(tgtP.X))
                        {
                            System.Windows.MessageBox.Show("坐标转换失败，请检查经纬度值是否有效", "转换错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                            return;
                        }
                        sx = srcP.X; sy = srcP.Y;
                        tx = tgtP.X; ty = tgtP.Y;
                    }
                    catch (Exception)
                    {
                        System.Windows.MessageBox.Show("坐标转换失败，请检查经纬度值是否有效", "转换错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                }
                cp.SourceX = sx; cp.SourceY = sy;
                cp.TargetX = tx; cp.TargetY = ty;
                _georefToolbar.RefreshPointIndices();
                GeorefRedrawLinks();
                GeorefUpdateResidualsAndPreview();
                dlg.Close();
            };
            dlg.ShowDialog();
        }

        private static void AddLabelAndTextBox(System.Windows.Controls.Grid grid, int row, string label, string value, out System.Windows.Controls.TextBox tb)
        {
            var lbl = new System.Windows.Controls.Label { Content = label, Margin = new System.Windows.Thickness(0, 4, 8, 4) };
            System.Windows.Controls.Grid.SetRow(lbl, row); System.Windows.Controls.Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);
            tb = new System.Windows.Controls.TextBox { Text = value, Margin = new System.Windows.Thickness(0, 4, 0, 4), Height = 24 };
            System.Windows.Controls.Grid.SetRow(tb, row); System.Windows.Controls.Grid.SetColumn(tb, 1);
            grid.Children.Add(tb);
        }

        private void GeorefRemovePoint(GeoreferenceToolbarWindow.ControlPointItem cp)
        {
            if (_georefToolbar == null) return;
            _georefToolbar.ControlPoints.Remove(cp);
            _georefToolbar.RefreshPointIndices();
            GeorefRedrawLinks();
            GeorefUpdateResidualsAndPreview();
            StatusBar.Text = "已删除控制点";
        }

        private void GeorefImport()
        {
            if (_georefToolbar == null) return;
            var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "控制点文件 (*.pts;*.csv;*.txt)|*.pts;*.csv;*.txt|PTS 文件 (*.pts)|*.pts|CSV 文件 (*.csv)|*.csv|文本文件 (*.txt)|*.txt",
                Title = "导入控制点"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try
            {
                string content = File.ReadAllText(dlg.FileName);
                string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                List<GeoreferenceToolbarWindow.ControlPointItem> parsed = null;
                switch (ext)
                {
                    case ".pts":
                        parsed = GeoreferenceToolbarWindow.ParsePts(content);
                        break;
                    case ".csv":
                        parsed = GeoreferenceToolbarWindow.ParseCsv(content);
                        break;
                    case ".txt":
                        parsed = GeoreferenceToolbarWindow.ParseTxt(content);
                        break;
                }
                if (parsed == null || parsed.Count == 0)
                {
                    MessageBox.Show("未能解析到有效的控制点数据", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _georefToolbar.LoadControlPoints(parsed);
                GeorefRedrawLinks();
                GeorefUpdateResidualsAndPreview();
                _georefToolbar.SetStatus($"已导入 {parsed.Count} 个控制点");
                StatusBar.Text = $"空间校准：已导入 {parsed.Count} 个控制点";
            }
            catch (Exception ex)
            {
                MessageBox.Show("导入失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GeorefExport()
        {
            if (_georefToolbar == null || _georefToolbar.ControlPoints.Count == 0)
            {
                MessageBox.Show("没有可导出的控制点", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new System.Windows.Forms.SaveFileDialog
            {
                Filter = "PTS 文件 (*.pts)|*.pts|CSV 文件 (*.csv)|*.csv|文本文件 (*.txt)|*.txt",
                Title = "导出控制点",
                FileName = $"控制点_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try
            {
                string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                string content;
                switch (ext)
                {
                    case ".csv":
                        content = _georefToolbar.ExportToCsv();
                        break;
                    case ".txt":
                        content = _georefToolbar.ExportToTxt();
                        break;
                    default:
                        content = _georefToolbar.ExportToPts();
                        break;
                }
                File.WriteAllText(dlg.FileName, content);
                _georefToolbar.SetStatus($"已导出 {_georefToolbar.ControlPoints.Count} 个控制点到 {Path.GetFileName(dlg.FileName)}");
                StatusBar.Text = $"控制点已导出到 {dlg.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task GeorefExportDataAsync()
        {
            if (_georefToolbar == null || string.IsNullOrEmpty(_georefLayerName))
            {
                MessageBox.Show("没有已加载的栅格图层", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(_georefFilePath) || !File.Exists(_georefFilePath))
            {
                MessageBox.Show("找不到栅格文件路径，无法导出", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dlg = new System.Windows.Forms.SaveFileDialog
            {
                Filter = "GeoTIFF (*.tif)|*.tif|TIFF (*.tiff)|*.tiff|JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png|控制点文件 (*.pts)|*.pts|所有文件 (*.*)|*.*",
                Title = "导出校正数据",
                FileName = $"{System.IO.Path.GetFileNameWithoutExtension(_georefFilePath)}_georef.tif"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            // 提示用户选择输出坐标系
            if (!PromptSpatialReference(out int exportSrWkid, out string _))
                return;
            if (exportSrWkid <= 0) exportSrWkid = 3857;

            try
            {
                string exportPath = dlg.FileName;
                string ext = Path.GetExtension(exportPath).ToLowerInvariant();

                // 如果选择导出控制点文件
                if (ext == ".pts")
                {
                    string content = _georefToolbar.ExportToPts();
                    File.WriteAllText(exportPath, content);
                    _georefToolbar.SetStatus($"控制点已导出到 {Path.GetFileName(exportPath)}");
                    StatusBar.Text = $"控制点已导出: {exportPath}";
                    MessageBox.Show($"已导出 {_georefToolbar.ControlPoints.Count} 个控制点:\n{exportPath}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 导出栅格数据
                StatusBar.Text = "正在导出栅格...";
                _georefToolbar.SetStatus("正在导出栅格，请稍候...");

                // 用 System.Drawing 重编码为 24-bit RGB（避免源文件 CMYK/16-bit/多页等不兼容格式），
                // 并先计算仿射参数，再写成带内嵌空间参考与地理变换的 GeoTIFF。
                bool isTif = ext == ".tif" || ext == ".tiff";
                GeoreferenceHelper.AffineParams exportAffine = null;
                using (var srcImg = System.Drawing.Image.FromFile(_georefFilePath))
                {
                    using (var bmp = new System.Drawing.Bitmap(srcImg.Width, srcImg.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                    {
                        using (var g = System.Drawing.Graphics.FromImage(bmp))
                        {
                            g.DrawImage(srcImg, 0, 0, srcImg.Width, srcImg.Height);
                        }

                        if (GeorefTrySolve(out var affine, out _, out _) && affine != null)
                        {
                            exportAffine = affine;
                            // 如果用户选择的坐标系与参考数据坐标系不同，需要重投影仿射参数
                            var refSr = _georefMapSr;
                            if (refSr != null && refSr.Wkid > 0 && refSr.Wkid != exportSrWkid)
                            {
                                try
                                {
                                    var tgtSr = SpatialReference.Create(exportSrWkid);
                                    double pxW = bmp.Width;
                                    double pxH = bmp.Height;
                                    var srcPoints = new MapPoint[]
                                    {
                                        new MapPoint(affine.C, affine.F, refSr),
                                        new MapPoint(affine.A * pxW + affine.C, affine.D * pxW + affine.F, refSr),
                                        new MapPoint(affine.B * pxH + affine.C, affine.E * pxH + affine.F, refSr),
                                        new MapPoint(affine.A * pxW + affine.B * pxH + affine.C, affine.D * pxW + affine.E * pxH + affine.F, refSr)
                                    };
                                    var tgtGeoms = srcPoints.Select(p => GeometryEngine.Project(p, tgtSr)).ToArray();
                                    double[] cols = { 0, pxW, 0, pxW };
                                    double[] rows = { 0, 0, pxH, pxH };
                                    var gcps = new List<GeoreferenceHelper.Gcp>();
                                    for (int i = 0; i < 4; i++)
                                    {
                                        var mp = tgtGeoms[i] as MapPoint;
                                        if (mp != null)
                                            gcps.Add(new GeoreferenceHelper.Gcp { Col = cols[i], Row = rows[i], MapX = mp.X, MapY = mp.Y });
                                    }
                                    if (gcps.Count >= 3)
                                    {
                                        var newAffine = GeoreferenceHelper.SolveAffine(gcps);
                                        if (newAffine != null) exportAffine = newAffine;
                                    }
                                }
                                catch (Exception reprojEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[GeorefExport] reproject affine failed: {reprojEx.Message}");
                                }
                            }
                        }

                        // 写出 GeoTIFF：内嵌地理变换 + EPSG 空间参考
                        if (isTif && exportAffine != null && exportSrWkid > 0)
                        {
                            bool isGeoExport = false;
                            try
                            {
                                var u = SpatialReference.Create(exportSrWkid)?.Unit?.Name ?? "";
                                isGeoExport = u.IndexOf("degree", StringComparison.OrdinalIgnoreCase) >= 0;
                            }
                            catch { }
                            GeoTiffWriter.Save(bmp, exportPath, exportAffine, exportSrWkid, isGeoExport);
                        }
                        else
                        {
                            bmp.Save(exportPath, System.Drawing.Imaging.ImageFormat.Tiff);
                        }
                    }
                }

                // 写入坐标系（.prj + .aux.xml），先于世界文件，确保始终有 SR 信息
                string mapWkt = GeoreferenceHelper.GetEsriWkt(exportSrWkid);
                if (string.IsNullOrEmpty(mapWkt))
                {
                    try
                    {
                        var sr = SpatialReference.Create(exportSrWkid);
                        string unitName = sr?.Unit?.Name ?? "Meter";
                        bool isGeo = unitName.IndexOf("degree", StringComparison.OrdinalIgnoreCase) >= 0;
                        mapWkt = isGeo
                            ? $"GEOGCS[\"GCS_EPSG_{exportSrWkid}\",DATUM[\"D_EPSG_{exportSrWkid}\",SPHEROID[\"S_EPSG_{exportSrWkid}\",6378137.0,298.257223563]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]]"
                            : $"PROJCS[\"EPSG_{exportSrWkid}\",GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137.0,298.257223563]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",0.0],PARAMETER[\"Central_Meridian\",0.0],PARAMETER[\"Scale_Factor\",1.0],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]";
                    }
                    catch { }
                }
                if (!string.IsNullOrEmpty(mapWkt))
                {
                    File.WriteAllText(Path.ChangeExtension(exportPath, ".prj"), mapWkt);
                    GeoreferenceHelper.WriteAuxXmlFromWkt(exportPath, mapWkt);
                }

                // 如果控制点足够，写入世界文件（供其它软件识别，GeoTIFF 内已嵌变换）
                if (exportAffine != null)
                    GeoreferenceHelper.WriteWorldFile(exportPath, exportAffine);

                // 生成 _display.png+.pgw，便于 Runtime 100.x 重新加载（纯 TIFF+tfw 常无 FullExtent）
                if (exportAffine != null)
                {
                    try
                    {
                        string displayPng = Path.Combine(
                            Path.GetDirectoryName(exportPath) ?? "",
                            Path.GetFileNameWithoutExtension(exportPath) + "_display.png");
                        using (var img = System.Drawing.Image.FromFile(exportPath))
                        {
                            int maxDim = 4096;
                            var writeAffine = exportAffine;
                            if (img.Width > maxDim || img.Height > maxDim)
                            {
                                double scale = Math.Min((double)maxDim / img.Width, (double)maxDim / img.Height);
                                int nw = Math.Max(1, (int)(img.Width * scale));
                                int nh = Math.Max(1, (int)(img.Height * scale));
                                using (var thumb = new System.Drawing.Bitmap(img, nw, nh))
                                    thumb.Save(displayPng, System.Drawing.Imaging.ImageFormat.Png);
                                double sx = (double)img.Width / nw;
                                double sy = (double)img.Height / nh;
                                writeAffine = new GeoreferenceHelper.AffineParams
                                {
                                    A = exportAffine.A * sx,
                                    D = exportAffine.D * sx,
                                    B = exportAffine.B * sy,
                                    E = exportAffine.E * sy,
                                    C = exportAffine.C,
                                    F = exportAffine.F
                                };
                            }
                            else
                            {
                                img.Save(displayPng, System.Drawing.Imaging.ImageFormat.Png);
                            }
                            GeoreferenceHelper.WriteWorldFile(displayPng, writeAffine);
                            int wkid = exportSrWkid > 0 ? exportSrWkid : 3857;
                            GeoreferenceHelper.WriteAuxXml(displayPng, wkid);
                            GeoreferenceHelper.WritePrjFile(displayPng, wkid);
                        }
                    }
                    catch (Exception dex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GeorefExport] display png: {dex.Message}");
                    }
                }

                // 同时导出控制点文件（同名 .pts）
                string ptsPath = Path.ChangeExtension(exportPath, ".pts");
                File.WriteAllText(ptsPath, _georefToolbar.ExportToPts());

                _georefToolbar.SetStatus($"已导出到 {Path.GetFileName(exportPath)}");
                StatusBar.Text = $"导出成功: {exportPath}";

                // 自动加载导出的文件（RasterLayer 用于图层管理器 + WPF 覆盖层用于渲染）
                try
                {
                    string layerName = Path.GetFileNameWithoutExtension(exportPath);
                    var wfAffine = GeoreferenceHelper.ReadWorldFile(exportPath);
                    if (wfAffine != null)
                    {
                        GetRasterPixelSize(exportPath, out double pxW, out double pxH);
                        var mapSr = SpatialReference.Create(exportSrWkid);
                        var imgExtent = ComputeWorldFileEnvelope(wfAffine, pxW, pxH, mapSr);
                        if (imgExtent != null && !imgExtent.IsEmpty)
                        {
                            // 设置 WPF 覆盖层（保证渲染）
                            try { SetImageOverlay(exportPath, imgExtent, layerName); System.Diagnostics.Debug.WriteLine("[GeorefExport] SetImageOverlay OK"); } catch (Exception ovEx) { System.Diagnostics.Debug.WriteLine($"[GeorefExport] SetImageOverlay failed: {ovEx.Message}"); }

                            // 优先直接加载导出的 GeoTIFF（内嵌空间参考），失败再试 _display.png
                            string pngPath = Path.Combine(
                                Path.GetDirectoryName(exportPath) ?? "",
                                Path.GetFileNameWithoutExtension(exportPath) + "_display.png");
                            string[] candidates = File.Exists(pngPath)
                                ? new[] { exportPath, pngPath }
                                : new[] { exportPath };
                            foreach (string candidate in candidates)
                            {
                                try
                                {
                                    var raster = new Raster(candidate);
                                    await raster.LoadAsync();
                                    if (raster.LoadStatus == LoadStatus.Loaded)
                                    {
                                        var rl = new RasterLayer(raster) { Name = layerName };
                                        await rl.LoadAsync();
                                        var fe = rl.FullExtent;
                                        bool pixelLike = (fe == null || fe.IsEmpty || IsPixelLikeExtent(fe, pxW, pxH));
                                        System.Diagnostics.Debug.WriteLine($"[GeorefExport] RasterLayer({candidate}) FullExtent: {(fe != null ? fe.XMin + "," + fe.YMin + " WKID=" + fe.SpatialReference?.Wkid : "null")} pixelLike={pixelLike}");
                                        if (!pixelLike)
                                        {
                                            CurrentMapView.Map.OperationalLayers.Add(rl);
                                            LayerManager.AddLayer(rl, false, exportPath);
                                            await CurrentMapView.SetViewpointGeometryAsync(fe, 50);
                                            RefreshGeorefLayerList();
                                            StatusBar.Text = $"导出并加载成功: {Path.GetFileName(exportPath)}";
                                            break;
                                        }
                                    }
                                }
                                catch { }
                            }

                            // 如果 RasterLayer 未添加到地图（任何原因），至少用 WPF 覆盖层
                            // 同时添加占位 GroupLayer 以显示在图层管理器中
                            if (!CurrentMapView.Map.OperationalLayers.Any(l => l.Name == layerName && l is RasterLayer))
                            {
                                var placeholder = new GroupLayer { Name = layerName };
                                CurrentMapView.Map.OperationalLayers.Add(placeholder);
                                LayerManager.AddLayer(placeholder, false, exportPath);
                                await CurrentMapView.SetViewpointGeometryAsync(imgExtent, 50);
                                RefreshGeorefLayerList();
                                StatusBar.Text = $"导出并加载成功: {Path.GetFileName(exportPath)} (覆盖层模式)";
                            }
                        }
                    }
                }
                catch (Exception loadEx)
                {
                    string err = loadEx.InnerException != null ? loadEx.InnerException.Message : loadEx.Message;
                    System.Diagnostics.Debug.WriteLine($"[GeorefExport] Auto-load failed: {err}");
                    StatusBar.Text = "自动加载失败: " + err;
                }

                string wfName = Path.GetFileName(GeoreferenceHelper.GetWorldFilePath(exportPath));
                string ptsName = Path.GetFileName(ptsPath);
                MessageBox.Show($"校正数据已导出并加载到地图:\n{exportPath}\n伴随文件：{wfName}, {ptsName}, *_display.png\n坐标系: EPSG:{exportSrWkid}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _georefToolbar.SetStatus("导出失败: " + ex.Message);
                StatusBar.Text = "导出失败";
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task GeorefBrowseRasterAsync()
        {
            if (CurrentMapView == MapView1)
            {
                MessageBox.Show("地图视图不支持加载无坐标图片，请切换到数据视图。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "栅格文件 (*.tif;*.tiff;*.jpg;*.jpeg;*.png;*.bmp)|*.tif;*.tiff;*.jpg;*.jpeg;*.png;*.bmp|所有文件 (*.*)|*.*",
                Title = "选择待校准栅格文件"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            string filePath = dlg.FileName;
            string layerName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            try
            {
                StatusBar.Text = "正在加载栅格文件...";

                // 先检查是否有世界文件，没有则创建临时世界文件
                _georefWorldBackup = GeoreferenceHelper.BackupWorldFile(filePath) ?? "";
                string wfPath = GeoreferenceHelper.GetWorldFilePath(filePath);
                bool needsTempWorld = !File.Exists(wfPath);

                if (needsTempWorld)
                {
                    double pixelW = 1000, pixelH = 1000;
                    try
                    {
                        using (var img = System.Drawing.Image.FromFile(filePath))
                        {
                            pixelW = Math.Max(1, img.Width);
                            pixelH = Math.Max(1, img.Height);
                        }
                    }
                    catch { }

                    double canvasW = UnreferencedCanvasExtent.Width;
                    double canvasH = UnreferencedCanvasExtent.Height;
                    double cellSize = Math.Min(canvasW / pixelW, canvasH / pixelH) * 0.8;
                    cellSize = Math.Max(1, cellSize);

                    var mapSr = CurrentMapView.SpatialReference ?? SpatialReferences.WebMercator;
                    var center = UnreferencedCanvasCenter;

                    var affine = new GeoreferenceHelper.AffineParams
                    {
                        A = cellSize,
                        D = 0,
                        B = 0,
                        E = -cellSize,
                        C = center.X - pixelW * cellSize / 2,
                        F = center.Y + pixelH * cellSize / 2
                    };
                    GeoreferenceHelper.WriteWorldFile(filePath, affine);
                    GeoreferenceHelper.WriteAuxXml(filePath, 3857);

                    // 加载栅格（AddRasterLayer 内部使用 WPF Image 覆盖层显示无参考影像）
                    await AddRasterLayer(filePath, layerName, skipSync: true);
                    // AddRasterOverlayAsync 内部已执行 SetViewpointGeometryAsync，无需重复缩放到栅格
                }
                else
                {
                    // 已有世界文件，直接加载（同步到地图视图）
                    await AddRasterLayer(filePath, layerName);
                }

                StatusBar.Text = $"已加载 {layerName}，请使用空间校准功能添加控制点";
                _georefToolbar?.SetStatus($"已加载 {layerName}，请添加控制点进行配准");
                // 选中新加载的栅格图层（SetRasterLayers 会自动触发图层切换事件）
                RefreshGeorefLayerList();
                _georefToolbar?.SetRasterLayers(_georefToolbar.RasterLayerNames, layerName);
                // 自动进入添加控制点模式
                _georefToolbar?.SetAddPointMode(true);
                _isGeorefAddingPoint = true;
                _isGeorefMode = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载栅格失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task GeorefApplyAsync()
        {
            if (!GeorefTrySolve(out var affine, out var poly2, out double rmse))
            {
                MessageBox.Show("控制点不足或解算失败", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (affine == null)
            {
                MessageBox.Show("无法计算变换参数", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (string.IsNullOrEmpty(_georefFilePath) || !File.Exists(_georefFilePath))
            {
                MessageBox.Show("找不到栅格文件路径，无法写出世界文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!PromptSpatialReference(out int outWkid, out string outWkt))
                return;

            try
            {
                bool overwrite = _georefToolbar.OverwriteOriginal;
                string targetPath = _georefFilePath;
                string layerName = _georefLayerName;

                if (!overwrite)
                {
                    string dir = Path.GetDirectoryName(_georefFilePath) ?? "";
                    string name = Path.GetFileNameWithoutExtension(_georefFilePath);
                    string ext = Path.GetExtension(_georefFilePath);
                    targetPath = Path.Combine(dir, name + "_georef" + ext);
                    File.Copy(_georefFilePath, targetPath, true);
                    // 复制 sidecar
                    foreach (var side in new[] { ".tfw", ".jgw", ".pgw", ".bpw", ".wld", ".prj", ".aux.xml" })
                    {
                        string s = Path.Combine(dir, name + side);
                        if (File.Exists(s))
                            File.Copy(s, Path.Combine(dir, name + "_georef" + side), true);
                    }
                    layerName = name + "_georef";
                }

                if (string.IsNullOrEmpty(_georefWorldBackup) && overwrite)
                    _georefWorldBackup = GeoreferenceHelper.BackupWorldFile(_georefFilePath) ?? "";

                _georefAffineParams = affine;
                GeoreferenceHelper.WriteWorldFile(targetPath, affine);
                // 世界文件基于地图 CRS 写出，使用地图 CRS 写 sidecar
                var mapSrForApply = _georefMapSr ?? CurrentMapView?.SpatialReference ?? SpatialReferences.WebMercator;
                string mapWktApply = GeoreferenceHelper.GetEsriWkt(mapSrForApply.Wkid);
                if (!string.IsNullOrEmpty(mapWktApply))
                {
                    File.WriteAllText(Path.ChangeExtension(targetPath, ".prj"), mapWktApply);
                    GeoreferenceHelper.WriteAuxXmlFromWkt(targetPath, mapWktApply);
                }

                if (overwrite)
                {
                    await ReloadRasterLayerAsync(_georefLayerName, _georefFilePath);
                }
                else
                {
                    await AddRasterLayer(targetPath, layerName);
                }

                // 更新内部 extent 为新位置
                var li = LayerManager.Layers.FirstOrDefault(l => l.Name == (overwrite ? _georefLayerName : layerName));
                if (li?.Layer != null)
                {
                    if (li.Layer is RasterLayer rl && rl.FullExtent != null)
                    {
                        _georefOriginalExtent = new Envelope(rl.FullExtent.XMin, rl.FullExtent.YMin, rl.FullExtent.XMax, rl.FullExtent.YMax, rl.FullExtent.SpatialReference);
                    }
                    else if (_hasOverlay && li.Name == _overlayLayerName && _overlayExtent != null)
                    {
                        _georefOriginalExtent = new Envelope(_overlayExtent.XMin, _overlayExtent.YMin,
                                                             _overlayExtent.XMax, _overlayExtent.YMax,
                                                             _overlayExtent.SpatialReference);
                    }
                    _georefLayerName = li.Name;
                    _georefFilePath = li.FilePath;
                    // 控制点源坐标更新为校正后位置（目标点）
                    foreach (var cp in _georefToolbar.ControlPoints)
                    {
                        if (!cp.HasTarget) continue;
                        cp.SourceX = cp.TargetX;
                        cp.SourceY = cp.TargetY;
                    }
                    _georefToolbar.RefreshPointIndices();
                    GeorefRedrawLinks();
                }

                _georefToolbar.SetRmse(rmse);
                _georefToolbar.SetStatus($"校正完成 RMSE={rmse:F6}");
                StatusBar.Text = $"空间校准完成，RMSE={rmse:F6}";
                MessageBox.Show($"校正完成！\nRMSE = {rmse:F6}\n输出: {(overwrite ? "已覆盖原图层" : "已生成新图层 " + layerName)}",
                    "空间校准", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("执行校正失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task GeorefResetAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(_georefFilePath))
                {
                    GeoreferenceHelper.RestoreWorldFile(_georefFilePath, _georefWorldBackup);
                    if (!string.IsNullOrEmpty(_georefLayerName))
                        await ReloadRasterLayerAsync(_georefLayerName, _georefFilePath);
                }
                GeorefClearPoints();
                _georefWorldBackup = null;
                await GeorefOnLayerChangedAsync();
                _georefToolbar?.SetStatus("已重置为配准前状态");
                StatusBar.Text = "空间校准已重置";
            }
            catch (Exception ex)
            {
                MessageBox.Show("重置失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task ReloadRasterLayerAsync(string layerName, string filePath)
        {
            // 如果当前是无参考影像覆盖层，通过 AddRasterLayer 重新加载
            if (_hasOverlay && layerName == _overlayLayerName)
            {
                ClearImageOverlay();
                // 移除旧的占位图层
                var oldLi = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
                if (oldLi?.Layer != null)
                {
                    CurrentMapView.Map.OperationalLayers.Remove(oldLi.Layer);
                    LayerManager.RemoveLayer(layerName);
                }
                await AddRasterLayer(filePath, layerName);
                return;
            }

            var li = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
            int index = -1;
            if (li?.Layer != null)
            {
                index = CurrentMapView.Map.OperationalLayers.IndexOf(li.Layer);
                CurrentMapView.Map.OperationalLayers.Remove(li.Layer);
            }
            else
            {
                var old = CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == layerName);
                if (old != null)
                {
                    index = CurrentMapView.Map.OperationalLayers.IndexOf(old);
                    CurrentMapView.Map.OperationalLayers.Remove(old);
                }
            }

            RasterLayer rasterLayer = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var raster = new Raster(filePath);
                await raster.LoadAsync();
                rasterLayer = new RasterLayer(raster) { Name = layerName };
                await rasterLayer.LoadAsync();
                if (rasterLayer.FullExtent != null && !rasterLayer.FullExtent.IsEmpty)
                    break;
                if (attempt == 0)
                    await System.Threading.Tasks.Task.Delay(200);
            }

            if (index >= 0 && index <= CurrentMapView.Map.OperationalLayers.Count)
            {
                CurrentMapView.Map.OperationalLayers.Insert(index, rasterLayer);
                SyncMapToOtherView(CurrentMapView);
            }
            else
            {
                CurrentMapView.Map.OperationalLayers.Add(rasterLayer);
                SyncMapToOtherView(CurrentMapView);
            }

            if (li != null)
            {
                li.Layer = rasterLayer;
                li.FilePath = filePath;
            }
            else
            {
                LayerManager.AddLayer(rasterLayer, false, filePath);
            }

            if (rasterLayer.FullExtent != null && !rasterLayer.FullExtent.IsEmpty)
                await CurrentMapView.SetViewpointGeometryAsync(rasterLayer.FullExtent, 20);
        }

        private void UpdateDrawingToolbar()
        {
            if (_drawingToolbar != null && _drawingToolbar.IsVisible)
            {
                int vertexCount = currentDrawTool == "Line" ? _lineVertices.Count :
                                  currentDrawTool == "Polygon" ? _polygonVertices.Count : 0;
                _drawingToolbar.UpdateGeometryType(currentDrawTool);
                _drawingToolbar.UpdateVertexCount(vertexCount);
                _drawingToolbar.BtnUndo.IsEnabled = vertexCount > 0;
            }
        }

        private void MapTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 取消所有进行中的操作
            CancelMoveFeature();
            _isDrawing = false;
            _streamDrawActive = false;
            _streamPoints.Clear();
            _nearestSnapPoint = null;
            if (_linePreviewOverlay != null) _linePreviewOverlay.Graphics.Clear();
            if (_polygonPreviewOverlay != null) _polygonPreviewOverlay.Graphics.Clear();
            if (_drawingOverlay != null) _drawingOverlay.Graphics.Clear();
            if (_editFeatureToolbar != null && _editFeatureToolbar.IsVisible)
            {
                _editFeatureToolbar.Hide();
            }
            CleanupVertexEditing();
            ClearSelection();
            GeorefCanvas?.Children.Clear();
            // 将共享叠加层移至当前激活视图
            var newView = CurrentMapView;
            foreach (var ov in _sharedOverlays)
            {
                if (ov == null) continue;
                if (MapView1.GraphicsOverlays.Contains(ov))
                    MapView1.GraphicsOverlays.Remove(ov);
                if (MapView2.GraphicsOverlays.Contains(ov))
                    MapView2.GraphicsOverlays.Remove(ov);
                newView.GraphicsOverlays.Add(ov);
            }
            // 更新坐标显示
            var txt = newView == MapView1 ? TxtCoordinate : TxtCoordinate2;
            txt.Text = "WGS84:  0.000000°, 0.000000°";
            // 更新工具栏状态
            if (_georefToolbar != null)
                _georefToolbar.SetGeorefModeEnabled(newView == MapView2);
            _isGeorefMode = newView == MapView2;
            // 切换视图时刷新覆盖层位置
            if (_hasOverlay)
                UpdateImageOverlayPosition();
            if (!_isGeorefMode)
            {
                _isGeorefAddingPoint = false;
                _georefWaitingTarget = false;
            }
        }

        private void SubscribeToMapEvents()
        {
            MapView1.MouseLeftButtonDown += MyMapView_OnLeftClick;
            MapView2.MouseLeftButtonDown += MyMapView_OnLeftClick;
            MapView2.GeoViewTapped += MyMapView_OnGeorefTapped;
            MapView1.MouseRightButtonDown += MyMapView_OnRightClick;
            MapView2.MouseRightButtonDown += MyMapView_OnRightClick;
            MapView1.MouseDoubleClick += MyMapView_MouseDoubleClick;
            MapView2.MouseDoubleClick += MyMapView_MouseDoubleClick;
            MapView1.MouseMove += MyMapView_OnMouseMove;
            MapView2.MouseMove += MyMapView_OnMouseMove;
            // 在外层 Grid 也监听移动，避免 MapView 吞掉 MouseMove 导致十字丝不更新
            if (MapView1.Parent is System.Windows.FrameworkElement host1)
                host1.PreviewMouseMove += MyMapViewHost_PreviewMouseMove;
            if (MapView2.Parent is System.Windows.FrameworkElement host2)
                host2.PreviewMouseMove += MyMapViewHost_PreviewMouseMove;
            MapView1.MouseLeftButtonUp += MyMapView_OnLeftButtonUp;
            MapView2.MouseLeftButtonUp += MyMapView_OnLeftButtonUp;
            MapView1.KeyDown += MyMapView_KeyDown;
            MapView2.KeyDown += MyMapView_KeyDown;
            MapView1.ViewpointChanged += MyMapView_ViewpointChanged;
            MapView2.ViewpointChanged += MyMapView_ViewpointChanged;
        }

        private void MyMapViewHost_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (currentDrawTool != "Point" || _pendingPoint != null) return;
            var mv = CurrentMapView;
            if (mv == null) return;
            // 仅当鼠标在当前 MapView 上时更新
            var screenPoint = e.GetPosition(mv);
            if (screenPoint.X < 0 || screenPoint.Y < 0 || screenPoint.X > mv.ActualWidth || screenPoint.Y > mv.ActualHeight)
                return;
            var mapPoint = mv.ScreenToLocation(screenPoint);
            if (mapPoint == null || double.IsNaN(mapPoint.X) || double.IsNaN(mapPoint.Y)) return;
            _trackingPoint = _nearestSnapPoint ?? mapPoint;
            UpdateCrosshair();
            if (_drawingToolbar != null && _drawingToolbar.IsVisible)
                _drawingToolbar.UpdateCoordinate(mapPoint.X, mapPoint.Y);
        }

        private void MyMapView_OnLeftClick(object sender, MouseButtonEventArgs e)
        {
            // 空间校准使用 GeoViewTapped 处理点击，避免干扰地图平移
            if (_isGeorefMode && _isGeorefAddingPoint)
                return;
            if (sender != CurrentMapView) return;
            if (e.MiddleButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
                return;

            var screenPoint = e.GetPosition(CurrentMapView);
            var mapPoint = CurrentMapView.ScreenToLocation(screenPoint);
            if (mapPoint == null)
                return;

            var modifiers = Keyboard.Modifiers;

            if (modifiers == ModifierKeys.Control)
            {
                if (_isVertexEditing)
                {
                    TryStartVertexDragAsync(screenPoint, mapPoint);
                }
                else
                {
                    StartEditFeatureAtPointAsync(screenPoint);
                }
                e.Handled = true;
                return;
            }

            if (modifiers == ModifierKeys.Shift)
            {
                DeleteFeatureAtPointAsync(screenPoint);
                e.Handled = true;
                return;
            }

            if (modifiers == ModifierKeys.Alt)
            {
                if (_movePendingFeature == null)
                {
                    StartMoveFeatureAtPointAsync(screenPoint);
                }
                else
                {
                    CompleteMoveFeatureAsync(mapPoint);
                }
                e.Handled = true;
                return;
            }

            if (currentDrawTool == "Point")
            {
                // 点模式两步交互：左键不生成要素，仅十字丝跟随鼠标；右键菜单保存/删除
                e.Handled = true;
            }
            else if (_isFreehandMode && (currentDrawTool == "Line" || currentDrawTool == "Polygon"))
            {
                // 开始流式绘制
                _isStreamDrawing = true;
                _lastStreamScreenPoint = screenPoint;
                CurrentMapView.CaptureMouse();
                var usePoint = _nearestSnapPoint ?? mapPoint;
                if (_nearestSnapPoint != null) ClearSnapIndicator();
                if (currentDrawTool == "Line")
                {
                    if (_lineVertices.Count == 0)
                        AddLineVertex(usePoint);
                }
                else
                {
                    if (_polygonVertices.Count == 0)
                        AddPolygonVertex(usePoint);
                }
                StatusBar.Text = "手绘中：按住拖拽绘制，松开完成";
                e.Handled = true;
                return;
            }
            else if (currentDrawTool == "Line")
            {
                var usePoint = _nearestSnapPoint ?? mapPoint;
                if (_nearestSnapPoint != null) ClearSnapIndicator();
                AddLineVertex(usePoint);
                UpdateDrawingToolbar();
                e.Handled = true;
            }
            else if (currentDrawTool == "Polygon")
            {
                var usePoint = _nearestSnapPoint ?? mapPoint;
                if (_nearestSnapPoint != null) ClearSnapIndicator();
                AddPolygonVertex(usePoint);
                UpdateDrawingToolbar();
                e.Handled = true;
            }
            else if (currentDrawTool == "Select")
            {
                SelectFeatureAtPoint(mapPoint, Keyboard.Modifiers);
                e.Handled = true;
            }
            else if (currentDrawTool == "EditFeature")
            {
                if (_isAddingVertexMode && _isVertexEditing)
                {
                    var usePoint = _nearestSnapPoint ?? mapPoint;
                    if (_nearestSnapPoint != null) ClearSnapIndicator();
                    InsertVertexAtMapPoint(usePoint);
                    _isAddingVertexMode = false;
                    e.Handled = true;
                    return;
                }

                if (_isVertexEditing)
                {
                    TryStartVertexDragAsync(screenPoint, mapPoint);
                }
                else
                {
                    StartEditFeatureAtPointAsync(screenPoint);
                }
                e.Handled = true;
            }
            else if (currentDrawTool == "DeleteFeature")
            {
                DeleteFeatureAtPointAsync(screenPoint);
                e.Handled = true;
            }
            else if (currentDrawTool == "MoveFeature")
            {
                if (_movePendingFeature == null)
                {
                    StartMoveFeatureAtPointAsync(screenPoint);
                }
                else
                {
                    CompleteMoveFeatureAsync(mapPoint);
                }
                e.Handled = true;
            }
        }

        private void MyMapView_OnGeorefTapped(object sender, GeoViewInputEventArgs e)
        {
            if (!_isGeorefMode || !_isGeorefAddingPoint) return;
            // 只在数据视图（待校准图）上点击源点
            var mv = sender as MapView;
            if (mv == null) return;
            GeorefHandleMapClick(e.Location, mv);
            e.Handled = true;
        }

        private async void MyMapView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender != CurrentMapView) return;
            if (_isVertexEditing)
                return;

            var screenPoint = e.GetPosition(CurrentMapView);
            var mapPoint = CurrentMapView.ScreenToLocation(screenPoint);
            if (mapPoint == null)
                return;

            if (currentDrawTool == "Select")
            {
                var tolerance = 10;
                var identifyResult = await CurrentMapView.IdentifyLayersAsync(screenPoint, tolerance, false);
                if (identifyResult.Count > 0 && identifyResult[0].GeoElements.Count > 0)
                {
                    var geoElement = identifyResult[0].GeoElements[0];
                    if (geoElement is Feature feature)
                    {
                        StartVertexEditing(feature);
                        e.Handled = true;
                    }
                }
            }
        }

        private async void MyMapView_OnRightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender != CurrentMapView) return;
            e.Handled = true;

            // 空间校准：右键菜单（编辑坐标 / 撤销）
            if (_isGeorefMode && _georefToolbar != null && _georefToolbar.ControlPoints.Count > 0)
            {
                var screenPt = e.GetPosition(CurrentMapView);
                var mapPt = CurrentMapView.ScreenToLocation(screenPt);
                if (mapPt != null)
                {
                    var hitCp = FindNearestControlPoint(mapPt, 20);
                    if (hitCp != null)
                    {
                        var menu = new System.Windows.Controls.ContextMenu();
                        var editItem = new System.Windows.Controls.MenuItem { Header = "编辑坐标" };
                        editItem.Click += (s2, e2) => GeorefShowEditDialog(hitCp);
                        menu.Items.Add(editItem);
                        var undoItem = new System.Windows.Controls.MenuItem { Header = "撤销此控制点" };
                        undoItem.Click += (s2, e2) => GeorefRemovePoint(hitCp);
                        menu.Items.Add(undoItem);
                        menu.IsOpen = true;
                        return;
                    }
                }

                if (_georefWaitingTarget)
                {
                    var menu = new System.Windows.Controls.ContextMenu();
                    var inputItem = new System.Windows.Controls.MenuItem { Header = "输入坐标作为目标点" };
                    inputItem.Click += (s2, e2) => GeorefInputTarget();
                    menu.Items.Add(inputItem);
                    var cancelItem = new System.Windows.Controls.MenuItem { Header = "取消" };
                    cancelItem.Click += (s2, e2) =>
                    {
                        _georefWaitingTarget = false;
                        _georefPendingSource = null;
                        GeorefRedrawLinks();
                        StatusBar.Text = "已取消当前源点";
                    };
                    menu.Items.Add(cancelItem);
                    menu.IsOpen = true;
                }
                else
                {
                    var menu = new System.Windows.Controls.ContextMenu();
                    var inputItem = new System.Windows.Controls.MenuItem { Header = "输入坐标" };
                    inputItem.Click += (s2, e2) => GeorefInputSource();
                    menu.Items.Add(inputItem);
                    var undoItem = new System.Windows.Controls.MenuItem { Header = "撤销上一个控制点" };
                    undoItem.Click += (s2, e2) => GeorefUndoLastPoint();
                    menu.Items.Add(undoItem);
                    menu.IsOpen = true;
                }
                return;
            }

            if (_isAddingVertexMode)
            {
                _isAddingVertexMode = false;
                StatusBar.Text = "已取消添加顶点";
                return;
            }

            if (_isVertexEditing)
            {
                await SaveVertexEditAndExit();
                return;
            }

            if (currentDrawTool == "Line" || currentDrawTool == "Polygon" || currentDrawTool == "Point")
            {
                ShowDrawContextMenu(e);
            }
            else if (_isLayerEditMode)
            {
                MapContextMenu.IsOpen = true;
            }
        }

        private void ShowDrawContextMenu(MouseButtonEventArgs e)
        {
            ContextMenu contextMenu = new ContextMenu();

            // 点模式：右键时冻结当前十字丝位置，菜单三选项（保存 / 撤回到上一个顶点 / 删除）
            if (currentDrawTool == "Point")
            {
                var screenPt = e.GetPosition(CurrentMapView);
                var mapPt = CurrentMapView.ScreenToLocation(screenPt);
                var usePoint = _nearestSnapPoint ?? mapPt ?? _trackingPoint;
                if (usePoint != null)
                {
                    _pendingPoint = usePoint;
                    _trackingPoint = usePoint;
                    UpdateCrosshair();
                }

                var saveItem = new MenuItem { Header = "保存" };
                saveItem.IsEnabled = _pendingPoint != null;
                saveItem.Click += (sender, args) =>
                {
                    CompleteCurrentDrawing();
                    UpdateDrawingToolbar();
                };
                contextMenu.Items.Add(saveItem);

                var undoItem = new MenuItem { Header = "撤回到上一个顶点", IsEnabled = false };
                contextMenu.Items.Add(undoItem);

                var deleteItem = new MenuItem { Header = "删除" };
                deleteItem.Click += (sender, args) =>
                {
                    CancelCurrentDrawing();
                    UpdateDrawingToolbar();
                };
                contextMenu.Items.Add(deleteItem);

                // 点击菜单外关闭：取消冻结，十字丝继续跟随鼠标
                contextMenu.Closed += (sender, args) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (currentDrawTool == "Point" && _pendingPoint != null)
                            _pendingPoint = null;
                    }), System.Windows.Threading.DispatcherPriority.Background);
                };
            }
            else
            {
                var saveItem = new MenuItem { Header = "保存结果" };
                saveItem.Click += (sender, args) => CompleteCurrentDrawing();
                contextMenu.Items.Add(saveItem);

                var undoVertexItem = new MenuItem { Header = "撤回到上一个顶点" };
                undoVertexItem.Click += (sender, args) => UndoLastVertex();
                bool hasVertices = (currentDrawTool == "Line" && _lineVertices.Count > 0) ||
                                   (currentDrawTool == "Polygon" && _polygonVertices.Count > 0);
                undoVertexItem.IsEnabled = hasVertices;
                contextMenu.Items.Add(undoVertexItem);

                var deleteItem = new MenuItem { Header = "删除整个要素" };
                deleteItem.Click += (sender, args) => CancelCurrentDrawing();
                contextMenu.Items.Add(deleteItem);
            }

            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            contextMenu.PlacementTarget = CurrentMapView;
            contextMenu.IsOpen = true;
        }

        private void CompleteCurrentDrawing()
        {
            switch (currentDrawTool)
            {
                case "Point":
                {
                    // 优先用右键冻结点，否则用当前跟踪十字丝位置
                    var pt = _pendingPoint ?? _trackingPoint;
                    if (pt != null)
                    {
                        DrawPoint(pt);
                        _pendingPoint = null;
                        // 保存后十字丝继续跟随，便于连续加点；不跳转地图
                        StatusBar.Text = "点已保存，移动鼠标定位下一个点，右键保存或删除";
                    }
                    else
                    {
                        StatusBar.Text = "无有效位置，请移动鼠标后再右键保存";
                    }
                    break;
                }
                case "Line":
                    CompleteLine();
                    break;
                case "Polygon":
                    CompletePolygon();
                    break;
            }
        }

        private void UndoLastVertex()
        {
            switch (currentDrawTool)
            {
                case "Line":
                    if (_lineVertices.Count > 0)
                    {
                        _lineVertices.RemoveAt(_lineVertices.Count - 1);
                        UpdatePreviewVertices();
                        if (_lineVertices.Count > 0)
                        {
                            var lastPoint = _lineVertices[_lineVertices.Count - 1];
                            UpdateRubberBand(lastPoint);
                        }
                        else
                        {
                            ClearLinePreview();
                        }
                        StatusBar.Text = $"已撤销上一个顶点，当前 {_lineVertices.Count} 个折点";
                    }
                    break;
                case "Polygon":
                    if (_polygonVertices.Count > 0)
                    {
                        _polygonVertices.RemoveAt(_polygonVertices.Count - 1);
                        UpdatePolygonPreviewVertices();
                        if (_polygonVertices.Count > 0)
                        {
                            var lastPoint = _polygonVertices[_polygonVertices.Count - 1];
                            UpdatePolygonPreview(lastPoint);
                        }
                        else
                        {
                            ClearPolygonPreview();
                        }
                        StatusBar.Text = $"已撤销上一个顶点，当前 {_polygonVertices.Count} 个折点";
                    }
                    break;
            }
        }

        private void CancelCurrentDrawing()
        {
            switch (currentDrawTool)
            {
                case "Line":
                    CancelLine();
                    break;
                case "Polygon":
                    CancelPolygon();
                    break;
                case "Point":
                    ClearCrosshair();
                    StatusBar.Text = "已删除，未生成点。移动鼠标可重新显示十字丝";
                    break;
            }
        }

        private void MyMapView_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (sender != CurrentMapView) return;
            var screenPoint = e.GetPosition(CurrentMapView);
            var mapPoint = CurrentMapView.ScreenToLocation(screenPoint);
            if (mapPoint == null)
                return;

            if (_isStreamDrawing && _isFreehandMode && (currentDrawTool == "Line" || currentDrawTool == "Polygon"))
            {
                // 流式绘制：自动添加折点
                double dist = Math.Sqrt(Math.Pow(screenPoint.X - _lastStreamScreenPoint.X, 2) +
                                        Math.Pow(screenPoint.Y - _lastStreamScreenPoint.Y, 2));
                if (dist >= StreamVertexDistance)
                {
                    var usePoint = _nearestSnapPoint ?? mapPoint;
                    if (_nearestSnapPoint != null) ClearSnapIndicator();

                    if (currentDrawTool == "Line")
                    {
                        AddLineVertex(usePoint);
                        UpdateRubberBand(usePoint);
                    }
                    else
                    {
                        AddPolygonVertex(usePoint);
                        UpdatePolygonPreview(usePoint);
                    }
                    _lastStreamScreenPoint = screenPoint;
                    e.Handled = true;
                }
                return;
            }

            if (currentDrawTool == "Line" && _lineVertices.Count > 0)
            {
                UpdateRubberBand(mapPoint);
            }
            else if (currentDrawTool == "Polygon" && _polygonVertices.Count > 0)
            {
                UpdatePolygonPreview(mapPoint);
            }
            else if (_isDraggingVertex && _isVertexEditing && _selectedVertexIndex >= 0 && _selectedVertexIndex < _currentVertices.Count)
            {
                var projectedPoint = ProjectToEditSr(mapPoint);
                if (projectedPoint != null)
                    _currentVertices[_selectedVertexIndex] = projectedPoint;
                UpdateEditPreview();
                ShowVertices();
            }
            else if (currentDrawTool == "MoveFeature" && _movePendingFeature != null && _moveAnchorPoint != null)
            {
                UpdateMovePreview(mapPoint);
            }

            // 捕捉检测：在绘制工具（点/线/面）及空间校准模式时进行
            if (_snappingEnabled && (currentDrawTool == "Point" || currentDrawTool == "Line" || currentDrawTool == "Polygon" || _isGeorefAddingPoint))
            {
                UpdateSnapping(screenPoint);
            }

            // 点模式：十字丝始终跟随鼠标（右键菜单打开期间 _pendingPoint 冻结位置）
            if (currentDrawTool == "Point" && _pendingPoint == null)
            {
                _trackingPoint = _nearestSnapPoint ?? mapPoint;
                UpdateCrosshair();
            }

            // 更新绘制工具栏的坐标显示
            if (_drawingToolbar != null && _drawingToolbar.IsVisible &&
                (currentDrawTool == "Point" || currentDrawTool == "Line" || currentDrawTool == "Polygon"))
            {
                _drawingToolbar.UpdateCoordinate(mapPoint.X, mapPoint.Y);
            }
        }

        private void MyMapView_OnLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender != CurrentMapView) return;
            if (_isStreamDrawing && _isFreehandMode)
            {
                _isStreamDrawing = false;
                CurrentMapView.ReleaseMouseCapture();
                // 自动完成（如果至少有足够顶点）
                if (currentDrawTool == "Line")
                {
                    if (_lineVertices.Count >= 2)
                    {
                        CompleteLine();
                    }
                    else
                    {
                        StatusBar.Text = "手绘线至少需要2个折点，请重试";
                        CancelLine();
                    }
                }
                else if (currentDrawTool == "Polygon")
                {
                    if (_polygonVertices.Count >= 3)
                    {
                        CompletePolygon();
                    }
                    else
                    {
                        StatusBar.Text = "手绘面至少需要3个折点，请重试";
                        CancelPolygon();
                    }
                }
                UpdateDrawingToolbar();
                e.Handled = true;
                return;
            }

            if (_isDraggingVertex)
            {
                _isDraggingVertex = false;
                CurrentMapView.ReleaseMouseCapture();

                // 拓扑锁定：移动共享顶点
                if (_topologyLockEnabled && _vertexDragStartPosition != null && _selectedVertexIndex >= 0 && _selectedVertexIndex < _currentVertices.Count)
                {
                    MapPoint newPos = _currentVertices[_selectedVertexIndex];
                    double dx = newPos.X - _vertexDragStartPosition.X;
                    double dy = newPos.Y - _vertexDragStartPosition.Y;
                    if (Math.Abs(dx) > 1e-10 || Math.Abs(dy) > 1e-10)
                    {
                        ApplyTopologyLockOffset(_vertexDragStartPosition, dx, dy);
                    }
                }
                _vertexDragStartPosition = null;

                StatusBar.Text = $"顶点已移动，右键确认保存，继续拖拽其他顶点，Delete 删除选中顶点，Esc 取消编辑";
            }
        }

        private void MyMapView_ViewpointChanged(object sender, EventArgs e)
        {
            if (_pendingPoint != null || _trackingPoint != null)
                UpdateCrosshair();
            if (_hasOverlay && sender == MapView2)
                UpdateImageOverlayPosition();
            if (_georefToolbar != null)
            {
                if (sender == MapView2 && (_georefToolbar.ControlPoints.Count > 0 || _georefWaitingTarget))
                    GeorefRedrawLinks();
            }
        }

        // ---------- 亮蓝色十字丝（GraphicsOverlay，地图/数据视图均可显示） ----------
        private void EnsureCrosshairOverlay()
        {
            if (_crosshairOverlay == null)
                _crosshairOverlay = new GraphicsOverlay();
            if (_crosshairSymbol == null)
            {
                // DodgerBlue 亮蓝色十字
                var blue = System.Drawing.Color.FromArgb(255, 30, 144, 255);
                _crosshairSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Cross, blue, 28)
                {
                    Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, blue, 2.5)
                };
            }
            var mv = CurrentMapView;
            if (mv == null) return;
            if (!mv.GraphicsOverlays.Contains(_crosshairOverlay))
            {
                if (MapView1 != null && MapView1.GraphicsOverlays.Contains(_crosshairOverlay))
                    MapView1.GraphicsOverlays.Remove(_crosshairOverlay);
                if (MapView2 != null && MapView2.GraphicsOverlays.Contains(_crosshairOverlay))
                    MapView2.GraphicsOverlays.Remove(_crosshairOverlay);
                mv.GraphicsOverlays.Add(_crosshairOverlay);
            }
        }

        private void UpdateCrosshair()
        {
            var anchorPoint = _pendingPoint ?? _trackingPoint;
            if (anchorPoint == null)
            {
                HideCrosshairGraphic();
                return;
            }
            if (double.IsNaN(anchorPoint.X) || double.IsNaN(anchorPoint.Y))
                return;

            EnsureCrosshairOverlay();
            if (_crosshairOverlay == null) return;

            if (_crosshairGraphic == null)
            {
                _crosshairGraphic = new Graphic(anchorPoint, _crosshairSymbol);
                _crosshairOverlay.Graphics.Add(_crosshairGraphic);
            }
            else
            {
                _crosshairGraphic.Geometry = anchorPoint;
                _crosshairGraphic.Symbol = _crosshairSymbol;
                if (!_crosshairOverlay.Graphics.Contains(_crosshairGraphic))
                    _crosshairOverlay.Graphics.Add(_crosshairGraphic);
            }

            // 兼容旧 WPF Canvas（仅数据视图有），避免残留
            try { CrosshairCanvas?.Children.Clear(); } catch { }
        }

        private void HideCrosshairGraphic()
        {
            if (_crosshairOverlay != null)
                _crosshairOverlay.Graphics.Clear();
            _crosshairGraphic = null;
            try { CrosshairCanvas?.Children.Clear(); } catch { }
        }

        private void ClearCrosshair()
        {
            HideCrosshairGraphic();
            _pendingPoint = null;
            _trackingPoint = null;
        }

        private async void MyMapView_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender != CurrentMapView) return;
            // 空间校准 Esc：取消待完成的控制点 / 退出添加模式
            if (e.Key == Key.Escape && _isGeorefMode)
            {
                if (_georefWaitingTarget)
                {
                    _georefWaitingTarget = false;
                    _georefPendingSource = null;
                    GeorefRedrawLinks();
                    StatusBar.Text = "已取消当前源点，请重新点击源点";
                    e.Handled = true;
                    return;
                }
                if (_isGeorefAddingPoint && _georefToolbar != null)
                {
                    _georefToolbar.SetAddPointMode(false);
                    _isGeorefAddingPoint = false;
                    StatusBar.Text = "已退出添加控制点模式";
                    e.Handled = true;
                    return;
                }
            }

            // 空格键切换捕捉（在绘制工具和编辑工具中）
            if (e.Key == Key.Space && (currentDrawTool == "Point" || currentDrawTool == "Line" || currentDrawTool == "Polygon" || currentDrawTool == "EditFeature"))
            {
                _snappingEnabled = !_snappingEnabled;
                if (!_snappingEnabled) ClearSnapIndicator();
                if (_drawingToolbar != null && _drawingToolbar.IsVisible)
                {
                    _drawingToolbar.BtnSnapToggle.IsChecked = _snappingEnabled;
                }
                StatusBar.Text = _snappingEnabled ? "捕捉已开启" : "捕捉已关闭";
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (_undoStack.Count > 0)
                {
                    await UndoLastEdit();
                    e.Handled = true;
                    return;
                }
                else if (_savedGraphics.Count > 0)
                {
                    UndoLastGraphic();
                    e.Handled = true;
                    return;
                }
            }

            // 顶点编辑中 Delete 只删顶点，必须优先于“删除整要素”
            if (e.Key == Key.Delete && _isVertexEditing)
            {
                RemoveSelectedVertex();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete && _selectedFeatures.Count > 0 && _isLayerEditMode && !_isVertexEditing)
            {
                await DeleteSelectedFeatures();
                e.Handled = true;
                return;
            }

            if (currentDrawTool == "Point")
            {
                if (e.Key == Key.Escape)
                {
                    ClearCrosshair();
                    StatusBar.Text = "已清除十字丝，移动鼠标可重新预览，右键保存或删除";
                    e.Handled = true;
                }
                else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    UndoLastGraphic();
                    e.Handled = true;
                }
            }
            else if (currentDrawTool == "Line")
            {
                if (e.Key == Key.Escape)
                {
                    CancelLine();
                    e.Handled = true;
                }
                else if (e.Key == Key.Back)
                {
                    RemoveLastLineVertex();
                    e.Handled = true;
                }
                else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    UndoLastGraphic();
                    e.Handled = true;
                }
            }
            else if (currentDrawTool == "Polygon")
            {
                if (e.Key == Key.Escape)
                {
                    CancelPolygon();
                    e.Handled = true;
                }
                else if (e.Key == Key.Back)
                {
                    RemoveLastPolygonVertex();
                    e.Handled = true;
                }
                else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    UndoLastGraphic();
                    e.Handled = true;
                }
            }
            else if (_isVertexEditing)
            {
                if (e.Key == Key.Escape)
                {
                    CancelVertexEditing();
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete)
                {
                    RemoveSelectedVertex();
                    e.Handled = true;
                }
            }
            else if (currentDrawTool == "DeleteFeature")
            {
                if (e.Key == Key.Escape)
                {
                    StatusBar.Text = "删除要素已取消";
                    e.Handled = true;
                }
            }
            else if (currentDrawTool == "MoveFeature")
            {
                if (e.Key == Key.Escape)
                {
                    CancelMoveFeature();
                    e.Handled = true;
                }
            }
            else if (currentDrawTool == "EditFeature")
            {
                if (e.Key == Key.Escape && _isVertexEditing)
                {
                    await CancelVertexEditing();
                    e.Handled = true;
                }
            }
            else
            {
                if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    if (_undoStack.Count > 0)
                    {
                        await UndoLastEdit();
                        e.Handled = true;
                    }
                    else if (_savedGraphics.Count > 0)
                    {
                        UndoLastGraphic();
                        e.Handled = true;
                    }
                }
            }
        }

        private SpatialReference GetEditVertexSpatialReference()
        {
            if (_currentVertices.Count > 0 && _currentVertices[0].SpatialReference != null)
                return _currentVertices[0].SpatialReference;
            if (_currentEditingFeature?.Geometry?.SpatialReference != null)
                return _currentEditingFeature.Geometry.SpatialReference;
            if (CurrentMapView.Map?.SpatialReference != null)
                return CurrentMapView.Map.SpatialReference;
            return SpatialReferences.WebMercator;
        }

        private MapPoint ProjectToEditSr(MapPoint point)
        {
            if (point == null) return null;
            var sr = GetEditVertexSpatialReference();
            if (point.SpatialReference == null)
                return new MapPoint(point.X, point.Y, sr);
            if (point.SpatialReference.Wkid == sr.Wkid || point.SpatialReference == sr)
                return new MapPoint(point.X, point.Y, sr);
            try
            {
                return (MapPoint)GeometryEngine.Project(point, sr);
            }
            catch
            {
                return new MapPoint(point.X, point.Y, sr);
            }
        }

        /// <summary>
        /// 在点击位置添加顶点：统一投影到要素坐标系，并插入到最近边段上
        /// </summary>
        private void InsertVertexAtMapPoint(MapPoint mapPoint)
        {
            if (_currentEditingFeature == null || _currentVertices.Count == 0)
            {
                StatusBar.Text = "请先选中一个要素进行顶点编辑";
                return;
            }

            MapPoint projected = ProjectToEditSr(mapPoint);
            if (projected == null)
            {
                StatusBar.Text = "坐标投影失败，无法添加顶点";
                return;
            }

            var sr = GetEditVertexSpatialReference();
            MapPoint newPt = new MapPoint(projected.X, projected.Y, sr);

            int insertIdx;
            if (_currentVertices.Count < 2)
            {
                insertIdx = _currentVertices.Count;
            }
            else
            {
                // 找到最近的边段，在该边之后插入，避免跨到另一侧导致飞边
                insertIdx = FindNearestSegmentInsertIndex(newPt);
            }

            _currentVertices.Insert(insertIdx, newPt);
            _selectedVertexIndex = insertIdx;
            UpdateEditPreview();
            ShowVertices();
            StatusBar.Text = $"已添加顶点（索引 {insertIdx}），右键确认保存，Esc 取消";
        }

        /// <summary>
        /// 返回应在其后插入新点的边段终点索引（即 insert 位置）
        /// </summary>
        private int FindNearestSegmentInsertIndex(MapPoint point)
        {
            bool isPolygon = _currentEditingFeature.Geometry.GeometryType == GeometryType.Polygon;
            int n = _currentVertices.Count;
            int segCount = isPolygon ? n : n - 1;

            double bestDist = double.MaxValue;
            int bestInsert = n; // 默认追加到末尾（线）或在 0 前（面）

            for (int i = 0; i < segCount; i++)
            {
                MapPoint a = _currentVertices[i];
                MapPoint b = _currentVertices[(i + 1) % n];
                double dist = DistancePointToSegment(point, a, b);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestInsert = i + 1; // 插入到 a 之后、b 之前
                }
            }

            // 对于面，bestInsert 可能等于 n（最后一条边 first←last），插入到末尾即可
            if (bestInsert > n) bestInsert = n;
            return bestInsert;
        }

        private static double DistancePointToSegment(MapPoint p, MapPoint a, MapPoint b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-20)
            {
                double ex = p.X - a.X;
                double ey = p.Y - a.Y;
                return Math.Sqrt(ex * ex + ey * ey);
            }
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            double projX = a.X + t * dx;
            double projY = a.Y + t * dy;
            double ox = p.X - projX;
            double oy = p.Y - projY;
            return Math.Sqrt(ox * ox + oy * oy);
        }

        private void ApplySmoothingToCurrentFeature()
        {
            if (_currentEditingFeature == null || _currentVertices.Count < 3)
            {
                StatusBar.Text = "当前要素顶点数不足，至少需要3个顶点才能平滑";
                return;
            }

            if (_editFeatureToolbar == null || _editFeatureToolbar.SmoothnessValue <= 0)
            {
                StatusBar.Text = "请先将平滑滑块调到大于 0，再点击应用";
                return;
            }

            // 备份，失败时完整回滚
            var backup = _currentVertices.Select(p =>
                new MapPoint(p.X, p.Y, p.SpatialReference ?? GetEditVertexSpatialReference())).ToList();
            Envelope originalExtent = null;
            try
            {
                var origGeom = BuildGeometryFromVertices();
                if (origGeom != null && !origGeom.IsEmpty)
                    originalExtent = origGeom.Extent;
            }
            catch { }

            var sr = GetEditVertexSpatialReference();
            var normalized = new List<MapPoint>();
            foreach (var pt in _currentVertices)
            {
                if (pt == null) continue;
                normalized.Add(EnsurePointSr(pt, sr));
            }
            if (normalized.Count < 3)
            {
                StatusBar.Text = "有效顶点数不足，无法平滑";
                return;
            }

            // 拉普拉斯平滑：顶点数不变，强度由滑块控制，不会炸环/反向填色
            double strength = Math.Max(0.05, Math.Min(0.45, _editFeatureToolbar.SmoothnessValue / 100.0 * 0.45));
            int iterations = Math.Max(1, Math.Min(8, (int)(_editFeatureToolbar.SmoothnessValue / 12.5)));
            bool isPolygon = _currentEditingFeature.Geometry.GeometryType == GeometryType.Polygon;

            List<MapPoint> smoothed = normalized;
            for (int iter = 0; iter < iterations; iter++)
                smoothed = LaplacianSmooth(smoothed, sr, strength, isPolygon);

            _currentVertices.Clear();
            _currentVertices.AddRange(smoothed);

            // 校验：范围不能明显异常（反向环会覆盖整图）
            if (!IsSmoothedGeometrySafe(originalExtent))
            {
                _currentVertices.Clear();
                _currentVertices.AddRange(backup);
                _selectedVertexIndex = -1;
                UpdateEditPreview();
                ShowVertices();
                StatusBar.Text = "平滑结果异常已撤销（可能引起反向填色），请降低平滑强度后重试";
                return;
            }

            _selectedVertexIndex = -1;
            UpdateEditPreview();
            ShowVertices();
            StatusBar.Text = $"平滑完成（强度 {strength:F2}×{iterations}次），顶点数 {_currentVertices.Count}，右键保存 / Esc 取消";
        }

        /// <summary>
        /// 拉普拉斯平滑：每个顶点向邻点中点靠拢，不改变顶点数，不会产生反向环
        /// </summary>
        private List<MapPoint> LaplacianSmooth(List<MapPoint> input, SpatialReference sr, double strength, bool isClosed)
        {
            int n = input.Count;
            var result = new List<MapPoint>(n);
            for (int i = 0; i < n; i++)
            {
                // 折线端点不动
                if (!isClosed && (i == 0 || i == n - 1))
                {
                    result.Add(new MapPoint(input[i].X, input[i].Y, sr));
                    continue;
                }

                MapPoint prev = input[(i - 1 + n) % n];
                MapPoint curr = input[i];
                MapPoint next = input[(i + 1) % n];
                double midX = (prev.X + next.X) / 2.0;
                double midY = (prev.Y + next.Y) / 2.0;
                double nx = curr.X + (midX - curr.X) * strength;
                double ny = curr.Y + (midY - curr.Y) * strength;
                result.Add(new MapPoint(nx, ny, sr));
            }
            return result;
        }

        private bool IsSmoothedGeometrySafe(Envelope originalExtent)
        {
            try
            {
                var geom = BuildGeometryFromVerticesWithoutSimplify();
                if (geom == null || geom.IsEmpty) return false;

                var ext = geom.Extent;
                if (ext == null || ext.IsEmpty) return false;

                // 坐标含 NaN/Infinity
                if (double.IsNaN(ext.XMin) || double.IsNaN(ext.YMin) ||
                    double.IsInfinity(ext.XMin) || double.IsInfinity(ext.YMin))
                    return false;

                if (originalExtent != null && !originalExtent.IsEmpty)
                {
                    double ow = Math.Max(originalExtent.Width, 1e-12);
                    double oh = Math.Max(originalExtent.Height, 1e-12);
                    double nw = Math.Max(ext.Width, 0);
                    double nh = Math.Max(ext.Height, 0);

                    // 范围膨胀超过 3 倍或缩成接近 0 → 拒绝
                    if (nw > ow * 3 || nh > oh * 3) return false;
                    if (nw < ow * 0.05 && nh < oh * 0.05) return false;

                    // 中心偏移过大
                    double ocx = (originalExtent.XMin + originalExtent.XMax) / 2;
                    double ocy = (originalExtent.YMin + originalExtent.YMax) / 2;
                    double ncx = (ext.XMin + ext.XMax) / 2;
                    double ncy = (ext.YMin + ext.YMax) / 2;
                    double maxShift = Math.Max(ow, oh) * 2;
                    if (Math.Abs(ncx - ocx) > maxShift || Math.Abs(ncy - ocy) > maxShift)
                        return false;
                }

                // 面：面积不能异常巨大（反向环特征）
                if (geom is Polygon poly)
                {
                    double area = Math.Abs(GeometryEngine.Area(poly));
                    if (originalExtent != null)
                    {
                        double oArea = Math.Abs(originalExtent.Width * originalExtent.Height);
                        if (oArea > 0 && area > oArea * 10) return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private Geometry BuildGeometryFromVerticesWithoutSimplify()
        {
            if (_currentEditingFeature == null || _currentVertices.Count == 0)
                return null;

            var sr = GetEditVertexSpatialReference();
            if (sr == null) return null;

            // 优先用原始几何的 GeometryType（编辑中途 Geometry 可能被改坏）
            var geomType = _originalEditGeometry?.GeometryType
                ?? _currentEditingFeature.Geometry?.GeometryType
                ?? GeometryType.Unknown;

            try
            {
                switch (geomType)
                {
                    case GeometryType.Point:
                        return new MapPoint(_currentVertices[0].X, _currentVertices[0].Y, sr);
                    case GeometryType.Polyline:
                        if (_currentVertices.Count < 2) return null;
                        {
                            var lineBuilder = new PolylineBuilder(sr);
                            var mainPart = new Part(sr);
                            foreach (var pt in _currentVertices)
                                mainPart.AddPoint(EnsurePointSr(pt, sr));
                            lineBuilder.Parts.Add(mainPart);
                            if (_originalEditGeometry is Polyline origLine && origLine.Parts.Count > 1)
                            {
                                for (int i = 1; i < origLine.Parts.Count; i++)
                                {
                                    var extra = new Part(sr);
                                    foreach (var p in origLine.Parts[i].Points)
                                        extra.AddPoint(new MapPoint(p.X, p.Y, sr));
                                    lineBuilder.Parts.Add(extra);
                                }
                            }
                            return lineBuilder.ToGeometry();
                        }
                    case GeometryType.Polygon:
                        if (_currentVertices.Count < 3) return null;
                        {
                            var polyBuilder = new PolygonBuilder(sr);
                            var exterior = new Part(sr);
                            foreach (var pt in _currentVertices)
                                exterior.AddPoint(EnsurePointSr(pt, sr));
                            polyBuilder.Parts.Add(exterior);
                            // 孔洞必须是独立 Part，绝不能压进外环（否则整图反向填色）
                            foreach (var hole in _preservedHoleRings)
                            {
                                if (hole == null || hole.Count < 3) continue;
                                var holePart = new Part(sr);
                                foreach (var hp in hole)
                                    holePart.AddPoint(EnsurePointSr(hp, sr));
                                polyBuilder.Parts.Add(holePart);
                            }
                            return polyBuilder.ToGeometry();
                        }
                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private async void ApplyTopologyLockOffset(MapPoint originalPos, double dx, double dy)
        {
            // 拓扑锁定：仅同步当前编辑图层内、共享同一坐标的顶点，并正式 Update + 安全校验
            // 禁止无校验地直接改 Geometry（会导致其他要素环被压坏、整层消失）
            if (originalPos == null || _currentEditingFeature == null) return;
            try
            {
                string layerName = _currentEditingLayerName ?? GetLayerNameByFeature(_currentEditingFeature);
                if (string.IsNullOrEmpty(layerName)) return;

                var table = await EnsureEditableMemoryTableAsync(layerName);
                if (table == null) return;

                // 容差：按图层坐标系自适应
                double tolerance = 0.001;
                if (table.SpatialReference != null && !table.SpatialReference.IsGeographic)
                    tolerance = Math.Max(0.01, Math.Abs(dx) + Math.Abs(dy)) * 0.01 + 0.01;
                else
                    tolerance = 1e-7; // 地理坐标约 1cm 级

                MapPoint originInTable = originalPos;
                try
                {
                    if (table.SpatialReference != null && originalPos.SpatialReference != null &&
                        originalPos.SpatialReference.Wkid != table.SpatialReference.Wkid)
                        originInTable = (MapPoint)GeometryEngine.Project(originalPos, table.SpatialReference);
                }
                catch { }

                int movedCount = 0;
                var features = (await table.QueryFeaturesAsync(new QueryParameters())).ToList();
                foreach (var feature in features)
                {
                    if (feature == _currentEditingFeature) continue;
                    if (feature.Geometry == null) continue;

                    Geometry original = CloneGeometry(feature.Geometry);
                    Geometry newGeom = null;
                    bool changed = false;

                    if (feature.Geometry is Polyline polyline)
                    {
                        var builder = new PolylineBuilder(table.SpatialReference ?? polyline.SpatialReference);
                        foreach (var part in polyline.Parts)
                        {
                            var p = new Part(builder.SpatialReference);
                            foreach (var pt in part.Points)
                            {
                                double dist = GeometryEngine.Distance(pt, originInTable);
                                if (dist <= tolerance)
                                {
                                    p.AddPoint(new MapPoint(pt.X + dx, pt.Y + dy, builder.SpatialReference));
                                    changed = true;
                                    movedCount++;
                                }
                                else
                                    p.AddPoint(pt);
                            }
                            builder.Parts.Add(p);
                        }
                        newGeom = builder.ToGeometry();
                    }
                    else if (feature.Geometry is Polygon polygon)
                    {
                        var builder = new PolygonBuilder(table.SpatialReference ?? polygon.SpatialReference);
                        foreach (var part in polygon.Parts)
                        {
                            var p = new Part(builder.SpatialReference);
                            foreach (var pt in part.Points)
                            {
                                double dist = GeometryEngine.Distance(pt, originInTable);
                                if (dist <= tolerance)
                                {
                                    p.AddPoint(new MapPoint(pt.X + dx, pt.Y + dy, builder.SpatialReference));
                                    changed = true;
                                    movedCount++;
                                }
                                else
                                    p.AddPoint(pt);
                            }
                            builder.Parts.Add(p);
                        }
                        newGeom = builder.ToGeometry();
                    }

                    if (!changed || newGeom == null || !IsGeometryEditSafe(original, newGeom))
                        continue;

                    try
                    {
                        feature.Geometry = newGeom;
                        if (table.CanUpdate(feature))
                            await table.UpdateFeatureAsync(feature);
                    }
                    catch
                    {
                        try { feature.Geometry = original; } catch { }
                    }
                }

                if (movedCount > 0)
                {
                    RefreshLayerDisplay(layerName);
                    StatusBar.Text = $"拓扑锁定：已同步 {movedCount} 个共享顶点";
                }
            }
            catch { }
        }

        private void RemoveSelectedVertex()
        {
            if (_currentEditingFeature == null || _currentVertices.Count == 0)
                return;

            if (!IsLayerEditable(_currentEditingFeature))
            {
                StatusBar.Text = "该图层已锁定，无法删除顶点，请在图层管理器中开启编辑模式";
                return;
            }

            if (_selectedVertexIndex < 0 || _selectedVertexIndex >= _currentVertices.Count)
            {
                StatusBar.Text = "请先点击选中一个顶点";
                return;
            }

            int minVertices = (_currentEditingFeature.Geometry.GeometryType == GeometryType.Polygon) ? 3 : 2;
            if (_currentVertices.Count <= minVertices)
            {
                StatusBar.Text = $"至少需要 {minVertices} 个顶点，无法删除";
                return;
            }

            _currentVertices.RemoveAt(_selectedVertexIndex);
            _selectedVertexIndex = -1;
            UpdateEditPreview();
            ShowVertices();
            StatusBar.Text = $"已删除顶点，剩余 {_currentVertices.Count} 个，右键确认保存，Esc取消编辑";
        }

        // ---------- 保存点（图层编辑模式写入要素表；否则写入绘制叠加层） ----------
        private async void DrawPoint(MapPoint mapPoint)
        {
            await DrawPointAsync(mapPoint);
        }

        private async Task DrawPointAsync(MapPoint mapPoint)
        {
            try
            {
                if (mapPoint == null || double.IsNaN(mapPoint.X) || double.IsNaN(mapPoint.Y))
                    return;

                if (_isLayerEditMode && !string.IsNullOrEmpty(_currentEditLayerName))
                {
                    if (await AddFeatureToEditLayerAsync(mapPoint))
                    {
                        StatusBar.Text = $"已在图层 {_currentEditLayerName} 创建点（未保存）: " + FormatCoordinate(mapPoint);
                        return;
                    }
                    // 写入图层失败时仍落到叠加层，避免“点了却看不见”
                }

                if (_drawingOverlay == null)
                    _drawingOverlay = new GraphicsOverlay();
                if (CurrentMapView != null && !CurrentMapView.GraphicsOverlays.Contains(_drawingOverlay))
                {
                    if (MapView1 != null && MapView1.GraphicsOverlays.Contains(_drawingOverlay))
                        MapView1.GraphicsOverlays.Remove(_drawingOverlay);
                    if (MapView2 != null && MapView2.GraphicsOverlays.Contains(_drawingOverlay))
                        MapView2.GraphicsOverlays.Remove(_drawingOverlay);
                    CurrentMapView.GraphicsOverlays.Add(_drawingOverlay);
                }

                var simpleSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 10);
                var simpleGraphic = new Graphic(mapPoint, simpleSymbol);
                _drawingOverlay.Graphics.Add(simpleGraphic);
                _savedGraphics.Add(simpleGraphic);

                StatusBar.Text = "已创建点: " + FormatCoordinate(mapPoint);
            }
            catch (Exception ex)
            {
                MessageBox.Show("绘制点失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddLineVertex(MapPoint mapPoint)
        {
            if (mapPoint == null || double.IsNaN(mapPoint.X) || double.IsNaN(mapPoint.Y))
                return;
            if (_linePreviewOverlay == null)
            {
                _linePreviewOverlay = new GraphicsOverlay();
                CurrentMapView.GraphicsOverlays.Add(_linePreviewOverlay);
            }

            _lineVertices.Add(mapPoint);

            UpdatePreviewVertices();

            if (_lineVertices.Count == 1)
            {
                StatusBar.Text = "已添加第一个折点，继续点击添加更多折点，右键完成线";
            }
            else
            {
                StatusBar.Text = $"已添加 {_lineVertices.Count} 个折点，继续点击或右键完成";
            }
        }

        private void UpdateRubberBand(MapPoint currentMousePoint)
        {
            if (_lineVertices.Count == 0)
                return;

            if (_linePreviewOverlay == null)
                return;

            if (_previewLineGraphic != null)
            {
                _linePreviewOverlay.Graphics.Remove(_previewLineGraphic);
            }

            var verticesCopy = new List<MapPoint>(_lineVertices);
            verticesCopy.Add(currentMousePoint);

            var builder = new PolylineBuilder(verticesCopy[0].SpatialReference);
            foreach (var pt in verticesCopy)
            {
                builder.AddPoint(pt);
            }
            var polyline = builder.ToGeometry();

            var lineSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Dash, System.Drawing.Color.Cyan, 2);
            _previewLineGraphic = new Graphic(polyline, lineSymbol);
            _linePreviewOverlay.Graphics.Add(_previewLineGraphic);
        }

        private void UpdatePreviewVertices()
        {
            if (_linePreviewOverlay == null)
                return;

            if (_previewVerticesGraphic != null)
            {
                _linePreviewOverlay.Graphics.Remove(_previewVerticesGraphic);
            }

            var builder = new MultipointBuilder(_lineVertices[0].SpatialReference);
            foreach (var pt in _lineVertices)
            {
                builder.Points.Add(pt);
            }
            var multipoint = builder.ToGeometry();

            var markerSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Cyan, 8);
            _previewVerticesGraphic = new Graphic(multipoint, markerSymbol);
            _linePreviewOverlay.Graphics.Add(_previewVerticesGraphic);
        }

        private async void CompleteLine()
        {
            try
            {
                StatusBar.Text = "正在完成线绘制...";

                if (_lineVertices.Count < 2)
                {
                    StatusBar.Text = "至少需要 2 个折点才能完成线";
                    return;
                }

                var sr = _lineVertices[0].SpatialReference;
                var builder = new PolylineBuilder(sr);
                foreach (var pt in _lineVertices)
                {
                    builder.AddPoint(pt);
                }
                var polyline = builder.ToGeometry();

                if (_isLayerEditMode && !string.IsNullOrEmpty(_currentEditLayerName))
                {
                    if (await AddFeatureToEditLayerAsync(polyline))
                    {
                        ClearLinePreview();
                        StatusBar.Text = $"已在图层 {_currentEditLayerName} 创建线（未保存），共 {_lineVertices.Count} 个折点";
                        return;
                    }
                    return;
                }

                if (_drawingOverlay == null)
                {
                    _drawingOverlay = new GraphicsOverlay();
                    CurrentMapView.GraphicsOverlays.Add(_drawingOverlay);
                }

                var freeLineSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Blue, 3);
                var freeGraphic = new Graphic(polyline, freeLineSymbol);
                _drawingOverlay.Graphics.Add(freeGraphic);
                _savedGraphics.Add(freeGraphic);

                ClearLinePreview();
                StatusBar.Text = $"线已完成，共 {_lineVertices.Count} 个折点，按 Ctrl+Z 撤销，按 Esc 退出";
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"线绘制失败: {ex.Message}";
                MessageBox.Show($"线绘制失败: {ex.Message}\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelLine()
        {
            ClearLinePreview();
            StatusBar.Text = "线绘制已取消，左键开始新线";
        }

        private void ClearLinePreview()
        {
            if (_linePreviewOverlay != null)
            {
                _linePreviewOverlay.Graphics.Clear();
            }
            _lineVertices.Clear();
            _previewLineGraphic = null;
            _previewVerticesGraphic = null;
        }

        private void RemoveLastLineVertex()
        {
            if (_lineVertices.Count == 0)
            {
                StatusBar.Text = "没有可撤销的折点";
                return;
            }
            _lineVertices.RemoveAt(_lineVertices.Count - 1);
            UpdatePreviewVertices();
            StatusBar.Text = $"已撤销折点，剩余 {_lineVertices.Count} 个折点";
        }

        private void AddPolygonVertex(MapPoint mapPoint)
        {
            if (mapPoint == null || double.IsNaN(mapPoint.X) || double.IsNaN(mapPoint.Y))
                return;
            if (_polygonPreviewOverlay == null)
            {
                _polygonPreviewOverlay = new GraphicsOverlay();
                CurrentMapView.GraphicsOverlays.Add(_polygonPreviewOverlay);
            }

            _polygonVertices.Add(mapPoint);
            UpdatePolygonPreviewVertices();

            if (_polygonVertices.Count == 1)
            {
                StatusBar.Text = "已添加第一个折点，继续点击添加更多折点，右键完成面";
            }
            else
            {
                StatusBar.Text = $"已添加 {_polygonVertices.Count} 个折点，继续点击或右键完成";
            }
        }

        private void UpdatePolygonPreview(MapPoint currentMousePoint)
        {
            if (_polygonVertices.Count == 0)
                return;

            if (_polygonPreviewOverlay == null)
                return;

            if (_previewPolygonGraphic != null)
            {
                _polygonPreviewOverlay.Graphics.Remove(_previewPolygonGraphic);
            }

            var verticesCopy = new List<MapPoint>(_polygonVertices);
            verticesCopy.Add(currentMousePoint);
            verticesCopy.Add(_polygonVertices[0]);

            var builder = new PolygonBuilder(verticesCopy[0].SpatialReference);
            foreach (var pt in verticesCopy)
            {
                builder.AddPoint(pt);
            }
            var polygon = builder.ToGeometry();

            var fillSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, 
                System.Drawing.Color.FromArgb(60, System.Drawing.Color.Green),
                new SimpleLineSymbol(SimpleLineSymbolStyle.Dash, System.Drawing.Color.Cyan, 2));
            _previewPolygonGraphic = new Graphic(polygon, fillSymbol);
            _polygonPreviewOverlay.Graphics.Add(_previewPolygonGraphic);
        }

        private void UpdatePolygonPreviewVertices()
        {
            if (_polygonPreviewOverlay == null || _polygonVertices.Count == 0)
                return;

            if (_previewPolygonVerticesGraphic != null)
            {
                _polygonPreviewOverlay.Graphics.Remove(_previewPolygonVerticesGraphic);
                _previewPolygonVerticesGraphic = null;
            }

            var builder = new MultipointBuilder(_polygonVertices[0].SpatialReference);
            foreach (var pt in _polygonVertices)
            {
                builder.Points.Add(pt);
            }
            var multipoint = builder.ToGeometry();

            var markerSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Cyan, 8);
            _previewPolygonVerticesGraphic = new Graphic(multipoint, markerSymbol);
            _polygonPreviewOverlay.Graphics.Add(_previewPolygonVerticesGraphic);
        }

        private async void CompletePolygon()
        {
            try
            {
                StatusBar.Text = "正在完成面绘制...";
                
                if (_polygonVertices.Count < 3)
                {
                    StatusBar.Text = "至少需要 3 个折点才能完成面";
                    return;
                }

                var sr = _polygonVertices[0].SpatialReference;
                StatusBar.Text = $"折点数量: {_polygonVertices.Count}, 空间参考: {sr?.Wkid}";

                var builder = new PolygonBuilder(sr);
                foreach (var pt in _polygonVertices)
                {
                    builder.AddPoint(pt);
                }
                var polygon = builder.ToGeometry();
                StatusBar.Text = $"多边形已构建，面积: {GeometryEngine.Area(polygon)}";

                if (_isLayerEditMode && !string.IsNullOrEmpty(_currentEditLayerName))
                {
                    if (await AddFeatureToEditLayerAsync(polygon))
                    {
                        ClearPolygonPreview();
                        StatusBar.Text = $"已在图层 {_currentEditLayerName} 创建面（未保存），共 {_polygonVertices.Count} 个折点";
                        return;
                    }
                    return;
                }

                if (_drawingOverlay == null)
                {
                    _drawingOverlay = new GraphicsOverlay();
                    CurrentMapView.GraphicsOverlays.Add(_drawingOverlay);
                }

                var freeFillSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid,
                    System.Drawing.Color.FromArgb(80, System.Drawing.Color.Green),
                    new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.DarkGreen, 2));
                var freeGraphic = new Graphic(polygon, freeFillSymbol);
                _drawingOverlay.Graphics.Add(freeGraphic);
                _savedGraphics.Add(freeGraphic);

                ClearPolygonPreview();
                StatusBar.Text = $"面已完成，共 {_polygonVertices.Count} 个折点，按 Ctrl+Z 撤销，按 Esc 退出";
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"面绘制失败: {ex.Message}";
                MessageBox.Show($"面绘制失败: {ex.Message}\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelPolygon()
        {
            ClearPolygonPreview();
            StatusBar.Text = "面绘制已取消，左键开始新面";
        }

        private async System.Threading.Tasks.Task RefreshFeatureLayer(FeatureLayer featureLayer)
        {
            try
            {
                if (featureLayer.FeatureTable is ShapefileFeatureTable shapefileTable)
                {
                    string filePath = shapefileTable.Path;
                    string layerName = featureLayer.Name;
                    Renderer renderer = featureLayer.Renderer;

                    int index = CurrentMapView.Map.OperationalLayers.IndexOf(featureLayer);
                    if (index >= 0)
                    {
                        CurrentMapView.Map.OperationalLayers.RemoveAt(index);

                        var newTable = new ShapefileFeatureTable(filePath);
                        await newTable.LoadAsync();

                        var newLayer = new FeatureLayer(newTable);
                        newLayer.Name = layerName;
                        newLayer.Renderer = renderer;

                        CurrentMapView.Map.OperationalLayers.Insert(index, newLayer);
                        SyncMapToOtherView(CurrentMapView);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"图层刷新失败: {ex.Message}";
            }
        }

        private async Task DeleteSelectedFeatures()
        {
            try
            {
                int deletedCount = 0;
                var featuresToRemove = new List<Feature>();

                foreach (var feature in _selectedFeatures)
                {
                    var graphic = FindGraphicForFeature(feature);
                    bool isUnsavedGraphic = graphic != null && _unsavedGraphics.Contains(graphic);
                    string layerName = GetLayerNameByFeature(feature);

                    _undoStack.Push(new EditOperation
                    {
                        Feature = feature,
                        OriginalGeometry = CloneGeometry(feature.Geometry),
                        OperationType = "删除",
                        LayerName = layerName,
                        DeletedGraphic = isUnsavedGraphic ? graphic : null,
                        DeletedAttributes = feature.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value),
                        DeletedSnapshots = new List<FeatureSnapshot> { CreateFeatureSnapshot(feature) }
                    });

                    if (isUnsavedGraphic)
                    {
                        if (_drawingOverlay != null)
                            _drawingOverlay.Graphics.Remove(graphic);
                        _unsavedGraphics.Remove(graphic);
                        deletedCount++;
                        featuresToRemove.Add(feature);
                        continue;
                    }

                    var featureTable = feature.FeatureTable;
                    if (featureTable != null)
                    {
                        try
                        {
                            await featureTable.DeleteFeatureAsync(feature);
                            deletedCount++;
                            featuresToRemove.Add(feature);

                            if (!string.IsNullOrEmpty(layerName))
                            {
                                MarkLayerHasUnsavedChanges(layerName);
                                RefreshLayerDisplay(layerName);
                            }
                        }
                        catch (Exception ex)
                        {
                            StatusBar.Text = $"删除要素失败: {ex.Message}";
                        }
                    }
                }

                foreach (var f in featuresToRemove)
                    _selectedFeatures.Remove(f);

                ShowSelectionHighlights();
                StatusBar.Text = $"已删除 {deletedCount} 个要素";

                if (_attributeTableWindow != null)
                    _attributeTableWindow.RefreshTable();
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"删除要素失败: {ex.Message}";
                MessageBox.Show($"删除要素失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void MenuItem_SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentEditLayerName))
            {
                await SaveLayerToShapefile(_currentEditLayerName);
            }
            else
            {
                MessageBox.Show("请先选择要编辑的图层", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void MenuItem_Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count > 0)
            {
                await UndoLastEdit();
            }
            else if (_savedGraphics.Count > 0 || (_drawingOverlay != null && _drawingOverlay.Graphics.Count > 0))
            {
                UndoLastGraphic();
            }
            else
            {
                MessageBox.Show("没有可撤销的编辑操作", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusBar.Text = "没有可撤销的编辑操作";
            }
        }

        private void MenuItem_UndoAll_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count > 0)
            {
                var result = MessageBox.Show("确定要撤回所有更改吗？此操作不可恢复。", "确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _undoStack.Clear();
                    ClearSelection();
                    StatusBar.Text = "已撤回所有更改";
                }
            }
            else
            {
                StatusBar.Text = "没有可撤回的编辑操作";
            }
        }

        private void ClearPolygonPreview()
        {
            if (_polygonPreviewOverlay != null)
            {
                _polygonPreviewOverlay.Graphics.Clear();
            }
            _polygonVertices.Clear();
            _previewPolygonGraphic = null;
            _previewPolygonVerticesGraphic = null;
        }

        private void RemoveLastPolygonVertex()
        {
            if (_polygonVertices.Count == 0)
            {
                StatusBar.Text = "没有可撤销的折点";
                return;
            }
            _polygonVertices.RemoveAt(_polygonVertices.Count - 1);
            UpdatePolygonPreviewVertices();
            StatusBar.Text = $"已撤销折点，剩余 {_polygonVertices.Count} 个折点";
        }

        private void UndoLastGraphic()
        {
            if (_savedGraphics.Count == 0)
            {
                StatusBar.Text = "没有可撤销的图形";
                return;
            }

            var lastGraphic = _savedGraphics[_savedGraphics.Count - 1];
            if (_drawingOverlay != null && _drawingOverlay.Graphics.Contains(lastGraphic))
            {
                _drawingOverlay.Graphics.Remove(lastGraphic);
            }
            _savedGraphics.RemoveAt(_savedGraphics.Count - 1);
            StatusBar.Text = $"已撤销，剩余 {_savedGraphics.Count} 个图形";
        }

        private FeatureTable ResolveEditTable(string layerName, Feature fallbackFeature = null)
        {
            if (!string.IsNullOrEmpty(layerName))
            {
                var layerItem = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
                if (layerItem != null)
                {
                    var table = GetFeatureTableFromLayer(layerItem.Layer) ?? layerItem.MemoryFeatureTable;
                    if (table != null) return table;
                }

                var layer = CurrentMapView?.Map?.OperationalLayers?.FirstOrDefault(l => l.Name == layerName);
                var mapTable = GetFeatureTableFromLayer(layer);
                if (mapTable != null) return mapTable;
            }

            return fallbackFeature?.FeatureTable;
        }

        private async Task RestoreFeatureSnapshotAsync(FeatureTable featureTable, FeatureSnapshot snapshot)
        {
            if (featureTable == null || snapshot == null) return;

            var newFeature = featureTable.CreateFeature();
            newFeature.Geometry = snapshot.Geometry;
            if (snapshot.Attributes != null)
            {
                foreach (var attr in snapshot.Attributes)
                {
                    string fieldName = attr.Key.ToUpperInvariant();
                    if (fieldName == "FID" || fieldName == "OBJECTID" || fieldName == "OID")
                        continue;
                    try
                    {
                        if (newFeature.Attributes.ContainsKey(attr.Key))
                            newFeature.Attributes[attr.Key] = attr.Value;
                    }
                    catch { }
                }
            }
            await featureTable.AddFeatureAsync(newFeature);
        }

        private static FeatureSnapshot CreateFeatureSnapshot(Feature feature)
        {
            if (feature == null) return null;
            var attrs = new Dictionary<string, object>();
            foreach (var kv in feature.Attributes)
                attrs[kv.Key] = kv.Value;
            return new FeatureSnapshot
            {
                Geometry = CloneGeometryStatic(feature.Geometry),
                Attributes = attrs
            };
        }

        private static Geometry CloneGeometryStatic(Geometry geometry)
        {
            if (geometry == null) return null;
            try { return Geometry.FromJson(geometry.ToJson()); }
            catch { return geometry; }
        }

        public async System.Threading.Tasks.Task UndoLastEditPublic()
        {
            await UndoLastEdit();
        }

        private async System.Threading.Tasks.Task UndoLastEdit()
        {
            if (_undoStack.Count == 0)
            {
                StatusBar.Text = "没有可撤销的编辑";
                return;
            }

            var lastOp = _undoStack.Pop();
            try
            {
                if (lastOp.OperationType == "新增")
                {
                    // 撤销新增 = 删除刚加的要素
                    var featureTable = ResolveEditTable(lastOp.LayerName, lastOp.Feature);
                    if (featureTable != null)
                    {
                        Feature toDelete = lastOp.Feature;
                        if (toDelete == null || toDelete.FeatureTable != featureTable)
                        {
                            var all = (await featureTable.QueryFeaturesAsync(new QueryParameters())).ToList();
                            if (lastOp.OriginalGeometry != null)
                            {
                                string gj = lastOp.OriginalGeometry.ToJson();
                                toDelete = all.LastOrDefault(f => f.Geometry != null &&
                                    string.Equals(f.Geometry.ToJson(), gj, StringComparison.Ordinal));
                            }
                        }
                        if (toDelete != null)
                        {
                            try { await featureTable.DeleteFeatureAsync(toDelete); }
                            catch { }
                        }
                    }
                    if (!string.IsNullOrEmpty(lastOp.LayerName))
                    {
                        MarkLayerHasUnsavedChanges(lastOp.LayerName);
                        RefreshLayerDisplay(lastOp.LayerName);
                    }
                    StatusBar.Text = "已撤销新增要素";
                }
                else if (lastOp.OperationType == "删除")
                {
                    if (lastOp.DeletedGraphic != null)
                    {
                        if (_drawingOverlay != null)
                            _drawingOverlay.Graphics.Add(lastOp.DeletedGraphic);
                        if (!_unsavedGraphics.Contains(lastOp.DeletedGraphic))
                            _unsavedGraphics.Add(lastOp.DeletedGraphic);
                    }

                    var featureTable = ResolveEditTable(lastOp.LayerName, lastOp.Feature);
                    var snapshots = lastOp.DeletedSnapshots;
                    if ((snapshots == null || snapshots.Count == 0) && lastOp.DeletedFeatures != null)
                    {
                        snapshots = lastOp.DeletedFeatures.Select(CreateFeatureSnapshot).Where(s => s != null).ToList();
                    }
                    if ((snapshots == null || snapshots.Count == 0) && lastOp.Feature != null)
                    {
                        snapshots = new List<FeatureSnapshot>
                        {
                            new FeatureSnapshot
                            {
                                Geometry = lastOp.OriginalGeometry ?? CloneGeometryStatic(lastOp.Feature.Geometry),
                                Attributes = lastOp.DeletedAttributes ?? lastOp.Feature.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value)
                            }
                        };
                    }

                    if (featureTable != null && snapshots != null)
                    {
                        foreach (var snapshot in snapshots)
                            await RestoreFeatureSnapshotAsync(featureTable, snapshot);
                    }

                    if (!string.IsNullOrEmpty(lastOp.LayerName))
                    {
                        MarkLayerHasUnsavedChanges(lastOp.LayerName);
                        RefreshLayerDisplay(lastOp.LayerName);
                    }
                    StatusBar.Text = "已撤销删除操作";
                }
                else if (lastOp.OperationType == "添加字段" || lastOp.OperationType == "删除字段")
                {
                    await RevertFieldOperation(lastOp);
                    StatusBar.Text = $"已撤销{lastOp.OperationType}操作";
                    MessageBox.Show($"已撤销{lastOp.OperationType}", "撤销", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (lastOp.OperationType == "修改属性")
                {
                    var featureTable = ResolveEditTable(lastOp.LayerName, lastOp.Feature);
                    Feature target = lastOp.Feature;
                    if (featureTable != null && !string.IsNullOrEmpty(lastOp.FieldName))
                    {
                        // 要素引用可能失效，按几何重匹配
                        if (target == null || target.FeatureTable != featureTable)
                        {
                            if (lastOp.OriginalGeometry != null || lastOp.Feature?.Geometry != null)
                            {
                                var g = lastOp.OriginalGeometry ?? lastOp.Feature.Geometry;
                                var all = (await featureTable.QueryFeaturesAsync(new QueryParameters())).ToList();
                                target = all.FirstOrDefault(f => f.Geometry != null &&
                                    string.Equals(f.Geometry.ToJson(), g.ToJson(), StringComparison.Ordinal));
                            }
                        }
                        if (target != null)
                        {
                            target.Attributes[lastOp.FieldName] = lastOp.OldAttributeValue;
                            await featureTable.UpdateFeatureAsync(target);
                            MarkLayerHasUnsavedChanges(lastOp.LayerName);
                            StatusBar.Text = "已撤销属性修改";
                        }
                    }
                }
                else
                {
                    // 编辑/移动等几何操作
                    string layerName = lastOp.LayerName ?? GetLayerNameByFeature(lastOp.Feature);
                    FeatureTable featureTable = null;
                    if (!string.IsNullOrEmpty(layerName))
                        featureTable = await EnsureEditableMemoryTableAsync(layerName);
                    if (featureTable == null)
                        featureTable = ResolveEditTable(lastOp.LayerName, lastOp.Feature) ?? lastOp.Feature?.FeatureTable;

                    if (featureTable != null && lastOp.OriginalGeometry != null)
                    {
                        Feature target = lastOp.Feature;
                        // 要素引用可能因转内存表而失效，按新几何或原几何重匹配
                        if (target == null || target.FeatureTable != featureTable)
                        {
                            var matchGeom = lastOp.NewGeometry ?? lastOp.Feature?.Geometry;
                            target = await FindFeatureInTableAsync(featureTable, lastOp.Feature, matchGeom);
                            if (target == null && lastOp.OriginalGeometry != null)
                            {
                                // 再按新几何 JSON 匹配失败时，遍历找最接近
                                var all = (await featureTable.QueryFeaturesAsync(new QueryParameters())).ToList();
                                if (lastOp.NewGeometry != null)
                                {
                                    string nj = lastOp.NewGeometry.ToJson();
                                    target = all.FirstOrDefault(f => f.Geometry != null &&
                                        string.Equals(f.Geometry.ToJson(), nj, StringComparison.Ordinal));
                                }
                            }
                        }

                        if (target != null)
                        {
                            try
                            {
                                var restored = ProjectGeometryToTable(CloneGeometry(lastOp.OriginalGeometry), featureTable);
                                target.Geometry = restored;
                                if (featureTable.CanUpdate(target))
                                    await featureTable.UpdateFeatureAsync(target);
                            }
                            catch
                            {
                                await RestoreFeatureSnapshotAsync(featureTable, new FeatureSnapshot
                                {
                                    Geometry = lastOp.OriginalGeometry,
                                    Attributes = lastOp.DeletedAttributes
                                });
                            }
                        }
                        else
                        {
                            // 找不到目标时用快照恢复
                            await RestoreFeatureSnapshotAsync(featureTable, new FeatureSnapshot
                            {
                                Geometry = lastOp.OriginalGeometry,
                                Attributes = lastOp.DeletedAttributes
                            });
                        }

                        if (!string.IsNullOrEmpty(layerName))
                        {
                            MarkLayerHasUnsavedChanges(layerName);
                            RefreshLayerDisplay(layerName);
                        }

                        ClearSelection();
                        StatusBar.Text = $"已撤销{lastOp.OperationType}操作";
                    }
                    else
                    {
                        StatusBar.Text = "撤销失败：找不到图层或原始几何";
                    }
                }

                UpdateAttributeTable();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"撤销失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                _undoStack.Push(lastOp);
            }
        }

        private async void SelectFeatureAtPoint(MapPoint point, ModifierKeys modifiers)
        {
            try
            {
                var tolerance = 10;
                var screenPoint = CurrentMapView.LocationToScreen(point);

                var identifyResult = await CurrentMapView.IdentifyLayersAsync(screenPoint, tolerance, false);

                Feature selectedFeature = null;

                foreach (var layerResult in identifyResult)
                {
                    selectedFeature = FindFeatureInIdentifyResult(layerResult);
                    if (selectedFeature != null)
                        break;
                }

                if (selectedFeature == null && _drawingOverlay != null)
                {
                    var graphicsResult = await CurrentMapView.IdentifyGraphicsOverlaysAsync(screenPoint, tolerance, false);
                    foreach (var overlayResult in graphicsResult)
                    {
                        if (overlayResult.Graphics.Count > 0)
                        {
                            var graphic = overlayResult.Graphics[0];
                            var feature = CreateFeatureFromGraphic(graphic);
                            if (feature != null)
                            {
                                selectedFeature = feature;
                                break;
                            }
                        }
                    }
                }

                if (selectedFeature != null)
                {
                    if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    {
                        if (!_selectedFeatures.Contains(selectedFeature))
                        {
                            _selectedFeatures.Add(selectedFeature);
                        }
                        else
                        {
                            _selectedFeatures.Remove(selectedFeature);
                        }
                    }
                    else
                    {
                        _selectedFeatures.Clear();
                        _selectedFeatures.Add(selectedFeature);
                    }
                    ShowSelectionHighlights();
                    StatusBar.Text = $"已选中 {_selectedFeatures.Count} 个要素";

                    if (_attributeTableWindow != null && _attributeTableWindow.IsVisible)
                    {
                        _attributeTableWindow.UpdateSelectionFromMap(_selectedFeatures);
                    }
                    return;
                }

                if ((modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                {
                    ClearSelection();
                    StatusBar.Text = "选择工具已激活，点击选中要素，Shift+点击多选";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("选择要素失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Graphic FindGraphicForFeature(Feature feature)
        {
            if (_drawingOverlay == null)
                return null;

            foreach (var graphic in _drawingOverlay.Graphics)
            {
                if (graphic.Geometry.Equals(feature.Geometry))
                    return graphic;

                if (graphic.Attributes.ContainsKey("LayerName") && 
                    feature.Attributes.ContainsKey("LayerName") &&
                    graphic.Attributes["LayerName"].Equals(feature.Attributes["LayerName"]))
                {
                    return graphic;
                }
            }
            return null;
        }

        public void SelectFeaturesFromAttributeTable(List<Feature> features)
        {
            _selectedFeatures.Clear();
            _selectedFeatures.AddRange(features);
            ShowSelectionHighlights();
            StatusBar.Text = $"已选中 {_selectedFeatures.Count} 个要素";
        }

        private Feature CreateFeatureFromGraphic(Graphic graphic)
        {
            try
            {
                var layerName = graphic.Attributes.ContainsKey("LayerName") ? 
                    graphic.Attributes["LayerName"].ToString() : string.Empty;
                
                foreach (var layerItem in LayerManager.Layers)
                {
                    if (layerItem.Name != layerName) continue;

                    FeatureTable table = layerItem.MemoryFeatureTable ?? GetFeatureTableFromLayer(layerItem.Layer);
                    if (table == null) continue;

                    var feature = table.CreateFeature();
                    feature.Geometry = graphic.Geometry;
                    foreach (var attr in graphic.Attributes)
                    {
                        if (attr.Key != "LayerName" && feature.Attributes.ContainsKey(attr.Key))
                        {
                            feature.Attributes[attr.Key] = attr.Value;
                        }
                    }
                    return feature;
                }
            }
            catch { }
            return null;
        }

        private void ShowSelectionHighlights()
        {
            if (_selectionOverlay == null)
            {
                _selectionOverlay = new GraphicsOverlay();
                CurrentMapView.GraphicsOverlays.Add(_selectionOverlay);
            }

            _selectionOverlay.Graphics.Clear();

            List<Feature> validFeatures = new List<Feature>();
            foreach (var feature in _selectedFeatures)
            {
                try
                {
                    if (feature == null || feature.Geometry == null)
                        continue;

                    var featureTable = feature.FeatureTable;
                    if (featureTable != null)
                    {
                        validFeatures.Add(feature);
                    }
                    else
                    {
                        var reloadedFeature = ReloadFeatureByGeometry(feature);
                        if (reloadedFeature != null)
                        {
                            validFeatures.Add(reloadedFeature);
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            _selectedFeatures = validFeatures;

            foreach (var feature in _selectedFeatures)
            {
                try
                {
                    var geometry = feature.Geometry;
                    if (geometry == null) continue;

                    Symbol symbol = null;

                    switch (geometry.GeometryType)
                    {
                        case GeometryType.Point:
                            symbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Yellow, 12);
                            break;
                        case GeometryType.Polyline:
                            symbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Yellow, 3);
                            break;
                        case GeometryType.Polygon:
                            // 仅描边高亮，避免反向环时整图填黄遮住其他要素
                            symbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Null,
                                System.Drawing.Color.Transparent,
                                new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Yellow, 3));
                            break;
                    }

                    if (symbol != null)
                    {
                        var graphic = new Graphic(geometry, symbol);
                        _selectionOverlay.Graphics.Add(graphic);
                    }
                }
                catch
                {
                    continue;
                }
            }

            if (_attributeTableWindow != null)
                _attributeTableWindow.UpdateSelectionFromMap(_selectedFeatures);
        }

        private Feature ReloadFeatureByGeometry(Feature oldFeature)
        {
            try
            {
                var oldGeometry = oldFeature.Geometry;
                if (oldGeometry == null) return null;

                foreach (var layer in CurrentMapView.Map.OperationalLayers)
                {
                    var table = GetFeatureTableFromLayer(layer);
                    if (table == null) continue;

                    var features = table.QueryFeaturesAsync(new QueryParameters()).Result;
                    foreach (var feature in features)
                    {
                        if (GeometryEquals(feature.Geometry, oldGeometry))
                        {
                            return feature;
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private bool GeometryEquals(Geometry g1, Geometry g2)
        {
            if (g1 == null || g2 == null) return false;
            if (g1.GeometryType != g2.GeometryType) return false;

            try
            {
                var distance = GeometryEngine.Distance(g1, g2);
                return distance < 0.001;
            }
            catch
            {
                return false;
            }
        }

        private void SetDefaultRenderer(Layer layer, GeometryType geometryType)
        {
            Symbol symbol = null;
            switch (geometryType)
            {
                case GeometryType.Point:
                    symbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 8);
                    break;
                case GeometryType.Polyline:
                    symbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Blue, 2);
                    break;
                case GeometryType.Polygon:
                    symbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(100, System.Drawing.Color.Green),
                        new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.DarkGreen, 1));
                    break;
            }

            if (symbol != null)
            {
                if (layer is FeatureLayer featureLayer)
                {
                    featureLayer.Renderer = new SimpleRenderer(symbol);
                }
                else if (layer is FeatureCollectionLayer collectionLayer && collectionLayer.FeatureCollection?.Tables.Count > 0)
                {
                    var table = collectionLayer.FeatureCollection.Tables[0] as FeatureCollectionTable;
                    if (table != null)
                    {
                        table.Renderer = new SimpleRenderer(symbol);
                    }
                }
            }
        }

        private void SetTableRenderer(FeatureCollectionTable table, Symbol symbol)
        {
            if (table != null && symbol != null)
            {
                table.Renderer = new SimpleRenderer(symbol);
            }
        }

        public void ClearSelection()
        {
            _selectedFeatures.Clear();
            if (_selectionOverlay != null)
            {
                _selectionOverlay.Graphics.Clear();
            }
        }

        /// <summary>
        /// 进入顶点编辑模式
        /// </summary>
        private void StartVertexEditing(Feature feature)
        {
            _isVertexEditing = true;
            _currentEditingFeature = feature;
            _currentVertices.Clear();
            _originalVertices = new List<MapPoint>();
            _preservedHoleRings.Clear();
            _originalEditGeometry = CloneGeometry(feature.Geometry);
            _currentEditingLayerName = GetLayerNameByFeature(feature);
            _selectedVertexIndex = -1;
            _isDraggingVertex = false;
            _isAddingVertexMode = false;

            if (!_selectedFeatures.Contains(feature))
            {
                _selectedFeatures.Clear();
                _selectedFeatures.Add(feature);
            }

            if (_editPreviewOverlay != null)
            {
                _editPreviewOverlay.Graphics.Clear();
                CurrentMapView.GraphicsOverlays.Remove(_editPreviewOverlay);
                _editPreviewOverlay = null;
            }
            if (_vertexOverlay != null)
            {
                _vertexOverlay.Graphics.Clear();
            }

            var geometry = feature.Geometry;
            var geomSr = geometry?.SpatialReference
                ?? CurrentMapView.Map?.SpatialReference
                ?? SpatialReferences.WebMercator;

            switch (geometry.GeometryType)
            {
                case GeometryType.Polyline:
                    // 仅编辑第一条折线部件，避免多部件被压成一条
                    var polyline = geometry as Polyline;
                    if (polyline.Parts.Count > 0)
                    {
                        foreach (var point in polyline.Parts[0].Points)
                            _currentVertices.Add(new MapPoint(point.X, point.Y, geomSr));
                    }
                    break;
                case GeometryType.Polygon:
                    // 仅编辑外环；孔洞原样保留，保存时再拼回（防止压环导致整图反向填色）
                    var polygon = geometry as Polygon;
                    for (int pi = 0; pi < polygon.Parts.Count; pi++)
                    {
                        var points = polygon.Parts[pi].Points.ToList();
                        if (points.Count > 1)
                        {
                            var first = points[0];
                            var last = points[points.Count - 1];
                            if (Math.Abs(first.X - last.X) < 1e-9 && Math.Abs(first.Y - last.Y) < 1e-9)
                                points.RemoveAt(points.Count - 1);
                        }
                        var ring = points.Select(p => new MapPoint(p.X, p.Y, geomSr)).ToList();
                        if (pi == 0)
                            _currentVertices.AddRange(ring);
                        else
                            _preservedHoleRings.Add(ring);
                    }
                    break;
                case GeometryType.Point:
                    var pt = geometry as MapPoint;
                    if (pt != null) _currentVertices.Add(new MapPoint(pt.X, pt.Y, geomSr));
                    break;
            }

            foreach (var p in _currentVertices)
                _originalVertices.Add(new MapPoint(p.X, p.Y, p.SpatialReference ?? geomSr));

            if (_selectionOverlay != null)
                _selectionOverlay.Graphics.Clear();
            try
            {
                foreach (var layer in CurrentMapView.Map.OperationalLayers)
                {
                    if (layer is FeatureLayer fl)
                        fl.ClearSelection();
                }
            }
            catch { }

            _editPreviewOverlay = new GraphicsOverlay();
            CurrentMapView.GraphicsOverlays.Add(_editPreviewOverlay);
            UpdateEditPreview();
            ShowVertices();

            StatusBar.Text = "顶点编辑模式：拖拽修改，右键保存，Esc 取消（共 " + _currentVertices.Count + " 个顶点）";
        }

        /// <summary>
        /// 保存顶点编辑并退出
        /// </summary>
        private async System.Threading.Tasks.Task SaveVertexEditAndExit()
        {
            try
            {
                if (_currentEditingFeature == null || _currentVertices.Count == 0)
                {
                    CleanupVertexEditing();
                    return;
                }

                if (!IsLayerEditable(_currentEditingFeature))
                {
                    StatusBar.Text = "该图层已被锁定，无法保存编辑";
                    await CancelVertexEditing();
                    return;
                }

                string layerName = _currentEditingLayerName ?? GetLayerNameByFeature(_currentEditingFeature);
                var originalGeom = _originalEditGeometry ?? CloneGeometry(_currentEditingFeature.Geometry);

                // 构建新几何并投影到表坐标系
                var newGeometry = BuildGeometryFromVerticesWithoutSimplify();
                if (newGeometry == null || newGeometry.IsEmpty)
                {
                    StatusBar.Text = "几何无效，未保存（Esc 取消或继续编辑）";
                    return;
                }

                // 确保可编辑内存表，并拿到当前表上的要素引用
                FeatureTable featureTable = null;
                Feature targetFeature = _currentEditingFeature;
                if (!string.IsNullOrEmpty(layerName))
                {
                    featureTable = await EnsureEditableMemoryTableAsync(layerName);
                    if (featureTable != null)
                    {
                        targetFeature = await FindFeatureInTableAsync(featureTable, _currentEditingFeature, originalGeom)
                            ?? _currentEditingFeature;
                    }
                }
                if (featureTable == null)
                    featureTable = targetFeature?.FeatureTable;

                if (featureTable == null)
                {
                    StatusBar.Text = "无法获取要素表，保存失败";
                    return;
                }

                // 投影到表 SR
                newGeometry = ProjectGeometryToTable(newGeometry, featureTable);
                if (newGeometry == null || newGeometry.IsEmpty)
                {
                    StatusBar.Text = "坐标投影失败，未保存";
                    return;
                }

                // 安全校验：相对原始范围不能暴涨（防止反向环盖住全图）
                if (!IsGeometryEditSafe(originalGeom, newGeometry))
                {
                    StatusBar.Text = "编辑结果异常（范围过大），已拒绝保存，请 Esc 取消后重试";
                    return;
                }

                try
                {
                    var simplified = GeometryEngine.Simplify(newGeometry);
                    if (simplified != null && !simplified.IsEmpty && IsGeometryEditSafe(originalGeom, simplified))
                        newGeometry = simplified;
                }
                catch { }

                _undoStack.Push(new EditOperation
                {
                    Feature = targetFeature,
                    OriginalGeometry = CloneGeometry(originalGeom),
                    NewGeometry = CloneGeometry(newGeometry),
                    OperationType = "编辑",
                    LayerName = layerName
                });

                targetFeature.Geometry = newGeometry;
                if (featureTable.CanUpdate(targetFeature))
                {
                    await featureTable.UpdateFeatureAsync(targetFeature);
                    if (!string.IsNullOrEmpty(layerName))
                    {
                        MarkLayerHasUnsavedChanges(layerName);
                        RefreshLayerDisplay(layerName);
                    }
                    StatusBar.Text = "顶点编辑已保存";
                }
                else
                {
                    // 回滚内存中的几何
                    if (originalGeom != null)
                        targetFeature.Geometry = CloneGeometry(originalGeom);
                    StatusBar.Text = "要素不支持更新";
                }
            }
            catch (Exception ex)
            {
                StatusBar.Text = "保存失败: " + ex.Message;
                try
                {
                    if (_currentEditingFeature != null && _originalEditGeometry != null)
                        _currentEditingFeature.Geometry = CloneGeometry(_originalEditGeometry);
                }
                catch { }
            }
            finally
            {
                CleanupVertexEditing();
                ClearSelection();
            }
        }

        private async Task<Feature> FindFeatureInTableAsync(FeatureTable table, Feature hint, Geometry matchGeom)
        {
            if (table == null) return null;
            try
            {
                // 优先 OID
                if (hint != null && hint.Attributes != null)
                {
                    object oid = null;
                    foreach (var key in new[] { "OBJECTID", "FID", "OID", "ObjectId" })
                    {
                        if (hint.Attributes.ContainsKey(key) && hint.Attributes[key] != null)
                        {
                            oid = hint.Attributes[key];
                            break;
                        }
                    }
                    if (oid != null)
                    {
                        var q = new QueryParameters();
                        q.ObjectIds.Add(Convert.ToInt64(oid));
                        var found = (await table.QueryFeaturesAsync(q)).FirstOrDefault();
                        if (found != null) return found;
                    }
                }

                // 回退：几何匹配
                if (matchGeom != null)
                {
                    string gj = matchGeom.ToJson();
                    var all = await table.QueryFeaturesAsync(new QueryParameters());
                    foreach (var f in all)
                    {
                        if (f.Geometry != null && string.Equals(f.Geometry.ToJson(), gj, StringComparison.Ordinal))
                            return f;
                    }
                }
            }
            catch { }
            return hint != null && hint.FeatureTable == table ? hint : null;
        }

        private Geometry ProjectGeometryToTable(Geometry geometry, FeatureTable table)
        {
            if (geometry == null || table?.SpatialReference == null)
                return geometry;
            try
            {
                if (geometry.SpatialReference == null)
                {
                    // 无 SR 时按表 SR 重新构建点集不安全，尽量 FromJson 后 Project
                    return geometry;
                }
                if (geometry.SpatialReference.Wkid == table.SpatialReference.Wkid)
                    return geometry;
                return GeometryEngine.Project(geometry, table.SpatialReference);
            }
            catch
            {
                return geometry;
            }
        }

        private bool IsGeometryEditSafe(Geometry original, Geometry edited)
        {
            if (edited == null || edited.IsEmpty) return false;
            try
            {
                var ee = edited.Extent;
                if (ee == null || ee.IsEmpty) return false;
                if (double.IsNaN(ee.XMin) || double.IsInfinity(ee.XMin) ||
                    double.IsNaN(ee.YMin) || double.IsInfinity(ee.YMin))
                    return false;

                if (original != null && !original.IsEmpty && original.Extent != null && !original.Extent.IsEmpty)
                {
                    var oe = original.Extent;
                    double ow = Math.Max(oe.Width, 1e-12);
                    double oh = Math.Max(oe.Height, 1e-12);
                    // 编辑后范围膨胀超过 5 倍 → 视为反向环/坐标系错误
                    if (ee.Width > ow * 5 || ee.Height > oh * 5)
                        return false;

                    double ocx = (oe.XMin + oe.XMax) / 2.0;
                    double ocy = (oe.YMin + oe.YMax) / 2.0;
                    double ncx = (ee.XMin + ee.XMax) / 2.0;
                    double ncy = (ee.YMin + ee.YMax) / 2.0;
                    if (Math.Abs(ncx - ocx) > ow * 3 || Math.Abs(ncy - ocy) > oh * 3)
                        return false;

                    if (edited is Polygon && original is Polygon)
                    {
                        double oArea = Math.Abs(GeometryEngine.Area(original));
                        double nArea = Math.Abs(GeometryEngine.Area(edited));
                        if (oArea > 1e-12 && nArea > oArea * 20)
                            return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 取消顶点编辑（恢复原始几何）并退出
        /// </summary>
        private async System.Threading.Tasks.Task CancelVertexEditing()
        {
            // 编辑中会原地改 Feature.Geometry 做实时预览，取消时必须恢复原始几何
            try
            {
                if (_currentEditingFeature != null && _originalEditGeometry != null)
                {
                    try
                    {
                        _currentEditingFeature.Geometry = CloneGeometry(_originalEditGeometry);
                        // 未点保存前不写库；内存表显示立即恢复即可
                    }
                    catch { }
                }
            }
            catch { }

            string layerName = _currentEditingLayerName;
            CleanupVertexEditing();
            if (!string.IsNullOrEmpty(layerName))
                RefreshLayerDisplay(layerName);
            ClearSelection();
            StatusBar.Text = "顶点编辑已取消";
            await System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// 清理顶点编辑状态
        /// </summary>
        private void CleanupVertexEditing()
        {
            _isVertexEditing = false;
            _currentEditingFeature = null;
            _currentVertices.Clear();
            _originalVertices = null;
            _originalEditGeometry = null;
            _preservedHoleRings.Clear();
            _currentEditingLayerName = null;
            _selectedVertexIndex = -1;
            _isDraggingVertex = false;
            _isAddingVertexMode = false;
            _vertexDragStartPosition = null;
            try { CurrentMapView.ReleaseMouseCapture(); } catch { }

            if (_vertexOverlay != null)
            {
                _vertexOverlay.Graphics.Clear();
                CurrentMapView.GraphicsOverlays.Remove(_vertexOverlay);
                _vertexOverlay = null;
            }
            if (_editPreviewOverlay != null)
            {
                _editPreviewOverlay.Graphics.Clear();
                CurrentMapView.GraphicsOverlays.Remove(_editPreviewOverlay);
                _editPreviewOverlay = null;
            }
        }

        /// <summary>
        /// 退出顶点编辑模式（已废弃，使用 CancelVertexEditing 或 SaveVertexEditAndExit）
        /// </summary>
        private void StopVertexEditing()
        {
            CancelVertexEditing();
        }

        /// <summary>
        /// 刷新顶点显示
        /// </summary>
        private void ShowVertices()
        {
            if (_vertexOverlay == null)
            {
                _vertexOverlay = new GraphicsOverlay();
                CurrentMapView.GraphicsOverlays.Add(_vertexOverlay);
            }

            _vertexOverlay.Graphics.Clear();

            for (int i = 0; i < _currentVertices.Count; i++)
            {
                bool isSelected = (i == _selectedVertexIndex);
                var color = isSelected ? System.Drawing.Color.Orange : System.Drawing.Color.White;
                var outlineColor = isSelected ? System.Drawing.Color.Red : System.Drawing.Color.Black;
                int size = isSelected ? 16 : 12;

                var symbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, color, size)
                {
                    Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outlineColor, 2)
                };
                var graphic = new Graphic(_currentVertices[i], symbol);
                graphic.Attributes["VertexIndex"] = i;
                _vertexOverlay.Graphics.Add(graphic);
            }
        }

        /// <summary>
        /// 更新编辑预览：原地替换要素几何 + 刷新黄色选中边框（不再叠旧几何）
        /// </summary>
        private void UpdateEditPreview()
        {
            if (_currentEditingFeature == null || _currentVertices.Count == 0)
                return;

            var newGeometry = BuildGeometryFromVerticesWithoutSimplify();
            if (newGeometry == null || newGeometry.IsEmpty) return;

            try
            {
                var ext = newGeometry.Extent;
                if (ext == null || ext.IsEmpty ||
                    double.IsNaN(ext.XMin) || double.IsInfinity(ext.XMin))
                    return;

                if (_originalEditGeometry != null && !IsGeometryEditSafe(_originalEditGeometry, newGeometry))
                    return;
            }
            catch { return; }

            // 1) 原地更新要素几何（替换旧几何，避免新旧叠加）
            try
            {
                var table = _currentEditingFeature.FeatureTable;
                var projected = ProjectGeometryToTable(newGeometry, table) ?? newGeometry;
                _currentEditingFeature.Geometry = projected;
                newGeometry = projected;
            }
            catch { }

            // 2) 青色编辑预览描边
            if (_editPreviewOverlay != null)
            {
                _editPreviewOverlay.Graphics.Clear();
                Symbol previewSymbol;
                if (newGeometry is Polyline)
                    previewSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Cyan, 2);
                else if (newGeometry is Polygon)
                    previewSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Null,
                        System.Drawing.Color.Transparent,
                        new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Cyan, 2));
                else
                    previewSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Cyan, 14);
                _editPreviewOverlay.Graphics.Add(new Graphic(newGeometry, previewSymbol));
            }

            // 3) 黄色选中边框跟随最新边界
            RefreshVertexEditSelectionHighlight(newGeometry);
        }

        private void RefreshVertexEditSelectionHighlight(Geometry geometry)
        {
            if (geometry == null || geometry.IsEmpty) return;
            if (_selectionOverlay == null)
            {
                _selectionOverlay = new GraphicsOverlay();
                CurrentMapView.GraphicsOverlays.Add(_selectionOverlay);
            }
            _selectionOverlay.Graphics.Clear();

            Symbol symbol = null;
            switch (geometry.GeometryType)
            {
                case GeometryType.Point:
                    symbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Yellow, 12);
                    break;
                case GeometryType.Polyline:
                    symbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Yellow, 3);
                    break;
                case GeometryType.Polygon:
                    symbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Null,
                        System.Drawing.Color.Transparent,
                        new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Yellow, 3));
                    break;
            }
            if (symbol != null)
                _selectionOverlay.Graphics.Add(new Graphic(geometry, symbol));
        }

        /// <summary>
        /// 从当前顶点构建几何
        /// </summary>
        private Geometry BuildGeometryFromVertices()
        {
            var result = BuildGeometryFromVerticesWithoutSimplify();
            if (result == null || result.IsEmpty)
                return null;

            try
            {
                // Simplify 修正环方向；若结果异常则退回未 Simplify 版本
                var simplified = GeometryEngine.Simplify(result);
                if (simplified == null || simplified.IsEmpty)
                    return result;

                var se = simplified.Extent;
                var re = result.Extent;
                if (se != null && re != null && !se.IsEmpty && !re.IsEmpty)
                {
                    // Simplify 后范围暴涨 → 可能是反向环，丢弃 Simplify 结果
                    if (se.Width > re.Width * 3 || se.Height > re.Height * 3)
                        return result;
                }
                return simplified;
            }
            catch
            {
                return result;
            }
        }

        private static MapPoint EnsurePointSr(MapPoint pt, SpatialReference sr)
        {
            if (pt == null) return new MapPoint(0, 0, sr);
            if (pt.SpatialReference == null)
                return new MapPoint(pt.X, pt.Y, sr);
            if (pt.SpatialReference.Wkid == sr.Wkid || ReferenceEquals(pt.SpatialReference, sr))
                return new MapPoint(pt.X, pt.Y, sr);
            try
            {
                return (MapPoint)GeometryEngine.Project(pt, sr);
            }
            catch
            {
                return new MapPoint(pt.X, pt.Y, sr);
            }
        }

        private async System.Threading.Tasks.Task<Feature> IdentifyFeatureAtPointAsync(Point screenPoint)
        {
            try
            {
                var tolerance = 12;
                var identifyResult = await CurrentMapView.IdentifyLayersAsync(screenPoint, tolerance, false);
                foreach (var layerResult in identifyResult)
                {
                    var feature = FindFeatureInIdentifyResult(layerResult);
                    if (feature != null)
                        return feature;
                }

                // 未保存的绘制图形
                if (_drawingOverlay != null)
                {
                    var graphicsResult = await CurrentMapView.IdentifyGraphicsOverlaysAsync(screenPoint, tolerance, false);
                    foreach (var overlayResult in graphicsResult)
                    {
                        if (overlayResult.Graphics.Count > 0)
                        {
                            var feature = CreateFeatureFromGraphic(overlayResult.Graphics[0]);
                            if (feature != null)
                                return feature;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusBar.Text = "识别要素失败: " + ex.Message;
            }
            return null;
        }

        private Feature FindFeatureInIdentifyResult(IdentifyLayerResult layerResult)
        {
            if (layerResult == null) return null;

            foreach (var geoElement in layerResult.GeoElements)
            {
                if (geoElement is Feature feature)
                    return feature;
            }

            // FeatureCollectionLayer 等结果在子图层中
            if (layerResult.SublayerResults != null)
            {
                foreach (var sub in layerResult.SublayerResults)
                {
                    var feature = FindFeatureInIdentifyResult(sub);
                    if (feature != null)
                        return feature;
                }
            }

            return null;
        }

        // ---------- 编辑要素：点击要素进入顶点编辑 ----------
        private async void StartEditFeatureAtPointAsync(Point screenPoint)
        {
            try
            {
                var feature = await IdentifyFeatureAtPointAsync(screenPoint);
                if (feature == null)
                {
                    StatusBar.Text = "未识别到要素，请点击要素进行编辑";
                    return;
                }

                if (!IsLayerEditable(feature))
                {
                    StatusBar.Text = "该图层已锁定，无法编辑，请在图层管理器中开启编辑模式";
                    return;
                }

                StartVertexEditing(feature);
            }
            catch (Exception ex)
            {
                MessageBox.Show("进入编辑模式失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- 顶点拖拽：尝试选中并开始拖拽 ----------
        private async void TryStartVertexDragAsync(Point screenPoint, MapPoint mapPoint)
        {
            if (_vertexOverlay == null || _currentVertices.Count == 0)
                return;

            try
            {
                var tolerance = 10;
                var identifyResult = await CurrentMapView.IdentifyGraphicsOverlayAsync(_vertexOverlay, screenPoint, tolerance, false);
                if (identifyResult.Graphics.Count > 0 && identifyResult.Graphics[0].Attributes.ContainsKey("VertexIndex"))
                {
                    _selectedVertexIndex = (int)identifyResult.Graphics[0].Attributes["VertexIndex"];
                    _isDraggingVertex = true;
                    _vertexDragStartPosition = _selectedVertexIndex >= 0 && _selectedVertexIndex < _currentVertices.Count
                        ? _currentVertices[_selectedVertexIndex] : null;
                    CurrentMapView.CaptureMouse();
                    ShowVertices();
                    StatusBar.Text = "已选中顶点 " + (_selectedVertexIndex + 1) + "，拖拽到目标位置后释放鼠标";
                }
                else
                {
                    _selectedVertexIndex = -1;
                    ShowVertices();
                    StatusBar.Text = "已取消选中顶点";
                }
            }
            catch (Exception ex)
            {
                StatusBar.Text = "识别顶点失败: " + ex.Message;
            }
        }

        private async void DeleteFeatureAtPointAsync(Point screenPoint)
        {
            try
            {
                var feature = await IdentifyFeatureAtPointAsync(screenPoint);
                if (feature == null)
                {
                    StatusBar.Text = "未识别到要素，请点击要删除的要素";
                    return;
                }

                if (!IsLayerEditable(feature))
                {
                    StatusBar.Text = "该图层已锁定，无法删除，请在图层管理器中开启编辑模式";
                    return;
                }

                var graphic = FindGraphicForFeature(feature);
                bool isUnsavedGraphic = graphic != null && _unsavedGraphics.Contains(graphic);

                if (!isUnsavedGraphic)
                {
                    var featureTable = feature.FeatureTable;
                    if (featureTable == null || !featureTable.CanDelete(feature))
                    {
                        StatusBar.Text = "该要素不支持删除";
                        return;
                    }
                }

                var result = MessageBox.Show("确定要删除该要素吗？", "确认删除",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    StatusBar.Text = "已取消删除";
                    return;
                }

                string deleteLayerName = GetLayerNameByFeature(feature);
                _undoStack.Push(new EditOperation
                {
                    Feature = feature,
                    OriginalGeometry = CloneGeometry(feature.Geometry),
                    OperationType = "删除",
                    LayerName = deleteLayerName,
                    DeletedGraphic = isUnsavedGraphic ? graphic : null,
                    DeletedAttributes = feature.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value),
                    DeletedSnapshots = new List<FeatureSnapshot> { CreateFeatureSnapshot(feature) }
                });

                if (isUnsavedGraphic)
                {
                    if (_drawingOverlay != null)
                        _drawingOverlay.Graphics.Remove(graphic);
                    _unsavedGraphics.Remove(graphic);
                }
                else
                {
                    var featureTable = feature.FeatureTable;
                    if (featureTable != null)
                    {
                        await featureTable.DeleteFeatureAsync(feature);

                        if (!string.IsNullOrEmpty(deleteLayerName))
                        {
                            MarkLayerHasUnsavedChanges(deleteLayerName);
                            RefreshLayerDisplay(deleteLayerName);
                        }
                    }
                }

                // 清理可能引用此要素的选中状态
                _selectedFeatures.RemoveAll(f => 
                {
                    try
                    {
                        if (f.Attributes.ContainsKey("FID") && feature.Attributes.ContainsKey("FID"))
                        {
                            return (int)f.Attributes["FID"] == (int)feature.Attributes["FID"];
                        }
                        return f == feature;
                    }
                    catch
                    {
                        return f == feature;
                    }
                });
                ShowSelectionHighlights();
                UpdateAttributeTable();

                StatusBar.Text = "要素已删除";
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除要素失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- 移动要素：第一次点击选中要素 ----------
        private async void StartMoveFeatureAtPointAsync(Point screenPoint)
        {
            try
            {
                var feature = await IdentifyFeatureAtPointAsync(screenPoint);
                if (feature == null)
                {
                    StatusBar.Text = "未识别到要素，请点击要移动的要素";
                    return;
                }

                if (!IsLayerEditable(feature))
                {
                    StatusBar.Text = "该图层已锁定，无法移动，请在图层管理器中开启编辑模式";
                    return;
                }

                var featureTable = feature.FeatureTable;
                if (featureTable == null || !featureTable.CanEditGeometry() || !featureTable.CanUpdate(feature))
                {
                    StatusBar.Text = "该要素不支持移动";
                    return;
                }

                _movePendingFeature = feature;
                var mapPoint = CurrentMapView.ScreenToLocation(screenPoint);
                
                var sr = feature.Geometry.SpatialReference;
                _moveAnchorPoint = (MapPoint)GeometryEngine.Project(mapPoint, sr);

                if (!_selectedFeatures.Contains(feature))
                {
                    _selectedFeatures.Clear();
                    _selectedFeatures.Add(feature);
                }

                ShowMoveSelectionHighlight(feature);
                StatusBar.Text = "已选中要素，移动鼠标查看预览，再次点击目标位置完成移动，按 Esc 取消";
            }
            catch (Exception ex)
            {
                MessageBox.Show("选择移动要素失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- 移动要素：显示选中高亮 ----------
        private void ShowMoveSelectionHighlight(Feature feature)
        {
            if (_movePreviewOverlay == null)
            {
                _movePreviewOverlay = new GraphicsOverlay();
                CurrentMapView.GraphicsOverlays.Add(_movePreviewOverlay);
            }
            _movePreviewOverlay.Graphics.Clear();

            var geometry = feature.Geometry;
            Symbol symbol = null;
            switch (geometry.GeometryType)
            {
                case GeometryType.Point:
                    symbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Orange, 14);
                    break;
                case GeometryType.Polyline:
                    symbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Orange, 4);
                    break;
                case GeometryType.Polygon:
                    symbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(40, System.Drawing.Color.Orange),
                        new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Orange, 3));
                    break;
            }
            if (symbol != null)
            {
                _movePreviewGraphic = new Graphic(geometry, symbol);
                _movePreviewOverlay.Graphics.Add(_movePreviewGraphic);
            }
        }

        // ---------- 移动要素：更新移动预览 ----------
        private void UpdateMovePreview(MapPoint currentMousePoint)
        {
            if (_movePendingFeature == null || _moveAnchorPoint == null || _movePreviewOverlay == null)
                return;

            var sr = _movePendingFeature.Geometry.SpatialReference;
            var projectedMousePoint = (MapPoint)GeometryEngine.Project(currentMousePoint, sr);
            var projectedAnchorPoint = (MapPoint)GeometryEngine.Project(_moveAnchorPoint, sr);

            var dx = projectedMousePoint.X - projectedAnchorPoint.X;
            var dy = projectedMousePoint.Y - projectedAnchorPoint.Y;

            if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
                return;

            var previewGeometry = MoveGeometry(_movePendingFeature.Geometry, dx, dy);
            if (previewGeometry == null)
                return;

            // 替换预览图形的几何
            if (_movePreviewGraphic != null)
                _movePreviewOverlay.Graphics.Remove(_movePreviewGraphic);

            Symbol symbol = null;
            switch (previewGeometry.GeometryType)
            {
                case GeometryType.Point:
                    symbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Orange, 14);
                    break;
                case GeometryType.Polyline:
                    symbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Dash, System.Drawing.Color.Orange, 3);
                    break;
                case GeometryType.Polygon:
                    symbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(60, System.Drawing.Color.Orange),
                        new SimpleLineSymbol(SimpleLineSymbolStyle.Dash, System.Drawing.Color.Orange, 2));
                    break;
            }
            if (symbol != null)
            {
                _movePreviewGraphic = new Graphic(previewGeometry, symbol);
                _movePreviewOverlay.Graphics.Add(_movePreviewGraphic);
            }
        }

        // ---------- 移动要素：第二次点击完成移动 ----------
        private async void CompleteMoveFeatureAsync(MapPoint destinationPoint)
        {
            if (_movePendingFeature == null || _moveAnchorPoint == null)
                return;

            try
            {
                string layerName = GetLayerNameByFeature(_movePendingFeature);
                var originalGeom = CloneGeometry(_movePendingFeature.Geometry);
                var sr = originalGeom?.SpatialReference ?? CurrentMapView.Map?.SpatialReference;
                if (sr == null)
                {
                    StatusBar.Text = "无法确定坐标系";
                    CancelMoveFeature();
                    return;
                }

                var projectedDestPoint = destinationPoint.SpatialReference != null && destinationPoint.SpatialReference.Wkid != sr.Wkid
                    ? (MapPoint)GeometryEngine.Project(destinationPoint, sr)
                    : new MapPoint(destinationPoint.X, destinationPoint.Y, sr);
                var projectedAnchorPoint = _moveAnchorPoint.SpatialReference != null && _moveAnchorPoint.SpatialReference.Wkid != sr.Wkid
                    ? (MapPoint)GeometryEngine.Project(_moveAnchorPoint, sr)
                    : new MapPoint(_moveAnchorPoint.X, _moveAnchorPoint.Y, sr);

                var dx = projectedDestPoint.X - projectedAnchorPoint.X;
                var dy = projectedDestPoint.Y - projectedAnchorPoint.Y;
                var newGeometry = MoveGeometry(originalGeom, dx, dy);

                if (newGeometry == null || !IsGeometryEditSafe(originalGeom, newGeometry))
                {
                    StatusBar.Text = "无法移动该要素或结果异常";
                    CancelMoveFeature();
                    return;
                }

                FeatureTable featureTable = null;
                Feature target = _movePendingFeature;
                if (!string.IsNullOrEmpty(layerName))
                {
                    featureTable = await EnsureEditableMemoryTableAsync(layerName);
                    if (featureTable != null)
                    {
                        target = await FindFeatureInTableAsync(featureTable, _movePendingFeature, originalGeom);
                        // 回退：逐个比较普通属性来找回新表中的对应要素
                        if (target == null)
                        {
                            try
                            {
                                var hintAttrs = _movePendingFeature.Attributes;
                                var all = await featureTable.QueryFeaturesAsync(new QueryParameters());
                                foreach (var f in all)
                                {
                                    bool match = true;
                                    foreach (var key in hintAttrs.Keys)
                                    {
                                        if (string.Equals(key, "OBJECTID", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(key, "FID", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(key, "OID", StringComparison.OrdinalIgnoreCase))
                                            continue;
                                        object hv = hintAttrs[key];
                                        object fv = f.Attributes.ContainsKey(key) ? f.Attributes[key] : null;
                                        if (hv == null || hv == DBNull.Value)
                                        {
                                            if (fv != null && fv != DBNull.Value) { match = false; break; }
                                        }
                                        else if (!hv.Equals(fv)) { match = false; break; }
                                    }
                                    if (match && f.Geometry != null && originalGeom != null)
                                    {
                                        string gj1 = f.Geometry.ToJson();
                                        string gj2 = originalGeom.ToJson();
                                        if (string.Equals(gj1, gj2, StringComparison.Ordinal))
                                        {
                                            target = f; break;
                                        }
                                    }
                                }
                            }
                            catch { }
                            target = target ?? _movePendingFeature;
                        }
                    }
                }
                if (featureTable == null)
                    featureTable = target?.FeatureTable;

                newGeometry = ProjectGeometryToTable(newGeometry, featureTable);

                _undoStack.Push(new EditOperation
                {
                    Feature = target,
                    OriginalGeometry = originalGeom,
                    NewGeometry = CloneGeometry(newGeometry),
                    OperationType = "移动",
                    LayerName = layerName
                });

                target.Geometry = newGeometry;
                if (featureTable != null && featureTable.CanUpdate(target))
                {
                    await featureTable.UpdateFeatureAsync(target);

                    // 清理旧几何残留（FeatureCollectionTable.UpdateFeatureAsync 在某些版本可能创建新要素而非更新）
                    try
                    {
                        string oldGj = originalGeom?.ToJson();
                        if (!string.IsNullOrEmpty(oldGj))
                        {
                            var all = await featureTable.QueryFeaturesAsync(new QueryParameters());
                            foreach (var f in all)
                            {
                                if (f != target && f.Geometry != null &&
                                    string.Equals(f.Geometry.ToJson(), oldGj, StringComparison.Ordinal))
                                {
                                    await featureTable.DeleteFeatureAsync(f);
                                }
                            }
                        }
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(layerName))
                    {
                        MarkLayerHasUnsavedChanges(layerName);
                        RefreshLayerDisplay(layerName);
                    }
                    // 同步另一个视图，避免旧几何残留
                    SyncMapToOtherView(CurrentMapView);
                    // 更新选中引用，避免旧要素的 Geometry 过时
                    if (target != _movePendingFeature)
                    {
                        for (int i = 0; i < _selectedFeatures.Count; i++)
                        {
                            if (_selectedFeatures[i] == _movePendingFeature)
                                _selectedFeatures[i] = target;
                        }
                    }
                    StatusBar.Text = "要素已移动到新位置";
                }
                else
                {
                    target.Geometry = originalGeom;
                    StatusBar.Text = "要素不支持更新";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("移动要素失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CancelMoveFeature();
                ShowSelectionHighlights();
            }
        }

        // ---------- 取消移动要素 ----------
        private void CancelMoveFeature()
        {
            _movePendingFeature = null;
            _moveAnchorPoint = null;
            _movePreviewGraphic = null;

            if (_movePreviewOverlay != null)
            {
                _movePreviewOverlay.Graphics.Clear();
            }
            if (currentDrawTool == "MoveFeature")
            {
                StatusBar.Text = "移动要素已取消，点击要素选中，再次点击目标位置完成移动";
            }
        }

        // ---------- 几何平移辅助方法 ----------
        private Geometry MoveGeometry(Geometry geometry, double dx, double dy)
        {
            if (geometry == null) return null;
            var sr = geometry.SpatialReference;

            switch (geometry.GeometryType)
            {
                case GeometryType.Point:
                    var p = geometry as MapPoint;
                    if (p.HasZ)
                        return new MapPoint(p.X + dx, p.Y + dy, p.Z, sr);
                    return new MapPoint(p.X + dx, p.Y + dy, sr);
                case GeometryType.Polyline:
                    {
                        var lineBuilder = new PolylineBuilder(sr);
                        foreach (var part in (geometry as Polyline).Parts)
                        {
                            var newPart = new Part(sr);
                            foreach (var pt in part.Points)
                                newPart.AddPoint(new MapPoint(pt.X + dx, pt.Y + dy, sr));
                            lineBuilder.Parts.Add(newPart);
                        }
                        return lineBuilder.ToGeometry();
                    }
                case GeometryType.Polygon:
                    {
                        // 每个环独立 Part，禁止压成单环（否则孔洞外环合并→反向填色盖全图）
                        var polyBuilder = new PolygonBuilder(sr);
                        foreach (var part in (geometry as Polygon).Parts)
                        {
                            var newPart = new Part(sr);
                            foreach (var pt in part.Points)
                                newPart.AddPoint(new MapPoint(pt.X + dx, pt.Y + dy, sr));
                            polyBuilder.Parts.Add(newPart);
                        }
                        return polyBuilder.ToGeometry();
                    }
                default:
                    return null;
            }
        }

        // ---------- 其余原有代码（未修改） ----------
        private async void HandleCoordinateInputForCurrentTool(bool clearAfter)
        {
            var dialog = new CoordinateInputDialog();
            if (dialog.ShowDialog() == true && dialog.ResultPoint != null)
            {
                var point = dialog.ResultPoint;
                if (point == null || double.IsNaN(point.X) || double.IsNaN(point.Y)) return;
                var mapSr = CurrentMapView.SpatialReference;
                if (mapSr != null && point.SpatialReference != null && point.SpatialReference.Wkid != mapSr.Wkid)
                {
                    point = (MapPoint)GeometryEngine.Project(point, mapSr);
                    if (point == null || double.IsNaN(point.X) || double.IsNaN(point.Y)) return;
                }
                else if (mapSr != null && point.SpatialReference == null)
                {
                    point = new MapPoint(point.X, point.Y, mapSr);
                }
                else if (mapSr == null && point.SpatialReference == null)
                {
                    var wgs84 = SpatialReference.Create(4326);
                    point = new MapPoint(point.X, point.Y, wgs84);
                }

                switch (currentDrawTool)
                {
                    case "Point":
                        // 精确坐标：立即创建点要素 + 跳转到该位置（无需再点“完成”）
                        await DrawPointAsync(point);
                        _pendingPoint = null;
                        _trackingPoint = point;
                        UpdateCrosshair();
                        double scale = CurrentMapView.MapScale > 0 ? CurrentMapView.MapScale : 5000;
                        try { await CurrentMapView.SetViewpointCenterAsync(point, scale); }
                        catch { try { await CurrentMapView.SetViewpointAsync(new Viewpoint(point, scale)); } catch { } }
                        StatusBar.Text = "点已按坐标创建并定位，移动鼠标可继续绘制";
                        UpdateDrawingToolbar();
                        break;
                    case "Line":
                        AddLineVertex(point);
                        UpdateDrawingToolbar();
                        try { await CurrentMapView.SetViewpointCenterAsync(point, CurrentMapView.MapScale > 0 ? CurrentMapView.MapScale : 5000); } catch { }
                        break;
                    case "Polygon":
                        AddPolygonVertex(point);
                        UpdateDrawingToolbar();
                        try { await CurrentMapView.SetViewpointCenterAsync(point, CurrentMapView.MapScale > 0 ? CurrentMapView.MapScale : 5000); } catch { }
                        break;
                    default:
                        DrawPoint(point);
                        _ = CurrentMapView.SetViewpointCenterAsync(point, 1000);
                        break;
                }
            }
        }

        // ---------- 捕捉功能 ----------
        private async void UpdateSnapping(Point screenPoint)
        {
            try
            {
                var result = await FindNearestSnapPointAsync(screenPoint);
                _nearestSnapPoint = result;
            }
            catch
            {
                _nearestSnapPoint = null;
            }
        }

        private async Task<MapPoint> FindNearestSnapPointAsync(Point screenPoint)
        {
            if (!_snappingEnabled || CurrentMapView?.Map == null)
                return null;

            try
            {
                var identifyResults = await CurrentMapView.IdentifyLayersAsync(screenPoint, SnapTolerancePixels, false);

                MapPoint nearestPoint = null;
                double nearestDist = double.MaxValue;
                bool isVertex = true;
                var mapSr = CurrentMapView.SpatialReference;

                foreach (var result in identifyResults)
                {
                    foreach (var element in result.GeoElements)
                    {
                        if (element is Feature feature && feature.Geometry != null)
                        {
                            var geo = feature.Geometry;
                            var mouseMapPoint = CurrentMapView.ScreenToLocation(screenPoint);
                            if (mouseMapPoint == null) continue;

                            var featureSr = geo.SpatialReference;
                            MapPoint projectedMouse = mouseMapPoint;
                            if (featureSr != null && mouseMapPoint.SpatialReference != featureSr)
                            {
                                projectedMouse = (MapPoint)GeometryEngine.Project(mouseMapPoint, featureSr);
                            }

                            var points = ExtractPointsFromGeometry(geo);
                            foreach (var pt in points)
                            {
                                double d = GeometryEngine.Distance(projectedMouse, pt);
                                if (d < nearestDist)
                                {
                                    nearestDist = d;
                                    nearestPoint = pt;
                                    isVertex = true;
                                }
                            }

                            if (geo is Polyline || geo is Polygon)
                            {
                                var closest = GeometryEngine.NearestCoordinate(geo, projectedMouse);
                                if (closest != null)
                                {
                                    double d = GeometryEngine.Distance(projectedMouse, closest.Coordinate);
                                    if (d < nearestDist)
                                    {
                                        nearestDist = d;
                                        nearestPoint = new MapPoint(closest.Coordinate.X, closest.Coordinate.Y, featureSr);
                                        isVertex = false;
                                    }
                                }
                            }
                        }
                    }
                }

                if (nearestPoint == null)
                    return null;

                double mapUnitTolerance = 50;
                if (nearestPoint.SpatialReference != null && nearestPoint.SpatialReference.IsGeographic)
                    mapUnitTolerance = 0.001;

                if (nearestDist >= mapUnitTolerance)
                {
                    ClearSnapIndicator();
                    return null;
                }

                _isSnappingToVertex = isVertex;

                // 转回地图空间参考，确保与 click 得到的 mapPoint 空间参考一致
                if (mapSr != null && nearestPoint.SpatialReference != mapSr)
                {
                    nearestPoint = (MapPoint)GeometryEngine.Project(nearestPoint, mapSr);
                }

                ShowSnapIndicator(nearestPoint, isVertex);
                return nearestPoint;
            }
            catch { }

            ClearSnapIndicator();
            return null;
        }

        private List<MapPoint> ExtractPointsFromGeometry(Geometry geo)
        {
            var points = new List<MapPoint>();
            try
            {
                if (geo is MapPoint pt)
                    points.Add(pt);
                else if (geo is Multipoint multipoint)
                    points.AddRange(multipoint.Points);
                else if (geo is Polyline polyline)
                {
                    foreach (var part in polyline.Parts)
                        points.AddRange(part.Points);
                }
                else if (geo is Polygon polygon)
                {
                    foreach (var part in polygon.Parts)
                        points.AddRange(part.Points);
                }
            }
            catch { }
            return points;
        }

        private void ShowSnapIndicator(MapPoint point, bool isVertex)
        {
            if (_snappingOverlay == null) return;
            _snappingOverlay.Graphics.Clear();
            var symbol = isVertex ? _snapVertexSymbol : _snapEdgeSymbol;
            _snappingOverlay.Graphics.Add(new Graphic(point, symbol));
        }

        private void ClearSnapIndicator()
        {
            _snappingOverlay?.Graphics.Clear();
            _nearestSnapPoint = null;
        }

        // ---------- 方位角/距离输入 ----------
        private async Task ShowAzimuthDistanceDialogAsync()
        {
            var dialog = new Window
            {
                Title = "方位角/距离输入",
                Width = 300,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x21, 0x26, 0x2D))
            };

            var panel = new StackPanel { Margin = new Thickness(12) };

            panel.Children.Add(new TextBlock { Text = "方位角（度，正北顺时针）：", Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(0, 0, 0, 4) });
            var txtAzimuth = new TextBox { Text = "0", Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(txtAzimuth);

            panel.Children.Add(new TextBlock { Text = "距离（地图单位）：", Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(0, 0, 0, 4) });
            var txtDistance = new TextBox { Text = "100", Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(txtDistance);

            var btnPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var btnOk = new Button { Content = "确定", Width = 80, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            var btnCancel = new Button { Content = "取消", Width = 80, Height = 32 };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            panel.Children.Add(btnPanel);

            dialog.Content = panel;

            btnOk.Click += (s, e) => dialog.DialogResult = true;
            btnCancel.Click += (s, e) => dialog.DialogResult = false;

            if (dialog.ShowDialog() == true)
            {
                if (!double.TryParse(txtAzimuth.Text, out double azimuth) ||
                    !double.TryParse(txtDistance.Text, out double distance))
                {
                    MessageBox.Show("请输入有效的数值", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MapPoint lastPoint = null;
                if (currentDrawTool == "Line" && _lineVertices.Count > 0)
                    lastPoint = _lineVertices[_lineVertices.Count - 1];
                else if (currentDrawTool == "Polygon" && _polygonVertices.Count > 0)
                    lastPoint = _polygonVertices[_polygonVertices.Count - 1];
                else if (currentDrawTool == "Point" && _pendingPoint != null)
                    lastPoint = _pendingPoint;

                if (lastPoint != null)
                {
                    MapPoint calcPoint = lastPoint;
                    SpatialReference calcSr = lastPoint.SpatialReference;
                    // 地理坐标系下无法直接用 dx/dy 计算，转为 Web Mercator
                    bool needProjectBack = false;
                    if (calcSr != null && calcSr.IsGeographic)
                    {
                        calcPoint = (MapPoint)GeometryEngine.Project(lastPoint, SpatialReference.Create(3857));
                        calcSr = calcPoint.SpatialReference;
                        needProjectBack = true;
                    }

                    double azimuthRad = azimuth * Math.PI / 180.0;
                    double dx = distance * Math.Sin(azimuthRad);
                    double dy = distance * Math.Cos(azimuthRad);

                    var newCalcPoint = new MapPoint(calcPoint.X + dx, calcPoint.Y + dy, calcSr);

                    MapPoint newPoint;
                    if (needProjectBack)
                        newPoint = (MapPoint)GeometryEngine.Project(newCalcPoint, lastPoint.SpatialReference);
                    else
                        newPoint = newCalcPoint;
                    if (newPoint == null || double.IsNaN(newPoint.X) || double.IsNaN(newPoint.Y)) return;

                    switch (currentDrawTool)
                    {
                        case "Line":
                            AddLineVertex(newPoint);
                            break;
                        case "Polygon":
                            AddPolygonVertex(newPoint);
                            break;
                        case "Point":
                            _pendingPoint = newPoint;
                            UpdateCrosshair();
                            _ = CurrentMapView.SetViewpointCenterAsync(newPoint, 1000);
                            StatusBar.Text = "已标记点位置，右键输入属性或取消";
                            break;
                    }
                    UpdateDrawingToolbar();
                }
            }
        }

        private string FormatCoordinate(MapPoint point)
        {
            try
            {
                MapPoint wgs84Point = point;
                if (point.SpatialReference.Wkid != 4326)
                {
                    wgs84Point = (MapPoint)GeometryEngine.Project(point, SpatialReference.Create(4326));
                }
                return string.Format("{0:F6}, {1:F6}", wgs84Point.X, wgs84Point.Y);
            }
            catch
            {
                return string.Format("{0:F2}, {1:F2}", point.X, point.Y);
            }
        }

        private bool IsLayerEditable(Feature feature)
        {
            var layerName = GetLayerNameByFeature(feature);
            if (!string.IsNullOrEmpty(layerName))
            {
                var layerItem = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
                return layerItem != null && layerItem.IsEditable;
            }
            return false;
        }

        public void MarkLayerHasUnsavedChanges(string layerName)
        {
            var layerItem = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
            if (layerItem != null)
            {
                layerItem.HasUnsavedChanges = true;
            }
        }

        private string GetLayerNameByFeature(Feature feature)
        {
            foreach (var layer in CurrentMapView.Map.OperationalLayers)
            {
                var table = GetFeatureTableFromLayer(layer);
                if (table != null && table == feature.FeatureTable)
                {
                    return layer.Name;
                }
            }
            return string.Empty;
        }

        private void SubscribeToLayerManager()
        {
            LayerManager.Layers.CollectionChanged += (s, e) => UpdateDrawToolEnabledByData();

            LayerManager.LayerRemoved += (layerName) =>
            {
                RemoveLayerFromMap(layerName);
            };

            LayerManager.LayerVisibilityChanged += (layerName, isVisible) =>
            {
                SetLayerVisibility(layerName, isVisible);
            };

            LayerManager.LayerSelected += (layerName) =>
            {
                ZoomToLayer(layerName);
                if (_attributeTableWindow != null && _attributeTableWindow.IsVisible)
                {
                    _attributeTableWindow.RefreshTable();
                }
            };

            LayerManager.LayerEditRequested += (layerName, isEditable, filePath) =>
            {
                if (isEditable)
                {
                    EnterLayerEditMode(layerName, filePath);
                }
                else
                {
                    ExitLayerEditMode();
                    if (_isVertexEditing && _currentEditingFeature != null)
                    {
                        var featureLayer = CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == layerName) as FeatureLayer;
                        if (featureLayer != null && featureLayer.FeatureTable == _currentEditingFeature.FeatureTable)
                        {
                            CancelVertexEditing();
                            StatusBar.Text = "图层已锁定，已取消当前编辑";
                        }
                    }
                }
            };

            LayerManager.NewLayerRequested += () =>
            {
                CreateNewFeatureLayer();
            };

            LayerManager.SaveLayerRequested += async (layerNameOrPath) =>
            {
                await SaveLayerToShapefile(layerNameOrPath);
            };

            LayerManager.ZoomToLayerRequested += (layerName) =>
            {
                ZoomToLayer(layerName);
            };

            LayerManager.ShowAttributeTableRequested += (layerName) =>
            {
                ShowAttributeTable(layerName);
            };

            LayerManager.BasemapChanged += (tag) =>
            {
                SwitchBasemap(tag);
            };

            LayerManager.LayerExportRequested += async (layerName, format) =>
            {
                await ExportLayer(layerName, format);
            };

            LayerManager.LayerMoveRequested += (layerName, moveUp) =>
            {
                var map = CurrentMapView?.Map;
                if (map == null) return;
                var layer = map.OperationalLayers.FirstOrDefault(l => l.Name == layerName);
                if (layer == null) return;
                int idx = map.OperationalLayers.IndexOf(layer);
                if (moveUp && idx > 0)
                {
                    map.OperationalLayers.RemoveAt(idx);
                    map.OperationalLayers.Insert(idx - 1, layer);
                }
                else if (!moveUp && idx < map.OperationalLayers.Count - 1)
                {
                    map.OperationalLayers.RemoveAt(idx);
                    map.OperationalLayers.Insert(idx + 1, layer);
                }
            };
        }

        private void SwitchBasemap(string tag)
        {
            if (CurrentMapView?.Map == null) return;
            try
            {
                BasemapStyle style;
                switch (tag)
                {
                    case "LightGray": style = BasemapStyle.ArcGISLightGray; break;
                    case "Streets": style = BasemapStyle.ArcGISStreets; break;
                    case "Topographic": style = BasemapStyle.ArcGISTopographic; break;
                    case "DarkGray": style = BasemapStyle.ArcGISDarkGray; break;
                    case "Oceans": style = BasemapStyle.ArcGISOceans; break;
                    default: style = BasemapStyle.ArcGISImagery; break;
                }
                CurrentMapView.Map.Basemap = new Basemap(style);
                SyncMapToOtherView(CurrentMapView);
                StatusBar.Text = $"底图已切换: {tag}";
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"底图切换失败: {ex.Message}";
            }
        }

        private async void InitializeMap()
        {
            try
            {
                var map1 = new Map(BasemapStyle.ArcGISImagery);
                var map2 = new Map();
                await System.Threading.Tasks.Task.WhenAll(map1.LoadAsync(), Task.CompletedTask);
                MapView1.Map = map1;
                MapView2.Map = map2;
                await EnsureAnchorLayer(map2);
                MapPoint center = new MapPoint(114.3, 30.6, SpatialReferences.Wgs84);
                await MapView1.SetViewpointCenterAsync(center, 50000);
                SubscribeToLayerManager();
            }
            catch (Exception ex)
            {
                try
                {
                    var map1 = new Map(BasemapStyle.ArcGISTopographic);
                    var map2 = new Map();
                    await System.Threading.Tasks.Task.WhenAll(map1.LoadAsync(), Task.CompletedTask);
                    MapView1.Map = map1;
                    MapView2.Map = map2;
                    await EnsureAnchorLayer(map2);
                    MapPoint center = new MapPoint(114.3, 30.6, SpatialReferences.Wgs84);
                    await MapView1.SetViewpointCenterAsync(center, 50000);
                    SubscribeToLayerManager();
                    MessageBox.Show($"地图加载失败，已切换到地形图: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex2)
                {
                    var map1 = new Map(BasemapStyle.ArcGISLightGray);
                    var map2 = new Map();
                    MapView1.Map = map1;
                    MapView2.Map = map2;
                    await EnsureAnchorLayer(map2);
                    MapPoint center = new MapPoint(114.3, 30.6, SpatialReferences.Wgs84);
                    await MapView1.SetViewpointCenterAsync(center, 50000);
                    SubscribeToLayerManager();
                    MessageBox.Show($"底图加载失败，已切换到灰度底图: {ex2.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            MapView1.IsEnabled = true;
            MapView2.IsEnabled = true;

            MapView1.MouseMove += OnMapMouseMove;
            MapView2.MouseMove += OnMapMouseMove;
        }

        private bool _isSyncingLayers = false;
        private const string AnchorLayerName = "__SR_ANCHOR__";
        private Layer _anchorLayer;

        /// <summary>为 Map 添加不可见的锚点图层以设定 SpatialReference，确保后续图层可正常共存</summary>
        private async System.Threading.Tasks.Task EnsureAnchorLayer(Map map)
        {
            if (map == null || map.SpatialReference != null) return;
            if (map.OperationalLayers.Any(l => l.Name == AnchorLayerName)) return;

            var table = new Esri.ArcGISRuntime.Data.FeatureCollectionTable(
                new System.Collections.Generic.List<Esri.ArcGISRuntime.Data.Field>(),
                Esri.ArcGISRuntime.Geometry.GeometryType.Point,
                SpatialReferences.WebMercator);
            var fc = new FeatureCollection();
            fc.Tables.Add(table);
            var layer = new FeatureCollectionLayer(fc);
            layer.Name = AnchorLayerName;
            layer.IsVisible = false;
            map.OperationalLayers.Add(layer);
            try { await layer.LoadAsync(); _anchorLayer = layer; }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("锚点图层加载失败: " + ex.Message, "锚点图层错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            if (map.SpatialReference == null)
            {
                System.Windows.MessageBox.Show("警告: 数据视图 Map 无 SpatialReference，图层可能无法正常叠加显示。", "坐标系错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        // ----- 无参考影像 WPF 覆盖层（规避 ArcGIS Runtime 100.x 无法渲染 JPG/PNG 世界文件的限制） -----
        private void SetImageOverlay(string filePath, Envelope extent, string layerName = null)
        {
            BitmapSource overlayBmp = null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(System.IO.Path.GetFullPath(filePath));
                bmp.EndInit();
                bmp.Freeze();
                overlayBmp = bmp;
            }
            catch { }
            if (overlayBmp == null)
            {
                try
                {
                    using (var img = System.Drawing.Image.FromFile(filePath))
                    using (var ms = new System.IO.MemoryStream())
                    {
                        img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Position = 0;
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        overlayBmp = bmp;
                    }
                }
                catch { }
            }
            if (overlayBmp == null) return;

            _overlayExtent = extent;
            _overlayOriginalExtent = new Envelope(extent.XMin, extent.YMin, extent.XMax, extent.YMax, extent.SpatialReference);
            _hasOverlay = true;
            _overlayLayerName = layerName;

            ApplyOverlayToView(MapView2, UnreferencedImageOverlay, overlayBmp);
            ApplyOverlayToView(MapView1, MapView1ImageOverlay, overlayBmp);
        }

        private void ApplyOverlayToView(MapView mapView, System.Windows.Controls.Image overlay, BitmapSource bitmap)
        {
            if (mapView == null || overlay == null) return;
            overlay.Source = bitmap;
            overlay.Width = bitmap.PixelWidth;
            overlay.Height = bitmap.PixelHeight;
            overlay.Margin = new System.Windows.Thickness(0);
            overlay.Visibility = System.Windows.Visibility.Visible;
            try { UpdateOverlayPosition(mapView, overlay); } catch { }
        }

        private void ClearImageOverlay()
        {
            _hasOverlay = false;
            _overlayLayerName = null;
            UnreferencedImageOverlay.Visibility = System.Windows.Visibility.Collapsed;
            UnreferencedImageOverlay.Source = null;
            if (MapView1ImageOverlay != null)
            {
                MapView1ImageOverlay.Visibility = System.Windows.Visibility.Collapsed;
                MapView1ImageOverlay.Source = null;
            }
            _overlayExtent = null;
        }

        private void UpdateImageOverlayPosition()
        {
            if (!_hasOverlay) return;
            try { UpdateOverlayPosition(MapView2, UnreferencedImageOverlay); } catch { }
            try { UpdateOverlayPosition(MapView1, MapView1ImageOverlay); } catch { }
        }

        private bool IsMapViewVisible(MapView mapView)
        {
            if (mapView == null) return false;
            if (mapView == MapView2 && MapTabControl?.SelectedIndex == 1) return true;
            if (mapView == MapView1 && MapTabControl?.SelectedIndex == 0) return true;
            return false;
        }

        private void UpdateOverlayPosition(MapView mapView, System.Windows.Controls.Image overlay)
        {
            if (!_hasOverlay || mapView == null || overlay == null) return;
            if (!IsMapViewVisible(mapView)) return;
            try
            {
                bool positioned = false;
                if (_georefAffineParams != null && _georefPixelWidth > 0 && _georefPixelHeight > 0)
                {
                    double pw = _georefPixelWidth;
                    double ph = _georefPixelHeight;
                    double half = 0.5;
                    var sr = _georefMapSr ?? _overlayExtent?.SpatialReference ?? SpatialReferences.WebMercator;

                    GeoreferenceHelper.TransformAffine(_georefAffineParams, -half, -half, out double mx00, out double my00);
                    GeoreferenceHelper.TransformAffine(_georefAffineParams, pw - half, -half, out double mxW0, out double myW0);
                    GeoreferenceHelper.TransformAffine(_georefAffineParams, -half, ph - half, out double mx0H, out double my0H);
                    GeoreferenceHelper.TransformAffine(_georefAffineParams, pw - half, ph - half, out double mxWH, out double myWH);

                    var sp00 = mapView.LocationToScreen(new MapPoint(mx00, my00, sr));
                    var spW0 = mapView.LocationToScreen(new MapPoint(mxW0, myW0, sr));
                    var sp0H = mapView.LocationToScreen(new MapPoint(mx0H, my0H, sr));
                    var spWH = mapView.LocationToScreen(new MapPoint(mxWH, myWH, sr));

                    if (!double.IsNaN(sp00.X + sp00.Y + spW0.X + spW0.Y + sp0H.X + sp0H.Y + spWH.X + spWH.Y))
                    {
                        double minX = Math.Min(Math.Min(sp00.X, spW0.X), Math.Min(sp0H.X, spWH.X));
                        double minY = Math.Min(Math.Min(sp00.Y, spW0.Y), Math.Min(sp0H.Y, spWH.Y));
                        double maxX = Math.Max(Math.Max(sp00.X, spW0.X), Math.Max(sp0H.X, spWH.X));
                        double maxY = Math.Max(Math.Max(sp00.Y, spW0.Y), Math.Max(sp0H.Y, spWH.Y));
                        double dispW = maxX - minX;
                        double dispH = maxY - minY;
                        if (dispW > 0 && dispH > 0)
                        {
                            overlay.Margin = new System.Windows.Thickness(minX, minY, 0, 0);
                            overlay.Width = dispW;
                            overlay.Height = dispH;

                            double ux = (spW0.X - sp00.X) / dispW;
                            double uy = (spW0.Y - sp00.Y) / dispW;
                            double vx = (sp0H.X - sp00.X) / dispH;
                            double vy = (sp0H.Y - sp00.Y) / dispH;

                            if (ux > 0 && vy > 0)
                            {
                                var matrix = new System.Windows.Media.Matrix(
                                    ux, uy, vx, vy,
                                    sp00.X - minX, sp00.Y - minY);
                                overlay.RenderTransform = new System.Windows.Media.MatrixTransform(matrix);
                                overlay.RenderTransformOrigin = new System.Windows.Point(0, 0);
                                positioned = true;
                            }
                        }
                    }
                }

                if (!positioned && _overlayExtent != null)
                {
                    var tl = mapView.LocationToScreen(
                        new MapPoint(_overlayExtent.XMin, _overlayExtent.YMax, _overlayExtent.SpatialReference));
                    var br = mapView.LocationToScreen(
                        new MapPoint(_overlayExtent.XMax, _overlayExtent.YMin, _overlayExtent.SpatialReference));

                    if (!double.IsNaN(tl.X) && !double.IsNaN(tl.Y) && !double.IsNaN(br.X) && !double.IsNaN(br.Y))
                    {
                        double x = tl.X;
                        double y = tl.Y;
                        double w = br.X - x;
                        double h = br.Y - y;
                        if (w > 0 && h > 0)
                        {
                            overlay.Margin = new System.Windows.Thickness(x, y, 0, 0);
                            overlay.Width = w;
                            overlay.Height = h;
                            overlay.RenderTransform = null;
                            positioned = true;
                        }
                    }
                }
            }
            catch { }
        }

        private async void SyncMapToOtherView(MapView sourceView)
        {
            if (_isSyncingLayers) return;
            _isSyncingLayers = true;
            try
            {
                var targetView = sourceView == MapView1 ? MapView2 : MapView1;
                var sourceMap = sourceView.Map;
                var targetMap = targetView.Map;
                if (sourceMap == null || targetMap == null) return;

                targetMap.OperationalLayers.Clear();
                foreach (var srcLayer in sourceMap.OperationalLayers)
                {
                    if (srcLayer.Name == AnchorLayerName) continue;
                    Layer newLayer = null;
                    try
                    {
                        if (srcLayer is FeatureLayer fl && fl.FeatureTable is ShapefileFeatureTable sft)
                        {
                            string path = sft.Path;
                            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                            {
                                var newTable = new ShapefileFeatureTable(path);
                                await newTable.LoadAsync();
                                var nfl = new FeatureLayer(newTable) { Name = fl.Name, IsVisible = fl.IsVisible, Opacity = fl.Opacity };
                                await nfl.LoadAsync();
                                if (fl.Renderer != null) nfl.Renderer = fl.Renderer;
                                newLayer = nfl;
                            }
                        }
                        else if (srcLayer is FeatureLayer fl2 && fl2.FeatureTable is GeodatabaseFeatureTable gft)
                        {
                            string path = gft.Geodatabase?.Path;
                            string tableName = gft.TableName;
                            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                            {
                                var gdb = await Geodatabase.OpenAsync(path);
                                var tables = gdb.GeodatabaseFeatureTables;
                                foreach (var t in tables)
                                {
                                    if (t.TableName == tableName)
                                    {
                                        await t.LoadAsync();
                                        var nfl2 = new FeatureLayer(t) { Name = fl2.Name, IsVisible = fl2.IsVisible, Opacity = fl2.Opacity };
                                        await nfl2.LoadAsync();
                                        if (fl2.Renderer != null) nfl2.Renderer = fl2.Renderer;
                                        newLayer = nfl2;
                                        break;
                                    }
                                }
                            }
                        }
                        else if (srcLayer is FeatureCollectionLayer fcl && fcl.FeatureCollection?.Tables.Count > 0)
                        {
                            var srcTable = fcl.FeatureCollection.Tables[0];
                            var fields = srcTable.Fields.ToList();
                            var geomType = srcTable.GeometryType;
                            var sr = srcTable.SpatialReference;
                            var snapshots = new List<FeatureSnapshot>();
                            var features = await srcTable.QueryFeaturesAsync(new QueryParameters());
                            foreach (var f in features)
                            {
                                var attrs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                                foreach (var kv in f.Attributes)
                                {
                                    if (kv.Value != null && kv.Value != DBNull.Value)
                                        attrs[kv.Key] = kv.Value;
                                }
                                snapshots.Add(new FeatureSnapshot
                                {
                                    Geometry = f.Geometry != null ? Geometry.FromJson(f.Geometry.ToJson()) : null,
                                    Attributes = attrs
                                });
                            }
                            var memTable = await BuildMemoryTableFromSnapshotsAsync(fields, geomType, sr, snapshots);
                            // 图层属性对话框把符号写在子 FeatureLayer 上，需一并保留
                            var existingSymbol = TryGetLayerSymbol(fcl);
                            newLayer = await CreateFeatureCollectionLayerAsync(memTable, srcLayer.Name, existingSymbol);
                            newLayer.Name = srcLayer.Name;
                            newLayer.IsVisible = srcLayer.IsVisible;
                            newLayer.Opacity = srcLayer.Opacity;
                        }
                        else if (srcLayer is RasterLayer rl)
                        {
                            string path = rl.Raster?.Path;
                            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                            {
                                var raster = new Raster(path);
                                await raster.LoadAsync();
                                newLayer = new RasterLayer(raster);
                                newLayer.Name = rl.Name;
                                newLayer.IsVisible = rl.IsVisible;
                                newLayer.Opacity = rl.Opacity;
                            }
                        }
                    }
                    catch { }

                    if (newLayer != null)
                        targetMap.OperationalLayers.Add(newLayer);
                }

                if (targetView == MapView2 && targetMap.SpatialReference == null)
                    await EnsureAnchorLayer(targetMap);
            }
            catch { }
            finally { _isSyncingLayers = false; }
        }

        private void OnMapMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                var view = CurrentMapView;
                if (sender != view) return;
                Point screenPoint = e.GetPosition(view);
                MapPoint mapPoint = view.ScreenToLocation(screenPoint);
                if (mapPoint == null) return;

                MapPoint wgs84Point;
                if (mapPoint.SpatialReference != null &&
                    mapPoint.SpatialReference.Wkid != SpatialReferences.Wgs84.Wkid)
                {
                    wgs84Point = (MapPoint)GeometryEngine.Project(mapPoint, SpatialReferences.Wgs84);
                }
                else if (mapPoint.SpatialReference == null)
                {
                    wgs84Point = (MapPoint)GeometryEngine.Project(mapPoint, SpatialReferences.Wgs84);
                }
                else
                {
                    wgs84Point = mapPoint;
                }

                string text = $"经度: {wgs84Point.X:F6}, 纬度: {wgs84Point.Y:F6}";
                if (view == MapView1)
                    TxtCoordinate.Text = text;
                else
                    TxtCoordinate2.Text = text;
            }
            catch { }
        }

        private void SwitchModule(RadioButton selectedButton)
        {
            BtnDataEdit.IsChecked = selectedButton == BtnDataEdit;
            BtnMapCompile.IsChecked = selectedButton == BtnMapCompile;
            BtnFieldData.IsChecked = selectedButton == BtnFieldData;

            PanelDataEdit.Visibility = selectedButton == BtnDataEdit ? Visibility.Visible : Visibility.Collapsed;
            PanelFieldData.Visibility = selectedButton == BtnFieldData ? Visibility.Visible : Visibility.Collapsed;
            PanelTopologyCheck.Visibility = Visibility.Collapsed;

            if (selectedButton == BtnDataEdit)
                ModuleTitle.Text = "数据编辑";
            else if (selectedButton == BtnMapCompile)
                ModuleTitle.Text = "图件编绘";
            else if (selectedButton == BtnFieldData)
                ModuleTitle.Text = "野外数据";

            RightPanelBorder.Visibility = Visibility.Visible;
            RightColumn.Width = new GridLength(360);
        }

        private void HideRightPanel()
        {
            RightPanelBorder.Visibility = Visibility.Collapsed;
            RightColumn.Width = new GridLength(0);
        }

        private void BtnDataEdit_Click(object sender, RoutedEventArgs e) => SwitchModule(BtnDataEdit);
        private void BtnMapCompile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mapCompileWindow == null || !_mapCompileWindow.IsVisible)
                {
                    _mapCompileWindow = new MapCompileWindow(this);
                    _mapCompileWindow.Closed += (s, args) => _mapCompileWindow = null;
                    _mapCompileWindow.Show();
                }
                else
                {
                    _mapCompileWindow.Activate();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开图件编绘窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void BtnFieldData_Click(object sender, RoutedEventArgs e) => SwitchModule(BtnFieldData);
        private void BtnLayerManager_Click(object sender, RoutedEventArgs e)
        {
            if (LayerManagerColumn.Width.Value == 0)
                LayerManagerColumn.Width = new GridLength(280);
            else
                LayerManagerColumn.Width = new GridLength(0);
        }

        private async System.Threading.Tasks.Task ExportLayer(string layerName, string format)
        {
            var layerItem = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
            if (layerItem?.Layer == null)
            {
                System.Windows.MessageBox.Show("未找到图层", "导出", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var layer = layerItem.Layer;
            string filter = "";
            string defaultExt = "";

            switch (format)
            {
                case "shp":
                    filter = "Shapefile (*.shp)|*.shp";
                    defaultExt = ".shp";
                    break;
                case "gpkg":
                    filter = "GeoPackage (*.gpkg)|*.gpkg|Shapefile (*.shp)|*.shp|GeoJSON (*.geojson)|*.geojson";
                    defaultExt = ".shp";
                    break;
                case "tif":
                    filter = "GeoTIFF (*.tif)|*.tif";
                    defaultExt = ".tif";
                    break;
                default:
                    filter = "所有文件 (*.*)|*.*";
                    defaultExt = ".dat";
                    break;
            }

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"导出图层: {layerName}",
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = SanitizeFileName(layerName)
            };

            if (sfd.ShowDialog() != true) return;

            var path = sfd.FileName;
            StatusBar.Text = $"正在导出图层 '{layerName}'...";

            try
            {
                if (format == "tif")
                {
                    ExportHelper.ExportRaster(layer, path);
                }
                else if (format == "shp" || format == "gpkg")
                {
                    await ExportHelper.ExportToShapefile(layer, System.IO.Path.ChangeExtension(path, null));
                }
                else
                {
                    await ExportHelper.ExportToGeoJson(layer, path);
                }

                System.Windows.MessageBox.Show($"图层 '{layerName}' 已导出到:\n{path}", "导出成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                StatusBar.Text = $"图层 '{layerName}' 导出完成";
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"导出失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusBar.Text = $"导出失败: {ex.Message}";
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private void BtnCloseLayerManager_Click(object sender, RoutedEventArgs e)
        {
            LayerManagerColumn.Width = new GridLength(0);
        }

        private async void BtnVersionManager_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppLog.Write($"[版本管理器] 打开，项目已打开={projectManager.IsProjectOpen}, 路径={projectManager.ProjectPath}");
                if (_versionManager == null)
                {
                    _versionManager = new VersionManagerWindow(this);
                    _versionManager.SaveVersionRequested += async (snap) => await CaptureVersionAsync(snap);
                    _versionManager.RollbackRequested += async (snap) => await RollbackToVersionAsync(snap);
                }

                var versions = projectManager.LoadVersions();
                _versionManager.LoadVersions(versions);
                _versionManager.Owner = this;
                _versionManager.Show();
                _versionManager.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开版本管理器失败: {ex.Message}\n\n请确认 ArcGIS Runtime 已正确初始化（许可）。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task CaptureVersionAsync(VersionSnapshot snap)
        {
            try
            {
                AppLog.Write($"[Capture] 开始快照「{snap.Name}」，图层数={LayerManager.Layers.Count}");
                foreach (var layerItem in LayerManager.Layers)
                {
                    try
                    {
                        var table = GetFeatureTableFromLayer(layerItem.Layer) ?? layerItem.MemoryFeatureTable;
                        if (table == null) continue;
                        if (table.GeometryType == GeometryType.Unknown) continue;

                        var features = await table.QueryFeaturesAsync(new QueryParameters());
                        var lvs = new LayerVersionState
                        {
                            LayerName = layerItem.Name,
                            LayerType = "vector",
                            FilePath = layerItem.FilePath ?? "",
                            Wkid = table.SpatialReference?.Wkid ?? 0,
                            GeometryTypeName = table.GeometryType.ToString()
                        };

                    foreach (var field in table.Fields)
                    {
                        if (field.FieldType == FieldType.OID || field.FieldType == FieldType.Geometry) continue;
                        lvs.Fields.Add(new FieldDef
                        {
                            Name = field.Name,
                            FieldType = field.FieldType.ToString(),
                            ArcGisFieldType = field.FieldType,
                            Length = field.Length
                        });
                    }

                    foreach (var feature in features)
                    {
                        var fs = new FeatureState
                        {
                            GeometryJson = feature.Geometry?.ToJson() ?? ""
                        };
                        foreach (var kv in feature.Attributes)
                        {
                            if (kv.Key != null &&
                                !kv.Key.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase) &&
                                !kv.Key.Equals("FID", StringComparison.OrdinalIgnoreCase) &&
                                !kv.Key.Equals("OID", StringComparison.OrdinalIgnoreCase) &&
                                kv.Value != null && kv.Value != DBNull.Value)
                            {
                                fs.Attributes[kv.Key] = kv.Value is ICloneable c ? c.Clone() : kv.Value;
                            }
                        }
                        lvs.Features.Add(fs);
                    }
                    snap.LayerStates.Add(lvs);
                    }
                    catch (Exception layerEx)
                    {
                        AppLog.WriteException($"[Capture] 图层「{layerItem.Name}」快照失败，已跳过", layerEx);
                        snap.LayerStates.Add(new LayerVersionState
                        {
                            LayerName = layerItem.Name,
                            LayerType = "vector",
                            FilePath = layerItem.FilePath ?? ""
                        });
                    }
                }

                foreach (var layerItem in LayerManager.Layers)
                {
                    if (layerItem.Layer is RasterLayer)
                    {
                        snap.LayerStates.Add(new LayerVersionState
                        {
                            LayerName = layerItem.Name,
                            LayerType = "raster",
                            FilePath = layerItem.FilePath ?? ""
                        });
                    }
                }

                var versions = projectManager.LoadVersions();
                versions.Add(snap);
                projectManager.SaveVersions(versions);
                _versionManager.LoadVersions(versions);
                StatusBar.Text = $"版本已保存: {snap.Name}";
                AppLog.Write($"[Capture] 「{snap.Name}」保存完成，当前共 {versions.Count} 个版本");
            }
            catch (Exception ex)
            {
                AppLog.WriteException($"[Capture] 「{snap.Name}」保存整体失败", ex);
                System.Windows.MessageBox.Show($"保存版本失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private async System.Threading.Tasks.Task RollbackToVersionAsync(VersionSnapshot target)
        {
            try
            {
                StatusBar.Text = $"正在回滚到版本「{target.Name}」...";

                var currentSnap = new VersionSnapshot
                {
                    Name = $"自动备份_{DateTime.Now:yyyyMMdd-HHmmss}",
                    Creator = Environment.UserName,
                    Description = "回滚前自动备份"
                };
                await CaptureVersionAsync(currentSnap);

                foreach (var lvs in target.LayerStates)
                {
                    if (lvs.LayerType != "vector" || lvs.Features.Count == 0) continue;

                    var layerItem = LayerManager.Layers.FirstOrDefault(l => l.Name == lvs.LayerName);
                    if (layerItem == null) continue;

                    var table = GetFeatureTableFromLayer(layerItem.Layer) ?? layerItem.MemoryFeatureTable;
                    if (table == null || !(table is FeatureCollectionTable)) continue;

                    var existing = await table.QueryFeaturesAsync(new QueryParameters());
                    foreach (var f in existing)
                        await table.DeleteFeatureAsync(f);

                    var snaps = lvs.Features.Select(fs => new FeatureSnapshot
                    {
                        Geometry = !string.IsNullOrEmpty(fs.GeometryJson) ? Geometry.FromJson(fs.GeometryJson) : null,
                        Attributes = new Dictionary<string, object>(fs.Attributes, StringComparer.OrdinalIgnoreCase)
                    }).ToList();

                    var fields = lvs.Fields.Select(fd => new Field(fd.ArcGisFieldType, fd.Name, fd.Name, fd.Length > 0 ? fd.Length : 255, null, true, true)).ToList();
                    var geomType = (GeometryType)Enum.Parse(typeof(GeometryType), lvs.GeometryTypeName);
                    var sr = lvs.Wkid > 0 ? SpatialReference.Create(lvs.Wkid) : null;

                    var newTable = await BuildMemoryTableFromSnapshotsAsync(fields, geomType, sr, snaps);
                    await ReplaceLayerWithMemoryTableAsync(lvs.LayerName, newTable, layerItem);
                }

                StatusBar.Text = $"已回滚到版本「{target.Name}」";
                System.Windows.MessageBox.Show($"回滚完成！\n已回滚到版本: {target.Name}\n当前状态已自动备份。",
                    "版本回滚", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"回滚失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void BtnBackModule_Click(object sender, RoutedEventArgs e)
        {
            // 从拓扑检查/其他子面板返回数据编辑主面板
            SwitchModule(BtnDataEdit);
            BtnBackModule.Visibility = Visibility.Collapsed;
        }

        private void BtnCloseModulePanel_Click(object sender, RoutedEventArgs e)
        {
            RightPanelBorder.Visibility = Visibility.Collapsed;
            RightColumn.Width = new GridLength(0);
            BtnDataEdit.IsChecked = false;
            BtnMapCompile.IsChecked = false;
            BtnFieldData.IsChecked = false;
        }

        private void BtnNewProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
                folderDialog.Description = "选择项目保存位置";

                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string savePath = folderDialog.SelectedPath;
                    string projectName = $"Project_{DateTime.Now:yyyyMMdd_HHmmss}";

                    bool success = projectManager.CreateNewProject(projectName, savePath);
                    if (success)
                    {
                        Title = $"地质填图桌面系统 - {projectName}";
                        MessageBox.Show($"项目创建成功！\n位置: {projectManager.ProjectPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"新建项目失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog();
                openFileDialog.Title = "打开项目文件";
                openFileDialog.Filter = "地质填图项目 (*.gmp)|*.gmp|所有文件 (*.*)|*.*";
                openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                if (openFileDialog.ShowDialog() == true)
                {
                    bool success = projectManager.OpenProject(openFileDialog.FileName);
                    if (success)
                    {
                        Title = $"地质填图桌面系统 - {projectManager.ProjectName}";
                        // 恢复空间校准控制点
                        var savedCp = projectManager.LoadControlPoints();
                        if (savedCp != null && savedCp.Count > 0 && _georefToolbar != null)
                        {
                            var items = savedCp.Select(cp => new GeoreferenceToolbarWindow.ControlPointItem
                            {
                                SourceX = cp.SourceX, SourceY = cp.SourceY,
                                TargetX = cp.TargetX, TargetY = cp.TargetY,
                                Col = cp.Col, Row = cp.Row,
                                HasTarget = true
                            }).ToList();
                            _georefToolbar.LoadControlPoints(items);
                            // 尝试选择校准图层
                            string savedLayer = projectManager.LoadGeorefLayerName();
                            if (!string.IsNullOrEmpty(savedLayer))
                            {
                                _georefToolbar.SetRasterLayers(_georefToolbar.RasterLayerNames, savedLayer);
                            }
                            GeorefRedrawLinks();
                            GeorefUpdateResidualsAndPreview();
                        }
                        MessageBox.Show($"项目打开成功！\n位置: {projectManager.ProjectPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开项目失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!projectManager.IsProjectOpen)
                {
                    MessageBox.Show("请先创建或打开一个项目", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 持久化空间校准控制点
                string georefLayer = _isGeorefMode ? _georefLayerName : null;
                List<ProjectManager.ControlPointData> cpList = null;
                if (_georefToolbar != null && _georefToolbar.ControlPoints.Count > 0)
                {
                    cpList = _georefToolbar.ControlPoints
                        .Where(p => p.HasTarget)
                        .Select(p => new ProjectManager.ControlPointData
                        {
                            SourceX = p.SourceX, SourceY = p.SourceY,
                            TargetX = p.TargetX, TargetY = p.TargetY,
                            Col = p.Col, Row = p.Row
                        }).ToList();
                }
                projectManager.SaveProjectFile(georefLayer, cpList);

                MessageBox.Show($"项目已保存到: {projectManager.ProjectPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存项目失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnAddData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CurrentMapView == null || CurrentMapView.Map == null)
                {
                    MessageBox.Show("地图视图未初始化，请等待地图加载完成", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var openFileDialog = new OpenFileDialog();
                openFileDialog.Title = "添加数据";
                openFileDialog.Multiselect = true;
                openFileDialog.Filter = "所有支持格式|*.shp;*.tif;*.tiff;*.jpg;*.jpeg;*.png;*.bmp;*.geodatabase|" +
                                        "矢量数据|*.shp|" +
                                        "栅格数据|*.tif;*.tiff;*.jpg;*.jpeg;*.png;*.bmp|" +
                                        "地理数据库|*.geodatabase|" +
                                        "Shapefile (*.shp)|*.shp|" +
                                        "TIFF (*.tif, *.tiff)|*.tif;*.tiff|" +
                                        "图片 (*.jpg, *.jpeg, *.png, *.bmp)|*.jpg;*.jpeg;*.png;*.bmp|" +
                                        "Mobile Geodatabase (*.geodatabase)|*.geodatabase|" +
                                        "所有文件 (*.*)|*.*";

                if (openFileDialog.ShowDialog() != true)
                    return;

                int successCount = 0;
                int failCount = 0;
                List<string> failedFiles = new List<string>();

                foreach (string filePath in openFileDialog.FileNames)
                {
                    string layerType = projectManager.GetLayerType(filePath);
                    string fileName = Path.GetFileNameWithoutExtension(filePath);

                    try
                    {
                        switch (layerType)
                        {
                            case "vector":
                                await AddVectorLayer(filePath, fileName);
                                successCount++;
                                break;
                            case "raster":
                                await AddRasterLayer(filePath, fileName);
                                successCount++;
                                break;
                            case "geodatabase":
                                await AddGeodatabaseLayer(filePath, fileName);
                                successCount++;
                                break;
                            default:
                                MessageBox.Show("不支持的文件格式: " + filePath, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                                failCount++;
                                failedFiles.Add(filePath);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        failedFiles.Add(fileName + ": " + ex.Message);
                        MessageBox.Show("加载失败 (" + fileName + "): " + ex.Message, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                string message = "添加完成！\n成功: " + successCount + " 个\n失败: " + failCount + " 个";
                if (failCount > 0)
                {
                    message += "\n\n失败列表:\n" + string.Join("\n", failedFiles);
                }
                MessageBox.Show(message, "完成", MessageBoxButton.OK, successCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("添加数据失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task AddVectorLayer(string filePath, string layerName)
        {
            try
            {
                if (CurrentMapView == null || CurrentMapView.Map == null)
                    throw new Exception("地图视图未初始化");

                if (!File.Exists(filePath))
                    throw new FileNotFoundException("文件不存在: " + filePath);

                string ext = Path.GetExtension(filePath).ToLower();

                if (ext != ".shp")
                    throw new Exception("不支持的文件格式: " + ext);

                var shapefileTable = new ShapefileFeatureTable(filePath);
                await shapefileTable.LoadAsync();

                if (shapefileTable.LoadStatus != LoadStatus.Loaded)
                {
                    string errorMsg = shapefileTable.LoadError != null ? shapefileTable.LoadError.Message : "未知错误";
                    throw new Exception("Shapefile 加载失败，状态: " + shapefileTable.LoadStatus + "，错误: " + errorMsg);
                }

                var featureLayer = new FeatureLayer(shapefileTable);
                featureLayer.Name = layerName;

                if (shapefileTable.GeometryType == GeometryType.Point)
                {
                    var renderer = new SimpleRenderer(new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 8));
                    featureLayer.Renderer = renderer;
                }
                else if (shapefileTable.GeometryType == GeometryType.Polyline)
                {
                    var renderer = new SimpleRenderer(new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Blue, 2));
                    featureLayer.Renderer = renderer;
                }
                else if (shapefileTable.GeometryType == GeometryType.Polygon)
                {
                    var renderer = new SimpleRenderer(new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(100, System.Drawing.Color.Green),
                        new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.DarkGreen, 1)));
                    featureLayer.Renderer = renderer;
                }

                bool isEditable = false;

                CurrentMapView.Map.OperationalLayers.Add(featureLayer);
                SyncMapToOtherView(CurrentMapView);
                LayerManager.AddLayer(featureLayer, isEditable, filePath);

                await featureLayer.LoadAsync();
                
                if (featureLayer.FullExtent != null && !featureLayer.FullExtent.IsEmpty)
                {
                    await CurrentMapView.SetViewpointGeometryAsync(featureLayer.FullExtent);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("矢量图层加载失败 (" + layerName + "): " + ex.Message);
            }
        }

        private bool IsShapefileEditable(string shpFilePath)
        {
            try
            {
                if (!File.Exists(shpFilePath))
                    return false;

                FileInfo fileInfo = new FileInfo(shpFilePath);
                if ((fileInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void GetRasterPixelSize(string filePath, out double pixelW, out double pixelH)
        {
            pixelW = 1000;
            pixelH = 1000;
            try
            {
                using (var img = System.Drawing.Image.FromFile(filePath))
                {
                    pixelW = Math.Max(1, img.Width);
                    pixelH = Math.Max(1, img.Height);
                }
            }
            catch { }
        }

        private static Envelope ComputeWorldFileEnvelope(GeoreferenceHelper.AffineParams a, double pixelW, double pixelH, SpatialReference sr)
        {
            double half = 0.5;
            GeoreferenceHelper.TransformAffine(a, -half, -half, out double x00, out double y00);
            GeoreferenceHelper.TransformAffine(a, pixelW - half, -half, out double xW0, out double yW0);
            GeoreferenceHelper.TransformAffine(a, -half, pixelH - half, out double x0H, out double y0H);
            GeoreferenceHelper.TransformAffine(a, pixelW - half, pixelH - half, out double xWH, out double yWH);
            double xMin = Math.Min(Math.Min(x00, xW0), Math.Min(x0H, xWH));
            double xMax = Math.Max(Math.Max(x00, xW0), Math.Max(x0H, xWH));
            double yMin = Math.Min(Math.Min(y00, yW0), Math.Min(y0H, yWH));
            double yMax = Math.Max(Math.Max(y00, yW0), Math.Max(y0H, yWH));
            return new Envelope(xMin, yMin, xMax, yMax, sr ?? SpatialReferences.WebMercator);
        }

        /// <summary>判断 FullExtent 是否像像素坐标（无有效地理范围时常见）</summary>
        private static bool IsPixelLikeExtent(Envelope ext, double pixelW, double pixelH)
        {
            if (ext == null || ext.IsEmpty) return true;
            if (ext.SpatialReference == null || ext.SpatialReference.Wkid == 0) return true;
            bool nearOrigin = ext.XMin > -50 && ext.YMin > -50 && ext.XMax < pixelW * 3 && ext.YMax < pixelH * 3;
            bool smallSpan = ext.Width < pixelW * 3 && ext.Height < pixelH * 3 && ext.Width > 0;
            return nearOrigin && smallSpan && Math.Abs(ext.XMax) < 1e6 && Math.Abs(ext.YMax) < 1e6
                && Math.Max(Math.Abs(ext.XMin), Math.Abs(ext.YMin)) < 100000;
        }

        /// <summary>
        /// 配准导出的 TIFF+tfw（无内嵌地理变换）Runtime 100.x 常读不到范围。
        /// 转为 _display.png + .pgw + .aux.xml，用 RasterLayer 加载（两视图均可用）。
        /// </summary>
        private async System.Threading.Tasks.Task<RasterLayer> TryLoadGeorefTiffAsPngRasterAsync(
            string filePath, string layerName, GeoreferenceHelper.AffineParams affine, bool skipSync)
        {
            try
            {
                GetRasterPixelSize(filePath, out double pixelW, out double pixelH);
                var mapSr = CurrentMapView.SpatialReference ?? SpatialReferences.WebMercator;
                int wkid = mapSr.Wkid > 0 ? mapSr.Wkid : 3857;

                string dir = Path.GetDirectoryName(filePath) ?? "";
                string baseName = Path.GetFileNameWithoutExtension(filePath);
                string pngPath = Path.Combine(dir, baseName + "_display.png");

                // 确保 _display.png 存在
                if (!File.Exists(pngPath))
                {
                    using (var img = System.Drawing.Image.FromFile(filePath))
                        img.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                }

                // 如果 PNG 尺寸与源 TIFF 不同，缩放仿射参数
                GetRasterPixelSize(pngPath, out double pngW, out double pngH);
                if (Math.Abs(pngW - pixelW) > 1 || Math.Abs(pngH - pixelH) > 1)
                {
                    double sx = pixelW / Math.Max(1, pngW);
                    double sy = pixelH / Math.Max(1, pngH);
                    affine = new GeoreferenceHelper.AffineParams
                    {
                        A = affine.A * sx,
                        D = affine.D * sx,
                        B = affine.B * sy,
                        E = affine.E * sy,
                        C = affine.C,
                        F = affine.F
                    };
                    pixelW = pngW;
                    pixelH = pngH;
                }

                // 写入 sidecar 文件：.pgw + .aux.xml + .prj
                GeoreferenceHelper.WriteWorldFile(pngPath, affine);
                GeoreferenceHelper.WriteAuxXml(pngPath, wkid);
                GeoreferenceHelper.WritePrjFile(pngPath, wkid);

                // 用 RasterLayer 加载 PNG
                var raster = new Raster(pngPath);
                await raster.LoadAsync();
                if (raster.LoadStatus != LoadStatus.Loaded)
                {
                    System.Diagnostics.Debug.WriteLine("[TryLoad] Raster load failed");
                    return null;
                }

                var rasterLayer = new RasterLayer(raster) { Name = layerName };
                await rasterLayer.LoadAsync();
                var extn = rasterLayer.FullExtent;
                if (extn == null || extn.IsEmpty || IsPixelLikeExtent(extn, pixelW, pixelH))
                {
                    System.Diagnostics.Debug.WriteLine($"[TryLoad] RasterLayer extent invalid: extn={extn}");
                    return null;
                }

                CurrentMapView.Map.OperationalLayers.Add(rasterLayer);
                if (!skipSync) SyncMapToOtherView(CurrentMapView);
                LayerManager.AddLayer(rasterLayer, false, filePath);
                await CurrentMapView.SetViewpointGeometryAsync(extn, 50);
                RefreshGeorefLayerList();
                System.Diagnostics.Debug.WriteLine("[TryLoad] RasterLayer loaded successfully");
                return rasterLayer;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TryLoadGeorefTiffAsPngRaster] {ex.Message}");
                return null;
            }
        }

        private async System.Threading.Tasks.Task<RasterLayer> AddRasterLayer(string filePath, string layerName, bool skipSync = false)
        {
            if (CurrentMapView == null || CurrentMapView.Map == null)
                throw new Exception("地图视图未初始化");

            if (!File.Exists(filePath))
                throw new FileNotFoundException("文件不存在: " + filePath);

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            bool isGeoTiff = ext == ".tif" || ext == ".tiff";
            var wfAffine = GeoreferenceHelper.ReadWorldFile(filePath);
            GetRasterPixelSize(filePath, out double pixelW, out double pixelH);

            // 非 TIFF：仅数据视图用 WPF 覆盖层
            if (!isGeoTiff)
            {
                if (CurrentMapView == MapView1)
                    throw new Exception("地图视图不支持加载无坐标图片，请在数据视图中加载。");
                return await AddRasterOverlayAsync(filePath, layerName, skipSync);
            }

            // 优先尝试直接加载 TIFF（.tfw 世界文件会提供地理范围）
            try
            {
                var raster = new Raster(filePath);
                await raster.LoadAsync();
                if (raster.LoadStatus == LoadStatus.Loaded)
                {
                    var rasterLayer = new RasterLayer(raster) { Name = layerName };
                    await rasterLayer.LoadAsync();
                    var fullExt = rasterLayer.FullExtent;
                    bool extentBad = fullExt == null || fullExt.IsEmpty || IsPixelLikeExtent(fullExt, pixelW, pixelH);

                    if (!extentBad)
                    {
                        CurrentMapView.Map.OperationalLayers.Add(rasterLayer);
                        if (!skipSync) SyncMapToOtherView(CurrentMapView);
                        LayerManager.AddLayer(rasterLayer, false, filePath);
                        await CurrentMapView.SetViewpointGeometryAsync(fullExt, 50);
                        RefreshGeorefLayerList();
                        System.Diagnostics.Debug.WriteLine("[AddRasterLayer] TIFF RasterLayer OK");
                        return rasterLayer;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AddRasterLayer] TIFF direct failed: {ex.Message}");
            }

            // TIFF 直接加载失败（无 GeoTIFF 标签），尝试 _display.png + sidecar
            if (wfAffine != null)
            {
                var pngLayer = await TryLoadGeorefTiffAsPngRasterAsync(filePath, layerName, wfAffine, skipSync);
                if (pngLayer != null)
                {
                    System.Diagnostics.Debug.WriteLine("[AddRasterLayer] PNG RasterLayer OK");
                    return pngLayer;
                }

                // PNG 失败，尝试 TIFF + 强制从 .tfw 计算范围后使用 GraphicsOverlay
                var ovResult = await AddRasterAsGraphicsOverlayAsync(filePath, layerName, wfAffine, pixelW, pixelH, skipSync);
                if (ovResult != null) return ovResult;

                // 最终回退：仅数据视图用 WPF 覆盖层
                if (CurrentMapView != MapView1)
                    return await AddRasterOverlayWithExistingAffineAsync(filePath, layerName, wfAffine, pixelW, pixelH, skipSync);
            }

            if (CurrentMapView != MapView1)
                return await AddRasterOverlayAsync(filePath, layerName, skipSync);
            throw new Exception("栅格加载失败 (" + layerName + "): 无法确定范围，请尝试在数据视图中打开。");
        }

        /// <summary>用 GraphicsOverlay + PictureFillSymbol 显示栅格（两视图均可用，无需 RasterLayer）</summary>
        private async System.Threading.Tasks.Task<RasterLayer> AddRasterAsGraphicsOverlayAsync(
            string filePath, string layerName, GeoreferenceHelper.AffineParams affine,
            double pixelW, double pixelH, bool skipSync)
        {
            try
            {
                var mapSr = CurrentMapView.SpatialReference ?? SpatialReferences.WebMercator;
                var imgExtent = ComputeWorldFileEnvelope(affine, pixelW, pixelH, mapSr);
                if (imgExtent == null || imgExtent.IsEmpty) return null;

                var polygon = new Polygon(new[]
                {
                    new MapPoint(imgExtent.XMin, imgExtent.YMin, mapSr),
                    new MapPoint(imgExtent.XMax, imgExtent.YMin, mapSr),
                    new MapPoint(imgExtent.XMax, imgExtent.YMax, mapSr),
                    new MapPoint(imgExtent.XMin, imgExtent.YMax, mapSr),
                    new MapPoint(imgExtent.XMin, imgExtent.YMin, mapSr)
                });

                // Always use _display.png for PictureFillSymbol (doesn't support TIFF)
                string pngPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(filePath) ?? "", System.IO.Path.GetFileNameWithoutExtension(filePath) + "_display.png");
                if (!System.IO.File.Exists(pngPath))
                {
                    using (var img = System.Drawing.Image.FromFile(filePath))
                        img.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                var fillSym = new PictureFillSymbol(new Uri(pngPath));
                var graphic = new Graphic(polygon, fillSym);
                var overlay = new GraphicsOverlay { Id = "RasterOverlay_" + layerName };
                overlay.Graphics.Add(graphic);
                CurrentMapView.GraphicsOverlays.Add(overlay);

                // 为 LayerManager 添加一个占位图层
                var placeholder = new GroupLayer { Name = layerName };
                CurrentMapView.Map.OperationalLayers.Add(placeholder);
                if (!skipSync) SyncMapToOtherView(CurrentMapView);
                LayerManager.AddLayer(placeholder, false, filePath);
                await CurrentMapView.SetViewpointGeometryAsync(imgExtent, 50);
                RefreshGeorefLayerList();
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AddRasterAsGraphicsOverlay] {ex.Message}");
                return null;
            }
        }

        /// <summary>已有仿射参数时的 WPF 覆盖层（不重写世界文件，避免破坏配准结果）</summary>
        private async System.Threading.Tasks.Task<RasterLayer> AddRasterOverlayWithExistingAffineAsync(
            string filePath, string layerName, GeoreferenceHelper.AffineParams affine,
            double pixelW, double pixelH, bool skipSync)
        {
            var mapSr = CurrentMapView.SpatialReference ?? SpatialReferences.WebMercator;
            var imgExtent = ComputeWorldFileEnvelope(affine, pixelW, pixelH, mapSr);
            SetImageOverlay(filePath, imgExtent, layerName);

            var placeholder = new GroupLayer { Name = layerName };
            CurrentMapView.Map.OperationalLayers.Add(placeholder);
            if (!skipSync) SyncMapToOtherView(CurrentMapView);
            LayerManager.AddLayer(placeholder, false, filePath);
            await CurrentMapView.SetViewpointGeometryAsync(imgExtent, 50);
            return null;
        }

        private async System.Threading.Tasks.Task<RasterLayer> AddRasterOverlayAsync(string filePath, string layerName, bool skipSync)
        {
            // 若已有世界文件（配准结果），绝不能覆盖写入临时画布仿射
            var existingAffine = GeoreferenceHelper.ReadWorldFile(filePath);
            GetRasterPixelSize(filePath, out double pixelW, out double pixelH);
            if (existingAffine != null)
                return await AddRasterOverlayWithExistingAffineAsync(filePath, layerName, existingAffine, pixelW, pixelH, skipSync);

            double canvasW = UnreferencedCanvasExtent.Width;
            double canvasH = UnreferencedCanvasExtent.Height;
            double cellSize = Math.Min(canvasW / pixelW, canvasH / pixelH) * 0.8;
            cellSize = Math.Max(1, cellSize);

            var mapSr = CurrentMapView.SpatialReference ?? SpatialReferences.WebMercator;
            var center = UnreferencedCanvasCenter;

            var affine = new GeoreferenceHelper.AffineParams
            {
                A = cellSize,
                D = 0,
                B = 0,
                E = -cellSize,
                C = center.X - pixelW * cellSize / 2,
                F = center.Y + pixelH * cellSize / 2
            };
            // 仅无参考影像才写临时世界文件（供后续配准）
            GeoreferenceHelper.WriteWorldFile(filePath, affine);
            GeoreferenceHelper.WriteAuxXml(filePath, mapSr.Wkid > 0 ? mapSr.Wkid : 3857);
            GeoreferenceHelper.WritePrjFile(filePath, mapSr.Wkid > 0 ? mapSr.Wkid : 3857);

            var imgExtent = ComputeWorldFileEnvelope(affine, pixelW, pixelH, mapSr);
            SetImageOverlay(filePath, imgExtent, layerName);

            var placeholder = new GroupLayer { Name = layerName };
            CurrentMapView.Map.OperationalLayers.Add(placeholder);
            if (!skipSync)
                SyncMapToOtherView(CurrentMapView);
            LayerManager.AddLayer(placeholder, false, filePath);

            await CurrentMapView.SetViewpointGeometryAsync(imgExtent, 50);
            return null;
        }

        /// <summary>
        /// 将导出的配准栅格添加到两个地图视图的 GraphicsOverlay（支持地图/数据双视图）
        /// </summary>
        private async System.Threading.Tasks.Task AddExportedRasterToViews(string filePath, string layerName)
        {
            var af = GeoreferenceHelper.ReadWorldFile(filePath);
            if (af == null) throw new Exception("找不到世界文件 (.tfw)");

            GetRasterPixelSize(filePath, out double pxW, out double pxH);

            // 确保 _display.png 存在
            string pngPath = Path.Combine(Path.GetDirectoryName(filePath) ?? "",
                Path.GetFileNameWithoutExtension(filePath) + "_display.png");
            if (!File.Exists(pngPath))
            {
                using (var img = System.Drawing.Image.FromFile(filePath))
                    img.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                if (!File.Exists(pngPath))
                    throw new Exception("无法生成 _display.png");
            }

            // 验证 PNG 可读
            long pngSize = new System.IO.FileInfo(pngPath).Length;
            System.Diagnostics.Debug.WriteLine($"[GeorefExport] _display.png: {pngSize} bytes");

            // 为两个视图添加 GraphicsOverlay
            var views = new[] { MapView1, MapView2 };
            MapView firstView = null;
            foreach (var v in views)
            {
                if (v == null || v.Map == null) continue;
                if (firstView == null) firstView = v;

                var mapSr = v.SpatialReference ?? SpatialReferences.WebMercator;
                System.Diagnostics.Debug.WriteLine($"[GeorefExport] View SR WKID={mapSr.Wkid}");

                var wfExt = ComputeWorldFileEnvelope(af, pxW, pxH, mapSr);
                if (wfExt == null || wfExt.IsEmpty) continue;
                System.Diagnostics.Debug.WriteLine($"[GeorefExport] Extent: X={wfExt.XMin:F2},{wfExt.XMax:F2} Y={wfExt.YMin:F2},{wfExt.YMax:F2}");

                var polygon = new Polygon(new[]
                {
                    new MapPoint(wfExt.XMin, wfExt.YMin, mapSr),
                    new MapPoint(wfExt.XMax, wfExt.YMin, mapSr),
                    new MapPoint(wfExt.XMax, wfExt.YMax, mapSr),
                    new MapPoint(wfExt.XMin, wfExt.YMax, mapSr),
                    new MapPoint(wfExt.XMin, wfExt.YMin, mapSr)
                });
                var fillSym = new PictureFillSymbol(new Uri(pngPath));
                var graphic = new Graphic(polygon, fillSym);
                var overlay = new GraphicsOverlay { Id = "RasterOverlay_" + layerName };
                overlay.Graphics.Add(graphic);
                v.GraphicsOverlays.Add(overlay);

                // 只在第一个视图添加占位图层和设置视点
                if (firstView == v)
                {
                    var placeholder = new GroupLayer { Name = layerName };
                    v.Map.OperationalLayers.Add(placeholder);
                    LayerManager.AddLayer(placeholder, false, filePath);
                    await v.SetViewpointGeometryAsync(wfExt, 50);
                    RefreshGeorefLayerList();
                }
            }
        }

        private async System.Threading.Tasks.Task AddGeodatabaseLayer(string gdbPath, string layerName)
        {
            try
            {
                var geodatabase = await Geodatabase.OpenAsync(gdbPath);
                bool firstLayer = true;

                foreach (var featureTable in geodatabase.GeodatabaseFeatureTables)
                {
                    await featureTable.LoadAsync();

                    var featureLayer = new FeatureLayer(featureTable);
                    featureLayer.Name = featureTable.TableName;

                    if (featureTable.GeometryType == GeometryType.Point)
                    {
                        var renderer = new SimpleRenderer(new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Orange, 8));
                        featureLayer.Renderer = renderer;
                    }
                    else if (featureTable.GeometryType == GeometryType.Polyline)
                    {
                        var renderer = new SimpleRenderer(new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Purple, 2));
                        featureLayer.Renderer = renderer;
                    }
                    else if (featureTable.GeometryType == GeometryType.Polygon)
                    {
                        var renderer = new SimpleRenderer(new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(100, System.Drawing.Color.Yellow),
                            new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Orange, 1)));
                        featureLayer.Renderer = renderer;
                    }

                    CurrentMapView.Map.OperationalLayers.Add(featureLayer);
                    SyncMapToOtherView(CurrentMapView);
                    LayerManager.AddLayer(featureLayer, false, gdbPath);

                    if (firstLayer && featureLayer.FullExtent != null && !featureLayer.FullExtent.IsEmpty)
                    {
                        await CurrentMapView.SetViewpointGeometryAsync(featureLayer.FullExtent);
                        firstLayer = false;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"地理数据库加载失败 ({layerName}): {ex.Message}");
            }
        }

        private void EnterLayerEditMode(string layerName, string filePath)
        {
            _isLayerEditMode = true;
            _currentEditLayerName = layerName;
            _currentEditFilePath = filePath;

            var layer = CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == layerName);
            var featureTable = GetFeatureTableFromLayer(layer);
            if (featureTable != null)
            {
                StatusBar.Text = $"已进入图层编辑模式：{layerName}，使用绘制工具添加要素";
                PanelDataEdit.SetDrawToolEnabledByGeometryType(featureTable.GeometryType);
                PanelDataEdit.SetEditToolsEnabled(true);
            }
        }

        private void ExitLayerEditMode()
        {
            _isLayerEditMode = false;
            _currentEditLayerName = string.Empty;
            _currentEditFilePath = string.Empty;
            PanelDataEdit.SetDrawToolEnabled(false);
            PanelDataEdit.SetEditToolsEnabled(false);
            StatusBar.Text = "已退出图层编辑模式";
        }

        /// <summary>非图层编辑模式下禁用三个绘制按钮（避免图层锁定仍可绘制）。</summary>
        private void UpdateDrawToolEnabledByData()
        {
            if (_isLayerEditMode) return;
            PanelDataEdit.SetDrawToolEnabled(false);
        }

        public bool IsLayerEditMode()
        {
            return _isLayerEditMode;
        }

        /// <summary>
        /// 指定图层是否处于可编辑状态（锁定/未开启编辑 返回 false）
        /// </summary>
        public bool CanEditLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
                return false;
            var item = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
            // 以图层管理器中的编辑开关为准
            return item != null && item.IsEditable;
        }

        public void PushUndoOperation(EditOperation op)
        {
            _undoStack.Push(op);
        }

        /// <summary>
        /// 克隆可用于 FeatureCollectionTable 的字段（排除 OID/Geometry，避免复用旧 Field 实例导致丢值）
        /// </summary>
        public static List<Field> CloneEditableFields(IEnumerable<Field> sourceFields, Field extraField = null)
        {
            var list = new List<Field>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sourceFields != null)
            {
                foreach (var f in sourceFields)
                {
                    if (f == null) continue;
                    if (f.FieldType == FieldType.OID || f.FieldType == FieldType.Geometry || f.FieldType == FieldType.Blob)
                        continue;
                    if (f.Name.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase) ||
                        f.Name.Equals("FID", StringComparison.OrdinalIgnoreCase) ||
                        f.Name.Equals("OID", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!names.Add(f.Name)) continue;

                    int len = f.Length > 0 ? f.Length : (f.FieldType == FieldType.Text ? 255 : 0);
                    list.Add(new Field(f.FieldType, f.Name, string.IsNullOrEmpty(f.Alias) ? f.Name : f.Alias,
                        len, null, true, true));
                }
            }
            if (extraField != null && names.Add(extraField.Name))
            {
                int len = extraField.Length > 0 ? extraField.Length : (extraField.FieldType == FieldType.Text ? 255 : 0);
                list.Add(new Field(extraField.FieldType, extraField.Name,
                    string.IsNullOrEmpty(extraField.Alias) ? extraField.Name : extraField.Alias,
                    len, null, true, true));
            }
            return list;
        }

        public static object GetAttributeIgnoreCase(Feature feature, string fieldName)
        {
            if (feature == null || string.IsNullOrEmpty(fieldName)) return null;
            if (feature.Attributes.ContainsKey(fieldName))
                return feature.Attributes[fieldName];
            foreach (var kv in feature.Attributes)
            {
                if (string.Equals(kv.Key, fieldName, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return null;
        }

        public object ConvertAttributeValue(object value, FieldType targetType)
        {
            return ConvertToCompatibleValue(value, targetType);
        }

        public static void CopyFeatureAttributes(Feature source, Feature dest)
        {
            if (source == null || dest == null || dest.FeatureTable == null) return;
            foreach (var field in dest.FeatureTable.Fields)
            {
                if (field.FieldType == FieldType.OID || field.FieldType == FieldType.Geometry)
                    continue;
                var value = GetAttributeIgnoreCase(source, field.Name);
                if (value == null)
                {
                    // 再扫一遍源属性字典（兼容大小写/别名）
                    foreach (var kv in source.Attributes)
                    {
                        if (string.Equals(kv.Key, field.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            value = kv.Value;
                            break;
                        }
                    }
                }
                if (value == null || value == DBNull.Value) continue;
                try
                {
                    dest.Attributes[field.Name] = value;
                }
                catch
                {
                    try
                    {
                        // 类型不兼容时尝试转换
                        if (value is string || value is IConvertible)
                        {
                            object converted = value;
                            switch (field.FieldType)
                            {
                                case FieldType.Int32:
                                    if (int.TryParse(value.ToString(), out int i)) converted = i;
                                    else continue;
                                    break;
                                case FieldType.Int16:
                                    if (short.TryParse(value.ToString(), out short s)) converted = s;
                                    else continue;
                                    break;
                                case FieldType.Float32:
                                    if (float.TryParse(value.ToString(), out float f)) converted = f;
                                    else continue;
                                    break;
                                case FieldType.Float64:
                                    if (double.TryParse(value.ToString(), out double d)) converted = d;
                                    else continue;
                                    break;
                                case FieldType.Text:
                                    converted = value.ToString();
                                    break;
                                default:
                                    continue;
                            }
                            dest.Attributes[field.Name] = converted;
                        }
                    }
                    catch { }
                }
            }
        }

        public async Task<List<FeatureSnapshot>> SnapshotTableAsync(FeatureTable sourceTable)
        {
            var snaps = new List<FeatureSnapshot>();
            if (sourceTable == null) return snaps;

            var features = (await sourceTable.QueryFeaturesAsync(new QueryParameters())).ToList();
            foreach (var feature in features)
            {
                var attrs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in feature.Attributes)
                {
                    if (kv.Key != null &&
                        !kv.Key.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase) &&
                        !kv.Key.Equals("FID", StringComparison.OrdinalIgnoreCase) &&
                        !kv.Key.Equals("OID", StringComparison.OrdinalIgnoreCase) &&
                        kv.Value != null && kv.Value != DBNull.Value)
                    {
                        attrs[kv.Key] = kv.Value is ICloneable c ? c.Clone() : kv.Value;
                    }
                }
                snaps.Add(new FeatureSnapshot
                {
                    Geometry = feature.Geometry != null ? Geometry.FromJson(feature.Geometry.ToJson()) : null,
                    Attributes = attrs
                });
            }
            return snaps;
        }

        public async Task<FeatureCollectionTable> BuildMemoryTableFromSnapshotsAsync(
            List<Field> fields, GeometryType geomType, SpatialReference sr, List<FeatureSnapshot> snapshots)
        {
            var fieldList = CloneEditableFields(fields);
            var memoryTable = new FeatureCollectionTable(fieldList, geomType, sr);
            await memoryTable.LoadAsync();

            if (snapshots != null)
            {
                foreach (var snap in snapshots)
                {
                    var newFeature = memoryTable.CreateFeature();
                    if (snap.Geometry != null)
                        newFeature.Geometry = Geometry.FromJson(snap.Geometry.ToJson());

                    foreach (var field in memoryTable.Fields)
                    {
                        if (field.FieldType == FieldType.OID || field.FieldType == FieldType.Geometry)
                            continue;

                        object val = null;
                        if (snap.Attributes != null)
                        {
                            if (snap.Attributes.ContainsKey(field.Name))
                                val = snap.Attributes[field.Name];
                            else
                            {
                                foreach (var kv in snap.Attributes)
                                {
                                    if (string.Equals(kv.Key, field.Name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        val = kv.Value;
                                        break;
                                    }
                                }
                            }
                        }
                        if (val == null || val == DBNull.Value)
                        {
                            try { newFeature.Attributes[field.Name] = DBNull.Value; } catch { }
                            continue;
                        }
                        try
                        {
                            newFeature.Attributes[field.Name] = ConvertToCompatibleValue(val, field.FieldType);
                        }
                        catch
                        {
                            try { newFeature.Attributes[field.Name] = val.ToString(); }
                            catch { }
                        }
                    }

                    await memoryTable.AddFeatureAsync(newFeature);
                }
            }
            return memoryTable;
        }

        /// <summary>
        /// 按新字段列表重建内存表并完整拷贝要素属性/几何
        /// </summary>
        public async Task<FeatureCollectionTable> RebuildMemoryTableAsync(
            FeatureTable sourceTable, IList<Field> newFields)
        {
            // 先完整快照，避免枚举过程中表被替换导致丢数据
            var snapshots = await SnapshotTableAsync(sourceTable);
            var fields = CloneEditableFields(newFields);
            return await BuildMemoryTableFromSnapshotsAsync(
                fields, sourceTable.GeometryType, sourceTable.SpatialReference, snapshots);
        }

        public async Task<FeatureTable> EnsureEditableMemoryTableAsync(string layerName)
        {
            var layerItem = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
            var layer = layerItem?.Layer ?? CurrentMapView.Map?.OperationalLayers?.FirstOrDefault(l => l.Name == layerName);
            var table = GetFeatureTableFromLayer(layer) ?? layerItem?.MemoryFeatureTable;
            if (table == null) return null;

            if (table is FeatureCollectionTable fct)
            {
                if (layerItem != null)
                    layerItem.MemoryFeatureTable = fct;
                return fct;
            }

            return await ConvertShapefileToMemoryTable(table, layerName);
        }

        private async Task<bool> AddFeatureToEditLayerAsync(Geometry geometry)
        {
            if (!_isLayerEditMode || string.IsNullOrEmpty(_currentEditLayerName) || geometry == null)
                return false;

            var featureTable = await EnsureEditableMemoryTableAsync(_currentEditLayerName);
            if (featureTable == null)
                throw new InvalidOperationException("无法获取可编辑图层表");

            if (featureTable.GeometryType != geometry.GeometryType)
            {
                StatusBar.Text = $"当前图层是 {featureTable.GeometryType} 类型，无法添加 {geometry.GeometryType} 要素";
                return false;
            }

            Geometry projected = geometry;
            if (featureTable.SpatialReference != null && geometry.SpatialReference != null &&
                featureTable.SpatialReference.Wkid != geometry.SpatialReference.Wkid)
            {
                projected = GeometryEngine.Project(geometry, featureTable.SpatialReference);
            }

            var newFeature = featureTable.CreateFeature();
            newFeature.Geometry = projected;

            // 点要素：写入当前选择的点类型（字段存在时）
            if ((geometry.GeometryType == GeometryType.Point || geometry.GeometryType == GeometryType.Multipoint) &&
                !string.IsNullOrEmpty(_currentPointTypeCode))
            {
                try
                {
                    if (featureTable.Fields.Any(f => string.Equals(f.Name, "pointType", StringComparison.OrdinalIgnoreCase)))
                        newFeature.Attributes["pointType"] = _currentPointTypeCode;
                }
                catch { }
            }

            // 线要素：写入当前选择的地质线类型（字段存在时）
            if (geometry.GeometryType == GeometryType.Polyline && !string.IsNullOrEmpty(_currentLineTypeCode))
            {
                try
                {
                    if (featureTable.Fields.Any(f => string.Equals(f.Name, "lineType", StringComparison.OrdinalIgnoreCase)))
                        newFeature.Attributes["lineType"] = _currentLineTypeCode;
                }
                catch { }
            }

            foreach (var field in featureTable.Fields)
            {
                if (field.FieldType == FieldType.OID || field.FieldType == FieldType.Geometry)
                    continue;
                try
                {
                    object current = null;
                    if (newFeature.Attributes.ContainsKey(field.Name))
                        current = newFeature.Attributes[field.Name];
                    if (current == null)
                        newFeature.Attributes[field.Name] = GetDefaultFieldValue(field.FieldType);
                }
                catch { }
            }

            await featureTable.AddFeatureAsync(newFeature);

            _undoStack.Push(new EditOperation
            {
                OperationType = "新增",
                LayerName = _currentEditLayerName,
                Feature = newFeature,
                OriginalGeometry = CloneGeometryStatic(projected),
                DeletedSnapshots = new List<FeatureSnapshot>
                {
                    new FeatureSnapshot
                    {
                        Geometry = CloneGeometryStatic(projected),
                        Attributes = newFeature.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value)
                    }
                }
            });

            MarkLayerHasUnsavedChanges(_currentEditLayerName);
            RefreshLayerDisplay(_currentEditLayerName);
            UpdateAttributeTable();
            return true;
        }

        /// <summary>切换点类型时，将当前点图层的渲染颜色同步为所选类型色（仅 SimpleRenderer 点图层）。</summary>
        private void ApplyPointTypeToEditLayerRenderer()
        {
            try
            {
                if (!_isLayerEditMode || string.IsNullOrEmpty(_currentEditLayerName) || string.IsNullOrEmpty(_currentPointTypeCode))
                    return;
                var def = FieldDataClassification.FindPointType(_currentPointTypeCode);
                if (def == null) return;

                var layer = CurrentMapView.Map?.OperationalLayers?.FirstOrDefault(l => l.Name == _currentEditLayerName);
                if (layer == null) return;
                var featureTable = GetFeatureTableFromLayer(layer);
                if (featureTable == null) return;
                if (featureTable.GeometryType != GeometryType.Point && featureTable.GeometryType != GeometryType.Multipoint)
                    return;

                var newSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, def.Color, 8)
                {
                    Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.White, 1)
                };

                if (layer is FeatureLayer fl && fl.Renderer is SimpleRenderer sr)
                {
                    sr.Symbol = newSymbol;
                    fl.Renderer = sr;
                }
                else if (featureTable is FeatureCollectionTable fct && fct.Renderer is SimpleRenderer sr2)
                {
                    sr2.Symbol = newSymbol;
                    fct.Renderer = sr2;
                    if (layer is FeatureCollectionLayer fcl && fcl.Layers != null)
                    {
                        foreach (var sub in fcl.Layers.OfType<FeatureLayer>())
                        {
                            if (sub.Renderer is SimpleRenderer subSr)
                            {
                                subSr.Symbol = newSymbol;
                                sub.Renderer = subSr;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>切换线类型时，将当前线图层的渲染颜色同步为所选类型色（仅 SimpleRenderer 线图层）。</summary>
        private void ApplyLineTypeToEditLayerRenderer()
        {
            try
            {
                if (!_isLayerEditMode || string.IsNullOrEmpty(_currentEditLayerName) || string.IsNullOrEmpty(_currentLineTypeCode))
                    return;
                var def = FieldDataClassification.FindLineType(_currentLineTypeCode);
                if (def == null) return;

                var layer = CurrentMapView.Map?.OperationalLayers?.FirstOrDefault(l => l.Name == _currentEditLayerName);
                if (layer == null) return;
                var featureTable = GetFeatureTableFromLayer(layer);
                if (featureTable == null) return;
                if (featureTable.GeometryType != GeometryType.Polyline)
                    return;

                var newSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, def.Color, 2);

                if (layer is FeatureLayer fl && fl.Renderer is SimpleRenderer sr)
                {
                    sr.Symbol = newSymbol;
                    fl.Renderer = sr;
                }
                else if (featureTable is FeatureCollectionTable fct && fct.Renderer is SimpleRenderer sr2)
                {
                    sr2.Symbol = newSymbol;
                    fct.Renderer = sr2;
                    if (layer is FeatureCollectionLayer fcl && fcl.Layers != null)
                    {
                        foreach (var sub in fcl.Layers.OfType<FeatureLayer>())
                        {
                            if (sub.Renderer is SimpleRenderer subSr)
                            {
                                subSr.Symbol = newSymbol;
                                sub.Renderer = subSr;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void CreateNewFeatureLayer()
        {
            var dialog = new NewFeatureLayerDialog { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                CreateNewFeatureLayerAsync(dialog.LayerName, dialog.GeometryType, dialog.SpatialReferenceWKID, dialog.UnitType);
            }
        }

        private async void CreateNewFeatureLayerAsync(string layerName, string geometryType, int wkid, string unitType = "")
        {
            try
            {
                var sfd = new System.Windows.Forms.SaveFileDialog();
                sfd.Filter = "Shapefile (*.shp)|*.shp";
                sfd.FileName = layerName + ".shp";
                if (sfd.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                var sr = SpatialReference.Create(wkid);
                GeometryType geomType = GeometryType.Point;
                if (geometryType == "Polyline")
                    geomType = GeometryType.Polyline;
                else if (geometryType == "Polygon")
                    geomType = GeometryType.Polygon;

                CreateEmptyShapefile(sfd.FileName, geomType, sr);

                var fields = new List<Field>
                {
                    new Field(FieldType.OID, "OBJECTID", "OBJECTID", 0, null, false, false),
                    new Field(FieldType.Int32, "ID", "ID", 0, null, true, true)
                };
                if (geomType == GeometryType.Point)
                {
                    fields.Add(new Field(FieldType.Text, "pointType", "点类型", 50, null, true, true));
                }
                else if (geomType == GeometryType.Polyline)
                {
                    fields.Add(new Field(FieldType.Text, "lineType", "线类型", 50, null, true, true));
                }
                else if (geomType == GeometryType.Polygon && !string.IsNullOrEmpty(unitType))
                {
                    fields.Add(new Field(FieldType.Text, "unitType", "单元类型", 20, null, true, true));
                    fields.Add(new Field(FieldType.Text, "unitName", "单元名称", 100, null, true, true));
                    fields.Add(new Field(FieldType.Text, "unitCode", "代号", 50, null, true, true));
                    fields.Add(new Field(FieldType.Text, "lithology", "岩性", 200, null, true, true));
                }

                var memoryTable = new FeatureCollectionTable(fields, geomType, sr);
                await memoryTable.LoadAsync();

                if (geomType == GeometryType.Point)
                {
                    memoryTable.Renderer = new SimpleRenderer(new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 8));
                }
                else if (geomType == GeometryType.Polyline)
                {
                    memoryTable.Renderer = new SimpleRenderer(new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Blue, 2));
                }
                else if (geomType == GeometryType.Polygon)
                {
                    if (!string.IsNullOrEmpty(unitType))
                    {
                        System.Drawing.Color fillColor, outlineColor;
                        if (unitType == "ROCK")
                        {
                            fillColor = System.Drawing.Color.FromArgb(110, 235, 120, 140);
                            outlineColor = System.Drawing.Color.FromArgb(255, 200, 60, 60);
                        }
                        else if (unitType == "TECTONIC")
                        {
                            fillColor = System.Drawing.Color.FromArgb(110, 120, 140, 235);
                            outlineColor = System.Drawing.Color.FromArgb(255, 60, 90, 200);
                        }
                        else
                        {
                            fillColor = System.Drawing.Color.FromArgb(110, 224, 165, 60);
                            outlineColor = System.Drawing.Color.FromArgb(255, 180, 90, 40);
                        }
                        memoryTable.Renderer = new SimpleRenderer(new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, fillColor,
                            new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outlineColor, 1.5)));
                    }
                    else
                    {
                        memoryTable.Renderer = new SimpleRenderer(new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(100, System.Drawing.Color.Green),
                            new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.DarkGreen, 1)));
                    }
                }

                var featureCollection = new FeatureCollection();
                featureCollection.Tables.Add(memoryTable);

                var featureLayer = new FeatureCollectionLayer(featureCollection);
                featureLayer.Name = layerName;

                CurrentMapView.Map.OperationalLayers.Add(featureLayer);
                SyncMapToOtherView(CurrentMapView);
                LayerManager.AddLayer(featureLayer, true, sfd.FileName, memoryTable);

                EnterLayerEditMode(layerName, sfd.FileName);
                StatusBar.Text = $"新建图层 '{layerName}' 已创建并加载，已进入编辑模式";
            }
            catch (Exception ex)
            {
                MessageBox.Show("创建图层失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateEmptyShapefile(string filePath, GeometryType geometryType, SpatialReference sr, List<Field> customFields = null)
        {
            string directory = Path.GetDirectoryName(filePath);
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string shpPath = Path.Combine(directory, fileName + ".shp");
            string shxPath = Path.Combine(directory, fileName + ".shx");
            string dbfPath = Path.Combine(directory, fileName + ".dbf");
            string prjPath = Path.Combine(directory, fileName + ".prj");
            string cpgPath = Path.Combine(directory, fileName + ".cpg");
            string sbnPath = Path.Combine(directory, fileName + ".sbn");
            string sbxPath = Path.Combine(directory, fileName + ".sbx");

            int shapeType = geometryType == GeometryType.Point ? 1 : geometryType == GeometryType.Polyline ? 3 : 5;

            CreateSHP(shpPath, shapeType);
            CreateSHX(shxPath, shapeType);
            CreateDBF(dbfPath, customFields);

            string prjContent = GetPRJContent(sr);
            File.WriteAllText(prjPath, prjContent);

            File.WriteAllText(cpgPath, "UTF-8");

            using (var fs = File.Create(sbnPath)) { }
            using (var fs = File.Create(sbxPath)) { }
        }

        private void CreateSHP(string filePath, int shapeType)
        {
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                byte[] header = new byte[100];
                
                WriteBigEndianInt32(header, 0, 9994);
                for (int i = 4; i < 24; i += 4)
                    WriteBigEndianInt32(header, i, 0);
                WriteBigEndianInt32(header, 24, 50);
                WriteLittleEndianInt32(header, 28, 1000);
                WriteLittleEndianInt32(header, 32, shapeType);
                for (int i = 36; i < 68; i += 8)
                    WriteLittleEndianDouble(header, i, 0.0);
                for (int i = 68; i < 100; i += 4)
                    WriteLittleEndianInt32(header, i, 0);

                fs.Write(header, 0, header.Length);
                fs.Flush();
            }
        }

        private void CreateSHX(string filePath, int shapeType)
        {
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                byte[] header = new byte[100];
                
                WriteBigEndianInt32(header, 0, 9994);
                for (int i = 4; i < 24; i += 4)
                    WriteBigEndianInt32(header, i, 0);
                WriteBigEndianInt32(header, 24, 50);
                WriteLittleEndianInt32(header, 28, 1000);
                WriteLittleEndianInt32(header, 32, shapeType);
                for (int i = 36; i < 68; i += 8)
                    WriteLittleEndianDouble(header, i, 0.0);
                for (int i = 68; i < 100; i += 4)
                    WriteLittleEndianInt32(header, i, 0);

                fs.Write(header, 0, header.Length);
                fs.Flush();
            }
        }

        private void CreateDBF(string filePath, List<Field> customFields = null)
        {
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                List<Field> fields = customFields ?? new List<Field>();
                if (!fields.Any(f => f.Name.Equals("ID", StringComparison.OrdinalIgnoreCase)))
                {
                    fields.Insert(0, new Field(FieldType.Int32, "ID", "ID", 10, null, true, true));
                }

                int recordLength = 1;
                foreach (var field in fields)
                {
                    switch (field.FieldType)
                    {
                        case FieldType.Text:
                            recordLength += field.Length > 0 ? field.Length : 255;
                            break;
                        case FieldType.Int32:
                        case FieldType.Int16:
                            recordLength += 10;
                            break;
                        case FieldType.Float32:
                        case FieldType.Float64:
                            recordLength += 20;
                            break;
                        case FieldType.Date:
                            recordLength += 8;
                            break;
                        default:
                            recordLength += 255;
                            break;
                    }
                }

                int headerLength = 32 + (fields.Count * 32) + 1;

                byte[] dbfHeader = new byte[32];
                
                dbfHeader[0] = 0x03;
                DateTime now = DateTime.Now;
                dbfHeader[1] = (byte)(now.Year - 1900);
                dbfHeader[2] = (byte)now.Month;
                dbfHeader[3] = (byte)now.Day;
                
                WriteLittleEndianInt32(dbfHeader, 4, 0);
                WriteLittleEndianInt16(dbfHeader, 8, (short)headerLength);
                WriteLittleEndianInt16(dbfHeader, 10, (short)recordLength);
                
                for (int i = 12; i < 32; i++)
                    dbfHeader[i] = 0;

                fs.Write(dbfHeader, 0, 32);

                foreach (var field in fields)
                {
                    byte[] fieldDescriptor = new byte[32];
                    string fieldName = field.Name.Length > 11 ? field.Name.Substring(0, 11) : field.Name;
                    byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(fieldName);
                    Array.Copy(nameBytes, fieldDescriptor, Math.Min(nameBytes.Length, 11));
                    for (int i = nameBytes.Length; i < 11; i++)
                        fieldDescriptor[i] = 0;

                    switch (field.FieldType)
                    {
                        case FieldType.Text:
                            fieldDescriptor[11] = 0x43;
                            fieldDescriptor[16] = (byte)(field.Length > 0 ? field.Length : 255);
                            break;
                        case FieldType.Int32:
                        case FieldType.Int16:
                            fieldDescriptor[11] = 0x4E;
                            fieldDescriptor[16] = 10;
                            break;
                        case FieldType.Float32:
                        case FieldType.Float64:
                            fieldDescriptor[11] = 0x46;
                            fieldDescriptor[16] = 20;
                            fieldDescriptor[17] = 10;
                            break;
                        case FieldType.Date:
                            fieldDescriptor[11] = 0x44;
                            fieldDescriptor[16] = 8;
                            break;
                        default:
                            fieldDescriptor[11] = 0x43;
                            fieldDescriptor[16] = 255;
                            break;
                    }

                    for (int i = 12; i < 16; i++)
                        fieldDescriptor[i] = 0;
                    fieldDescriptor[17] = field.FieldType == FieldType.Float32 || field.FieldType == FieldType.Float64 ? (byte)10 : (byte)0;
                    for (int i = 18; i < 22; i++)
                        fieldDescriptor[i] = 0;
                    WriteLittleEndianInt16(fieldDescriptor, 22, 1);
                    for (int i = 24; i < 32; i++)
                        fieldDescriptor[i] = 0;

                    fs.Write(fieldDescriptor, 0, 32);
                }

                fs.WriteByte(0x0D);
                fs.WriteByte(0x1A);
                fs.Flush();
            }
        }

        private void WriteBigEndianInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private void WriteLittleEndianInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private void WriteLittleEndianInt16(byte[] buffer, int offset, short value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private void WriteLittleEndianDouble(byte[] buffer, int offset, double value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Copy(bytes, 0, buffer, offset, 8);
            else
                Array.Copy(bytes.Reverse().ToArray(), 0, buffer, offset, 8);
        }

        private string GetPRJContent(SpatialReference sr)
        {
            if (sr == null)
                return GetDefaultPRJ();

            int wkid = sr.Wkid;
            switch (wkid)
            {
                case 4326:
                    return "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]]";
                case 4490:
                    return "GEOGCS[\"China Geodetic Coordinate System 2000\",DATUM[\"China_2000\",SPHEROID[\"CGCS2000\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"1024\"]],AUTHORITY[\"EPSG\",\"1043\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4490\"]]";
                case 32650:
                    return "PROJCS[\"WGS 84 / UTM zone 50N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",117],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"32650\"]]";
                case 32651:
                    return "PROJCS[\"WGS 84 / UTM zone 51N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",123],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"32651\"]]";
                case 3857:
                    return "PROJCS[\"WGS 84 / Pseudo-Mercator\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]],PROJECTION[\"Mercator_1SP\"],PARAMETER[\"central_meridian\",0],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",0],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],EXTENSION[\"PROJ4\",\"+proj=merc +a=6378137 +b=6378137 +lat_ts=0.0 +lon_0=0.0 +x_0=0.0 +y_0=0 +k=1.0 +units=m +nadgrids=@null +wktext +no_defs\"],AUTHORITY[\"EPSG\",\"3857\"]]";
                default:
                    return GetDefaultPRJ();
            }
        }

        private string GetDefaultPRJ()
        {
            return "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]]";
        }

        private void DeleteShapefileFiles(string shpFilePath)
        {
            string dir = Path.GetDirectoryName(shpFilePath);
            string name = Path.GetFileNameWithoutExtension(shpFilePath);
            
            string[] extensions = { ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sbn", ".sbx", ".fbn", ".fbx", ".ain", ".aih", ".ixs", ".mxs", ".atx", ".shp.xml" };
            
            foreach (string ext in extensions)
            {
                string filePath = Path.Combine(dir, name + ext);
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void CopyShapefileFiles(string sourceShpPath, string destShpPath)
        {
            string srcDir = Path.GetDirectoryName(sourceShpPath);
            string srcName = Path.GetFileNameWithoutExtension(sourceShpPath);
            string dstDir = Path.GetDirectoryName(destShpPath);
            string dstName = Path.GetFileNameWithoutExtension(destShpPath);
            if (!Directory.Exists(dstDir))
                Directory.CreateDirectory(dstDir);

            string[] extensions = { ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sbn", ".sbx" };
            foreach (string ext in extensions)
            {
                string src = Path.Combine(srcDir, srcName + ext);
                string dst = Path.Combine(dstDir, dstName + ext);
                if (File.Exists(src))
                    File.Copy(src, dst, true);
            }
        }

        private void TryDeleteDirectory(string dir)
        {
            try
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch { }
        }

        public async System.Threading.Tasks.Task SaveLayerToShapefile(string layerNameOrPath)
        {
            string workDir = null;
            Layer keptLayer = null;
            FeatureCollectionTable keepMemory = null;
            int keepIndex = -1;
            try
            {
                // 属性表未提交编辑先写回
                if (_attributeTableWindow != null && _attributeTableWindow.IsVisible)
                    await _attributeTableWindow.CommitAllEditsToFeaturesAsync();

                var layerItem = LayerManager.Layers.FirstOrDefault(l => l.Name == layerNameOrPath);
                if (layerItem == null)
                    layerItem = LayerManager.Layers.FirstOrDefault(l => l.FilePath == layerNameOrPath);

                if (layerItem == null)
                {
                    MessageBox.Show("未找到指定图层，请在图层列表中选中要保存的图层", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var filePath = layerItem.FilePath;
                if (string.IsNullOrEmpty(filePath))
                {
                    MessageBox.Show("该图层没有关联的 Shapefile 路径", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusBar.Text = $"正在保存图层 '{layerItem.Name}'...";

                FeatureTable sourceTable = GetFeatureTableFromLayer(layerItem.Layer) ?? layerItem.MemoryFeatureTable;
                if (sourceTable == null)
                {
                    MessageBox.Show("无法获取图层数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var geomType = sourceTable.GeometryType;
                var sr = sourceTable.SpatialReference;

                // 完整快照（地图图层仍在，数据安全）
                var featureData = await SnapshotTableAsync(sourceTable);
                int withGeom = featureData.Count(s => s.Geometry != null);
                if (featureData.Count == 0)
                {
                    var confirm = MessageBox.Show(
                        $"图层 '{layerItem.Name}' 当前没有要素，保存将写入空文件。是否继续？",
                        "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.Yes)
                        return;
                }
                else if (withGeom == 0)
                {
                    MessageBox.Show("要素没有有效几何，已取消保存以防数据丢失", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                List<Field> fields = CloneEditableFields(sourceTable.Fields);
                // shapefile 字段名最长 10
                fields = fields.Select(f =>
                {
                    string n = f.Name.Length > 10 ? f.Name.Substring(0, 10) : f.Name;
                    return new Field(f.FieldType, n, f.Alias, f.Length > 0 ? f.Length : 255, null, true, true);
                }).ToList();

                // 建映射：截断名 → 原名（用于精确匹配属性，避免 StartsWith 模糊匹配误赋值）
                var truncatedToOriginal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var origField in sourceTable.Fields)
                {
                    if (origField.FieldType == FieldType.Geometry) continue;
                    string t = origField.Name.Length > 10 ? origField.Name.Substring(0, 10) : origField.Name;
                    truncatedToOriginal[t] = origField.Name;
                }

                workDir = Path.Combine(Path.GetTempPath(), "GeoMapSave_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workDir);
                string tempShp = Path.Combine(workDir, "out.shp");
                string backupDir = Path.Combine(workDir, "backup");

                CreateEmptyShapefile(tempShp, geomType, sr, fields);

                var shapefileTable = new ShapefileFeatureTable(tempShp);
                await shapefileTable.LoadAsync();

                int savedCount = 0;
                foreach (var snap in featureData)
                {
                    if (snap.Geometry == null) continue;
                    var newFeature = shapefileTable.CreateFeature();
                    newFeature.Geometry = snap.Geometry;

                    foreach (var field in shapefileTable.Fields)
                    {
                        if (field.Name.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase) ||
                            field.Name.Equals("FID", StringComparison.OrdinalIgnoreCase) ||
                            field.FieldType == FieldType.OID)
                            continue;

                        object value = null;
                        if (snap.Attributes != null)
                        {
                            // 优先直匹配截断名
                            if (snap.Attributes.ContainsKey(field.Name))
                                value = snap.Attributes[field.Name];
                            else if (truncatedToOriginal.TryGetValue(field.Name, out string origName) &&
                                     snap.Attributes.ContainsKey(origName))
                                value = snap.Attributes[origName];
                        }

                        if (value != null)
                            newFeature.Attributes[field.Name] = ConvertToCompatibleValue(value, field.FieldType);
                    }

                    await shapefileTable.AddFeatureAsync(newFeature);
                    savedCount++;
                }

                // 校验写入数量
                if (withGeom > 0 && savedCount == 0)
                    throw new InvalidOperationException("写入 shapefile 失败：未写入任何要素");

                shapefileTable = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(300);

                // 备份原文件（图层仍保留在地图上）
                if (File.Exists(filePath))
                {
                    Directory.CreateDirectory(backupDir);
                    CopyShapefileFiles(filePath, Path.Combine(backupDir, Path.GetFileName(filePath)));
                }

                // 保留当前图层引用，失败时可恢复
                keptLayer = layerItem.Layer;
                keepMemory = layerItem.MemoryFeatureTable;
                keepIndex = keptLayer != null ? CurrentMapView.Map.OperationalLayers.IndexOf(keptLayer) : -1;

                // 解锁原 shp：先移走图层
                if (keptLayer != null && keepIndex >= 0)
                    CurrentMapView.Map.OperationalLayers.RemoveAt(keepIndex);
                layerItem.Layer = null;
                layerItem.MemoryFeatureTable = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(200);

                try
                {
                    DeleteShapefileFiles(filePath);
                    CopyShapefileFiles(tempShp, filePath);
                    if (!File.Exists(filePath))
                        throw new FileNotFoundException("保存后未找到目标 shapefile", filePath);
                    await RecreateLayerFromShapefile(layerItem);
                    keptLayer = null; // 成功，无需恢复旧图层
                    keepMemory = null;
                }
                catch (Exception saveEx)
                {
                    // 恢复备份文件
                    string backupShp = Path.Combine(backupDir, Path.GetFileName(filePath));
                    if (File.Exists(backupShp))
                    {
                        try
                        {
                            DeleteShapefileFiles(filePath);
                            CopyShapefileFiles(backupShp, filePath);
                        }
                        catch { }
                    }

                    // 恢复地图图层（优先内存表，避免要素消失）
                    if (keepMemory != null)
                    {
                        await ReplaceLayerWithMemoryTableAsync(layerItem.Name, keepMemory, layerItem);
                    }
                    else if (keptLayer != null)
                    {
                        if (keepIndex >= 0 && keepIndex <= CurrentMapView.Map.OperationalLayers.Count)
                        {
                            CurrentMapView.Map.OperationalLayers.Insert(keepIndex, keptLayer);
                            SyncMapToOtherView(CurrentMapView);
                        }
                        else
                        {
                            CurrentMapView.Map.OperationalLayers.Add(keptLayer);
                            SyncMapToOtherView(CurrentMapView);
                        }
                        layerItem.Layer = keptLayer;
                    }
                    else
                    {
                        try { await RecreateLayerFromShapefile(layerItem); }
                        catch { }
                    }
                    throw new Exception("保存失败，已尝试恢复图层显示: " + saveEx.Message, saveEx);
                }

                _unsavedGraphics.RemoveAll(g =>
                {
                    if (g.Attributes.ContainsKey("LayerName") &&
                        (g.Attributes["LayerName"].ToString() == layerNameOrPath ||
                         g.Attributes["LayerName"].ToString() == layerItem.Name))
                    {
                        if (_drawingOverlay != null)
                            _drawingOverlay.Graphics.Remove(g);
                        return true;
                    }
                    return false;
                });

                layerItem.HasUnsavedChanges = false;
                _currentEditLayerName = layerItem.Name;
                UpdateAttributeTable();

                if (_isLayerEditMode)
                {
                    var table = GetFeatureTableFromLayer(layerItem.Layer);
                    if (table != null)
                        PanelDataEdit.SetDrawToolEnabledByGeometryType(table.GeometryType);
                }

                StatusBar.Text = $"图层 '{layerItem.Name}' 保存成功，共保存 {savedCount} 个要素";
                MessageBox.Show($"图层 '{layerItem.Name}' 保存成功！共保存 {savedCount} 个要素。", "保存成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusBar.Text = "保存图层失败: " + ex.Message;
                MessageBox.Show("保存图层失败:\n" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TryDeleteDirectory(workDir);
            }
        }

        private object ConvertToCompatibleValue(object value, FieldType targetType)
        {
            try
            {
                if (value == null) return null;

                switch (targetType)
                {
                    case FieldType.Int32:
                        if (value is int) return value;
                        if (value is double) return Convert.ToInt32((double)value);
                        return int.TryParse(value.ToString(), out int intResult) ? intResult : 0;
                    case FieldType.Int16:
                        if (value is short) return value;
                        if (value is double) return Convert.ToInt16((double)value);
                        return short.TryParse(value.ToString(), out short shortResult) ? shortResult : (short)0;
                    case FieldType.Float32:
                        if (value is float) return value;
                        if (value is double) return (float)(double)value;
                        return float.TryParse(value.ToString(), out float floatResult) ? floatResult : 0f;
                    case FieldType.Float64:
                        if (value is double) return value;
                        return double.TryParse(value.ToString(), out double doubleResult) ? doubleResult : 0.0;
                    case FieldType.Date:
                        if (value is DateTime) return value;
                        return DateTime.TryParse(value.ToString(), out DateTime dateResult) ? dateResult : DateTime.Now;
                    case FieldType.Text:
                        return value.ToString();
                    default:
                        return value.ToString();
                }
            }
            catch
            {
                return value.ToString();
            }
        }

        private object GetDefaultFieldValue(FieldType fieldType)
        {
            switch (fieldType)
            {
                case FieldType.Int32:
                    return 0;
                case FieldType.Int16:
                    return (short)0;
                case FieldType.Float32:
                    return 0f;
                case FieldType.Float64:
                    return 0.0;
                case FieldType.Date:
                    return DateTime.Now;
                case FieldType.Text:
                    return "";
                default:
                    return null;
            }
        }

        /// <summary>
        /// FeatureCollectionTable 必须通过 FeatureCollectionLayer 加载，不能直接 new FeatureLayer(table)
        /// </summary>
        internal async Task<FeatureCollectionLayer> CreateFeatureCollectionLayerAsync(
            FeatureCollectionTable memoryTable, string layerName, Symbol existingSymbol = null)
        {
            if (existingSymbol != null)
            {
                memoryTable.Renderer = new SimpleRenderer(existingSymbol);
            }
            else if (memoryTable.Renderer == null)
            {
                switch (memoryTable.GeometryType)
                {
                    case GeometryType.Point:
                        memoryTable.Renderer = new SimpleRenderer(new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 8));
                        break;
                    case GeometryType.Polyline:
                        memoryTable.Renderer = new SimpleRenderer(new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Blue, 2));
                        break;
                    case GeometryType.Polygon:
                        memoryTable.Renderer = new SimpleRenderer(new SimpleFillSymbol(SimpleFillSymbolStyle.Solid,
                            System.Drawing.Color.FromArgb(100, System.Drawing.Color.Green),
                            new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.DarkGreen, 1)));
                        break;
                }
            }

            var collection = new FeatureCollection();
            collection.Tables.Add(memoryTable);
            var layer = new FeatureCollectionLayer(collection) { Name = layerName };
            await layer.LoadAsync();
            // 同步到子图层 Renderer（与 LayerPropertiesDialog 一致，否则切视图后仍可能丢色）
            if (memoryTable.Renderer != null && layer.Layers != null)
            {
                foreach (var sub in layer.Layers.OfType<FeatureLayer>())
                    sub.Renderer = memoryTable.Renderer;
            }
            return layer;
        }

        private Symbol TryGetLayerSymbol(Layer layer)
        {
            if (layer is FeatureLayer fl && fl.Renderer is SimpleRenderer sr)
                return sr.Symbol;
            if (layer is FeatureCollectionLayer fcl)
            {
                // LayerPropertiesDialog 把符号写在子 FeatureLayer.Renderer 上
                if (fcl.Layers != null)
                {
                    foreach (var sub in fcl.Layers.OfType<FeatureLayer>())
                    {
                        if (sub.Renderer is SimpleRenderer subSr && subSr.Symbol != null)
                            return subSr.Symbol;
                    }
                }
                if (fcl.FeatureCollection?.Tables.Count > 0)
                {
                    var table = fcl.FeatureCollection.Tables[0] as FeatureCollectionTable;
                    if (table?.Renderer is SimpleRenderer sr2)
                        return sr2.Symbol;
                }
            }
            return null;
        }

        public Task ReplaceLayerWithMemoryTablePublicAsync(string layerName, FeatureCollectionTable memoryTable, LayerItem layerItem = null)
        {
            return ReplaceLayerWithMemoryTableAsync(layerName, memoryTable, layerItem);
        }

        private async Task ReplaceLayerWithMemoryTableAsync(string layerName, FeatureCollectionTable memoryTable, LayerItem layerItem = null)
        {
            layerItem = layerItem ?? LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
            var existingLayer = layerItem?.Layer ?? CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == layerName);
            var symbol = TryGetLayerSymbol(existingLayer);
            int index = existingLayer != null ? CurrentMapView.Map.OperationalLayers.IndexOf(existingLayer) : -1;

            // 先创建新图层，成功后再替换，避免 Remove 后创建失败导致图层永久消失
            var newLayer = await CreateFeatureCollectionLayerAsync(memoryTable, layerName, symbol);
            newLayer.IsVisible = true;

            if (existingLayer != null && index >= 0)
            {
                CurrentMapView.Map.OperationalLayers.RemoveAt(index);
                CurrentMapView.Map.OperationalLayers.Insert(index, newLayer);
                SyncMapToOtherView(CurrentMapView);
            }
            else if (existingLayer != null)
            {
                CurrentMapView.Map.OperationalLayers.Remove(existingLayer);
                CurrentMapView.Map.OperationalLayers.Add(newLayer);
                SyncMapToOtherView(CurrentMapView);
            }
            else
            {
                CurrentMapView.Map.OperationalLayers.Add(newLayer);
                SyncMapToOtherView(CurrentMapView);
            }

            if (layerItem != null)
            {
                layerItem.Layer = newLayer;
                layerItem.MemoryFeatureTable = memoryTable;
            }
        }

        private async Task RevertFieldOperation(EditOperation op)
        {
            if (op.OriginalFields == null || op.LayerName == null)
                return;

            var layerItem = LayerManager.Layers.FirstOrDefault(l => l.Name == op.LayerName);
            if (layerItem == null)
                return;

            FeatureCollectionTable memoryTable;
            if (op.TableSnapshots != null && op.TableSnapshots.Count >= 0 &&
                op.SnapshotSpatialReference != null)
            {
                // 用添加/删除字段前的完整快照恢复（含属性值）
                memoryTable = await BuildMemoryTableFromSnapshotsAsync(
                    op.OriginalFields,
                    op.SnapshotGeometryType,
                    op.SnapshotSpatialReference,
                    op.TableSnapshots);
            }
            else
            {
                var currentTable = GetFeatureTableFromLayer(layerItem.Layer) ?? layerItem.MemoryFeatureTable;
                if (currentTable == null) return;
                var fields = CloneEditableFields(op.OriginalFields);
                memoryTable = await RebuildMemoryTableAsync(currentTable, fields);
            }

            await ReplaceLayerWithMemoryTableAsync(op.LayerName, memoryTable, layerItem);
            MarkLayerHasUnsavedChanges(op.LayerName);
            UpdateAttributeTable();
        }

        private async Task<FeatureCollectionTable> ConvertShapefileToMemoryTable(FeatureTable sourceTable, string layerName)
        {
            // 若已经是内存表，直接返回
            if (sourceTable is FeatureCollectionTable existingMemory)
            {
                var layerItemExisting = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
                if (layerItemExisting != null)
                    layerItemExisting.MemoryFeatureTable = existingMemory;
                return existingMemory;
            }

            var memoryTable = await RebuildMemoryTableAsync(sourceTable, sourceTable.Fields.ToList());
            var layerItem = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
            await ReplaceLayerWithMemoryTableAsync(layerName, memoryTable, layerItem);
            return memoryTable;
        }

        private async System.Threading.Tasks.Task RecreateLayerFromShapefile(LayerItem layerItem)
        {
            try
            {
                var filePath = layerItem.FilePath;
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    throw new FileNotFoundException("Shapefile 不存在", filePath);

                var shapefileTable = new ShapefileFeatureTable(filePath);
                await shapefileTable.LoadAsync();

                var newFeatureLayer = new FeatureLayer(shapefileTable);
                newFeatureLayer.Name = layerItem.Name;
                await newFeatureLayer.LoadAsync();

                if (shapefileTable.GeometryType == GeometryType.Point)
                {
                    newFeatureLayer.Renderer = new SimpleRenderer(new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 8));
                }
                else if (shapefileTable.GeometryType == GeometryType.Polyline)
                {
                    newFeatureLayer.Renderer = new SimpleRenderer(new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Blue, 2));
                }
                else if (shapefileTable.GeometryType == GeometryType.Polygon)
                {
                    newFeatureLayer.Renderer = new SimpleRenderer(new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(100, System.Drawing.Color.Green),
                        new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.DarkGreen, 1)));
                }

                var oldLayer = layerItem.Layer ?? CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == layerItem.Name);
                var oldIndex = oldLayer != null ? CurrentMapView.Map.OperationalLayers.IndexOf(oldLayer) : -1;
                if (oldIndex >= 0)
                {
                    CurrentMapView.Map.OperationalLayers.RemoveAt(oldIndex);
                    CurrentMapView.Map.OperationalLayers.Insert(oldIndex, newFeatureLayer);
                    SyncMapToOtherView(CurrentMapView);
                }
                else
                {
                    CurrentMapView.Map.OperationalLayers.Add(newFeatureLayer);
                    SyncMapToOtherView(CurrentMapView);
                }

                layerItem.Layer = newFeatureLayer;
                layerItem.MemoryFeatureTable = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show("重新加载图层失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private async void RemoveLayerFromMap(string layerName)
        {
            if (CurrentMapView == null || CurrentMapView.Map == null) return;

            var layerItem = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
            if (layerItem != null && layerItem.HasUnsavedChanges)
            {
                var result = MessageBox.Show($"图层 '{layerName}' 有未保存的更改，是否先保存？", "提示", 
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    await SaveLayerToShapefile(layerName);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            var layer = CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == layerName);
            if (layer != null)
            {
                CurrentMapView.Map.OperationalLayers.Remove(layer);
            }

            // 若移除的是当前编辑图层，退出编辑模式
            if (_isLayerEditMode && _currentEditLayerName == layerName)
            {
                ExitLayerEditMode();
                if (_isVertexEditing && _currentEditingFeature != null)
                    CancelVertexEditing();
            }

            // 若移除的是无参考影像占位图层，清除 WPF 覆盖层
            if (_hasOverlay && layerName == _overlayLayerName)
                ClearImageOverlay();

            _selectedFeatures.RemoveAll(f => 
            {
                var table = f.FeatureTable;
                if (table == null) return false;
                foreach (var lyr in CurrentMapView.Map.OperationalLayers)
                {
                    var lyrTable = GetFeatureTableFromLayer(lyr);
                    if (lyrTable == table) return false;
                }
                return true;
            });

            if (_selectionOverlay != null)
            {
                _selectionOverlay.Graphics.Clear();
            }

            LayerManager.RemoveLayer(layerName);
            StatusBar.Text = $"图层 '{layerName}' 已移除";
        }

        private void SetLayerVisibility(string layerName, bool isVisible)
        {
            if (CurrentMapView == null || CurrentMapView.Map == null) return;

            var layer = CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == layerName);
            if (layer != null)
            {
                layer.IsVisible = isVisible;
                LayerManager.UpdateLayerVisibility(layerName, isVisible);
            }

            // 同步无参考影像覆盖层可见性
            if (_hasOverlay && layerName == _overlayLayerName)
            {
                UnreferencedImageOverlay.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void ZoomToLayer(string layerName)
        {
            if (CurrentMapView == null || CurrentMapView.Map == null) return;

            // 无参考影像覆盖层：使用缓存的 extent
            if (_hasOverlay && layerName == _overlayLayerName && _overlayExtent != null)
            {
                await CurrentMapView.SetViewpointGeometryAsync(_overlayExtent, 50);
                StatusBar.Text = $"已缩放至图层 '{layerName}'";
                return;
            }

            var layer = CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == layerName);
            if (layer != null)
            {
                try
                {
                    if (layer.FullExtent != null && !layer.FullExtent.IsEmpty)
                    {
                        await CurrentMapView.SetViewpointGeometryAsync(layer.FullExtent);
                        StatusBar.Text = $"已缩放至图层 '{layerName}'";
                    }
                    else
                    {
                        MessageBox.Show("该图层没有有效的范围", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("缩放失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ShowAttributeTable(string layerName)
        {
            ShowAttributeTable(layerName, null);
        }

        private void ShowAttributeTable(string layerName, List<Feature> selectedFeatures)
        {
            if (CurrentMapView == null || CurrentMapView.Map == null) return;

            var layerItem = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
            var layer = layerItem?.Layer ?? CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == layerName);
            var table = GetFeatureTableFromLayer(layer) ?? layerItem?.MemoryFeatureTable;
            if (table != null)
            {
                _currentEditLayerName = layerName;
                ShowAttributeTablePanel(null, layerName, selectedFeatures);
            }
            else
            {
                MessageBox.Show("该图层不是要素图层，无法查看属性表", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>供野外数据面板「属性及编号修改」调用：打开当前选中要素所在图层的属性表（仅显示选中要素）。</summary>
        public void OpenAttributeTableForSelected()
        {
            string name = null;
            List<Feature> selected = _selectedFeatures != null && _selectedFeatures.Count > 0
                ? _selectedFeatures.ToList()
                : null;

            // 优先使用当前选中要素所在图层
            if (selected != null && selected.Count > 0)
            {
                var ft = selected[0].FeatureTable;
                if (ft != null)
                    name = GetLayerNameForTable(ft);
            }
            // 其次使用当前编辑图层
            if (string.IsNullOrEmpty(name))
                name = _currentEditLayerName;
            // 最后回退到任意可编辑图层
            if (string.IsNullOrEmpty(name))
                name = LayerManager.Layers.FirstOrDefault(l => !l.IsFolderPlaceholder && l.IsEditable)?.Name
                    ?? LayerManager.Layers.FirstOrDefault(l => !l.IsFolderPlaceholder)?.Name;
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请先加载并选择一个矢量图层", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShowAttributeTable(name, selected);
        }

        public string GetLayerNameForTable(FeatureTable table)
        {
            if (table == null) return null;
            return LayerManager.Layers
                .Where(l => l.MemoryFeatureTable != null || GetFeatureTableFromLayer(l.Layer) != null)
                .FirstOrDefault(l => (l.MemoryFeatureTable ?? GetFeatureTableFromLayer(l.Layer)) == table)?.Name;
        }

        private void ShowAttributeTablePanel(System.Data.DataTable dataTable, string layerName)
        {
            ShowAttributeTablePanel(dataTable, layerName, null);
        }

        private void ShowAttributeTablePanel(System.Data.DataTable dataTable, string layerName, List<Feature> selectedFeatures)
        {
            if (_attributeTableWindow != null && _attributeTableWindow.IsVisible)
            {
                _attributeTableWindow.ForceClose();
            }

            _attributeTableWindow = new AttributeTableWindow(this, layerName, CurrentMapView, selectedFeatures);
            _attributeTableWindow.AddFieldRequested += (name) =>
            {
                if (!string.IsNullOrEmpty(name))
                    _currentEditLayerName = name;
                BtnAddField_Click(null, null);
            };
            _attributeTableWindow.FieldCalculatorRequested += (name) =>
            {
                if (!string.IsNullOrEmpty(name))
                    _currentEditLayerName = name;
                BtnFieldCalculator_Click(null, null);
            };
            _attributeTableWindow.ExportRequested += (name) => BtnExport_Click(null, null);
            _attributeTableWindow.BatchUpdateRequested += async (name) =>
            {
                if (!string.IsNullOrEmpty(name))
                    _currentEditLayerName = name;
                await RunBatchUpdateAttributesAsync(name);
            };
            _attributeTableWindow.Closed += (s, e) => _attributeTableWindow = null;
            _attributeTableWindow.Show();
        }

        

        private async void BtnAddField_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentEditLayerName))
            {
                MessageBox.Show("请先选择一个可编辑的矢量图层", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new AddFieldDialog { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            string fieldName = dialog.FieldName;
            string fieldTypeStr = dialog.FieldType;
            int length = dialog.Length;
            string fieldAlias = dialog.FieldAlias;

            FieldType fieldType;
            switch (fieldTypeStr)
            {
                case "ShortInteger":
                    fieldType = FieldType.Int16;
                    break;
                case "LongInteger":
                    fieldType = FieldType.Int32;
                    break;
                case "Float":
                    fieldType = FieldType.Float32;
                    break;
                case "Double":
                    fieldType = FieldType.Float64;
                    break;
                case "Date":
                    fieldType = FieldType.Date;
                    break;
                case "Text":
                default:
                    fieldType = FieldType.Text;
                    break;
            }

            try
            {
                // 先把属性表里未提交的单元格写回要素
                if (_attributeTableWindow != null && _attributeTableWindow.IsVisible)
                    await _attributeTableWindow.CommitAllEditsToFeaturesAsync();

                var table = await EnsureEditableMemoryTableAsync(_currentEditLayerName);
                if (table == null)
                {
                    MessageBox.Show("该图层不支持添加字段", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (table.Fields.Any(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show($"字段 '{fieldName}' 已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 完整快照（含已有字段值），再建新表
                var snapshots = await SnapshotTableAsync(table);
                var originalFields = CloneEditableFields(table.Fields);

                _undoStack.Push(new EditOperation
                {
                    OperationType = "添加字段",
                    LayerName = _currentEditLayerName,
                    OriginalFields = originalFields,
                    TableSnapshots = snapshots,
                    SnapshotGeometryType = table.GeometryType,
                    SnapshotSpatialReference = table.SpatialReference,
                    NewFields = null
                });

                int fieldLength = fieldType == FieldType.Text ? (length > 0 ? length : 255) : 0;
                var newField = new Field(fieldType, fieldName,
                    string.IsNullOrEmpty(fieldAlias) ? fieldName : fieldAlias,
                    fieldLength, null, dialog.IsNullable, true);

                var fieldsList = CloneEditableFields(table.Fields, newField);
                var newMemoryTable = await BuildMemoryTableFromSnapshotsAsync(
                    fieldsList, table.GeometryType, table.SpatialReference, snapshots);

                if (snapshots.Count > 0)
                {
                    var check = (await newMemoryTable.QueryFeaturesAsync(new QueryParameters())).ToList();
                    if (check.Count == 0)
                        throw new InvalidOperationException("重建要素表后数据为空，已取消添加字段以防丢数据");
                }

                var layerItem = LayerManager.Layers.FirstOrDefault(item => item.Name == _currentEditLayerName);
                await ReplaceLayerWithMemoryTableAsync(_currentEditLayerName, newMemoryTable, layerItem);
                if (layerItem != null)
                    layerItem.HasUnsavedChanges = true;

                string displayTypeName = GetFieldTypeDisplayName(fieldTypeStr);
                StatusBar.Text = $"已添加字段: {fieldName} ({displayTypeName})，保留 {snapshots.Count} 个要素";
                MessageBox.Show($"字段 '{fieldName}' 添加成功！\n已保留 {snapshots.Count} 个要素的属性值。", "成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateAttributeTable();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加字段失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AttributeDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                var dataGrid = sender as DataGrid;
                if (dataGrid != null && dataGrid.SelectedItem is System.Data.DataRowView rowView)
                {
                    var row = rowView.Row;
                    string columnHeader = e.Column.Header.ToString();
                    
                    string newValue = "";
                    var textBox = e.EditingElement as TextBox;
                    if (textBox != null)
                    {
                        newValue = textBox.Text;
                    }
                    else
                    {
                        var comboBox = e.EditingElement as ComboBox;
                        if (comboBox != null && comboBox.SelectedItem != null)
                        {
                            newValue = comboBox.SelectedItem.ToString();
                        }
                    }

                    if (!_isLayerEditMode)
                    {
                        MessageBox.Show("请先进入编辑模式才能修改属性", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    if (!string.IsNullOrEmpty(_currentEditLayerName))
                    {
                        var layer = CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == _currentEditLayerName);
                        var table = GetFeatureTableFromLayer(layer);
                        if (table != null)
                        {
                            try
                            {
                                int fid = int.Parse(row["FID"].ToString());
                                var features = await table.QueryFeaturesAsync(new Esri.ArcGISRuntime.Data.QueryParameters());
                                
                                var field = table.Fields.FirstOrDefault(f => 
                                    f.Name.Equals(columnHeader, StringComparison.OrdinalIgnoreCase) || 
                                    (f.Alias != null && f.Alias.Equals(columnHeader, StringComparison.OrdinalIgnoreCase)));
                                
                                if (field == null)
                                {
                                    MessageBox.Show($"字段 '{columnHeader}' 不存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                    return;
                                }

                                if (field.FieldType == FieldType.OID)
                                {
                                    MessageBox.Show("OBJECTID 字段不能编辑", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                                    return;
                                }

                                object typedValue = newValue;

                                if (!string.IsNullOrWhiteSpace(newValue))
                                {
                                    switch (field.FieldType)
                                    {
                                        case FieldType.Int32:
                                            if (int.TryParse(newValue, out int intVal))
                                                typedValue = intVal;
                                            else
                                            {
                                                MessageBox.Show($"无法将值 '{newValue}' 转换为整数", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                                return;
                                            }
                                            break;
                                        case FieldType.Int16:
                                            if (short.TryParse(newValue, out short shortVal))
                                                typedValue = shortVal;
                                            else
                                            {
                                                MessageBox.Show($"无法将值 '{newValue}' 转换为短整数", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                                return;
                                            }
                                            break;
                                        case FieldType.Float32:
                                            if (float.TryParse(newValue, out float floatVal))
                                                typedValue = floatVal;
                                            else
                                            {
                                                MessageBox.Show($"无法将值 '{newValue}' 转换为浮点数", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                                return;
                                            }
                                            break;
                                        case FieldType.Float64:
                                            if (double.TryParse(newValue, out double doubleVal))
                                                typedValue = doubleVal;
                                            else
                                            {
                                                MessageBox.Show($"无法将值 '{newValue}' 转换为双精度数", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                                return;
                                            }
                                            break;
                                        case FieldType.Date:
                                            if (DateTime.TryParse(newValue, out DateTime dateVal))
                                                typedValue = dateVal;
                                            else
                                            {
                                                MessageBox.Show($"无法将值 '{newValue}' 转换为日期格式", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                                return;
                                            }
                                            break;
                                    }
                                }

                                foreach (var feature in features)
                                {
                                    bool fidMatch = false;
                                    if (feature.Attributes.ContainsKey("FID"))
                                    {
                                        var fidValue = feature.Attributes["FID"];
                                        if (fidValue is int)
                                        {
                                            fidMatch = (int)fidValue == fid;
                                        }
                                        else
                                        {
                                            try
                                            {
                                                fidMatch = Convert.ToInt32(fidValue) == fid;
                                            }
                                            catch
                                            {
                                                fidMatch = false;
                                            }
                                        }
                                    }
                                    
                                    if (fidMatch)
                                    {
                                        try
                                        {
                                            if (feature.Attributes.ContainsKey(field.Name))
                                            {
                                                feature.Attributes[field.Name] = typedValue;
                                            }
                                            else
                                            {
                                                feature.Attributes.Add(field.Name, typedValue);
                                            }
                                        }
                                        catch (Exception attrEx)
                                        {
                                            MessageBox.Show($"设置属性失败: {attrEx.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                            return;
                                        }
                                        
                                        try
                                        {
                                            await table.UpdateFeatureAsync(feature);
                                        }
                                        catch (Exception updateEx)
                                        {
                                            MessageBox.Show($"更新要素失败: {updateEx.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                            return;
                                        }
                                        break;
                                    }
                                }
                                
                                MarkLayerHasUnsavedChanges(_currentEditLayerName);
                                RefreshFeatureLayer(layer);
                                StatusBar.Text = $"已更新属性: {field.Name} = {newValue}";
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"更新属性失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    }
                }
            }
        }

        private void UpdateAttributeTable()
        {
            if (_attributeTableWindow != null && _attributeTableWindow.IsVisible)
            {
                _attributeTableWindow.RefreshTable();
            }
        }

        public void RefreshLayerDisplay(string layerName)
        {
            try
            {
                var layerItem = LayerManager.Layers.FirstOrDefault(l => l.Name == layerName);
                var layer = layerItem?.Layer ?? CurrentMapView.Map?.OperationalLayers.FirstOrDefault(l => l.Name == layerName);
                if (layer == null) return;

                // 绝不把图层留在 IsVisible=false（旧逻辑 toggle 异常时会导致整层消失）
                layer.IsVisible = true;

                if (layer is FeatureLayer featureLayer)
                {
                    try { featureLayer.ClearSelection(); } catch { }
                    var r = featureLayer.Renderer;
                    featureLayer.Renderer = r;
                }
                else if (layer is FeatureCollectionLayer collectionLayer)
                {
                    if (collectionLayer.FeatureCollection?.Tables.Count > 0)
                    {
                        var table = collectionLayer.FeatureCollection.Tables[0] as FeatureCollectionTable;
                        if (table != null)
                        {
                            var r = table.Renderer;
                            table.Renderer = r;
                        }
                    }
                    // 切换可见性强制重绘
                    collectionLayer.IsVisible = false;
                    collectionLayer.IsVisible = true;
                }

                // 同步 LayerManager 引用
                if (layerItem != null)
                    layerItem.Layer = layer;
            }
            catch { }
        }

        private string GetFieldTypeDisplayName(string fieldTypeStr)
        {
            switch (fieldTypeStr)
            {
                case "ShortInteger":
                    return "短整型";
                case "LongInteger":
                    return "长整型";
                case "Float":
                    return "浮点型";
                case "Double":
                    return "双精度";
                case "Date":
                    return "日期";
                case "Text":
                default:
                    return "文本";
            }
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            currentDrawTool = "Select";
            StatusBar.Text = "选择工具已激活，点击选择要素，Shift+点击多选，点击空白处取消选中";
        }

        private async void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var viewpoint = CurrentMapView.GetCurrentViewpoint(ViewpointType.CenterAndScale);
                if (viewpoint != null)
                {
                    double currentScale = viewpoint.TargetScale;
                    await CurrentMapView.SetViewpointScaleAsync(currentScale / 2);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"放大失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var viewpoint = CurrentMapView.GetCurrentViewpoint(ViewpointType.CenterAndScale);
                if (viewpoint != null)
                {
                    double currentScale = viewpoint.TargetScale;
                    await CurrentMapView.SetViewpointScaleAsync(currentScale * 2);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"缩小失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnZoomToFullExtent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CurrentMapView.Map != null && CurrentMapView.Map.InitialViewpoint != null)
                {
                    await CurrentMapView.SetViewpointAsync(CurrentMapView.Map.InitialViewpoint);
                }
                else
                {
                    MapPoint defaultCenter = new MapPoint(114.3, 30.6, SpatialReferences.Wgs84);
                    await CurrentMapView.SetViewpointCenterAsync(defaultCenter, 50000);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"全图失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count > 0)
                await UndoLastEdit();
            else if (_savedGraphics.Count > 0 || (_drawingOverlay != null && _drawingOverlay.Graphics.Count > 0))
                UndoLastGraphic();
            else
                MessageBox.Show("没有可撤销的编辑操作", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void BtnRedo_Click(object sender, RoutedEventArgs e) => MessageBox.Show("重做");
        private void BtnIdentify_Click(object sender, RoutedEventArgs e) => MessageBox.Show("属性查询");


        internal FeatureTable GetFeatureTableFromLayer(Layer layer)
        {
            if (layer is FeatureLayer featureLayer)
            {
                return featureLayer.FeatureTable;
            }
            if (layer is FeatureCollectionLayer collectionLayer)
            {
                return collectionLayer.FeatureCollection?.Tables.FirstOrDefault();
            }
            return null;
        }

        private void RefreshFeatureLayer(Layer layer)
        {
            if (layer is FeatureLayer featureLayer)
            {
                RefreshFeatureLayer(featureLayer);
            }
            ShowSelectionHighlights();
        }

        internal FeatureTable GetFeatureTableByFeature(Feature feature)
        {
            try
            {
                foreach (var layerItem in LayerManager.Layers)
                {
                    var table = GetFeatureTableFromLayer(layerItem.Layer) ?? layerItem.MemoryFeatureTable;
                    if (table == null) continue;
                    if (feature.Attributes.ContainsKey("OBJECTID"))
                    {
                        var oid = Convert.ToInt64(feature.Attributes["OBJECTID"]);
                        var qp = new QueryParameters();
                        qp.ObjectIds.Add(oid);
                        var results = System.Threading.Tasks.Task.Run(async () =>
                            await table.QueryFeaturesAsync(qp)).GetAwaiter().GetResult();
                        if (results.Any()) return table;
                    }
                }
            }
            catch { }
            return null;
        }


        

        private async void BtnFieldCalculator_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentEditLayerName) || !CanEditLayer(_currentEditLayerName))
            {
                MessageBox.Show("请先对该图层开启编辑模式", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_attributeTableWindow != null && _attributeTableWindow.IsVisible)
                await _attributeTableWindow.CommitAllEditsToFeaturesAsync();

            FeatureTable table = null;
            try
            {
                table = await EnsureEditableMemoryTableAsync(_currentEditLayerName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("准备编辑表失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (table == null)
            {
                MessageBox.Show("无法获取图层数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var targetFieldInfo = table.Fields.ToList();
            var dialog = new FieldCalculatorDialog(targetFieldInfo) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            var targetField = table.Fields.FirstOrDefault(f =>
                f.Name.Equals(dialog.TargetFieldName, StringComparison.OrdinalIgnoreCase));
            if (targetField == null)
            {
                MessageBox.Show("目标字段不存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var features = (await table.QueryFeaturesAsync(new QueryParameters())).ToList();
                int successCount = 0;
                int failCount = 0;
                string lastError = null;

                foreach (var feature in features)
                {
                    try
                    {
                        object result = EvaluateFieldExpression(dialog.Expression, feature, table, targetField);
                        feature.Attributes[targetField.Name] = result;
                        await table.UpdateFeatureAsync(feature);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        lastError = ex.Message;
                    }
                }

                MarkLayerHasUnsavedChanges(_currentEditLayerName);
                UpdateAttributeTable();
                RefreshLayerDisplay(_currentEditLayerName);

                StatusBar.Text = $"字段计算完成: 成功 {successCount} 条，失败 {failCount} 条";
                string msg = $"字段计算完成！成功 {successCount} 条，失败 {failCount} 条";
                if (failCount > 0 && !string.IsNullOrEmpty(lastError))
                    msg += "\n最后错误: " + lastError;
                MessageBox.Show(msg, failCount > 0 && successCount == 0 ? "失败" : "完成",
                    MessageBoxButton.OK,
                    failCount > 0 && successCount == 0 ? MessageBoxImage.Error : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"字段计算失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private object EvaluateFieldExpression(string expression, Feature feature, FeatureTable table, Field targetField)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return GetDefaultFieldValue(targetField.FieldType);

            string expr = expression.Trim();

            // 纯字符串字面量
            if ((expr.StartsWith("\"") && expr.EndsWith("\"")) || (expr.StartsWith("'") && expr.EndsWith("'")))
            {
                string lit = expr.Substring(1, expr.Length - 2);
                return ConvertToCompatibleValue(lit, targetField.FieldType);
            }

            // 替换字段引用
            foreach (var field in table.Fields.OrderByDescending(f => f.Name.Length))
            {
                if (field.FieldType == FieldType.OID || field.FieldType == FieldType.Geometry)
                    continue;

                object raw = GetAttributeIgnoreCase(feature, field.Name);
                string token = "[" + field.Name + "]";
                if (!expr.Contains(token))
                    continue;

                string replacement;
                if (raw == null || raw == DBNull.Value)
                {
                    replacement = (field.FieldType == FieldType.Text) ? "\"\"" : "0";
                }
                else if (field.FieldType == FieldType.Text || field.FieldType == FieldType.Date)
                {
                    replacement = "\"" + raw.ToString().Replace("\"", "\"\"") + "\"";
                }
                else
                {
                    replacement = Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture);
                    if (string.IsNullOrEmpty(replacement)) replacement = "0";
                }
                expr = expr.Replace(token, replacement);
            }

            // 简单字符串连接： "a" & "b" 或 "a" + "b"
            if (targetField.FieldType == FieldType.Text && (expr.Contains("&") || expr.Contains("+\"")))
            {
                try
                {
                    string s = expr.Replace("&", "+");
                    // 去掉引号拼接估算
                    if (!s.Contains("[") && s.All(c => char.IsLetterOrDigit(c) || " \t\"'+.".Contains(c)))
                    {
                        var parts = System.Text.RegularExpressions.Regex.Matches(s, "\"([^\"]*)\"|([^\"+]+)")
                            .Cast<System.Text.RegularExpressions.Match>()
                            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value.Trim())
                            .Where(p => !string.IsNullOrEmpty(p) && p != "+");
                        // fallback to DataTable
                    }
                }
                catch { }
            }

            try
            {
                var dataTable = new System.Data.DataTable();
                dataTable.Locale = System.Globalization.CultureInfo.InvariantCulture;
                object result = dataTable.Compute(expr, string.Empty);
                return ConvertToCompatibleValue(result, targetField.FieldType);
            }
            catch
            {
                // 文本字段：表达式本身作为文本（去掉外层引号）
                if (targetField.FieldType == FieldType.Text)
                    return expression.Trim().Trim('"', '\'');
                throw;
            }
        }

        public async void BtnBatchUpdateAttributes_Click(object sender, RoutedEventArgs e)
        {
            await RunBatchUpdateAttributesAsync(_currentEditLayerName);
        }

        public async Task RunBatchUpdateAttributesAsync(string layerName)
        {
            if (string.IsNullOrEmpty(layerName) || !CanEditLayer(layerName))
            {
                MessageBox.Show("请先对该图层开启编辑模式", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_attributeTableWindow != null && _attributeTableWindow.IsVisible)
                await _attributeTableWindow.CommitAllEditsToFeaturesAsync();

            FeatureTable table;
            try
            {
                table = await EnsureEditableMemoryTableAsync(layerName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("准备编辑表失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (table == null)
            {
                MessageBox.Show("无法获取图层数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dialog = new BatchUpdateAttributesDialog(table.Fields.ToList()) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                List<Feature> targets;
                if (dialog.ApplyToSelectedOnly && _selectedFeatures != null && _selectedFeatures.Count > 0)
                {
                    targets = _selectedFeatures
                        .Where(f => f.FeatureTable == table || GetLayerNameByFeature(f) == layerName)
                        .ToList();
                    if (targets.Count == 0)
                    {
                        // 按几何从当前表重匹配
                        var all = (await table.QueryFeaturesAsync(new QueryParameters())).ToList();
                        targets = all.Where(f => _selectedFeatures.Any(s =>
                            s.Geometry != null && f.Geometry != null &&
                            string.Equals(s.Geometry.ToJson(), f.Geometry.ToJson(), StringComparison.Ordinal))).ToList();
                    }
                }
                else
                {
                    targets = (await table.QueryFeaturesAsync(new QueryParameters())).ToList();
                }

                if (targets.Count == 0)
                {
                    MessageBox.Show("没有可更新的要素", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int success = 0;
                foreach (var feature in targets)
                {
                    try
                    {
                        foreach (var kv in dialog.FieldValues)
                        {
                            var field = table.Fields.FirstOrDefault(f =>
                                f.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                            if (field == null || field.FieldType == FieldType.OID) continue;
                            feature.Attributes[field.Name] = ConvertToCompatibleValue(kv.Value, field.FieldType);
                        }
                        await table.UpdateFeatureAsync(feature);
                        success++;
                    }
                    catch { }
                }

                MarkLayerHasUnsavedChanges(layerName);
                UpdateAttributeTable();
                RefreshLayerDisplay(layerName);
                MessageBox.Show($"批量更新完成，成功 {success} / {targets.Count} 条", "完成",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("批量更新失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void MenuItem_SelectByAttribute(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentEditLayerName))
            {
                MessageBox.Show("请先选择一个矢量图层", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var layer = CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == _currentEditLayerName);
            var table = GetFeatureTableFromLayer(layer);

            if (table == null)
            {
                MessageBox.Show("无法获取图层数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var fieldsList = new List<Field>(table.Fields);
            var dialog = new SelectByAttributeDialog(fieldsList) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var queryParams = new QueryParameters { WhereClause = dialog.WhereClause };
                var result = await table.QueryFeaturesAsync(queryParams);
                var matchingFeatures = result.ToList();

                switch (dialog.SelectedMethod)
                {
                    case SelectByAttributeDialog.SelectionMethod.NewSelection:
                        ClearSelection();
                        foreach (var feature in matchingFeatures)
                        {
                            _selectedFeatures.Add(feature);
                        }
                        break;
                    case SelectByAttributeDialog.SelectionMethod.AddToSelection:
                        foreach (var feature in matchingFeatures)
                        {
                            if (!_selectedFeatures.Contains(feature))
                                _selectedFeatures.Add(feature);
                        }
                        break;
                    case SelectByAttributeDialog.SelectionMethod.RemoveFromSelection:
                        foreach (var feature in matchingFeatures)
                        {
                            _selectedFeatures.Remove(feature);
                        }
                        break;
                }

                ShowSelectionHighlights();
                StatusBar.Text = $"按属性选择完成，共选中 {_selectedFeatures.Count} 个要素";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"按属性选择失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuItem_ToggleSelection(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentEditLayerName))
            {
                MessageBox.Show("请先选择一个矢量图层", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var layer = CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == _currentEditLayerName);
            var table = GetFeatureTableFromLayer(layer);

            if (table == null)
            {
                MessageBox.Show("无法获取图层数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var allFeatures = table.QueryFeaturesAsync(new QueryParameters()).Result.ToList();
                var currentSelectedOids = _selectedFeatures.Select(f => Convert.ToInt64(f.Attributes["OBJECTID"])).ToList();

                ClearSelection();

                foreach (var feature in allFeatures)
                {
                    long oid = Convert.ToInt64(feature.Attributes["OBJECTID"]);
                    if (!currentSelectedOids.Contains(oid))
                    {
                        _selectedFeatures.Add(feature);
                    }
                }

                ShowSelectionHighlights();
                StatusBar.Text = $"切换选择完成，共选中 {_selectedFeatures.Count} 个要素";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"切换选择失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuItem_ClearSelection(object sender, RoutedEventArgs e)
        {
            ClearSelection();
            StatusBar.Text = "选择已清除";
        }

        private async void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFeatures.Count == 0)
            {
                MessageBox.Show("请先选择要删除的要素", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"确定要删除选中的 {_selectedFeatures.Count} 个要素吗？",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            await DeleteSelectedFeatures();
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentEditLayerName))
            {
                MessageBox.Show("请先选择一个矢量图层", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new ExportDialog { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            var layer = CurrentMapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == _currentEditLayerName);
            var table = GetFeatureTableFromLayer(layer);

            if (table == null)
            {
                MessageBox.Show("无法获取图层数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                QueryParameters queryParams = new QueryParameters();
                if (dialog.ExportSelectedOnly && _selectedFeatures.Count > 0)
                {
                    var oids = _selectedFeatures.Select(f => Convert.ToInt64(f.Attributes["OBJECTID"])).ToList();
                    foreach (var oid in oids)
                    {
                        queryParams.ObjectIds.Add(oid);
                    }
                }

                var features = await table.QueryFeaturesAsync(queryParams);

                if (dialog.ExportFormat == "CSV")
                {
                    using (var writer = new System.IO.StreamWriter(dialog.ExportPath, false, System.Text.Encoding.UTF8))
                    {
                        writer.WriteLine(string.Join(",", table.Fields.Where(f => f.FieldType != FieldType.Geometry).Select(f => f.Name)));
                        foreach (var feature in features)
                        {
                            var values = table.Fields.Where(f => f.FieldType != FieldType.Geometry)
                                .Select(f =>
                                {
                                    var val = feature.Attributes[f.Name];
                                    if (val == null) return "";
                                    string str = val.ToString();
                                    if (str.Contains(","))
                                        return $"\"{str}\"";
                                    return str;
                                });
                            writer.WriteLine(string.Join(",", values));
                        }
                    }
                    StatusBar.Text = $"导出完成，共 {features.Count()} 条记录";
                    MessageBox.Show("导出成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("暂不支持该导出格式，请选择 CSV 格式", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private bool PromptSpatialReference(out int wkid, out string wkt, int defaultWkid = 4326)
        {
            wkid = 0;
            wkt = "";
            if (_georefMapSr != null && _georefMapSr.Wkid > 0) defaultWkid = _georefMapSr.Wkid;
            var browser = new SpatialReferenceBrowser(defaultWkid)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (browser.ShowDialog() == true)
            {
                wkid = browser.SelectedWkid;
                wkt = browser.SelectedWkt;
                return true;
            }
            return false;
        }

        private async void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ImportDialog { Owner = this };
            if (dialog.ShowDialog() != true) return;

            if (dialog.ParsedFeatures == null || dialog.ParsedFeatures.Count == 0)
            {
                MessageBox.Show("没有可导入的要素", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await ImportFeaturesAsNewLayer(dialog.ParsedFeatures, dialog.LayerName);
            StatusBar.Text = $"导入完成: {dialog.ParsedFeatures.Count} 个要素 → 图层 \"{dialog.LayerName}\"";
            MessageBox.Show($"成功导入 {dialog.ParsedFeatures.Count} 个要素到图层 \"{dialog.LayerName}\"",
                "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        internal async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            if (_syncDialog == null)
            {
                _syncDialog = new SyncDialog { Owner = this };
            }
            _syncDialog.Show();
            _syncDialog.Focus();
        }

        private void BtnCloudSync_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CloudSyncDialog { Owner = this };
            dlg.ShowDialog();
            UpdateLoginButton();
        }

        public void UpdateLoginButton()
        {
            var cfgPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GeologicalMappingDesktop", "cloudsync.json");
            if (File.Exists(cfgPath))
            {
                try
                {
                    var raw = File.ReadAllText(cfgPath);
                    var json = new System.Web.Script.Serialization.JavaScriptSerializer();
                    var data = json.Deserialize<Dictionary<string, object>>(raw);
                    if (data != null && data.ContainsKey("username"))
                    {
                        TxtLoginIcon.Text = "☁️";
                        TxtLoginLabel.Text = Convert.ToString(data["username"]);
                        BtnLoginToolbar.ToolTip = $"已登录: {Convert.ToString(data["username"])}";
                        return;
                    }
                }
                catch { }
            }
            TxtLoginIcon.Text = "🔑";
            TxtLoginLabel.Text = "登录";
            BtnLoginToolbar.ToolTip = "登录";
        }

        public string GetCurrentFeaturesAsGeoJson()
        {
            try
            {
                var allFeatures = new System.Collections.Generic.List<FeatureState>();
                foreach (var layerItem in LayerManager.Layers)
                {
                    var table = GetFeatureTableFromLayer(layerItem.Layer) ?? layerItem.MemoryFeatureTable;
                    if (table == null) continue;
                    var features = System.Threading.Tasks.Task.Run(async () =>
                        await table.QueryFeaturesAsync(new QueryParameters())).GetAwaiter().GetResult();
                    foreach (var f in features)
                    {
                        allFeatures.Add(new FeatureState
                        {
                            GeometryJson = f.Geometry?.ToJson() ?? "",
                            Attributes = f.Attributes.ToDictionary(
                                kv => kv.Key,
                                kv => kv.Value,
                                StringComparer.OrdinalIgnoreCase)
                        });
                    }
                }
                return GeoJsonHelper.ExportToGeoJson(allFeatures, "全部图层");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetCurrentFeaturesAsGeoJson failed: {ex.Message}");
                return "{\"type\":\"FeatureCollection\",\"features\":[]}";
            }
        }

        public async System.Threading.Tasks.Task ImportFeaturesFromGeoJson(string geojson)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[ImportFeaturesFromGeoJson] input length={geojson?.Length ?? 0}");
                var parsed = GeoJsonHelper.ParseGeoJsonFeatures(geojson);
                System.Diagnostics.Debug.WriteLine($"[ImportFeaturesFromGeoJson] ParseGeoJsonFeatures returned {parsed?.Count ?? 0} features");
                if (parsed != null && parsed.Count > 0)
                {
                    foreach (var f in parsed)
                    {
                        var keyList = f.Attributes?.Keys != null ? string.Join(",", f.Attributes.Keys) : "";
                        System.Diagnostics.Debug.WriteLine($"[ImportFeaturesFromGeoJson] feature: geomJsonLength={(f.GeometryJson?.Length ?? 0)}, attrCount={f.Attributes?.Count ?? 0}, attrKeys=[{keyList}]");
                    }
                    string layerName = $"云端同步_{System.DateTime.Now:HHmmss}";
                    await ImportFeaturesAsNewLayer(parsed, layerName);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ImportFeaturesFromGeoJson] no features parsed, skipping");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImportFeaturesFromGeoJson] exception: {ex.Message}");
                System.Windows.MessageBox.Show($"导入云端数据失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public async System.Threading.Tasks.Task ImportFeaturesAsNewLayer(System.Collections.Generic.List<FeatureState> features, string layerName)
        {
            if (features == null || features.Count == 0) { System.Diagnostics.Debug.WriteLine("[ImportFeaturesAsNewLayer] no features, returning"); return; }

            System.Diagnostics.Debug.WriteLine($"[ImportFeaturesAsNewLayer] processing {features.Count} features");

            // Group features by geometry type
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<FeatureState>>();
            foreach (var fs in features)
            {
                string key = "Point";
                if (!string.IsNullOrEmpty(fs.GeometryJson))
                {
                    try
                    {
                        var g = Esri.ArcGISRuntime.Geometry.Geometry.FromJson(fs.GeometryJson);
                        key = g.GeometryType.ToString();
                    }
                    catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"[ImportFeaturesAsNewLayer] geometry parse error: {ex.Message}"); }
                }
                if (!groups.ContainsKey(key))
                    groups[key] = new System.Collections.Generic.List<FeatureState>();
                groups[key].Add(fs);
            }

            System.Diagnostics.Debug.WriteLine($"[ImportFeaturesAsNewLayer] groups: {string.Join(", ", groups.Select(kv => $"{kv.Key}={kv.Value.Count}"))}");

            int groupIdx = 0;
            foreach (var kv in groups)
            {
                var geomType = (Esri.ArcGISRuntime.Geometry.GeometryType)System.Enum.Parse(
                    typeof(Esri.ArcGISRuntime.Geometry.GeometryType), kv.Key);

                // 按类别进一步拆分：点按 pointType（移动端 7 类），线按 status/lineType
                var subGroups = new Dictionary<string, List<FeatureState>>();
                if (geomType == Esri.ArcGISRuntime.Geometry.GeometryType.Point ||
                    geomType == Esri.ArcGISRuntime.Geometry.GeometryType.Multipoint)
                {
                    subGroups = FieldDataClassification.SplitPointsByType(kv.Value);
                }
                else if (geomType == Esri.ArcGISRuntime.Geometry.GeometryType.Polyline)
                {
                    subGroups = FieldDataClassification.SplitLinesByCategory(kv.Value);
                }
                else
                {
                    subGroups[kv.Key] = kv.Value;
                }

                foreach (var sg in subGroups)
                {
                var featList = sg.Value;
                string displayName = sg.Key;
                string subLayerName = $"{layerName} · {displayName}";
                Esri.ArcGISRuntime.Geometry.SpatialReference sr = Esri.ArcGISRuntime.Geometry.SpatialReferences.Wgs84;

                if (!string.IsNullOrEmpty(featList[0].GeometryJson))
                {
                    try
                    {
                        var g = Esri.ArcGISRuntime.Geometry.Geometry.FromJson(featList[0].GeometryJson);
                        sr = g.SpatialReference ?? Esri.ArcGISRuntime.Geometry.SpatialReferences.Wgs84;
                    }
                    catch { }
                }

                // Collect all unique attribute keys
                var allKeys = new System.Collections.Generic.List<string>();
                var keySet = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in featList)
                {
                    foreach (var k in f.Attributes.Keys)
                    {
                        if (keySet.Add(k))
                            allKeys.Add(k);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[ImportFeaturesAsNewLayer] group={kv.Key}: {featList.Count} features, {allKeys.Count} unique keys: [{string.Join(",", allKeys)}]");

                // Build fields
                var fields = new System.Collections.Generic.List<Esri.ArcGISRuntime.Data.Field>();
                    fields.Add(new Esri.ArcGISRuntime.Data.Field(Esri.ArcGISRuntime.Data.FieldType.Text, "NAME", "名称", 255, null, true, true));
                    fields.Add(new Esri.ArcGISRuntime.Data.Field(Esri.ArcGISRuntime.Data.FieldType.Text, "SourceID", "源ID", 255, null, true, true));
                    foreach (var ak in allKeys)
                    {
                        if (string.Equals(ak, "NAME", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(ak, "SourceID", StringComparison.OrdinalIgnoreCase)) continue;
                        object sample = null;
                    foreach (var f in featList)
                    {
                        if (f.Attributes.TryGetValue(ak, out sample) && sample != null) break;
                    }
                    Esri.ArcGISRuntime.Data.FieldType fieldType = Esri.ArcGISRuntime.Data.FieldType.Text;
                    if (sample is double || sample is float)
                        fieldType = Esri.ArcGISRuntime.Data.FieldType.Float64;
                    else if (sample is int || sample is long)
                        fieldType = Esri.ArcGISRuntime.Data.FieldType.Int32;
                    else if (sample is bool)
                        fieldType = Esri.ArcGISRuntime.Data.FieldType.Text;
                    int fieldLen = (fieldType == Esri.ArcGISRuntime.Data.FieldType.Text) ? 4000 : 255;
                    fields.Add(new Esri.ArcGISRuntime.Data.Field(fieldType, ak, ak, fieldLen, null, true, true));
                }

                var memoryTable = new Esri.ArcGISRuntime.Data.FeatureCollectionTable(fields, geomType, sr);
                if (geomType == Esri.ArcGISRuntime.Geometry.GeometryType.Point ||
                    geomType == Esri.ArcGISRuntime.Geometry.GeometryType.Multipoint)
                {
                    memoryTable.Renderer = new SimpleRenderer(
                        new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle,
                            FieldDataClassification.GetPointColor(displayName), 8)
                        { Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.White, 1) });
                }
                else if (geomType == Esri.ArcGISRuntime.Geometry.GeometryType.Polyline)
                {
                    memoryTable.Renderer = new SimpleRenderer(
                        new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
                            FieldDataClassification.GetLineColor(displayName), 2));
                }
                foreach (var fs in featList)
                {
                    var feature = memoryTable.CreateFeature();
                    if (!string.IsNullOrEmpty(fs.GeometryJson))
                    {
                        try { feature.Geometry = Esri.ArcGISRuntime.Geometry.Geometry.FromJson(fs.GeometryJson); }
                        catch { }
                    }
                    string nameVal = null;
                    if (fs.Attributes.TryGetValue("NAME", out object nv) && nv != null)
                        nameVal = nv.ToString();
                    feature.Attributes["NAME"] = nameVal ?? layerName;

                    // Set SourceID from the feature's id attribute
                    string sourceId = null;
                    if (fs.Attributes.TryGetValue("id", out object idVal) && idVal != null)
                        sourceId = idVal.ToString();
                    else if (fs.Attributes.TryGetValue("customId", out object cidVal) && cidVal != null)
                        sourceId = cidVal.ToString();
                    feature.Attributes["SourceID"] = sourceId ?? layerName;

                    foreach (var ak2 in fs.Attributes)
                    {
                        if (string.Equals(ak2.Key, "NAME", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(ak2.Key, "SourceID", StringComparison.OrdinalIgnoreCase)) continue;
                        if (feature.Attributes.ContainsKey(ak2.Key))
                        {
                            try
                            {
                                object attrVal = ak2.Value ?? DBNull.Value;
                                if (attrVal is bool bv)
                                    attrVal = bv ? "true" : "false";
                                feature.Attributes[ak2.Key] = attrVal;
                            }
                            catch (System.Exception attrEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ImportFeaturesAsNewLayer] attribute set error for '{ak2.Key}' = '{ak2.Value}': {attrEx.Message}");
                            }
                        }
                    }

                    try { await memoryTable.AddFeatureAsync(feature); }
                    catch (System.Exception addEx) { System.Diagnostics.Debug.WriteLine($"[ImportFeaturesAsNewLayer] AddFeatureAsync failed: {addEx.Message}"); }
                }

                var newLayer = await CreateFeatureCollectionLayerAsync(memoryTable, subLayerName, null);
                if (newLayer != null)
                {
                    CurrentMapView.Map.OperationalLayers.Add(newLayer);
                    LayerManager.AddLayer(newLayer, true, "", memoryTable);
                }
                }
                groupIdx++;
            }
        }

        internal SyncDialog _syncDialog;
    }
}
