using System;
using System.Collections.Generic;

namespace QuinAutobot
{
    [Serializable]
    public class LMChatRequest
    {
        public string model;
        public List<LMMessage> messages;
        public float temperature = 0.3f;
        public int max_tokens = 4096;
        public bool stream = false;
    }

    [Serializable]
    public class LMMessage
    {
        public string role;
        public string content;

        public LMMessage(string role, string content)
        {
            this.role    = role;
            this.content = content;
        }
    }

    [Serializable]
    public class LMChatResponse
    {
        public string    id;
        public string    model;
        public LMChoice[] choices;
        public LMUsage   usage;
    }

    [Serializable]
    public class LMChoice
    {
        public int       index;
        public LMMessage message;
        public string    finish_reason;
    }

    [Serializable]
    public class LMUsage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }

    [Serializable]
    public class LMModelsResponse
    {
        public LMModelData[] data;
    }

    [Serializable]
    public class LMModelData
    {
        public string id;
        public string owned_by;
    }

    [Serializable]
    public class StreamDelta
    {
        public StreamChoice[] choices;
    }

    [Serializable]
    public class StreamChoice
    {
        public StreamDeltaContent delta;
        public string             finish_reason;
    }

    [Serializable]
    public class StreamDeltaContent
    {
        public string content;
        public string role;
    }

    public enum ConnectionStatus { Unknown, Connected, Disconnected, Probing }
}
