using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace QuinAutobot
{
    public class CommandDispatcher
    {
        private static readonly Regex JsonFenceRegex =
            new Regex(@"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.Compiled);

        public CommandDispatcher()
        {
            CommandRegistry.Initialize();
        }

        public bool RequiresApproval(AgentCommand cmd)
        {
            if (cmd?.command == null) return false;
            switch (cmd.command.type)
            {
                case "SEND_MESSAGE":
                case "read_console":
                case "find_gameobjects":
                case "get_test_job":
                case "unity_reflect":
                case "unity_docs":
                case "debug_request_context":
                case "refresh_unity":
                    return false;
                default:
                    return true;
            }
        }

        public AgentCommand TryParse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var match = JsonFenceRegex.Match(raw);
            string json = match.Success ? match.Groups[1].Value : raw.Trim();
            if (!json.StartsWith("{")) return null;
            return AgentCommand.Parse(json);
        }

        public async Task<string> ExecuteAsync(AgentCommand cmd)
        {
            if (cmd?.command == null) return null;

            if (cmd.command.type == "SEND_MESSAGE")
            {
                return cmd.command.payload?["text"]?.ToString() ?? "";
            }

            try
            {
                var payload = cmd.command.payload ?? new JObject();
                object result = await CommandRegistry.InvokeCommandAsync(cmd.command.type, payload);
                return Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QuinAutobot] Execute error: {ex.Message}\n{ex.StackTrace}");
                return $"Command failed: {ex.Message}";
            }
        }
    }
}
