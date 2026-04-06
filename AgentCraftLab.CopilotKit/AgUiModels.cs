using System.Text.Json.Serialization;

namespace AgentCraftLab.CopilotKit;

/// <summary>
/// AG-UI Protocol 請求模型 — CopilotKit 前端送來的 RunAgentInput。
/// </summary>
public class RunAgentInput
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = "";

    [JsonPropertyName("runId")]
    public string RunId { get; set; } = "";

    [JsonPropertyName("state")]
    public Dictionary<string, object>? State { get; set; }

    [JsonPropertyName("messages")]
    public List<AgUiMessage>? Messages { get; set; }

    [JsonPropertyName("tools")]
    public List<AgUiToolDef>? Tools { get; set; }

    [JsonPropertyName("context")]
    public List<object>? Context { get; set; }

    [JsonPropertyName("forwardedProps")]
    public Dictionary<string, object>? ForwardedProps { get; set; }
}

public class AgUiMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

public class AgUiToolDef
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public object? Parameters { get; set; }
}

/// <summary>
/// AG-UI Protocol 事件基底 — 所有事件都有 type 欄位。
/// </summary>
public class AgUiEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonPropertyName("runId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RunId { get; set; }

    [JsonPropertyName("messageId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MessageId { get; set; }

    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    [JsonPropertyName("delta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Delta { get; set; }

    [JsonPropertyName("toolCallId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("toolCallName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallName { get; set; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }

    [JsonPropertyName("stepName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StepName { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Value { get; set; }
}

/// <summary>
/// AG-UI 事件類型常數。
/// </summary>
public static class AgUiEventTypes
{
    public const string RunStarted = "RUN_STARTED";
    public const string RunFinished = "RUN_FINISHED";
    public const string RunError = "RUN_ERROR";

    public const string StepStarted = "STEP_STARTED";
    public const string StepFinished = "STEP_FINISHED";

    public const string TextMessageStart = "TEXT_MESSAGE_START";
    public const string TextMessageContent = "TEXT_MESSAGE_CONTENT";
    public const string TextMessageEnd = "TEXT_MESSAGE_END";

    public const string ToolCallStart = "TOOL_CALL_START";
    public const string ToolCallArgs = "TOOL_CALL_ARGS";
    public const string ToolCallEnd = "TOOL_CALL_END";

    public const string Custom = "CUSTOM";
}
