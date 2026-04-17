using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace QuinAutobot
{
    public class LMStudioBridge
    {
        public event Action<ConnectionStatus> OnStatusChanged;
        public event Action<string>           OnModelChanged;

        private string _baseUrl;
        private string _model_version;
        private float  _temperature;
        private string _apiKey;

        private const string ChatEndpoint   = "/v1/chat/completions";
        private const string ModelsEndpoint = "/v1/models";
        private const int    TimeoutSeconds = 120;

        public LMStudioBridge(string baseUrl, string modelVersion, float temperature, string apiKey = "")
        {
            _baseUrl       = baseUrl.TrimEnd('/');
            _model_version = modelVersion;
            _temperature   = temperature;
            _apiKey        = apiKey;
        }

        public void SetUrl(string url)     => _baseUrl       = url.TrimEnd('/');
        public void SetModel(string model) => _model_version = model;
        public void SetTemperature(float t)=> _temperature   = t;
        public void SetApiKey(string k)    => _apiKey        = k;

        public async Task<(bool ok, string model)> ProbeAsync()
        {
            try
            {
                using var req = UnityWebRequest.Get(_baseUrl + ModelsEndpoint);
                if (!string.IsNullOrEmpty(_apiKey))
                    req.SetRequestHeader("Authorization", "Bearer " + _apiKey);
                req.timeout = 10;
                await SendAsync(req);
                if (req.result != UnityWebRequest.Result.Success) return (false, null);

                var resp = JsonUtility.FromJson<LMModelsResponse>(req.downloadHandler.text);
                string first = (resp?.data?.Length > 0) ? resp.data[0].id : null;
                if (!string.IsNullOrEmpty(first) && string.IsNullOrEmpty(_model_version))
                {
                    _model_version = first;
                    OnModelChanged?.Invoke(first);
                }
                else if (!string.IsNullOrEmpty(_model_version))
                {
                    OnModelChanged?.Invoke(_model_version);
                }
                return (true, _model_version);
            }
            catch { return (false, null); }
        }

        public async Task<LMChatResponse> CompleteAsync(List<LMMessage> messages, string base64Image = null, string imageMime = "image/png")
        {
            string json    = BuildJson(messages, stream: false, base64Image, imageMime);
            byte[] raw     = Encoding.UTF8.GetBytes(json);
            using var req  = new UnityWebRequest(_baseUrl + ChatEndpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(raw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(_apiKey))
                req.SetRequestHeader("Authorization", "Bearer " + _apiKey);
            req.timeout = TimeoutSeconds;

            try
            {
                await SendAsync(req);
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[QuinAutobot] LM Studio error: {req.error}\n{req.downloadHandler?.text}");
                    return null;
                }
                return JsonUtility.FromJson<LMChatResponse>(req.downloadHandler.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuinAutobot] CompleteAsync exception: {ex.Message}");
                return null;
            }
        }

        public async Task CompleteStreamingAsync(
            List<LMMessage> messages,
            Action<string> onToken,
            Action<string> onDone,
            string base64Image = null,
            string imageMime   = "image/png")
        {
            string json   = BuildJson(messages, stream: true, base64Image, imageMime);
            byte[] raw    = Encoding.UTF8.GetBytes(json);

            var handler  = new StreamHandler(onToken);
            using var req = new UnityWebRequest(_baseUrl + ChatEndpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(raw);
            req.downloadHandler = handler;
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(_apiKey))
                req.SetRequestHeader("Authorization", "Bearer " + _apiKey);
            req.timeout = TimeoutSeconds;

            try
            {
                await SendAsync(req);
                onDone?.Invoke(handler.FullText);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuinAutobot] Streaming exception: {ex.Message}");
                onDone?.Invoke(null);
            }
        }

        private string BuildJson(List<LMMessage> messages, bool stream, string base64Image, string imageMime)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":\"{J(_model_version)}\",");
            sb.Append($"\"temperature\":{_temperature.ToString("F2", CultureInfo.InvariantCulture)},");
            sb.Append("\"max_tokens\":4096,");
            sb.Append($"\"stream\":{(stream ? "true" : "false")},");
            sb.Append("\"messages\":[");

            for (int i = 0; i < messages.Count; i++)
            {
                var msg           = messages[i];
                bool attachImage  = base64Image != null && i == messages.Count - 1 && msg.role == "user";

                sb.Append("{");
                sb.Append($"\"role\":\"{J(msg.role)}\",");

                if (attachImage)
                {
                    sb.Append("\"content\":[");
                    sb.Append($"{{\"type\":\"text\",\"text\":\"{J(msg.content)}\"}},");
                    sb.Append($"{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"data:{imageMime};base64,{base64Image}\"}}}}");
                    sb.Append("]");
                }
                else
                {
                    sb.Append($"\"content\":\"{J(msg.content)}\"");
                }

                sb.Append(i < messages.Count - 1 ? "}," : "}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static string J(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        private static Task SendAsync(UnityWebRequest req)
        {
            var tcs = new TaskCompletionSource<bool>();
            req.SendWebRequest().completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }

        private class StreamHandler : DownloadHandlerScript
        {
            private readonly Action<string> _onToken;
            private readonly StringBuilder  _full   = new StringBuilder();
            private readonly StringBuilder  _buf    = new StringBuilder();

            public string FullText => _full.ToString();

            public StreamHandler(Action<string> onToken) : base(new byte[32768])
            {
                _onToken = onToken;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                _buf.Append(Encoding.UTF8.GetString(data, 0, dataLength));
                Flush();
                return true;
            }

            private void Flush()
            {
                string raw   = _buf.ToString();
                int    nl    = raw.LastIndexOf('\n');
                if (nl < 0) return;

                string ready = raw.Substring(0, nl + 1);
                _buf.Clear();
                _buf.Append(raw.Substring(nl + 1));

                foreach (var line in ready.Split('\n'))
                {
                    string l = line.Trim();
                    if (!l.StartsWith("data: ")) continue;
                    string payload = l.Substring(6).Trim();
                    if (payload == "[DONE]") continue;

                    try
                    {
                        var delta  = JsonUtility.FromJson<StreamDelta>(payload);
                        string tok = delta?.choices?[0]?.delta?.content;
                        if (!string.IsNullOrEmpty(tok))
                        {
                            _full.Append(tok);
                            _onToken?.Invoke(tok);
                        }
                    }
                    catch { }
                }
            }

            protected override float GetProgress() => 0f;
        }
    }
}
