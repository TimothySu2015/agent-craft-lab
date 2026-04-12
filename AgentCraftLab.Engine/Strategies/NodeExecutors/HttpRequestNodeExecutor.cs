using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Services.Variables;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// HTTP Request 節點執行器 — 確定性 HTTP 呼叫，零 LLM 成本。
/// 透過 <see cref="HttpRequestSpec"/> polymorphic 分派 Catalog（引用預定義 API）或 Inline（就地定義）。
/// </summary>
public sealed class HttpRequestNodeExecutor : NodeExecutorBase<HttpRequestNode>
{
    protected override async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, HttpRequestNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = string.IsNullOrWhiteSpace(node.Name) ? $"HTTP_{node.Id}" : node.Name;
        yield return ExecutionEvent.AgentStarted(nodeName);

        var specDescription = node.Spec switch
        {
            CatalogHttpRef catalog => catalog.ApiId,
            InlineHttpRequest inline => inline.Url,
            _ => "(unknown)"
        };
        yield return ExecutionEvent.ToolCall(nodeName, "HTTP", specDescription);

        var result = await CallHttpAsync(node, state.PreviousResult, state);

        yield return ExecutionEvent.TextChunk(nodeName, result);
        yield return ExecutionEvent.AgentCompleted(nodeName, result);
    }

    private static async Task<string> CallHttpAsync(
        HttpRequestNode node, string input, ImperativeExecutionState state)
    {
        var httpService = state.AgentContext.HttpApiService;
        if (httpService is null)
        {
            return "[HTTP Error] HttpApiToolService not available";
        }

        var (apiDef, argsJson) = node.Spec switch
        {
            CatalogHttpRef catalog => BuildFromCatalog(catalog, input, state),
            InlineHttpRequest inline => BuildFromInline(inline, node.Id, node.Name, input, state),
            _ => ((HttpApiDefinition?)null, "{}")
        };

        if (apiDef is null)
        {
            return "[HTTP Error] API not found and no inline URL configured. " +
                   "Either set apiId (catalog mode) or fill in URL (inline mode).";
        }

        try
        {
            return await httpService.CallApiAsync(apiDef, argsJson);
        }
        catch (Exception ex)
        {
            return $"[HTTP Error] {ex.Message}";
        }
    }

    private static (HttpApiDefinition? Def, string Args) BuildFromCatalog(
        CatalogHttpRef catalog, string input, ImperativeExecutionState state)
    {
        var httpDefs = state.AgentContext.HttpApiDefs;
        if (httpDefs is null || !httpDefs.TryGetValue(catalog.ApiId, out var def))
        {
            return (null, "{}");
        }

        var escapedInput = JsonSerializer.Serialize(input).Trim('"');
        var argsJson = (catalog.Args?.ToJsonString() ?? "{}").Replace("{input}", escapedInput);
        return (def, argsJson);
    }

    private static (HttpApiDefinition? Def, string Args) BuildFromInline(
        InlineHttpRequest inline, string nodeId, string nodeName, string input,
        ImperativeExecutionState state)
    {
        if (string.IsNullOrWhiteSpace(inline.Url))
        {
            return (null, "{}");
        }

        var resolver = state.VariableResolver;
        var ctx = state.ToVariableContext();

        var url = resolver.Resolve(inline.Url, ctx);
        var headersStr = FormatHeaders(inline.Headers, resolver, ctx);
        var bodyTemplate = inline.Body?.Content?.ToString() ?? "";
        if (!string.IsNullOrEmpty(bodyTemplate))
        {
            bodyTemplate = resolver.Resolve(bodyTemplate, ctx);
        }

        var (authMode, authCredential, authKeyName) = inline.Auth switch
        {
            BearerAuth b => ("bearer", resolver.Resolve(b.Token, ctx), ""),
            BasicAuth ba => ("basic", resolver.Resolve(ba.UserPass, ctx), ""),
            ApiKeyHeaderAuth kh => ("apikey-header", resolver.Resolve(kh.Value, ctx), kh.KeyName),
            ApiKeyQueryAuth kq => ("apikey-query", resolver.Resolve(kq.Value, ctx), kq.KeyName),
            _ => ("none", "", "")
        };

        var (responseFormat, responseJsonPath) = inline.Response switch
        {
            JsonParser => ("json", ""),
            JsonPathParser jp => ("jsonpath", jp.Path),
            _ => ("text", "")
        };

        var escapedInput = JsonSerializer.Serialize(input).Trim('"');
        var argsJson = string.IsNullOrEmpty(bodyTemplate) ? "{}" : bodyTemplate.Replace("{input}", escapedInput);

        var def = new HttpApiDefinition
        {
            Id = string.IsNullOrEmpty(nodeId) ? "inline" : nodeId,
            Name = string.IsNullOrEmpty(nodeName) ? "inline-http" : nodeName,
            Url = url,
            Method = FormatMethod(inline.Method),
            Headers = headersStr,
            BodyTemplate = bodyTemplate,
            ContentType = inline.ContentType,
            ResponseMaxLength = inline.ResponseMaxLength,
            TimeoutSeconds = inline.TimeoutSeconds,
            AuthMode = authMode,
            AuthCredential = authCredential,
            AuthKeyName = authKeyName,
            RetryCount = inline.Retry.Count,
            RetryDelayMs = inline.Retry.DelayMs,
            ResponseFormat = responseFormat,
            ResponseJsonPath = responseJsonPath
        };

        return (def, argsJson);
    }

    private static string FormatHeaders(
        IReadOnlyList<HttpHeader> headers, IVariableResolver resolver, VariableContext ctx)
    {
        if (headers.Count == 0)
        {
            return "";
        }

        return string.Join("\n",
            headers.Select(h => $"{h.Name}: {resolver.Resolve(h.Value, ctx)}"));
    }

    private static string FormatMethod(HttpMethodKind method) => method switch
    {
        HttpMethodKind.Post => "POST",
        HttpMethodKind.Put => "PUT",
        HttpMethodKind.Delete => "DELETE",
        HttpMethodKind.Patch => "PATCH",
        HttpMethodKind.Head => "HEAD",
        HttpMethodKind.Options => "OPTIONS",
        _ => "GET"
    };
}
