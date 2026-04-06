using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Loop 節點執行器 — 重複執行 body 直到條件滿足或達上限。
/// 自行管理導航（ManagesOwnNavigation = true）。
/// </summary>
public sealed class LoopNodeExecutor : INodeExecutor
{
    public string NodeType => NodeTypes.Loop;

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, WorkflowNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Loop 不直接產生 events — 邏輯在 BuildResultAsync 中處理
        await Task.CompletedTask;
        yield break;
    }

    public async Task<NodeExecutionResult> BuildResultAsync(
        string nodeId, WorkflowNode node,
        ImperativeExecutionState state, List<ExecutionEvent> collectedEvents,
        CancellationToken cancellationToken = default)
    {
        state.LoopCounters.TryGetValue(nodeId, out var iteration);

        // 評估退出條件
        var exitMet = await EvaluateConditionAsync(node, state.PreviousResult, state, cancellationToken);

        if (exitMet || iteration >= node.MaxIterations)
        {
            state.LoopCounters.Remove(nodeId);
            return new NodeExecutionResult
            {
                ManagesOwnNavigation = true,
                NextNodeId = WorkflowGraphHelper.GetNextNodeId(state.Adjacency, nodeId, OutputPorts.Output2)
            };
        }

        state.LoopCounters[nodeId] = iteration + 1;

        var bodyStartId = WorkflowGraphHelper.GetNextNodeId(state.Adjacency, nodeId, OutputPorts.Output1);
        if (bodyStartId is null || state.ExecuteBodyChain is null)
        {
            state.LoopCounters.Remove(nodeId);
            return new NodeExecutionResult
            {
                ManagesOwnNavigation = true,
                NextNodeId = WorkflowGraphHelper.GetNextNodeId(state.Adjacency, nodeId, OutputPorts.Output2)
            };
        }

        var bodyResult = await state.ExecuteBodyChain(bodyStartId, nodeId, state.PreviousResult, state, cancellationToken);
        state.PreviousResult = bodyResult;

        // 回到 loop 節點重新評估
        return new NodeExecutionResult
        {
            Output = bodyResult,
            ManagesOwnNavigation = true,
            NextNodeId = nodeId
        };
    }

    private static async Task<bool> EvaluateConditionAsync(
        WorkflowNode node, string text, ImperativeExecutionState state, CancellationToken cancellationToken)
    {
        var expr = node.ConditionExpression;
        if (string.IsNullOrWhiteSpace(expr))
            expr = "DONE";

        return node.ConditionType?.ToLowerInvariant() switch
        {
            "contains" => text.Contains(expr, StringComparison.OrdinalIgnoreCase),
            "regex" => Regex.IsMatch(text, expr, RegexOptions.IgnoreCase),
            "llm-judge" => await EvaluateLlmJudgeAsync(expr, text, state, cancellationToken),
            _ => text.Contains(expr, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static async Task<bool> EvaluateLlmJudgeAsync(
        string prompt, string text, ImperativeExecutionState state, CancellationToken cancellationToken)
    {
        if (state.JudgeHolder.Client is null)
            return text.Contains("YES", StringComparison.OrdinalIgnoreCase);

        var judgePrompt = $"{prompt}\n\nContent to evaluate:\n{text}\n\nReply with exactly YES or NO.";
        var messages = new List<ChatMessage> { new(ChatRole.User, judgePrompt) };
        var response = await state.JudgeHolder.Client.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return (response.Text ?? "").Contains("YES", StringComparison.OrdinalIgnoreCase);
    }
}
