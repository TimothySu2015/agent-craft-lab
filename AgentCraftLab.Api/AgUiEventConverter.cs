using System.Diagnostics;
using AgentCraftLab.Engine.Models;
using TraceSpanModel = AgentCraftLab.Engine.Models.TraceSpanModel;

namespace AgentCraftLab.Api;

/// <summary>
/// 將 AgentCraftLab 的 ExecutionEvent 轉換為 AG-UI Protocol 事件。
/// 每個 request 建立一個 instance（thread-safe，計數器不共享）。
/// 追蹤 active message 狀態，確保 TEXT_MESSAGE_CONTENT 前一定有 START。
/// 維護內部 execution state，透過 STATE_SNAPSHOT 事件同步到前端。
/// </summary>
public class AgUiEventConverter
{
    // 節點狀態常數 — 前端 agent-state.ts 的 NodeStatus 需保持一致
    internal const string NodeStatusExecuting = "executing";
    internal const string NodeStatusCompleted = "completed";
    internal const string NodeStatusCancelled = "cancelled";
    internal const string NodeStatusDebugPaused = "debug-paused";

    private readonly string _runId;
    private int _toolCallCounter;
    private int _messageCounter;
    private bool _hasActiveMessage;
    private readonly Dictionary<string, Stack<string>> _agentToolCalls = new();
    private int _agentDepth;
    private string? _lastAgentName;

    // ─── Shared State（透過 STATE_SNAPSHOT 同步到前端 useCoAgent） ───
    private readonly Dictionary<string, string> _nodeStates = new();
    private readonly Dictionary<string, string> _nodeOutputs = new();
    private object? _pendingHumanInput;
    private object? _pendingDebugAction;
    private readonly List<object> _recentLogs = [];
    private const int MaxRecentLogs = 50;
    private const int MaxOutputPreviewLength = 500;

    // ─── Execution Stats（計時 + token 累計 + 成本） ───
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _totalTokens;
    private int _totalSteps;
    private int _toolCallTotal;
    private decimal _totalCost;

    private IReadOnlyList<TraceSpanModel>? _currentTraceSpans;
    private List<object>? _lastRagCitations;
    private List<string>? _lastExpandedQueries;

    public AgUiEventConverter(string runId)
    {
        _runId = runId;
    }

