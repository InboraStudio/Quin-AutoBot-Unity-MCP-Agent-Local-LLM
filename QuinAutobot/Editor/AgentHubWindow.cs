using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuinAutobot
{
    public class AgentHubWindow : EditorWindow
    {
        private const string WindowTitle = "Quin Autobot";

        private VisualElement _root;
        private VisualElement _mainView;
        private VisualElement _settingsPanel;
        private ScrollView    _chatScroll;
        private VisualElement _messageList;
        private VisualElement _welcomeState;
        private VisualElement _thinkingBar;
        private VisualElement _attachPopup;
        private TextField     _inputField;
        private Label         _lblModel;
        private Label         _lblCpu;
        private Label         _lblMem;
        private Label         _lblFps;
        private Label         _lblTokens;
        private VisualElement _statusDot;
        private VisualElement _imagePreview;
        private Label         _lblImageName;

        private Button _btnSend;
        private Button _btnAttach;
        private Button _btnProbe;
        private Button _btnClear;
        private Button _btnTelemetry;
        private Button _btnSettings;
        private Button _btnThink;
        private Button _btnImage;
        private Button _btnAutoAllow;
        private Button _chipSpawn;
        private Button _chipFix;
        private Button _chipScript;
        private Button _chipExplain;

        private Button _attachSpawn;
        private Button _attachScript;
        private Button _attachScene;
        private Button _attachDocs;
        private Button _attachCapture;

        private TextField _settingsUrl;
        private TextField _settingsApiKey;
        private TextField _settingsModel;
        private TextField _settingsTemp;
        private TextField _settingsHistory;
        private Toggle    _settingsTelemetry;
        private Button    _settingsSave;
        private Button    _settingsReset;
        private Button    _settingsBackBtn;

        private bool _attachOpen      = false;
        private bool _telemetryActive = true;
        private bool _thinkingActive  = false;
        private bool _autoAllowActive = false;

        private AutoAgentCore _core;
        private readonly Stopwatch _perfWatch = Stopwatch.StartNew();
        private long _lastPerfMs;
        private int  _frameCount;

        [MenuItem("Window/Quin Autobot/Agent Hub")]
        public static void OpenWindow()
        {
            var win = GetWindow<AgentHubWindow>();
            win.titleContent = new GUIContent(WindowTitle);
            win.minSize = new Vector2(380, 520);
        }

        private void CreateGUI()
        {
            _core = AutoAgentCore.instance;
            _core.OnMessageAdded   += HandleMessageAdded;
            _core.OnStatusChanged  += HandleStatusChanged;
            _core.OnModelChanged   += HandleModelChanged;
            _core.OnThinking       += HandleThinking;
            _core.OnTokensUsed     += HandleTokensUsed;
            _core.OnApprovalNeeded += HandleApprovalNeeded;

            var uxmlGuids = AssetDatabase.FindAssets("AgentHubWindow t:VisualTreeAsset");
            if (uxmlGuids.Length == 0) { DrawFallback(); return; }

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                AssetDatabase.GUIDToAssetPath(uxmlGuids[0]));

            var ussGuids = AssetDatabase.FindAssets("AgentHubWindow t:StyleSheet");
            if (ussGuids.Length > 0)
                rootVisualElement.styleSheets.Add(
                    AssetDatabase.LoadAssetAtPath<StyleSheet>(
                        AssetDatabase.GUIDToAssetPath(ussGuids[0])));

            _root = uxml.CloneTree();
            _root.style.flexGrow = 1;
            rootVisualElement.Add(_root);

            BindElements();
            RegisterCallbacks();
            PopulateSettingsFields();
            SyncThinkingButton();
            SyncAutoAllowButton();
            RefreshWelcomeState();

            if (!string.IsNullOrEmpty(_core.ModelVersion))
                HandleModelChanged(_core.ModelVersion);

            EditorApplication.update += OnEditorUpdate;
            _ = _core.ProbeConnectionAsync();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            if (_core == null) return;
            _core.OnMessageAdded   -= HandleMessageAdded;
            _core.OnStatusChanged  -= HandleStatusChanged;
            _core.OnModelChanged   -= HandleModelChanged;
            _core.OnThinking       -= HandleThinking;
            _core.OnTokensUsed     -= HandleTokensUsed;
            _core.OnApprovalNeeded -= HandleApprovalNeeded;
        }

        private void BindElements()
        {
            _mainView      = _root.Q<VisualElement>("main-view");
            _settingsPanel = _root.Q<VisualElement>("settings-panel");
            _chatScroll    = _root.Q<ScrollView>("chat-scroll");
            _messageList   = _root.Q<VisualElement>("message-list");
            _welcomeState  = _root.Q<VisualElement>("welcome-state");
            _thinkingBar   = _root.Q<VisualElement>("thinking-bar");
            _attachPopup   = _root.Q<VisualElement>("attach-popup");
            _inputField    = _root.Q<TextField>("input-field");
            _imagePreview  = _root.Q<VisualElement>("image-preview");
            _lblImageName  = _root.Q<Label>("lbl-image-name");
            _lblModel      = _root.Q<Label>("lbl-model");
            _lblCpu        = _root.Q<Label>("lbl-cpu");
            _lblMem        = _root.Q<Label>("lbl-mem");
            _lblFps        = _root.Q<Label>("lbl-fps");
            _lblTokens     = _root.Q<Label>("lbl-tokens");
            _statusDot     = _root.Q<VisualElement>("status-dot");

            _btnSend      = _root.Q<Button>("btn-send");
            _btnAttach    = _root.Q<Button>("btn-attach");
            _btnProbe     = _root.Q<Button>("btn-probe");
            _btnClear     = _root.Q<Button>("btn-clear-session");
            _btnTelemetry = _root.Q<Button>("btn-telemetry-toggle");
            _btnSettings  = _root.Q<Button>("btn-settings");
            _btnThink     = _root.Q<Button>("btn-mode");
            _btnImage     = _root.Q<Button>("btn-image");
            _btnAutoAllow = _root.Q<Button>("btn-auto-allow");

            _chipSpawn    = _root.Q<Button>("chip-spawn");
            _chipFix      = _root.Q<Button>("chip-fix");
            _chipScript   = _root.Q<Button>("chip-script");
            _chipExplain  = _root.Q<Button>("chip-explain");

            _attachSpawn   = _root.Q<Button>("attach-spawn");
            _attachScript  = _root.Q<Button>("attach-script");
            _attachScene   = _root.Q<Button>("attach-scene");
            _attachDocs    = _root.Q<Button>("attach-docs");
            _attachCapture = _root.Q<Button>("attach-capture");

            _settingsUrl       = _root.Q<TextField>("settings-url");
            _settingsApiKey    = _root.Q<TextField>("settings-apikey");
            _settingsModel     = _root.Q<TextField>("settings-model");
            _settingsTemp      = _root.Q<TextField>("settings-temp");
            _settingsHistory   = _root.Q<TextField>("settings-history");
            _settingsTelemetry = _root.Q<Toggle>("settings-telemetry");
            _settingsSave      = _root.Q<Button>("settings-save");
            _settingsReset     = _root.Q<Button>("settings-reset");
            _settingsBackBtn   = _root.Q<Button>("settings-back-btn");
        }

        private void RegisterCallbacks()
        {
            _btnSend.clicked      += OnSendClicked;
            _btnAttach.clicked    += ToggleAttachPopup;
            _btnProbe.clicked     += () => _ = _core.ProbeConnectionAsync();
            _btnClear.clicked     += OnClearSession;
            _btnTelemetry.clicked += OnToggleTelemetry;
            _btnSettings.clicked  += ShowSettings;
            _btnThink.clicked     += OnToggleThinking;
            _btnAutoAllow?.RegisterCallback<ClickEvent>(_ => OnToggleAutoAllow());
            _btnImage?.RegisterCallback<ClickEvent>(_ => OnPickImage());

            _inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown);
            _root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);

            _chipSpawn.clicked   += () => SendSuggestion("Spawn a Cube at position 0, 1, 0 named AgentCube.");
            _chipFix.clicked     += () => SendSuggestion("Check the Unity console for errors and fix them.");
            _chipScript.clicked  += () => SendSuggestion("Write and inject a C# MonoBehaviour called Rotator that spins the Y axis at 90 degrees per second.");
            _chipExplain.clicked += () => SendSuggestion("Describe every GameObject in the active scene.");

            _attachSpawn.clicked   += () => { CloseAttachPopup(); SendSuggestion("Spawn a primitive or prefab."); };
            _attachScript.clicked  += () => { CloseAttachPopup(); SendSuggestion("Write and inject a C# script."); };
            _attachScene.clicked   += () => { CloseAttachPopup(); _core.SendSceneDescriptionAsync(); };
            _attachDocs.clicked    += () => { CloseAttachPopup(); OpenDocumentation(); };
            _attachCapture?.RegisterCallback<ClickEvent>(_ => { CloseAttachPopup(); _core.CaptureSceneAndQueryAsync(); });

            _settingsBackBtn.clicked += HideSettings;
            _settingsSave.clicked    += SaveSettings;
            _settingsReset.clicked   += ResetSettings;

            _root.Q<Button>("btn-remove-image")?.RegisterCallback<ClickEvent>(_ => ClearImagePreview());
        }

        private void OnPickImage()
        {
            string path = EditorUtility.OpenFilePanel("Select Image", "", "png,jpg,jpeg,webp");
            if (string.IsNullOrEmpty(path)) return;
            string b64  = SceneCapture.LoadImageAsBase64(path);
            string mime = SceneCapture.GetMimeType(path);
            if (b64 == null) return;
            _core.AttachImage(b64, mime);
            ShowImagePreview(System.IO.Path.GetFileName(path));
        }

        private void ShowImagePreview(string name)
        {
            if (_imagePreview != null) _imagePreview.RemoveFromClassList("image-preview--hidden");
            if (_lblImageName != null) _lblImageName.text = name;
        }

        private void ClearImagePreview()
        {
            _core.ClearPendingImage();
            _imagePreview?.AddToClassList("image-preview--hidden");
        }

        private void OnToggleThinking()
        {
            _thinkingActive = !_thinkingActive;
            _core.SetThinkingMode(_thinkingActive);
            SyncThinkingButton();
        }

        private void OnToggleAutoAllow()
        {
            _autoAllowActive = !_autoAllowActive;
            _core.SetAutoAllow(_autoAllowActive);
            SyncAutoAllowButton();
        }

        private void SyncAutoAllowButton()
        {
            _autoAllowActive = _core.AutoAllow;
            if (_btnAutoAllow == null) return;
            var icon = _btnAutoAllow.Q<Label>(className: "auto-allow-icon");
            var lbl  = _btnAutoAllow.Q<Label>(className: "auto-allow-label");
            if (icon != null) icon.text = _autoAllowActive ? "▶" : "▷";
            if (lbl  != null) lbl.text  = _autoAllowActive ? "Allow" : "Auto";
            _btnAutoAllow.EnableInClassList("toolbar-btn--auto-allow-active", _autoAllowActive);
        }

        private void SyncThinkingButton()
        {
            _thinkingActive = _core.ThinkingMode;
            if (_btnThink == null) return;
            var icon = _btnThink.Q<Label>(className: "mode-icon");
            var lbl  = _btnThink.Q<Label>(className: "mode-label");
            if (icon != null) icon.text = _thinkingActive ? "◈" : "⚡";
            if (lbl  != null) lbl.text  = _thinkingActive ? "Think" : "Auto";
            _btnThink.EnableInClassList("toolbar-btn--active", _thinkingActive);
        }

        private void ShowSettings()
        {
            PopulateSettingsFields();
            _settingsPanel.RemoveFromClassList("settings-panel--hidden");
        }

        private void HideSettings() =>
            _settingsPanel.AddToClassList("settings-panel--hidden");

        private void PopulateSettingsFields()
        {
            _settingsUrl?.SetValueWithoutNotify(_core.LMStudioUrl);
            _settingsApiKey?.SetValueWithoutNotify(_core.ApiKey);
            _settingsModel?.SetValueWithoutNotify(_core.ModelVersion);
            _settingsTelemetry?.SetValueWithoutNotify(_core.TelemetryEnabled);
        }

        private void SaveSettings()
        {
            string url = _settingsUrl?.value?.Trim();
            if (!string.IsNullOrEmpty(url)) _core.SetLMStudioUrl(url);
            _core.SetApiKey(_settingsApiKey?.value?.Trim() ?? "");
            _core.SetModelVersion(_settingsModel?.value?.Trim() ?? "");
            if (float.TryParse(_settingsTemp?.value, out float t))
                _core.SetTemperature(Mathf.Clamp01(t));
            if (int.TryParse(_settingsHistory?.value, out int h))
                _core.SetMaxHistory(Mathf.Clamp(h, 4, 200));
            if (_settingsTelemetry != null)
                _core.SetTelemetryEnabled(_settingsTelemetry.value);
            HideSettings();
            _ = _core.ProbeConnectionAsync();
        }

        private void ResetSettings()
        {
            _core.SetLMStudioUrl("http://localhost:1234");
            _core.SetApiKey("");
            _core.SetModelVersion("");
            _core.SetTemperature(0.3f);
            _core.SetMaxHistory(40);
            _core.SetTelemetryEnabled(true);
            PopulateSettingsFields();
        }

        private void OnSendClicked()
        {
            var text = _inputField.value?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            _inputField.SetValueWithoutNotify(string.Empty);
            ClearImagePreview();
            _core.SendUserMessageAsync(text);
        }

        private void SendSuggestion(string text)
        {
            _inputField.SetValueWithoutNotify(string.Empty);
            _core.SendUserMessageAsync(text);
        }

        private void OnInputKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return && e.ctrlKey)
            {
                e.StopPropagation();
                OnSendClicked();
            }
        }

        private void ToggleAttachPopup()
        {
            _attachOpen = !_attachOpen;
            _attachPopup.EnableInClassList("attach-popup--hidden", !_attachOpen);
        }

        private void CloseAttachPopup()
        {
            _attachOpen = false;
            _attachPopup.AddToClassList("attach-popup--hidden");
        }

        private void OnRootPointerDown(PointerDownEvent e)
        {
            if (_attachOpen &&
                !_attachPopup.worldBound.Contains(e.position) &&
                !_btnAttach.worldBound.Contains(e.position))
                CloseAttachPopup();
        }

        private void OnClearSession()
        {
            _messageList.Clear();
            _core.ClearSession();
            RefreshWelcomeState();
        }

        private void OnToggleTelemetry()
        {
            _telemetryActive = !_telemetryActive;
            _core.SetTelemetryEnabled(_telemetryActive);
            var icon = _btnTelemetry.Q<Label>(className: "icon-label");
            if (icon != null) icon.text = _telemetryActive ? "⚡" : "○";
        }

        private void OpenDocumentation()
        {
            var guids = AssetDatabase.FindAssets("SystemPrompt t:TextAsset");
            if (guids.Length > 0)
                AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<TextAsset>(
                    AssetDatabase.GUIDToAssetPath(guids[0])));
        }

        private void HandleMessageAdded(ChatMessage msg)
        {
            switch (msg.Role)
            {
                case ChatRole.User:     AppendUserBubble(msg.Content);     break;
                case ChatRole.Agent:    AppendAgentBubble(msg.Content);    break;
                case ChatRole.Error:    AppendErrorBubble(msg.Content);    break;
                case ChatRole.Command:  AppendCommandBubble(msg.Content);  break;
            }
            RefreshWelcomeState();
            ScrollToBottom();
        }

        private void HandleApprovalNeeded(AgentCommand cmd)
        {
            AppendApprovalBubble(cmd);
            RefreshWelcomeState();
            ScrollToBottom();
        }

        private void HandleStatusChanged(ConnectionStatus status)
        {
            if (_statusDot == null) return;
            _statusDot.RemoveFromClassList("status-dot--unknown");
            _statusDot.RemoveFromClassList("status-dot--connected");
            _statusDot.RemoveFromClassList("status-dot--disconnected");
            _statusDot.RemoveFromClassList("status-dot--probing");
            switch (status)
            {
                case ConnectionStatus.Connected:    _statusDot.AddToClassList("status-dot--connected");    break;
                case ConnectionStatus.Disconnected: _statusDot.AddToClassList("status-dot--disconnected"); break;
                case ConnectionStatus.Probing:      _statusDot.AddToClassList("status-dot--probing");      break;
                default:                            _statusDot.AddToClassList("status-dot--unknown");      break;
            }
        }

        private void HandleModelChanged(string name)
        {
            if (_lblModel != null) _lblModel.text = string.IsNullOrEmpty(name) ? "No Model" : name;
        }

        private void HandleThinking(bool thinking)
        {
            _thinkingBar?.EnableInClassList("thinking-bar--hidden", !thinking);
            _btnSend?.SetEnabled(!thinking);
        }

        private void HandleTokensUsed(int tokens)
        {
            if (_lblTokens != null) _lblTokens.text = $"Tokens: {tokens}";
        }

        private void AppendUserBubble(string text)
        {
            var row    = new VisualElement();
            row.AddToClassList("msg-row-user");
            var bubble = new VisualElement();
            bubble.AddToClassList("msg-bubble-user");
            var lbl    = new Label { text = text };
            lbl.AddToClassList("msg-text-user");
            bubble.Add(lbl);
            row.Add(bubble);
            _messageList.Add(row);
        }

        private void AppendAgentBubble(string text)
        {
            var row    = new VisualElement();
            row.AddToClassList("msg-row-agent");
            var tag    = new Label { text = "QUIN" };
            tag.AddToClassList("msg-agent-label");
            var bubble = new VisualElement();
            bubble.AddToClassList("msg-bubble-agent");
            var lbl    = new Label { text = "" };
            lbl.AddToClassList("msg-text-agent");
            bubble.Add(lbl);
            row.Add(tag);
            row.Add(bubble);
            _messageList.Add(row);
            AnimateText(lbl, text);
        }

        private void AppendErrorBubble(string text)
        {
            var row    = new VisualElement();
            row.AddToClassList("msg-row-error");
            var tag    = new Label { text = "CONSOLE ERROR" };
            tag.AddToClassList("msg-error-tag");
            var bubble = new VisualElement();
            bubble.AddToClassList("msg-bubble-error");
            var lbl    = new Label { text = text };
            lbl.AddToClassList("msg-text-error");
            bubble.Add(lbl);
            row.Add(tag);
            row.Add(bubble);
            _messageList.Add(row);
        }

        private void AppendCommandBubble(string text)
        {
            var row    = new VisualElement();
            row.AddToClassList("msg-row-command");
            var tag    = new Label { text = "EXECUTING" };
            tag.AddToClassList("msg-command-tag");
            var bubble = new VisualElement();
            bubble.AddToClassList("msg-bubble-command");
            var lbl    = new Label { text = text };
            lbl.AddToClassList("msg-text-command");
            bubble.Add(lbl);
            row.Add(tag);
            row.Add(bubble);
            _messageList.Add(row);
        }

        private void AppendApprovalBubble(AgentCommand cmd)
        {
            string cmdType    = cmd.command?.type ?? "UNKNOWN";
            string cmdDetail  = BuildApprovalDetail(cmd);

            var row = new VisualElement();
            row.AddToClassList("msg-row-approval");

            var tag = new Label { text = "PERMISSION REQUIRED" };
            tag.AddToClassList("msg-approval-tag");

            var bubble = new VisualElement();
            bubble.AddToClassList("msg-bubble-approval");

            var header = new Label { text = $"QUIN wants to run: {cmdType}" };
            header.AddToClassList("msg-approval-header");

            var detail = new Label { text = cmdDetail };
            detail.AddToClassList("msg-approval-detail");
            detail.style.whiteSpace = WhiteSpace.Normal;

            var btnRow = new VisualElement();
            btnRow.AddToClassList("approval-btn-row");

            var allowBtn = new Button(() =>
            {
                bubble.SetEnabled(false);
                _core.ApproveCommand(true);
            });
            allowBtn.text = "Allow";
            allowBtn.AddToClassList("approval-btn--allow");

            var denyBtn = new Button(() =>
            {
                bubble.SetEnabled(false);
                _core.ApproveCommand(false);
            });
            denyBtn.text = "Deny";
            denyBtn.AddToClassList("approval-btn--deny");

            btnRow.Add(allowBtn);
            btnRow.Add(denyBtn);
            bubble.Add(header);
            bubble.Add(detail);
            bubble.Add(btnRow);
            row.Add(tag);
            row.Add(bubble);
            _messageList.Add(row);
        }

        private static string BuildApprovalDetail(AgentCommand cmd)
        {
            var p = cmd.command?.payload;
            if (p == null) return cmd.thought ?? "";
            var sb = new System.Text.StringBuilder();
            try {
                sb.AppendLine(p.ToString(Newtonsoft.Json.Formatting.Indented));
            } catch {
                sb.AppendLine("{ ... }");
            }
            if (!string.IsNullOrEmpty(cmd.thought))  sb.AppendLine($"\n{cmd.thought}");
            return sb.ToString().Trim();
        }

        private static void AnimateText(Label lbl, string fullText)
        {
            int[] i = { 0 };
            lbl.schedule
                .Execute(() =>
                {
                    if (i[0] <= fullText.Length)
                        lbl.text = fullText.Substring(0, i[0]++);
                })
                .Every(8)
                .Until(() => i[0] > fullText.Length);
        }

        private void RefreshWelcomeState()
        {
            if (_welcomeState == null || _messageList == null) return;
            _welcomeState.style.display = _messageList.childCount > 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void ScrollToBottom()
        {
            if (_chatScroll == null || _messageList == null || _messageList.childCount == 0) return;
            _chatScroll.schedule.Execute(() =>
            {
                int last = Mathf.Max(0, _messageList.childCount - 1);
                _chatScroll.ScrollTo(_messageList.ElementAt(last));
            }).StartingIn(50);
        }

        private void OnEditorUpdate()
        {
            _frameCount++;
            long nowMs = _perfWatch.ElapsedMilliseconds;
            if (nowMs - _lastPerfMs < 1000) return;
            float elapsed = (nowMs - _lastPerfMs) / 1000f;
            _lastPerfMs = nowMs;
            float fps   = _frameCount / elapsed;
            _frameCount = 0;
            long memMB  = System.GC.GetTotalMemory(false) / (1024 * 1024);
            if (_lblFps != null) _lblFps.text = $"FPS {fps:F0}";
            if (_lblMem != null) _lblMem.text = $"RAM {memMB} MB";
        }

        private void DrawFallback()
        {
            var lbl = new Label("AgentHubWindow.uxml not found — reimport QuinAutobot package.");
            lbl.style.color = Color.red;
            lbl.style.whiteSpace = WhiteSpace.Normal;
            lbl.style.paddingTop = lbl.style.paddingBottom = lbl.style.paddingLeft = lbl.style.paddingRight = 16;
            rootVisualElement.Add(lbl);
        }
    }

    public enum ChatRole { User, Agent, Error, Command, Approval }

    public class ChatMessage
    {
        public ChatRole Role;
        public string   Content;
        public DateTime Timestamp;

        public ChatMessage(ChatRole role, string content)
        {
            Role      = role;
            Content   = content;
            Timestamp = DateTime.Now;
        }
    }
}
