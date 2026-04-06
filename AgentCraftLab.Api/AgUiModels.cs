using System.Text.Json.Serialization;

namespace AgentCraftLab.Api;

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
/// AG-UI Protocol 事件基底 — 所有事件共用同一個 class。
/// 這是 AG-UI Protocol 的設計（單一事件 shape + type 欄位判別），非 God DTO。
/// 參考：https://docs.ag-ui.com/concepts/events
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

    [JsonPropertyName("snapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Snapshot { get; set; }
}

/// <summary>
/// Human Input 提交請求 — 前端透過 POST /ag-ui/human-input 發送。
/// </summary>
public class HumanInputRequest
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = "";

    [JsonPropertyName("runId")]
    public string RunId { get; set; } = "";

    [JsonPropertyName("response")]
    public string? Response { get; set; }
}

/// <summary>
/// Debug Action 提交請求 — 前端透過 POST /ag-ui/debug-action 發送。
/// </summary>
public class DebugActionRequest
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; set; }

    [JsonPropertyName("runId")]
    public string? RunId { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }
}

/// <summary>
/// 節點重跑請求 — 前端透過 POST /ag-ui/rerun 發送。
/// </summary>
public class RerunRequest
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = "";

    [JsonPropertyName("runId")]
    public string RunId { get; set; } = "";

    [JsonPropertyName("executionId")]
    public string ExecutionId { get; set; } = "";

    [JsonPropertyName("rerunFromNodeId")]
    public string RerunFromNodeId { get; set; } = "";

    [JsonPropertyName("workflowJson")]
    public string WorkflowJson { get; set; } = "";

    [JsonPropertyName("userMessage")]
    public string UserMessage { get; set; } = "";
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

    public const string StateSnapshot = "STATE_SNAPSHOT";
    public const string StateDelta = "STATE_DELTA";

    public const string ReasoningStart = "REASONING_START";
    public const string ReasoningMessageStart = "REASONING_MESSAGE_START";
    public const string ReasoningMessageContent = "REASONING_MESSAGE_CONTENT";
    public const string ReasoningMessageEnd = "REASONING_MESSAGE_END";
    public const string ReasoningEnd = "REASONING_END";

    public const string Custom = "CUSTOM";
}
