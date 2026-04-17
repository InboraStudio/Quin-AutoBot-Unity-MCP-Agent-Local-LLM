using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuinAutobot
{
    [FilePath("QuinAutobot/AutoAgentCore.asset", FilePathAttribute.Location.PreferencesFolder)]
    public class AutoAgentCore : ScriptableSingleton<AutoAgentCore>
    {
        public event Action<ChatMessage>      OnMessageAdded;
        public event Action<ConnectionStatus> OnStatusChanged;
        public event Action<string>           OnModelChanged;
        public event Action<bool>             OnThinking;
        public event Action<int>              OnTokensUsed;
        public event Action<AgentCommand>     OnApprovalNeeded;
        public event Action<string>           OnStreamToken;

        [SerializeField] private string _lmStudioUrl        = "http://localhost:1234";
        [SerializeField] private string _model_version      = "";
        [SerializeField] private bool   _telemetryEnabled   = true;
        [SerializeField] private bool   _thinkingMode       = false;
        [SerializeField] private bool   _autoAllow          = false;
        [SerializeField] private int    _maxHistoryMessages = 40;
        [SerializeField] private float  _temperature        = 0.3f;
        [SerializeField] private string _apiKey             = "";

        public string LMStudioUrl      => _lmStudioUrl;
        public string ModelVersion     => _model_version;
        public bool   TelemetryEnabled => _telemetryEnabled;
        public bool   ThinkingMode     => _thinkingMode;
        public bool   AutoAllow        => _autoAllow;
        public string ApiKey           => _apiKey;

        private LMStudioBridge    _bridge;
        private AgentSession      _session;
        private TelemetryWatcher  _watcher;
        private CommandDispatcher _dispatcher;

        private TaskCompletionSource<bool> _approvalTcs;
        private string _pendingImageBase64;
        private string _pendingImageMime = "image/png";

        [InitializeOnLoadMethod]
        private static void Bootstrap() => instance.EnsureInitialized();

        private void EnsureInitialized()
        {
            if (_bridge != null) return;
            _session    = new AgentSession(_maxHistoryMessages);
            _bridge     = new LMStudioBridge(NormalizeUrl(_lmStudioUrl), _model_version, _temperature, _apiKey);
            _dispatcher = new CommandDispatcher();
            _watcher    = new TelemetryWatcher(this);
            _bridge.OnStatusChanged += s => OnStatusChanged?.Invoke(s);
            _bridge.OnModelChanged  += m => { _model_version = m; OnModelChanged?.Invoke(m); };
            _watcher.SetEnabled(_telemetryEnabled);
        }

        public async Task ProbeConnectionAsync()
        {
            EnsureInitialized();
            OnStatusChanged?.Invoke(ConnectionStatus.Probing);
            var (ok, model) = await _bridge.ProbeAsync();
            OnStatusChanged?.Invoke(ok ? ConnectionStatus.Connected : ConnectionStatus.Disconnected);
            if (!string.IsNullOrEmpty(_model_version)) OnModelChanged?.Invoke(_model_version);
            if (ok && !string.IsNullOrEmpty(model))    OnModelChanged?.Invoke(model);
        }

        public void AttachImage(string base64, string mime)
        {
            _pendingImageBase64 = base64;
            _pendingImageMime   = mime;
        }

        public void ClearPendingImage()
        {
            _pendingImageBase64 = null;
            _pendingImageMime   = "image/png";
        }

        public async void CaptureSceneAndQueryAsync(string prompt = null)
        {
            EnsureInitialized();
            string b64 = SceneCapture.CaptureSceneViewBase64();
            if (b64 == null)
            {
                PushMessage(new ChatMessage(ChatRole.Agent, "⚠ Could not capture scene view. Make sure the Scene window is open."));
                return;
            }
            string userPrompt = string.IsNullOrEmpty(prompt) ? "Analyze this screenshot of the Unity scene view and describe what you see." : prompt;
            PushMessage(new ChatMessage(ChatRole.User, $"[Scene Screenshot] {userPrompt}"));
            _session.AppendUser(userPrompt);
            OnThinking?.Invoke(true);
            try   { await RunAgentStepsAsync(b64, _pendingImageMime, 0); }
            finally { OnThinking?.Invoke(false); }
        }

        public async void SendUserMessageAsync(string userText)
        {
            EnsureInitialized();
            PushMessage(new ChatMessage(ChatRole.User, userText));
            _session.AppendUser(userText);

            string img  = _pendingImageBase64;
            string mime = _pendingImageMime;
            ClearPendingImage();

            OnThinking?.Invoke(true);
            try   { await RunAgentStepsAsync(img, mime, 0); }
            finally { OnThinking?.Invoke(false); }
        }

        private const int MaxAgentSteps = 8;

        private async Task RunAgentStepsAsync(string base64Image, string imageMime, int step)
        {
            if (step >= MaxAgentSteps)
            {
                PushMessage(new ChatMessage(ChatRole.Agent, $"Max steps ({MaxAgentSteps}) reached. Task may need further input."));
                return;
            }

            EnsureInitialized();
            try
            {
                var messages     = BuildMessages();
                string fullResponse = "";

                await _bridge.CompleteStreamingAsync(
                    messages,
                    onToken: tok => { fullResponse += tok; },
                    onDone:  _ => { },
                    base64Image: base64Image,
                    imageMime:   imageMime
                );

                if (string.IsNullOrEmpty(fullResponse))
                {
                    var resp = await _bridge.CompleteAsync(messages, base64Image, imageMime);
                    if (resp == null)
                    {
                        PushMessage(new ChatMessage(ChatRole.Agent, "Could not reach LM Studio. Check the connection in Settings."));
                        return;
                    }
                    fullResponse = resp.choices?[0]?.message?.content ?? "";
                    OnTokensUsed?.Invoke(resp.usage?.total_tokens ?? 0);
                }

                _session.AppendAssistant(fullResponse);

                string display = StripThinkTags(fullResponse, out string thinking);
                string reasonNote = string.IsNullOrEmpty(thinking) ? "" : $" [Reasoning: {thinking.Length} chars]";

                var command = _dispatcher.TryParse(display);

                if (command == null)
                {
                    PushMessage(new ChatMessage(ChatRole.Agent, display + reasonNote));
                    return;
                }

                bool isSendMessage = command.command?.type == "SEND_MESSAGE";

                if (!isSendMessage && !string.IsNullOrEmpty(command.thought))
                    PushMessage(new ChatMessage(ChatRole.Agent, command.thought + reasonNote));

                if (!isSendMessage && _dispatcher.RequiresApproval(command))
                {
                    if (_autoAllow)
                    {
                        PushMessage(new ChatMessage(ChatRole.Agent, $"[Auto-Allow] {command.command?.type}"));
                    }
                    else
                    {
                        _approvalTcs = new TaskCompletionSource<bool>();
                        OnApprovalNeeded?.Invoke(command);
                        bool approved = await _approvalTcs.Task;
                        if (!approved)
                        {
                            string denial = $"User denied: {command.command?.type}. Suggest an alternative approach.";
                            _session.AppendUser(denial);
                            PushMessage(new ChatMessage(ChatRole.Agent, "Understood — command cancelled."));
                            await RunAgentStepsAsync(null, "image/png", step + 1);
                            return;
                        }
                    }
                }

                if (!isSendMessage)
                    PushMessage(new ChatMessage(ChatRole.Command, $"[{command.command?.type}] {DescribePayload(command)}"));

                string result = await _dispatcher.ExecuteAsync(command);

                if (isSendMessage)
                {
                    PushMessage(new ChatMessage(ChatRole.Agent, result ?? ""));
                    return;
                }

                if (!string.IsNullOrEmpty(result))
                    PushMessage(new ChatMessage(ChatRole.Agent, result));

                string feedback = result?.StartsWith("failed", StringComparison.OrdinalIgnoreCase) == true
                    ? $"[STEP {step + 1} FAILED] {result}\nFix the error and try again."
                    : $"[STEP {step + 1} DONE] {result}\nIf more steps are needed, emit the next command. If the task is complete, use SEND_MESSAGE to confirm.";

                _session.AppendUser(feedback);
                await RunAgentStepsAsync(null, "image/png", step + 1);
            }
            catch (Exception ex)
            {
                PushMessage(new ChatMessage(ChatRole.Error, ex.Message));
            }
        }

        public void ApproveCommand(bool approved)
        {
            _approvalTcs?.TrySetResult(approved);
            _approvalTcs = null;
        }

        public async void HandleConsoleError(string condition, string stackTrace, LogType logType)
        {
            if (!_telemetryEnabled) return;
            string prompt = $"Unity console error:\nERROR: {condition}\nSTACK TRACE:\n{stackTrace}\nSCENE: {BuildSceneContext()}";
            PushMessage(new ChatMessage(ChatRole.Error, $"{condition}\n\n{stackTrace}"));
            _session.AppendUser(prompt);
            OnThinking?.Invoke(true);
            try   { await RunAgentStepsAsync(null, "image/png", 0); }
            finally { OnThinking?.Invoke(false); }
        }

        public async void SendSceneDescriptionAsync()
        {
            string desc   = BuildSceneContext();
            string prompt = $"Scene snapshot:\n{desc}\n\nWhat would you like me to do with it?";
            PushMessage(new ChatMessage(ChatRole.Agent, $"Scene context captured ({desc.Length} chars)."));
            _session.AppendUser(prompt);
        }

        public void ClearSession()            => _session?.Clear();
        public void SetAutoAllow(bool v)       { _autoAllow = v; Save(true); }
        public void SetTelemetryEnabled(bool v){ _telemetryEnabled = v; _watcher?.SetEnabled(v); Save(true); }
        public void SetThinkingMode(bool v)    { _thinkingMode = v; Save(true); }
        public void SetTemperature(float v)    { _temperature = v; _bridge?.SetTemperature(v); Save(true); }
        public void SetMaxHistory(int v)       { _maxHistoryMessages = v; _session?.SetMaxHistory(v); Save(true); }
        public void SetModelVersion(string v)  { _model_version = v; _bridge?.SetModel(v); OnModelChanged?.Invoke(v); Save(true); }
        public void SetApiKey(string v)        { _apiKey = v; _bridge?.SetApiKey(v); Save(true); }

        public void SetLMStudioUrl(string url)
        {
            string n = NormalizeUrl(url);
            _lmStudioUrl = n;
            _bridge?.SetUrl(n);
            Save(true);
        }

        private List<LMMessage> BuildMessages()
        {
            var msgs = new List<LMMessage>();
            msgs.Add(new LMMessage("system", SystemPromptProvider.GetPrompt(_thinkingMode)));
            string ctx = BuildSceneContext() + "\n" + BuildProjectContext();
            msgs.Add(new LMMessage("user", $"[LIVE UNITY CONTEXT]\n{ctx}"));
            msgs.Add(new LMMessage("assistant", "Context received. I know the scene objects and project assets."));
            msgs.AddRange(_session.GetHistory());
            return msgs;
        }

        private string BuildProjectContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Unity {Application.unityVersion} | Product: {Application.productName}");

            var scriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
            sb.AppendLine($"\nScripts ({scriptGuids.Length}):");
            int sCount = 0;
            foreach (var g in scriptGuids)
            {
                if (sCount++ > 40) { sb.AppendLine("  ..."); break; }
                sb.AppendLine($"  {AssetDatabase.GUIDToAssetPath(g)}");
            }

            var matGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
            sb.AppendLine($"\nMaterials ({matGuids.Length}):");
            int mCount = 0;
            foreach (var g in matGuids)
            {
                if (mCount++ > 20) { sb.AppendLine("  ..."); break; }
                sb.AppendLine($"  {AssetDatabase.GUIDToAssetPath(g)}");
            }

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            sb.AppendLine($"\nPrefabs ({prefabGuids.Length}):");
            int pCount = 0;
            foreach (var g in prefabGuids)
            {
                if (pCount++ > 20) { sb.AppendLine("  ..."); break; }
                sb.AppendLine($"  {AssetDatabase.GUIDToAssetPath(g)}");
            }

            return sb.ToString();
        }

        private string BuildSceneContext()
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var sb    = new StringBuilder();
            sb.AppendLine($"Scene: {scene.name} | Objects: {roots.Length}");
            foreach (var go in roots) DescribeGO(go, sb, 0);
            return sb.ToString();
        }

        private void DescribeGO(GameObject go, StringBuilder sb, int depth)
        {
            string indent = new string(' ', depth * 2);
            sb.AppendLine($"{indent}- {go.name} [{(go.activeInHierarchy ? "on" : "off")}]");
            foreach (var c in go.GetComponents<Component>())
                if (c != null) sb.AppendLine($"{indent}    • {c.GetType().Name}");
            for (int i = 0; i < go.transform.childCount; i++)
                DescribeGO(go.transform.GetChild(i).gameObject, sb, depth + 1);
        }

        private static string StripThinkTags(string text, out string thinking)
        {
            thinking = "";
            int s = text.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
            int e = text.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (s >= 0 && e > s)
            {
                thinking = text.Substring(s + 7, e - s - 7).Trim();
                text     = (text.Substring(0, s) + text.Substring(e + 8)).Trim();
            }
            return text;
        }

        private static string DescribePayload(AgentCommand cmd)
        {
            var p = cmd.command?.payload;
            if (p == null) return "";
            try {
                return p.ToString(Newtonsoft.Json.Formatting.None);
            } catch {
                return "{ ... }";
            }
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "http://localhost:1234";
            url = url.Trim();
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "http://" + url;
            return url.TrimEnd('/');
        }

        private void PushMessage(ChatMessage msg) => OnMessageAdded?.Invoke(msg);
    }
}
