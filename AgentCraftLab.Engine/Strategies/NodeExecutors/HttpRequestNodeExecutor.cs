using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// HTTP Request 節點執行器 — 確定性 HTTP 呼叫，零 LLM 成本。
/// 支援兩種模式：
/// 1. Catalog 模式：填 HttpApiId → 從 HttpApiDefs 查找定義
/// 2. Inline 模式：HttpApiId 為空 → 用節點自身的 HttpUrl/HttpMethod/HttpHeaders/HttpBodyTemplate
/// </summary>
public sealed class HttpRequestNodeExecutor : INodeExecutor
{
    public string NodeType => NodeTypes.HttpRequest;

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, WorkflowNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = string.IsNullOrWhiteSpace(node.Name) ? $"HTTP_{node.Id}" : node.Name;
        yield return ExecutionEvent.AgentStarted(nodeName);
        yield return ExecutionEvent.ToolCall(nodeName, "HTTP", node.HttpApiId);

        var result = await CallHttpApiAsync(node, state.PreviousResult, state);

        yield return ExecutionEvent.TextChunk(nodeName, result);
        yield return ExecutionEvent.AgentCompleted(nodeName, result);
    }

    private static async Task<string> CallHttpApiAsync(
        WorkflowNode node, string input, ImperativeExecutionState state)
    {
        var httpService = state.AgentContext.HttpApiService;

        if (httpService is null)
            return "[HTTP Error] HttpApiToolService not available";

        var apiDef = ResolveApiDefinition(node, state);
        if (apiDef is null)
            return $"[HTTP Error] API '{node.HttpApiId}' not found and no inline URL configured. " +
                   "Either set httpApiId to reference a catalog entry, or fill in URL directly.";

        var escapedInput = JsonSerializer.Serialize(input).Trim('"');
        var argsJson = (node.HttpArgsTemplate ?? "{}").Replace("{input}", escapedInput);

        try
        {
            return await httpService.CallApiAsync(apiDef, argsJson);
        }
        catch (Exception ex)
        {
            return $"[HTTP Error] {ex.Message}";
        }
    }

    /// <summary>
    /// 解析 API 定義：優先 catalog，fallback inline 欄位。
    /// </summary>
    private static HttpApiDefinition? ResolveApiDefinition(
        WorkflowNode node, ImperativeExecutionState state)
    {
        // Catalog 模式
        if (!string.IsNullOrWhiteSpace(node.HttpApiId))
        {
            var httpDefs = state.AgentContext.HttpApiDefs;
            if (httpDefs is not null && httpDefs.TryGetValue(node.HttpApiId, out var catalogDef))
                return catalogDef;
        }

        // Inline 模式
        if (!string.IsNullOrWhiteSpace(node.HttpUrl))
        {
            return new HttpApiDefinition
            {
                Id = node.Id ?? "inline",
                Name = node.Name ?? "inline-http",
                Url = node.HttpUrl,
                Method = string.IsNullOrWhiteSpace(node.HttpMethod) ? "GET" : node.HttpMethod,
                Headers = node.HttpHeaders ?? "",
                BodyTemplate = node.HttpBodyTemplate ?? "",
                ContentType = string.IsNullOrWhiteSpace(node.HttpContentType) ? "application/json" : node.HttpContentType,
                ResponseMaxLength = node.HttpResponseMaxLength,
                TimeoutSeconds = node.HttpTimeoutSeconds,
                AuthMode = string.IsNullOrWhiteSpace(node.HttpAuthMode) ? "none" : node.HttpAuthMode,
                AuthCredential = node.HttpAuthCredential ?? "",
                AuthKeyName = node.HttpAuthKeyName ?? "",
                RetryCount = node.HttpRetryCount,
                RetryDelayMs = node.HttpRetryDelayMs,
                ResponseFormat = string.IsNullOrWhiteSpace(node.HttpResponseFormat) ? "text" : node.HttpResponseFormat,
                ResponseJsonPath = node.HttpResponseJsonPath ?? "",
            };
        }

        return null;
    }
}
