using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace GeologicalMappingDesktop
{
    public partial class SyncDialog : Window
    {
        public ObservableCollection<string> LogEntries { get; } = new ObservableCollection<string>();
        private SyncService _syncService;

        public SyncDialog()
        {
            InitializeComponent();
            LogList.ItemsSource = LogEntries;
            RdoServerMode.Checked += (s, e) => UpdateMode();
            RdoClientMode.Checked += (s, e) => UpdateMode();
            _syncService = new SyncService();
            _syncService.LogMessage += msg =>
            {
                Dispatcher.Invoke(() => AddLog(msg));
            };
            _syncService.DataReceived += OnDataReceived;
        }

        private void UpdateMode()
        {
            bool isServer = RdoServerMode.IsChecked == true;
            TxtServerAddress.Text = isServer ? "http://0.0.0.0:5800" : "http://192.168.1.100:5800";
            TxtServerAddress.IsEnabled = !isServer;
            BtnStartStop.Content = _syncService.IsRunning ? "停止服务" : "启动服务";
        }

        private void AddLog(string msg)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogEntries.Add($"[{timestamp}] {msg}");
            if (LogEntries.Count > 200)
                LogEntries.RemoveAt(0);
        }

        private async void BtnStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (_syncService.IsRunning)
            {
                _syncService.Stop();
                BtnStartStop.Content = "启动服务";
                AddLog("同步服务已停止");
                return;
            }

            bool isServer = RdoServerMode.IsChecked == true;
            string address = TxtServerAddress.Text.Trim();

            try
            {
                BtnStartStop.IsEnabled = false;
                if (isServer)
                {
                    AddLog("正在启动同步服务...");
                    await _syncService.StartServerAsync(address, GetCurrentLayersForSync);
                    AddLog($"服务已启动: {address}");
                }
                else
                {
                    AddLog($"正在连接到服务器 {address}...");
                    await _syncService.StartClientAsync(address, GetCurrentLayersForSync);
                    AddLog("已连接到服务器");
                }
                BtnStartStop.Content = "停止服务";
            }
            catch (Exception ex)
            {
                AddLog($"错误: {ex.Message}");
                MessageBox.Show($"启动同步失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnStartStop.IsEnabled = true;
            }
        }

        private string GetCurrentLayersForSync()
        {
            var main = Owner as MainWindow;
            if (main == null) return "[]";
            return main.GetCurrentFeaturesAsGeoJson();
        }

        private async void OnDataReceived(string geoJson)
        {
            await Dispatcher.Invoke(async () =>
            {
                var features = GeoJsonHelper.ParseGeoJsonFeatures(geoJson);
                AddLog($"收到 {features.Count} 个要素");

                var main = Owner as MainWindow;
                if (main != null && features.Count > 0)
                {
                    string layerName = $"同步数据_{DateTime.Now:HHmmss}";
                    await main.ImportFeaturesAsNewLayer(features, layerName);
                    AddLog($"已导入为图层 \"{layerName}\"");
                }
            });
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_syncService.IsRunning)
                _syncService.Stop();
            Hide();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            if (_syncService.IsRunning)
                _syncService.Stop();
            Hide();
        }
    }
}
