using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GeologicalMappingDesktop
{
    public class SyncService
    {
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Func<string> _getLocalDataFn;
        private bool _isRunning;

        public bool IsRunning => _isRunning;
        public event Action<string> LogMessage;
        public event Action<string> DataReceived;

        public async Task StartServerAsync(string address, Func<string> getLocalDataFn)
        {
            if (_isRunning) throw new InvalidOperationException("服务已在运行");

            _getLocalDataFn = getLocalDataFn;
            _listener = new HttpListener();
            _listener.Prefixes.Add(address.EndsWith("/") ? address : address + "/");
            _listener.Start();
            _cts = new CancellationTokenSource();
            _isRunning = true;

            LogMessage?.Invoke($"监听地址: {address}");

            await ListenLoop(_cts.Token);
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    await HandleRequest(context);
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                string path = request.Url.AbsolutePath.TrimEnd('/');

                if (request.HttpMethod == "GET" && path == "/api/ping")
                {
                    await RespondJson(response, "{\"status\":\"ok\",\"name\":\"GeologicalMappingDesktop\"}");
                    LogMessage?.Invoke("收到 ping 请求");
                }
                else if (request.HttpMethod == "GET" && path == "/api/pull")
                {
                    string data = _getLocalDataFn?.Invoke() ?? "[]";
                    await RespondJson(response, data);
                    LogMessage?.Invoke("移动端拉取数据完成");
                }
                else if (request.HttpMethod == "POST" && path == "/api/push")
                {
                    using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                    {
                        string body = await reader.ReadToEndAsync();
                        DataReceived?.Invoke(body);
                        LogMessage?.Invoke($"收到移动端推送数据 ({body.Length} 字节)");
                    }
                    await RespondJson(response, "{\"status\":\"ok\"}");
                }
                else
                {
                    response.StatusCode = 404;
                    await RespondJson(response, "{\"error\":\"not found\"}");
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"处理请求失败: {ex.Message}");
                try { response.StatusCode = 500; await RespondJson(response, $"{{\"error\":\"{ex.Message}\"}}"); }
                catch { }
            }
            finally
            {
                try { response.Close(); } catch { }
            }
        }

        private async Task RespondJson(HttpListenerResponse response, string json)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        public async Task StartClientAsync(string serverAddress, Func<string> getLocalDataFn)
        {
            if (_isRunning) throw new InvalidOperationException("服务已在运行");
            _getLocalDataFn = getLocalDataFn;
            _isRunning = true;

            try
            {
                using (var client = new WebClient { Encoding = Encoding.UTF8 })
                {
                    // Push local data
                    string localData = getLocalDataFn?.Invoke() ?? "[]";
                    LogMessage?.Invoke($"推送本地数据 ({localData.Length} 字节)...");
                    string pushResult = await client.UploadStringTaskAsync(
                        new Uri(serverAddress.TrimEnd('/') + "/api/push"),
                        "POST", localData);
                    LogMessage?.Invoke($"推送结果: {pushResult}");

                    // Pull remote data
                    LogMessage?.Invoke("拉取远程数据...");
                    string remoteData = await client.DownloadStringTaskAsync(
                        new Uri(serverAddress.TrimEnd('/') + "/api/pull"));
                    DataReceived?.Invoke(remoteData);
                    LogMessage?.Invoke($"拉取完成 ({remoteData.Length} 字节)");
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"同步失败: {ex.Message}");
                throw;
            }
            finally
            {
                _isRunning = false;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _cts?.Cancel();
            if (_listener != null && _listener.IsListening)
            {
                try { _listener.Stop(); } catch { }
                try { _listener.Close(); } catch { }
            }
        }
    }
}
