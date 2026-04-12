using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AgentCraftLab.Engine.Diagnostics;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Agents.AI.Workflows;

namespace AgentCraftLab.Engine.Strategies;

/// <summary>
/// 共用的 workflow streaming 事件消費迴圈，供 Sequential 和 Handoff 策略使用。
/// </summary>
public static class WorkflowStreamHelper
{
    public static async IAsyncEnumerable<ExecutionEvent> RunWorkflowStreamAsync(
        Workflow workflow,
        string userMessage,
        System.Collections.Concurrent.ConcurrentQueue<(string AgentName, string Type, string Text)>? toolCallLogs,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        string? sessionId = null)
    {
        await using StreamingRun run = await InProcessExecution.Default
            .OpenStreamingAsync(workflow);
        await run.TrySendMessageAsync(userMessage);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string currentAgent = "";
        var currentText = new StringBuilder();
        Activity? currentAgentActivity = null;

        await foreach (WorkflowEvent evt in run.WatchStreamAsync()
                           .WithCancellation(cancellationToken))
        {
            if (evt is AgentResponseUpdateEvent e)
            {
                var executorName = e.ExecutorId ?? "";

                if (executorName != currentAgent)
                {
                    if (toolCallLogs is not null)
                    {
                        while (toolCallLogs.TryDequeue(out var log))
                        {
                            yield return log.Type == "call"
                                ? ExecutionEvent.ToolCall(log.AgentName, log.Text, "")
                                : ExecutionEvent.ToolResult(log.AgentName, log.Text, "");
                        }
                    }

                    // 關閉前一個 agent 的 Activity（設 tokens 再 Stop）
                    if (!string.IsNullOrEmpty(currentAgent))
                    {
                        var prevText = currentText.ToString();
                        var est = ModelPricing.EstimateTokens(prevText);
                        SetAgentTags(currentAgentActivity, est, est, prevText);
                        currentAgentActivity?.Stop();
                        currentAgentActivity?.Dispose();
                        yield return ExecutionEvent.AgentCompleted(currentAgent, prevText, est, est);
                    }

                    currentAgent = executorName;
                    currentText.Clear();
                    // 開始新 agent 的 Activity
                    currentAgentActivity = EngineActivitySource.StartNodeExecution(
                        "agent", executorName, sessionId: sessionId);
                    yield return ExecutionEvent.AgentStarted(executorName);
                }

                if (toolCallLogs is not null)
                {
                    while (toolCallLogs.TryDequeue(out var toolLog))
                    {
                        yield return toolLog.Type == "call"
                            ? ExecutionEvent.ToolCall(toolLog.AgentName, toolLog.Text, "")
                            : ExecutionEvent.ToolResult(toolLog.AgentName, toolLog.Text, "");
                    }
                }

                var text = e.Update?.Text ?? "";
                if (!string.IsNullOrEmpty(text))
                {
                    currentText.Append(text);
                    yield return ExecutionEvent.TextChunk(executorName, text);
                }
            }
            else if (evt is WorkflowErrorEvent errorEvt)
            {
                var ex = errorEvt.Exception;
                var errorDetail = ex is not null
                    ? $"{ex.Message}{(ex.InnerException is not null ? $" → {ex.InnerException.Message}" : "")}\n{ex.StackTrace}"
                    : "Unknown workflow error";
                yield return ExecutionEvent.Error(errorDetail);
                yield break;
            }
            else if (evt is WorkflowOutputEvent)
            {
                if (!string.IsNullOrEmpty(currentAgent))
                {
                    var outText = currentText.ToString();
                    var est = ModelPricing.EstimateTokens(outText);
                    SetAgentTags(currentAgentActivity, est, est, outText);
                    currentAgentActivity?.Stop();
                    currentAgentActivity?.Dispose();
                    currentAgentActivity = null;
                    yield return ExecutionEvent.AgentCompleted(currentAgent, outText, est, est);
                }
                break;
            }
        }
    }

    /// <summary>
    /// 執行 PreAgent hook，回傳處理後的 input。若 hook 阻擋則回傳 null。
    /// </summary>
    public static async Task<(string? transformedInput, ExecutionEvent? hookEvent)> RunPreAgentHookAsync(
        WorkflowStrategyContext context, string agentName, string agentId, string input, CancellationToken ct)
    {
        if (context.HookRunner is null || context.Hooks?.PreAgent is null)
        {
            return (input, null);
        }

        var hookCtx = new HookContext
        {
            Input = input,
            AgentName = agentName,
            AgentId = agentId,
            WorkflowName = context.Payload.Settings.Strategy,
            UserId = context.UserId
        };
        var result = await context.HookRunner.ExecuteAsync(context.Hooks.PreAgent, hookCtx, ct);
        if (result.IsBlocked)
        {
            return (null, ExecutionEvent.HookBlocked("PreAgent", result.Message ?? "Blocked"));
        }

        var evt = result.Message is not null ? ExecutionEvent.HookExecuted("PreAgent", result.Message) : null;
        return (result.TransformedInput, evt);
    }

    /// <summary>
    /// 執行 PostAgent hook（通知用途，不修改 output）。
    /// </summary>
    public static async Task<ExecutionEvent?> RunPostAgentHookAsync(
        WorkflowStrategyContext context, string agentName, string agentId, string input, string output, CancellationToken ct)
    {
        if (context.HookRunner is null || context.Hooks?.PostAgent is null)
        {
            return null;
        }

        var hookCtx = new HookContext
        {
            Input = input,
            Output = output,
            AgentName = agentName,
            AgentId = agentId,
            WorkflowName = context.Payload.Settings.Strategy,
            UserId = context.UserId
        };
        var result = await context.HookRunner.ExecuteAsync(context.Hooks.PostAgent, hookCtx, ct);
        return result.Message is not null ? ExecutionEvent.HookExecuted("PostAgent", result.Message) : null;
    }

    /// <summary>在 Activity Stop 前設定 agent 相關的 OTel tags。</summary>
    private static void SetAgentTags(Activity? activity, long inputTokens, long outputTokens, string? result)
    {
        if (activity is null) return;
        activity.SetTag("gen_ai.usage.input_tokens", inputTokens);
        activity.SetTag("gen_ai.usage.output_tokens", outputTokens);
        if (result is { Length: > 0 and <= 2000 })
            activity.SetTag("gen_ai.response.text", result);
        else if (result is { Length: > 2000 })
            activity.SetTag("gen_ai.response.text", result[..2000]);
    }
}
