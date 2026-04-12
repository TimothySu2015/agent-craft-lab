using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Condition 節點執行器 — 評估條件決定分支路徑。
/// 支援 contains、regex、llm-judge 三種模式。
/// </summary>
public sealed class ConditionNodeExecutor : NodeExecutorBase<ConditionNode>
{
    protected override async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, ConditionNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Condition 不產生 events — 靜默評估
        await Task.CompletedTask;
        yield break;
    }

    protected override async Task<NodeExecutionResult> BuildResultAsync(
        string nodeId, ConditionNode node,
        ImperativeExecutionState state, List<ExecutionEvent> collectedEvents,
        CancellationToken cancellationToken = default)
    {
        var expr = string.IsNullOrWhiteSpace(node.Condition.Value) ? "DONE" : node.Condition.Value;

        // 解析條件表達式中的變數引用
        if (state.VariableResolver.HasReferences(expr))
        {
            expr = state.VariableResolver.Resolve(expr, state.ToVariableContext());
        }

        var text = state.PreviousResult;
        var met = node.Condition.Kind switch
        {
            ConditionKind.Contains => text.Contains(expr, StringComparison.OrdinalIgnoreCase),
            ConditionKind.Regex => Regex.IsMatch(text, expr, RegexOptions.IgnoreCase),
            ConditionKind.LlmJudge => await EvaluateLlmJudgeAsync(expr, text, state, cancellationToken),
            _ => text.Contains(expr, StringComparison.OrdinalIgnoreCase)
        };

        return new NodeExecutionResult
        {
            OutputPort = met ? OutputPorts.Output1 : OutputPorts.Output2
        };
    }

    private static async Task<bool> EvaluateLlmJudgeAsync(
        string prompt, string text,
        ImperativeExecutionState state, CancellationToken cancellationToken)
    {
        if (state.JudgeHolder.Client is null)
            return text.Contains("YES", StringComparison.OrdinalIgnoreCase);

        var judgePrompt = $"{prompt}\n\nContent to evaluate:\n{text}\n\nReply with exactly YES or NO.";
        var messages = new List<ChatMessage> { new(ChatRole.User, judgePrompt) };
        var response = await state.JudgeHolder.Client.GetResponseAsync(messages, cancellationToken: cancellationToken);
        var result = response.Text ?? "";
        return result.Contains("YES", StringComparison.OrdinalIgnoreCase);
    }
}
