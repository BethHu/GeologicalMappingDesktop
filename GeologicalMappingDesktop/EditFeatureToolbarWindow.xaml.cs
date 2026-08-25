using System;
using System.Windows;

namespace GeologicalMappingDesktop
{
    public partial class EditFeatureToolbarWindow : Window
    {
        public event Action DeletePointRequested;
        public event Action AddPointRequested;
        public event Action<bool> ModifyPointToggled;
        public event Action<bool> TopologyLockToggled;
        public event Action<double> SmoothingChanged;
        public event Action ApplySmoothRequested;
        public event Action ExitRequested;

        public EditFeatureToolbarWindow()
        {
            InitializeComponent();
            SmoothSlider.ValueChanged += SmoothSlider_ValueChanged;
            UpdateEditHint();
        }

        private void UpdateEditHint()
        {
            TxtEditHint.Text = "Esc 退出编辑 | Delete 删除选中顶点 | 拖拽移动顶点";
        }

        private void BtnDeletePoint_Click(object sender, RoutedEventArgs e)
        {
            DeletePointRequested?.Invoke();
        }

        private void BtnAddPoint_Click(object sender, RoutedEventArgs e)
        {
            AddPointRequested?.Invoke();
        }

        private void BtnModifyPoint_Click(object sender, RoutedEventArgs e)
        {
            ModifyPointToggled?.Invoke(BtnModifyPoint.IsChecked == true);
        }

        private void BtnTopologyLock_Click(object sender, RoutedEventArgs e)
        {
            TopologyLockToggled?.Invoke(BtnTopologyLock.IsChecked == true);
        }

        private void SmoothSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int val = (int)e.NewValue;
            TxtSmoothValue.Text = val.ToString();
            SmoothingChanged?.Invoke(e.NewValue);
        }

        private void BtnApplySmooth_Click(object sender, RoutedEventArgs e)
        {
            ApplySmoothRequested?.Invoke();
        }

        public void SetModifyPointActive(bool active)
        {
            BtnModifyPoint.IsChecked = active;
        }

        public bool IsModifyPointActive => BtnModifyPoint.IsChecked == true;

        public int SmoothnessValue => (int)SmoothSlider.Value;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
