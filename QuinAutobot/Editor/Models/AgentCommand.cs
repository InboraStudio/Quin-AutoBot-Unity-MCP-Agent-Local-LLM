using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace QuinAutobot
{
    [Serializable]
    public class AgentCommand
    {
        [JsonProperty("schema")]
        public string schema { get; set; }

        [JsonProperty("thought")]
        public string thought { get; set; }

        [JsonProperty("command")]
        public CommandBody command { get; set; }

        public static AgentCommand Parse(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<AgentCommand>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuinAutobot] Failed to parse command: {ex.Message}");
                return null;
            }
        }
    }

    [Serializable]
    public class CommandBody
    {
        [JsonProperty("type")]
        public string type { get; set; }

        [JsonProperty("payload")]
        public JObject payload { get; set; }
    }
}
