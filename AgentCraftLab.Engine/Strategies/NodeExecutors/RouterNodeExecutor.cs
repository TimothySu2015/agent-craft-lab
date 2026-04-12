using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Router 節點執行器 — 多路分類，將輸入分派到 N 條路由之一。
/// 兩種模式：
///   contains（預設）— 確定性比對前一個節點的輸出，零 LLM 成本。搭配 Classifier Agent 使用。
///   llm — 內建 LLM 分類（類似 Dify 問題分類器），借用畫布上已有的 ChatClient。
/// 新 schema 的 RouterNode 目前無 Mode 欄位 — 暫時以第一條路由 Keywords 非空判斷 llm-mode（TODO Phase F 加 RouterMode）。
/// </summary>
public sealed class RouterNodeExecutor : NodeExecutorBase<RouterNode>
{
    protected override async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, RouterNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var routes = node.Routes.Select(r => r.Name).ToArray();

        if (routes.Length == 0)
        {
            _lastOutputPort = OutputPorts.Output1;
            yield break;
        }

        // Phase C 過渡：Router 沒有 Mode 欄位，一律走 keyword matching。
        // LLM 分類模式待 Phase F 加 Mode enum 後恢復。
        var selectedIndex = MatchRouteByKeyword(state.PreviousResult, routes);

        var selectedRoute = selectedIndex < routes.Length ? routes[selectedIndex] : routes[^1];
        _lastOutputPort = $"output_{selectedIndex + 1}";

        var agentName = string.IsNullOrEmpty(node.Name) ? nodeId : node.Name;
        var outputText = $"Routed to: {selectedRoute}";

        yield return ExecutionEvent.AgentStarted(agentName, null);
        yield return ExecutionEvent.TextChunk(agentName, outputText);
        yield return ExecutionEvent.AgentCompleted(agentName, outputText, 0, 0, null);
        await Task.CompletedTask;
    }

    [ThreadStatic] private static string? _lastOutputPort;

    protected override Task<NodeExecutionResult> BuildResultAsync(
        string nodeId, RouterNode node,
        ImperativeExecutionState state, List<ExecutionEvent> collectedEvents,
        CancellationToken cancellationToken = default)
    {
        var port = _lastOutputPort ?? OutputPorts.Output1;
        _lastOutputPort = null;
        return Task.FromResult(new NodeExecutionResult { OutputPort = port });
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

        return routes.Length - 1;
    }
}
