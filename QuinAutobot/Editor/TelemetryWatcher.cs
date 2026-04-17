using System;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace QuinAutobot
{
    [InitializeOnLoad]
    public class TelemetryWatcher
    {
        private AutoAgentCore _core;
        private bool _enabled    = true;
        private bool _processing = false;

        private string _lastErrorHash = "";
        private double _lastErrorTime = -999;
        private const double CooldownSeconds = 8.0;

        public TelemetryWatcher(AutoAgentCore core)
        {
            _core = core;
            Application.logMessageReceived += OnLogReceived;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
        }

        static TelemetryWatcher() { }

        public void SetEnabled(bool enabled) => _enabled = enabled;

        private void OnLogReceived(string condition, string stackTrace, LogType type)
        {
            if (!_enabled) return;
            if (type != LogType.Error && type != LogType.Exception) return;
            if (_processing) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastErrorTime < CooldownSeconds) return;

            string hash = Hash(condition + stackTrace);
            if (hash == _lastErrorHash) return;

            _lastErrorHash = hash;
            _lastErrorTime = now;
            _processing    = true;

            EditorApplication.delayCall += () =>
            {
                try   { _core.HandleConsoleError(condition, stackTrace, type); }
                finally { _processing = false; }
            };
        }

        private void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            if (!_enabled || _processing) return;

            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                if (msg.type == CompilerMessageType.Error)
                    sb.AppendLine($"{msg.file}({msg.line},{msg.column}): {msg.message}");
            }

            if (sb.Length == 0) return;

            string errors = sb.ToString();
            string hash   = Hash(errors);
            if (hash == _lastErrorHash) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastErrorTime < CooldownSeconds) return;

            _lastErrorHash = hash;
            _lastErrorTime = now;
            _processing    = true;

            EditorApplication.delayCall += () =>
            {
                try   { _core.HandleConsoleError($"Compile errors in {assemblyPath}", errors, LogType.Error); }
                finally { _processing = false; }
            };
        }

        private static string Hash(string input)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-", "");
        }
    }
}
