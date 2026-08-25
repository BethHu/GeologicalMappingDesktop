using Esri.ArcGISRuntime.Symbology;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinColor = System.Windows.Media.Color;
using SysColor = System.Drawing.Color;

namespace GeologicalMappingDesktop
{
    public partial class GridSettingsWindow : Window
    {
        private readonly GridOverlayManager _manager;
        private bool _updating = false;

        public GridSettingsWindow(GridOverlayManager manager)
        {
            InitializeComponent();
            _manager = manager;
            LoadSettings();
        }

        private void LoadSettings()
        {
            _updating = true;
            var s = _manager.Settings;
            ChkEnabled.IsChecked = s.Enabled;

            switch (s.DisplayMode)
            {
                case GridDisplayMode.GridOnly: RbGridOnly.IsChecked = true; break;
                case GridDisplayMode.LabelOnly: RbLabelOnly.IsChecked = true; break;
                default: RbBoth.IsChecked = true; break;
            }

            if (s.IntervalMode == IntervalMode.Fixed)
            {
                RbIntervalFixed.IsChecked = true;
                TxtInterval.Text = s.FixedInterval.ToString("0.#####");
            }
            else
            {
                RbIntervalAuto.IsChecked = true;
            }

            ClrLinePreview.Background = new SolidColorBrush(
                WinColor.FromArgb(s.LineColor.A, s.LineColor.R, s.LineColor.G, s.LineColor.B));
            TxtLineWidth.Text = s.LineWidth.ToString("0.#");

            TxtFontSize.Text = s.FontSize.ToString("0");
            ClrFontPreview.Background = new SolidColorBrush(
                WinColor.FromArgb(s.FontColor.A, s.FontColor.R, s.FontColor.G, s.FontColor.B));
            ChkBold.IsChecked = s.FontBold;
            ChkItalic.IsChecked = s.FontItalic;

            ChkTop.IsChecked = s.Top.ShowLabels;
            ChkBottom.IsChecked = s.Bottom.ShowLabels;
            ChkLeft.IsChecked = s.Left.ShowLabels;
            ChkRight.IsChecked = s.Right.ShowLabels;

            UpdateFixedIntervalEnabled();
            _updating = false;
        }

        private void SaveSettings()
        {
            if (_updating) return;
            var s = _manager.Settings;
            s.Enabled = ChkEnabled.IsChecked ?? false;

            if (RbGridOnly.IsChecked == true) s.DisplayMode = GridDisplayMode.GridOnly;
            else if (RbLabelOnly.IsChecked == true) s.DisplayMode = GridDisplayMode.LabelOnly;
            else s.DisplayMode = GridDisplayMode.Both;

            s.IntervalMode = RbIntervalFixed.IsChecked == true ? IntervalMode.Fixed : IntervalMode.Auto;
            if (double.TryParse(TxtInterval.Text, out double iv)) s.FixedInterval = iv;

            if (double.TryParse(TxtLineWidth.Text, out double lw)) s.LineWidth = lw;
            if (int.TryParse(TxtFontSize.Text, out int fs)) s.FontSize = fs;

            s.FontBold = ChkBold.IsChecked ?? false;
            s.FontItalic = ChkItalic.IsChecked ?? false;

            s.Top.ShowLabels = ChkTop.IsChecked ?? false;
            s.Bottom.ShowLabels = ChkBottom.IsChecked ?? false;
            s.Left.ShowLabels = ChkLeft.IsChecked ?? false;
            s.Right.ShowLabels = ChkRight.IsChecked ?? false;

            _manager.UpdateGrid();
        }

        private void ChkEnabled_Changed(object sender, RoutedEventArgs e) => SaveSettings();
        private void DisplayMode_Changed(object sender, RoutedEventArgs e) => SaveSettings();

        private void IntervalMode_Changed(object sender, RoutedEventArgs e)
        {
            UpdateFixedIntervalEnabled();
            SaveSettings();
        }

        private void UpdateFixedIntervalEnabled()
        {
            var parent = TxtInterval.Parent as StackPanel;
            if (parent != null) parent.IsEnabled = RbIntervalFixed.IsChecked == true;
        }

        private void TxtInterval_Changed(object sender, TextChangedEventArgs e) => SaveSettings();
        private void TxtLineWidth_Changed(object sender, TextChangedEventArgs e) => SaveSettings();
        private void TxtFontSize_Changed(object sender, TextChangedEventArgs e) => SaveSettings();
        private void FontStyle_Changed(object sender, RoutedEventArgs e) => SaveSettings();
        private void Side_Changed(object sender, RoutedEventArgs e) => SaveSettings();

        private void PickLineColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog();
            if (ClrLinePreview.Background is SolidColorBrush existing)
                dlg.Color = SysColor.FromArgb(existing.Color.A, existing.Color.R, existing.Color.G, existing.Color.B);
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var c = dlg.Color;
                _manager.Settings.LineColor = SysColor.FromArgb(c.A, c.R, c.G, c.B);
                ClrLinePreview.Background = new SolidColorBrush(WinColor.FromArgb(c.A, c.R, c.G, c.B));
                SaveSettings();
            }
        }

        private void PickFontColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog();
            if (ClrFontPreview.Background is SolidColorBrush existing)
                dlg.Color = SysColor.FromArgb(existing.Color.A, existing.Color.R, existing.Color.G, existing.Color.B);
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var c = dlg.Color;
                _manager.Settings.FontColor = SysColor.FromArgb(c.A, c.R, c.G, c.B);
                ClrFontPreview.Background = new SolidColorBrush(WinColor.FromArgb(c.A, c.R, c.G, c.B));
                SaveSettings();
            }
        }
    }
}
