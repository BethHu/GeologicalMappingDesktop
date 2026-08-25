using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using DrawingColor = System.Drawing.Color;

namespace GeologicalMappingDesktop
{
    public enum GridDisplayMode { GridOnly, LabelOnly, Both }
    public enum IntervalMode { Auto, Fixed }

    public class GridSideSettings
    {
        public bool ShowLabels { get; set; } = true;
        public bool VerticalOrientation { get; set; } = false;
    }

    public class GridSettings
    {
        public bool Enabled { get; set; } = false;
        public GridDisplayMode DisplayMode { get; set; } = GridDisplayMode.Both;
        public IntervalMode IntervalMode { get; set; } = IntervalMode.Auto;
        public double FixedInterval { get; set; } = 1.0;
        public DrawingColor LineColor { get; set; } = DrawingColor.FromArgb(128, 100, 100, 100);
        public double LineWidth { get; set; } = 0.5;
        public double FontSize { get; set; } = 10;
        public string FontName { get; set; } = "Times New Roman";
        public DrawingColor FontColor { get; set; } = DrawingColor.FromArgb(180, 60, 60, 60);
        public bool FontBold { get; set; } = false;
        public bool FontItalic { get; set; } = false;
        public GridSideSettings Top { get; set; } = new GridSideSettings { ShowLabels = true, VerticalOrientation = false };
        public GridSideSettings Bottom { get; set; } = new GridSideSettings { ShowLabels = true, VerticalOrientation = false };
        public GridSideSettings Left { get; set; } = new GridSideSettings { ShowLabels = true, VerticalOrientation = true };
        public GridSideSettings Right { get; set; } = new GridSideSettings { ShowLabels = true, VerticalOrientation = true };
        public double AutoInterval(double scale)
        {
            double mPerPx = scale / 96.0 / 0.0254;
            double viewDeg = mPerPx * 500 / 111320.0;
            double[] nice = { 0.0001, 0.0002, 0.0005, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 30 };
            foreach (var v in nice) if (v >= viewDeg * 3) return v;
            return 30;
        }
    }

    public class GridOverlayManager
    {
        private readonly MapView _mapView;
        private readonly GraphicsOverlay _gridOverlay;
        public GridSettings Settings { get; } = new GridSettings();
        private bool _attached = true;

        public GridOverlayManager(MapView mapView)
        {
            _mapView = mapView;
            _gridOverlay = new GraphicsOverlay { Id = "MapGridOverlay" };
            mapView.GraphicsOverlays.Add(_gridOverlay);
            mapView.ViewpointChanged += OnViewpointChanged;
        }

        private void OnViewpointChanged(object sender, EventArgs e)
        {
            if (Settings.Enabled)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => UpdateGrid()),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        public void UpdateGrid()
        {
            _gridOverlay.Graphics.Clear();
            if (!Settings.Enabled) return;

            var vp = _mapView.GetCurrentViewpoint(ViewpointType.BoundingGeometry);
            if (vp?.TargetGeometry is Envelope env && env.SpatialReference != null)
            {
                var projEnv = GeometryEngine.Project(env, SpatialReferences.Wgs84) as Envelope;
                if (projEnv == null) return;
                double interval = Settings.IntervalMode == IntervalMode.Auto
                    ? Settings.AutoInterval(_mapView.MapScale) : Settings.FixedInterval;
                int dec = interval < 0.01 ? 4 : interval < 0.1 ? 3 : interval < 1 ? 2 : interval < 10 ? 1 : 0;
                double minLon = Math.Floor(projEnv.XMin / interval) * interval;
                double maxLon = Math.Ceiling(projEnv.XMax / interval) * interval;
                double minLat = Math.Floor(projEnv.YMin / interval) * interval;
                double maxLat = Math.Ceiling(projEnv.YMax / interval) * interval;

                var lines = new List<Graphic>();
                var labels = new List<Graphic>();
                bool hasGrid = Settings.DisplayMode != GridDisplayMode.LabelOnly;
                bool hasLabel = Settings.DisplayMode != GridDisplayMode.GridOnly;

                if (hasGrid)
                {
                    var lineSym = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, Settings.LineColor, Settings.LineWidth);
                    for (double lon = minLon; lon <= maxLon; lon += interval)
                    {
                        var pts = new List<MapPoint> { new MapPoint(lon, minLat), new MapPoint(lon, maxLat) };
                        lines.Add(new Graphic(new Polyline(pts, SpatialReferences.Wgs84), lineSym));
                    }
                    for (double lat = minLat; lat <= maxLat; lat += interval)
                    {
                        var pts = new List<MapPoint> { new MapPoint(minLon, lat), new MapPoint(maxLon, lat) };
                        lines.Add(new Graphic(new Polyline(pts, SpatialReferences.Wgs84), lineSym));
                    }
                }

                if (hasLabel)
                {
                    var fc = Settings.FontColor;
                    double fs = Settings.FontSize;
                    bool bold = Settings.FontBold;
                    bool italic = Settings.FontItalic;
                    if (Settings.Top.ShowLabels)
                        for (double lon = minLon; lon <= maxLon; lon += interval)
                            AddLabel(labels, lon, maxLat, FormatCoord(lon, dec), fc, fs, bold, italic, Settings.Top.VerticalOrientation);
                    if (Settings.Bottom.ShowLabels)
                        for (double lon = minLon; lon <= maxLon; lon += interval)
                            AddLabel(labels, lon, minLat, FormatCoord(lon, dec), fc, fs, bold, italic, Settings.Bottom.VerticalOrientation);
                    if (Settings.Left.ShowLabels)
                        for (double lat = minLat; lat <= maxLat; lat += interval)
                            AddLabel(labels, minLon, lat, FormatCoord(lat, dec), fc, fs, bold, italic, Settings.Left.VerticalOrientation);
                    if (Settings.Right.ShowLabels)
                        for (double lat = minLat; lat <= maxLat; lat += interval)
                            AddLabel(labels, maxLon, lat, FormatCoord(lat, dec), fc, fs, bold, italic, Settings.Right.VerticalOrientation);
                }

                foreach (var g in lines) _gridOverlay.Graphics.Add(g);
                foreach (var g in labels) _gridOverlay.Graphics.Add(g);
            }
        }

        private static void AddLabel(List<Graphic> list, double lon, double lat, string text, DrawingColor color, double fontSize, bool bold, bool italic, bool vertical)
        {
            var sym = new TextSymbol(text, color, fontSize, HorizontalAlignment.Center, VerticalAlignment.Middle);
            if (bold) sym.FontWeight = FontWeight.Bold;
            if (italic) sym.FontStyle = FontStyle.Italic;
            if (vertical) sym.Angle = 90;
            list.Add(new Graphic(new MapPoint(lon, lat, SpatialReferences.Wgs84), sym));
        }

        private static string FormatCoord(double val, int dec)
        {
            double abs = Math.Abs(val);
            int d = (int)abs;
            double m = (abs - d) * 60;
            if (m < 0.01) return $"{d}°";
            return $"{d}°{m:F0}'";
        }

        public void Detach()
        {
            if (!_attached) return;
            _attached = false;
            _mapView.ViewpointChanged -= OnViewpointChanged;
            _mapView.GraphicsOverlays.Remove(_gridOverlay);
        }
    }
}
