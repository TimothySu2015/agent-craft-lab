using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.CopilotKit;

/// <summary>
/// 將 AgentCraftLab 的 ExecutionEvent 轉換為 AG-UI Protocol 事件。
/// 每個 request 建立一個 instance（thread-safe，計數器不共享）。
/// 追蹤 active message 狀態，確保 TEXT_MESSAGE_CONTENT 前一定有 START。
/// </summary>
public class AgUiEventConverter
{
    private int _toolCallCounter;
    private int _messageCounter;
    private bool _hasActiveMessage;

    public IEnumerable<AgUiEvent> Convert(ExecutionEvent evt, string threadId, string runId)
    {
        switch (evt.Type)
        {
            case EventTypes.AgentStarted:
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.StepStarted,
                    StepName = evt.AgentName
                };
                foreach (var e in EnsureMessageStarted()) yield return e;
                break;

            case EventTypes.TextChunk:
                if (!string.IsNullOrEmpty(evt.Text))
                {
                    foreach (var e in EnsureMessageStarted()) yield return e;
                    yield return new AgUiEvent
                    {
                        Type = AgUiEventTypes.TextMessageContent,
                        MessageId = CurrentMessageId(),
                        Delta = evt.Text
                    };
                }
                break;

            case EventTypes.AgentCompleted:
                foreach (var e in EnsureMessageEnded()) yield return e;
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.StepFinished,
                    StepName = evt.AgentName
                };
                break;

            case EventTypes.ToolCall:
                // Tool call 前先關閉 active message（CopilotKit 要求）
                foreach (var e in EnsureMessageEnded()) yield return e;
                var toolCallId = NextToolCallId();
                var toolName = ExtractToolName(evt.Text);
                var toolArgs = ExtractToolArgs(evt.Text);
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.ToolCallStart,
                    ToolCallId = toolCallId,
                    ToolCallName = toolName
                };
                if (!string.IsNullOrEmpty(toolArgs))
                {
                    yield return new AgUiEvent
                    {
                        Type = AgUiEventTypes.ToolCallArgs,
                        ToolCallId = toolCallId,
                        Delta = toolArgs
                    };
                }
                break;

            case EventTypes.ToolResult:
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.ToolCallEnd,
                    ToolCallId = CurrentToolCallId()
                };
                break;

            case EventTypes.Error:
                foreach (var e in EnsureMessageEnded()) yield return e;
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.RunError,
                    Message = evt.Text
                };
                break;

            // Autonomous：ReasoningStep → 作為 STEP 事件（不開新 message，避免 ID 衝突）
            case EventTypes.ReasoningStep:
                foreach (var e in EnsureMessageEnded()) yield return e;
                var stepName = $"Step {evt.Metadata?.GetValueOrDefault(MetadataKeys.Step, "")}";
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.StepStarted,
                    StepName = stepName
                };
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.StepFinished,
                    StepName = stepName
                };
                break;

            // Sub-agent / Plan / Audit / Flow 事件 → CUSTOM
            case EventTypes.SubAgentCreated:
            case EventTypes.SubAgentAsked:
            case EventTypes.SubAgentResponded:
            case EventTypes.PlanGenerated:
            case EventTypes.PlanRevised:
            case EventTypes.NodeExecuting:
            case EventTypes.NodeCompleted:
            case EventTypes.AuditStarted:
            case EventTypes.AuditCompleted:
            case EventTypes.FlowCrystallized:
            case EventTypes.WaitingForRiskApproval:
            case EventTypes.RiskApprovalResult:
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.Custom,
                    Name = evt.Type,
                    Value = new { agentName = evt.AgentName, text = evt.Text, metadata = evt.Metadata }
                };
                break;

            case EventTypes.RagProcessing:
            case EventTypes.RagReady:
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.Custom,
                    Name = evt.Type,
                    Value = new { text = evt.Text }
                };
                break;

            case EventTypes.WorkflowCompleted:
                foreach (var e in EnsureMessageEnded()) yield return e;
                break;
        }
    }

    /// <summary>確保有 active message，沒有就開一個新的。</summary>
    private IEnumerable<AgUiEvent> EnsureMessageStarted()
    {
        if (!_hasActiveMessage)
        {
            _hasActiveMessage = true;
            yield return new AgUiEvent
            {
                Type = AgUiEventTypes.TextMessageStart,
                MessageId = NextMessageId(),
                Role = "assistant"
            };
        }
    }

    /// <summary>如果有 active message 就關閉它。</summary>
    private IEnumerable<AgUiEvent> EnsureMessageEnded()
    {
        if (_hasActiveMessage)
        {
            _hasActiveMessage = false;
            yield return new AgUiEvent
            {
                Type = AgUiEventTypes.TextMessageEnd,
                MessageId = CurrentMessageId()
            };
        }
    }

    private string NextMessageId() => $"msg-{++_messageCounter}";
    private string CurrentMessageId() => $"msg-{_messageCounter}";
    private string NextToolCallId() => $"tc-{++_toolCallCounter}";
    private string CurrentToolCallId() => $"tc-{_toolCallCounter}";

    private static string ExtractToolName(string text)
    {
        var idx = text.IndexOf('(');
        return idx > 0 ? text[..idx] : text;
    }

    private static string ExtractToolArgs(string text)
    {
        var start = text.IndexOf('(');
        var end = text.LastIndexOf(')');
        return start > 0 && end > start ? text[(start + 1)..end] : "";
    }
}