    public IEnumerable<AgUiEvent> Convert(ExecutionEvent evt, string threadId, string runId,
        IReadOnlyList<TraceSpanModel>? traceSpans = null)
    {
        _currentTraceSpans = traceSpans;
        switch (evt.Type)
        {
            case EventTypes.AgentStarted:
                _agentDepth++;
                _lastAgentName = evt.AgentName;
                AddLog("info", $"[{evt.AgentName}] started");
                // 新 agent 開始：結束前一個 message，送分隔標題
                foreach (var e in EnsureMessageEnded()) yield return e;
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.StepStarted,
                    StepName = evt.AgentName
                };
                // 每個 agent 開始新 message，加上 agent 名稱作為標題
                foreach (var e in EnsureMessageStarted()) yield return e;
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.TextMessageContent,
                    MessageId = CurrentMessageId(),
                    Delta = $"**[{evt.AgentName}]**\n\n"
                };
                yield return BuildStateSnapshot();
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
                _agentDepth = Math.Max(0, _agentDepth - 1);
                _totalSteps++;
                AccumulateTokens(evt);
                AddLog("success", $"[{evt.AgentName}] completed");
                foreach (var e in EnsureMessageEnded()) yield return e;
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.StepFinished,
                    StepName = evt.AgentName
                };
                yield return BuildStateSnapshot();
                break;

            case EventTypes.ToolCall:
                _toolCallTotal++;
                AddLog("info", $"[{evt.AgentName}] \ud83d\udd27 {evt.Text}");
                // Tool call 前先關閉 active message（CopilotKit 要求）
                foreach (var e in EnsureMessageEnded()) yield return e;
                var toolCallId = NextToolCallId();
                if (!_agentToolCalls.TryGetValue(evt.AgentName ?? "", out var stack))
                {
                    stack = new Stack<string>();
                    _agentToolCalls[evt.AgentName ?? ""] = stack;
                }
                stack.Push(toolCallId);
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
                AddLog("success", $"[{evt.AgentName}] tool result received");
                // Pop 該 agent 的 tool call ID（per-agent stack 解決 parallel 交錯）
                if (_agentToolCalls.TryGetValue(evt.AgentName ?? "", out var agentStack) &&
                    agentStack.TryPop(out var endToolCallId))
                {
                    yield return new AgUiEvent
                    {
                        Type = AgUiEventTypes.ToolCallEnd,
                        ToolCallId = endToolCallId
                    };
                }
                break;

            case EventTypes.Error:
                Console.Error.WriteLine($"[AG-UI] Workflow error: [{evt.AgentName}] {evt.Text}");
                AddLog("error", evt.Text ?? "Unknown error");
                foreach (var e in EnsureMessageEnded()) yield return e;
                foreach (var e in BuildErrorMessage(evt.Text ?? "Unknown error")) yield return e;
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.RunError,
                    Message = evt.Text
                };
                break;

            // Autonomous：ReasoningStep → AG-UI REASONING 系列事件
            case EventTypes.ReasoningStep:
                AccumulateTokens(evt);
                foreach (var e in EnsureMessageEnded()) yield return e;
                var reasoningId = $"{_runId}-reason-{evt.Metadata?.GetValueOrDefault(MetadataKeys.Step, _messageCounter.ToString())}";
                yield return new AgUiEvent { Type = AgUiEventTypes.ReasoningStart, MessageId = reasoningId };
                yield return new AgUiEvent { Type = AgUiEventTypes.ReasoningMessageStart, MessageId = reasoningId, Role = "reasoning" };
                yield return new AgUiEvent { Type = AgUiEventTypes.ReasoningMessageContent, MessageId = reasoningId, Delta = evt.Text ?? "" };
                yield return new AgUiEvent { Type = AgUiEventTypes.ReasoningMessageEnd, MessageId = reasoningId };
                yield return new AgUiEvent { Type = AgUiEventTypes.ReasoningEnd, MessageId = reasoningId };
                break;

            // ─── State Sync：NodeExecuting/NodeCompleted → STATE_SNAPSHOT ───
            case EventTypes.NodeExecuting:
            {
                var nodeName = evt.Metadata?.GetValueOrDefault(MetadataKeys.NodeName, "") ?? "";
                var nodeType = evt.Metadata?.GetValueOrDefault(MetadataKeys.NodeType, "") ?? "";
                if (!string.IsNullOrEmpty(nodeName))
                {
                    _nodeStates[nodeName] = NodeStatusExecuting;
                }
                // Custom 事件保留（向後相容）
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.Custom,
                    Name = evt.Type,
                    Value = new { agentName = evt.AgentName, text = evt.Text, metadata = evt.Metadata }
                };
                // STATE_SNAPSHOT 同步完整狀態到前端
                yield return BuildStateSnapshot();
                break;
            }

            case EventTypes.NodeCompleted:
            {
                var nodeName = evt.Metadata?.GetValueOrDefault(MetadataKeys.NodeName, "") ?? "";
                if (!string.IsNullOrEmpty(nodeName))
                {
                    _nodeStates[nodeName] = NodeStatusCompleted;
                    // 記錄節點 output 預覽（從 metadata 取完整 output，截斷供前端 Debug/Rerun 顯示）
                    var output = evt.Metadata?.GetValueOrDefault("output", "") ?? "";
                    _nodeOutputs[nodeName] = output.Length > MaxOutputPreviewLength
                        ? output[..MaxOutputPreviewLength] + "..."
                        : output;
                }
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.Custom,
                    Name = evt.Type,
                    Value = new { agentName = evt.AgentName, text = evt.Text, metadata = evt.Metadata }
                };
                yield return BuildStateSnapshot();
                break;
            }

            case EventTypes.NodeCancelled:
            {
                var nodeName = evt.Metadata?.GetValueOrDefault(MetadataKeys.NodeName, "") ?? "";
                if (!string.IsNullOrEmpty(nodeName))
                {
                    _nodeStates[nodeName] = NodeStatusCancelled;
                }
                AddLog("warn", $"[speculative] {nodeName} cancelled — {evt.Metadata?.GetValueOrDefault("reason", "")}");
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.Custom,
                    Name = evt.Type,
                    Value = new { agentName = evt.AgentName, text = evt.Text, metadata = evt.Metadata }
                };
                yield return BuildStateSnapshot();
                break;
            }

            // Sub-agent / Plan / Audit / Flow 事件 → CUSTOM + Console log
            case EventTypes.SubAgentCreated:
                AddLog("info", $"[{evt.AgentName}] created sub-agent: {evt.Metadata?.GetValueOrDefault(MetadataKeys.SubAgentName, "")}");
                goto case EventTypes.RiskApprovalResult;
            case EventTypes.SubAgentAsked:
            case EventTypes.SubAgentResponded:
                AddLog("info", $"[{evt.AgentName}] {evt.Type}: {evt.Text?.Substring(0, Math.Min(evt.Text?.Length ?? 0, 80))}");
                goto case EventTypes.RiskApprovalResult;
            case EventTypes.PlanGenerated:
                AddLog("info", "Plan generated");
                goto case EventTypes.RiskApprovalResult;
            case EventTypes.AuditStarted:
                AddLog("info", "Auditing response...");
                goto case EventTypes.RiskApprovalResult;
            case EventTypes.AuditCompleted:
                AddLog("info", $"Audit: {evt.Metadata?.GetValueOrDefault(MetadataKeys.Verdict, "")}");
                goto case EventTypes.RiskApprovalResult;
            case EventTypes.WaitingForRiskApproval:
                AddLog("warning", $"[Risk] Approval required: {evt.Text}");
                goto case EventTypes.RiskApprovalResult;
            case EventTypes.PlanRevised:
            case EventTypes.FlowCrystallized:
            case EventTypes.RiskApprovalResult:
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.Custom,
                    Name = evt.Type,
                    Value = new { agentName = evt.AgentName, text = evt.Text, metadata = evt.Metadata }
                };
                break;

            case EventTypes.RagProcessing:
                AddLog("info", "[RAG] Processing documents...");
                goto case EventTypes.RagReady;
            case EventTypes.RagReady:
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.Custom,
                    Name = evt.Type,
                    Value = new { text = evt.Text }
                };
                break;

            case EventTypes.RagCitations:
                _lastRagCitations = evt.Citations;
                if (evt.Metadata?.TryGetValue("expandedQueries", out var eqJson) == true)
                {
                    try { _lastExpandedQueries = System.Text.Json.JsonSerializer.Deserialize<List<string>>(eqJson); }
                    catch { /* ignore */ }
                }
                yield return BuildStateSnapshot();
                break;

            // ─── Debug Mode：DebugPaused/DebugResumed → STATE_SNAPSHOT ───
            case EventTypes.DebugPaused:
            {
                var debugNodeName = evt.Metadata?.GetValueOrDefault(MetadataKeys.NodeName, "") ?? "";
                var debugNodeType = evt.Metadata?.GetValueOrDefault("nodeType", "") ?? "";
                var debugOutput = evt.Metadata?.GetValueOrDefault("output", "") ?? "";
                if (!string.IsNullOrEmpty(debugNodeName))
                {
                    _nodeStates[debugNodeName] = NodeStatusDebugPaused;
                }
                _pendingDebugAction = new
                {
                    nodeName = debugNodeName,
                    nodeType = debugNodeType,
                    output = debugOutput.Length > MaxOutputPreviewLength
                        ? debugOutput[..MaxOutputPreviewLength] + "..."
                        : debugOutput
                };
                AddLog("warning", $"[Debug] Paused at {debugNodeName}");
                yield return BuildStateSnapshot();
                break;
            }

            case EventTypes.DebugResumed:
            {
                var resumedNodeName = evt.Metadata?.GetValueOrDefault(MetadataKeys.NodeName, "") ?? "";
                var resumedAction = evt.Metadata?.GetValueOrDefault("action", "") ?? "";
                _pendingDebugAction = null;
                AddLog("info", $"[Debug] {resumedAction} — {resumedNodeName}");
                yield return BuildStateSnapshot();
                break;
            }

            // ─── Human-in-the-Loop：WaitingForInput → Text + STATE_SNAPSHOT ───
            case EventTypes.WaitingForInput:
                // 先發 text message 讓 Chat 顯示 prompt
                foreach (var e in EnsureMessageStarted()) yield return e;
                var promptText = evt.Text ?? "Please provide your input";
                if (evt.InputType == "choice" && !string.IsNullOrEmpty(evt.Choices))
                {
                    promptText += $"\n\nOptions: {evt.Choices}";
                }
                else if (evt.InputType == "approval")
                {
                    promptText += "\n\n(Approve / Reject)";
                }
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.TextMessageContent,
                    MessageId = CurrentMessageId(),
                    Delta = promptText
                };
                foreach (var e in EnsureMessageEnded()) yield return e;
                // 更新 shared state — 前端 useCoAgent 會自動收到
                _pendingHumanInput = new
                {
                    prompt = evt.Text,
                    inputType = evt.InputType,
                    choices = evt.Choices
                };
                yield return BuildStateSnapshot();
                break;

            case EventTypes.UserInputReceived:
                // 清除 pending human input
                _pendingHumanInput = null;
                yield return BuildStateSnapshot();
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.Custom,
                    Name = evt.Type,
                    Value = new { text = evt.Text }
                };
                break;

            case EventTypes.HookBlocked:
                foreach (var e in EnsureMessageEnded()) yield return e;
                yield return new AgUiEvent
                {
                    Type = AgUiEventTypes.Custom,
                    Name = evt.Type,
                    Value = new { text = evt.Text, metadata = evt.Metadata }
                };
                break;

            case EventTypes.WorkflowCompleted:
                _stopwatch.Stop();
                // Flow 模式可能帶有精確 token/cost 資訊
                if (evt.Metadata is not null)
                {
                    if (evt.Metadata.TryGetValue("totalTokens", out var flowTokensStr) &&
                        long.TryParse(flowTokensStr, out var flowTokens) && flowTokens > 0)
                    {
                        _totalTokens = flowTokens; // 使用 Flow 的精確值覆蓋
                    }
                }
                var flowCost = evt.Metadata?.GetValueOrDefault("estimatedCost");
                AddLog("success", "Workflow completed");
                foreach (var e in EnsureMessageEnded()) yield return e;

                // 在 Chat 訊息中顯示執行統計
                foreach (var e in BuildStatsMessage(flowCost)) yield return e;

                // nodeStates 和 nodeOutputs 保留（供 Rerun/Debug 使用），只清 pending 狀態
                _pendingHumanInput = null;
                _pendingDebugAction = null;
                yield return BuildStateSnapshot();
                break;
        }
    }

    // ─── Token/Cost 累加 ───

    private void AccumulateTokens(ExecutionEvent evt)
    {
        if (evt.Metadata?.TryGetValue(MetadataKeys.Tokens, out var tokensStr) != true ||
            !long.TryParse(tokensStr, out var tokens)) return;

        _totalTokens += tokens;
        var model = evt.Metadata.GetValueOrDefault(MetadataKeys.Model, "");
        if (!string.IsNullOrEmpty(model))
        {
            _totalCost += ModelPricing.EstimateCost(model, tokens);
        }
    }

    // ─── Console Log ───

    private void AddLog(string level, string message)
    {
        _recentLogs.Add(new { ts = DateTime.UtcNow.ToString("HH:mm:ss"), level, message });
        if (_recentLogs.Count > MaxRecentLogs) _recentLogs.RemoveAt(0);
    }

    // ─── State Snapshot 建構 ───

    private AgUiEvent BuildStateSnapshot()
    {
        return new AgUiEvent
        {
            Type = AgUiEventTypes.StateSnapshot,
            Snapshot = new
            {
                nodeStates = new Dictionary<string, string>(_nodeStates),
                nodeOutputs = new Dictionary<string, string>(_nodeOutputs),
                pendingHumanInput = _pendingHumanInput,
                pendingDebugAction = _pendingDebugAction,
                recentLogs = _recentLogs.ToList(),
                executionStats = new
                {
                    durationMs = _stopwatch.ElapsedMilliseconds,
                    totalTokens = _totalTokens,
                    totalSteps = _totalSteps,
                    totalToolCalls = _toolCallTotal,
                    estimatedCost = _totalCost > 0
                        ? "~" + ModelPricing.FormatCost(_totalCost)
                        : null,
                },
                traceSpans = _currentTraceSpans,
                ragCitations = _lastRagCitations,
                expandedQueries = _lastExpandedQueries,
                executionId = _runId,
            }
        };
    }

    // ─── Message 管理 ───

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

    /// <summary>
    /// 產生執行統計文字訊息（steps / tools / tokens / cost / duration）。
    /// </summary>
    private IEnumerable<AgUiEvent> BuildStatsMessage(string? flowCost = null)
    {
        var parts = new List<string>();
        if (_totalSteps > 0) parts.Add($"Steps {_totalSteps}");
        if (_toolCallTotal > 0) parts.Add($"Tools {_toolCallTotal}");
        if (_totalTokens > 0) parts.Add($"~{_totalTokens:N0} tokens");

        // 成本：優先使用 Flow 精確值，否則用 per-agent 累加值
        var costStr = flowCost;
        if (costStr is null && _totalCost > 0)
        {
            costStr = "~" + ModelPricing.FormatCost(_totalCost);
        }
        if (costStr is not null) parts.Add(costStr);

        var seconds = _stopwatch.ElapsedMilliseconds / 1000.0;
        parts.Add($"{seconds:F1}s");

        var statsText = $"\n\n📊 {string.Join(" · ", parts)}";
        var msgId = NextMessageId();
        yield return new AgUiEvent { Type = AgUiEventTypes.TextMessageStart, MessageId = msgId, Role = "assistant" };
        yield return new AgUiEvent { Type = AgUiEventTypes.TextMessageContent, MessageId = msgId, Delta = statsText };
        yield return new AgUiEvent { Type = AgUiEventTypes.TextMessageEnd, MessageId = msgId };
    }

    /// <summary>
    /// 產生錯誤文字訊息事件序列（TextMessageStart → Content → End）。
    /// 供 converter 和 StreamExecutionEvents 共用。
    /// </summary>
    public IEnumerable<AgUiEvent> BuildErrorMessage(string message)
    {
        var msgId = NextMessageId();
        yield return new AgUiEvent { Type = AgUiEventTypes.TextMessageStart, MessageId = msgId, Role = "assistant" };
        yield return new AgUiEvent { Type = AgUiEventTypes.TextMessageContent, MessageId = msgId, Delta = $"❌ 執行失敗：{message}" };
        yield return new AgUiEvent { Type = AgUiEventTypes.TextMessageEnd, MessageId = msgId };
    }

    private string NextMessageId() => $"{_runId}-msg-{++_messageCounter}";
    private string CurrentMessageId() => $"{_runId}-msg-{_messageCounter}";
    private string NextToolCallId() => $"{_runId}-tc-{++_toolCallCounter}";
    private string CurrentToolCallId() => $"{_runId}-tc-{_toolCallCounter}";

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
