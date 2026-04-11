using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Router 節點執行器 — 多路分類，將輸入分派到 N 條路由之一。
/// 兩種模式：
///   contains（預設）— 確定性比對前一個節點的輸出，零 LLM 成本。搭配 Classifier Agent 使用。
///   llm — 內建 LLM 分類（類似 Dify 問題分類器），借用畫布上已有的 ChatClient。
/// </summary>
public sealed class RouterNodeExecutor : INodeExecutor
{
    public string NodeType => NodeTypes.Router;

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, WorkflowNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var routes = node.Routes?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        if (routes.Length == 0)
        {
            _lastOutputPort = OutputPorts.Output1;
            yield break;
        }

        var isLlmMode = node.ConditionType?.Equals("llm", StringComparison.OrdinalIgnoreCase) == true;
        var selectedIndex = isLlmMode
            ? await ClassifyWithLlmAsync(node, routes, state, cancellationToken)
            : MatchRouteByKeyword(state.PreviousResult, routes);

        var selectedRoute = selectedIndex < routes.Length ? routes[selectedIndex] : routes[^1];
        _lastOutputPort = $"output_{selectedIndex + 1}";

        var agentName = node.Name ?? nodeId;
        var outputText = $"Routed to: {selectedRoute}";

        yield return ExecutionEvent.AgentStarted(agentName, null);
        yield return ExecutionEvent.TextChunk(agentName, outputText);
        yield return ExecutionEvent.AgentCompleted(agentName, outputText, 0, 0, null);
    }

    [ThreadStatic] private static string? _lastOutputPort;

    public Task<NodeExecutionResult> BuildResultAsync(
        string nodeId, WorkflowNode node,
        ImperativeExecutionState state, List<ExecutionEvent> collectedEvents,
        CancellationToken cancellationToken = default)
    {
        var port = _lastOutputPort ?? OutputPorts.Output1;
        _lastOutputPort = null;
        return Task.FromResult(new NodeExecutionResult { OutputPort = port });
    }

    /// <summary>
    /// LLM 模式 — 借用畫布上任一 agent 的 ChatClient 或 JudgeClient 做分類。
    /// </summary>
    private static async Task<int> ClassifyWithLlmAsync(
        WorkflowNode node, string[] routes,
        ImperativeExecutionState state, CancellationToken cancellationToken)
    {
        var client = state.ChatClients.Values.FirstOrDefault() ?? state.JudgeHolder.Client;
        if (client is null)
        {
            // 無 LLM 可用 — fallback 到關鍵字匹配
            return MatchRouteByKeyword(state.PreviousResult, routes);
        }

        var condExpr = node.ConditionExpression;
        if (!string.IsNullOrWhiteSpace(condExpr) && NodeReferenceResolver.HasVariableReferences(condExpr))
        {
            condExpr = NodeReferenceResolver.ResolveVariables(condExpr, state.SystemVariables, state.Variables, state.EnvironmentVariables);
        }

        var routeList = string.Join(", ", routes.Select((r, i) => $"{i + 1}. {r}"));
        var prompt = string.IsNullOrWhiteSpace(condExpr)
            ? $"Classify the following input into one of these categories: {routeList}\n\nInput:\n{state.PreviousResult}\n\nReply with ONLY the category number (e.g., 1, 2, or 3)."
            : $"{condExpr}\n\nCategories:\n{routeList}\n\nInput:\n{state.PreviousResult}\n\nReply with ONLY the category number (e.g., 1, 2, or 3).";

        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var response = await client.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return ParseRouteIndex(response.Text?.Trim() ?? "", routes);
    }

    /// <summary>
    /// 從 LLM 回覆中解析路由索引。支援數字（"1"、"2"）和路由名稱（"billing"）。
    /// </summary>
    internal static int ParseRouteIndex(string response, string[] routes)
    {
        var digits = new string(response.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var num) && num >= 1 && num <= routes.Length)
        {
            return num - 1;
        }

        return MatchRouteByKeyword(response, routes);
    }

    /// <summary>
    /// 確定性模式 — 比對文字是否包含各 route name。都沒命中回傳最後一個（default）。
    /// </summary>
    internal static int MatchRouteByKeyword(string text, string[] routes)
    {
        for (var i = 0; i < routes.Length; i++)
        {
            if (text.Contains(routes[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        // 都沒命中 → 最後一個 route（通常設為「一般」或 default）
        return routes.Length - 1;
    }
}
