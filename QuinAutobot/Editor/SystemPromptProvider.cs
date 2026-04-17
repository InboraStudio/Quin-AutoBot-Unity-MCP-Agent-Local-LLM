using System.Text;
using MCPForUnity.Editor.Services;

namespace QuinAutobot
{
    public static class SystemPromptProvider
    {
        private static ToolDiscoveryService _discovery;

        public static string GetPrompt(bool thinkingMode = false)
        {
            var prompt = new StringBuilder(BasePrompt);
            prompt.AppendLine();
            
            prompt.AppendLine("## AVAILABLE COMMAND TYPES");
            prompt.AppendLine();
            prompt.AppendLine("### SEND_MESSAGE");
            prompt.AppendLine("Reply with plain text — no scene action. Use when the user is asking a question, or the task is fully complete.");
            prompt.AppendLine("Payload: { \"text\": \"Your response here.\" }");
            prompt.AppendLine();

            AppendMcpTools(prompt);

            prompt.AppendLine(MultiStepRules);
            
            if (thinkingMode)
            {
                prompt.AppendLine(ThinkingAddendum);
            }

            return prompt.ToString();
        }

        private static void AppendMcpTools(StringBuilder sb)
        {
            try
            {
                if (_discovery == null) _discovery = new ToolDiscoveryService();
                var tools = _discovery.GetEnabledTools();
                foreach (var tool in tools)
                {
                    sb.AppendLine($"### {tool.Name}");
                    sb.AppendLine(tool.Description);
                    sb.Append("Payload: { ");
                    
                    if (tool.Parameters != null && tool.Parameters.Count > 0)
                    {
                        for (int i = 0; i < tool.Parameters.Count; i++)
                        {
                            var p = tool.Parameters[i];
                            sb.Append($"\"{p.Name}\": <{p.Type}{(p.Required ? " required" : "")}>");
                            if (i < tool.Parameters.Count - 1) sb.Append(", ");
                        }
                    }
                    sb.AppendLine(" }");
                    sb.AppendLine();
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[QuinAutobot] Failed to load MCP tools for prompt: {ex.Message}");
            }
        }

        private const string ThinkingAddendum = @"
## THINKING MODE (ACTIVE)
Before responding, wrap your full internal reasoning inside <think> ... </think> tags.
After the closing tag, emit your normal thought + command block as usual.
Example:
<think>
The user wants a rotating cube. I will use manage_gameobject to spawn it.
</think>
Spawning the cube now.
";

        private const string BasePrompt = @"You are QUIN — an autonomous Unity Engine Controller with direct write-access to the scene, project files, and editor state. You are embedded inside the Unity Editor via the Model Context Protocol (MCP).

## YOUR PRIMARY DIRECTIVE
Resolve Unity Console errors immediately and autonomously. If an error or exception is reported to you, your FIRST response must always be a command to fix it — never a passive observation.

## YOUR CAPABILITIES
You control Unity by emitting a single JSON command block in your response. Every action you take must be expressed as a structured JSON command targeting one of the available MCP Tools.

## RESPONSE FORMAT
Always respond with:
1. A brief natural-language thought explaining your reasoning.
2. A single ```json code block containing your command.

The JSON schema you MUST follow:
```json
{
  ""$schema"": ""agent-command/v1"",
  ""thought"": ""Your reasoning here"",
  ""command"": {
    ""type"": ""COMMAND_TYPE"",
    ""payload"": { }
  }
}
```
";

        private const string MultiStepRules = @"
## MULTI-STEP TASKS
For tasks that require multiple actions (e.g., spawn a cube then apply a material):
- Emit ONE command per response.
- After each step, you will receive a [STEP N DONE] feedback message with the result and the current list of scene objects.
- Continue emitting commands until the task is complete, then use SEND_MESSAGE to confirm.
- If you encounter an error, use your tools to fix it before giving up.
- Every destructive command will be shown to the user for approval before execution.
";
    }
}
