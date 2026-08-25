using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Web.Script.Serialization;

namespace GeologicalMappingDesktop
{
    public partial class CloudSyncDialog : Window
    {
        private string _token;
        private string _username;
        private string _serverUrl;
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GeologicalMappingDesktop", "cloudsync.json");

        public string Token => _token;
        public string UserName => _username;
        public string ServerUrl => _serverUrl;
        public bool IsLoggedIn => !string.IsNullOrEmpty(_token);

        public CloudSyncDialog()
        {
            InitializeComponent();
            LoadSavedAuth();
        }

        private void LoadSavedAuth()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var raw = File.ReadAllText(ConfigPath);
                    var data = _json.Deserialize<Dictionary<string, object>>(raw);
                    if (data != null)
                    {
                        _token = data.ContainsKey("token") ? Convert.ToString(data["token"]) : "";
                        _username = data.ContainsKey("username") ? Convert.ToString(data["username"]) : "";
                        _serverUrl = data.ContainsKey("serverUrl") ? Convert.ToString(data["serverUrl"]) : "";

                        if (!string.IsNullOrEmpty(_token) && !string.IsNullOrEmpty(_username))
                        {
                            TxtServerUrl.Text = _serverUrl ?? "";
                            ShowLoggedInUI();
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveAuth()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var data = _json.Serialize(new Dictionary<string, object>
                {
                    { "token", _token },
                    { "username", _username },
                    { "serverUrl", _serverUrl ?? "" }
                });
                File.WriteAllText(ConfigPath, data);
            }
            catch { }
        }

        private void ClearSavedAuth()
        {
            try { if (File.Exists(ConfigPath)) File.Delete(ConfigPath); } catch { }
        }

        private string BaseUrl => TxtServerUrl.Text.Trim().TrimEnd('/');

        private async void BtnLogin_Click(object sender, RoutedEventArgs e) => await DoAuth("login");
        private async void BtnRegister_Click(object sender, RoutedEventArgs e) => await DoAuth("register");

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            _token = null;
            _username = null;
            _serverUrl = null;
            ClearSavedAuth();
            TxtLoggedInUser.Text = "";
            UserInfoBar.Visibility = Visibility.Collapsed;
            LoginForm.Visibility = Visibility.Visible;
            BtnLogout.Visibility = Visibility.Collapsed;
            BtnLogin.Visibility = Visibility.Visible;
            BtnRegister.Visibility = Visibility.Visible;
            TxtUsername.IsEnabled = true;
            TxtPassword.IsEnabled = true;
            TxtServerUrl.IsEnabled = true;
            TxtStatus.Text = "已退出登录";
            TxtStatus.Foreground = System.Windows.Media.Brushes.Gray;
        }

        private void ShowLoggedInUI()
        {
            TxtLoggedInUser.Text = $"欢迎，{_username}";
            TxtLoggedInStatus.Text = $"服务器: {_serverUrl}";
            UserInfoBar.Visibility = Visibility.Visible;
            LoginForm.Visibility = Visibility.Collapsed;
            BtnLogout.Visibility = Visibility.Visible;
            BtnLogin.Visibility = Visibility.Collapsed;
            BtnRegister.Visibility = Visibility.Collapsed;
            TxtUsername.IsEnabled = false;
            TxtPassword.IsEnabled = false;
            TxtServerUrl.IsEnabled = false;
        }

        private async Task DoAuth(string action)
        {
            _serverUrl = BaseUrl;
            var url = $"{_serverUrl}/api/auth/{action}";
            var body = _json.Serialize(new Dictionary<string, object> {
                { "username", TxtUsername.Text.Trim() },
                { "password", TxtPassword.Password }
            });

            try
            {
                TxtStatus.Text = "请稍候...";
                var response = await _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
                var raw = await response.Content.ReadAsStringAsync();
                var result = _json.Deserialize<Dictionary<string, object>>(raw);

                if (result != null && result.ContainsKey("ok") && Convert.ToBoolean(result["ok"]))
                {
                    _token = result.ContainsKey("token") ? Convert.ToString(result["token"]) : "";
                    var user = result.ContainsKey("user") ? result["user"] as Dictionary<string, object> : null;
                    _username = user != null && user.ContainsKey("username") ? Convert.ToString(user["username"]) : "";
                    SaveAuth();
                    TxtStatus.Text = $"{(action == "login" ? "登录" : "注册")}成功";
                    TxtStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                    ShowLoggedInUI();
                }
                else
                {
                    var msg = result != null && result.ContainsKey("message") ? Convert.ToString(result["message"]) : "未知错误";
                    TxtStatus.Text = msg;
                    TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"连接失败: {ex.Message}";
                TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;
            }
        }
    }
}
