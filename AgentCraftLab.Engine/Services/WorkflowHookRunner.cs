using System.Text.Json;
using System.Text.RegularExpressions;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.Logging;
using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 執行 Workflow Hook（<see cref="Schema.CodeHook"/> / <see cref="Schema.WebhookHook"/> 兩種 polymorphic 類型）。
/// 回傳值：(blocked, transformedInput)
/// - blocked=true 表示 hook 攔截了請求，不應繼續執行
/// - transformedInput 是經過 hook 處理後的輸入（僅 code 類型有意義）
/// </summary>
public class WorkflowHookRunner
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WorkflowHookRunner> _logger;

    private static readonly TimeSpan WebhookTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BlockPatternTimeout = TimeSpan.FromSeconds(2);

    public WorkflowHookRunner(IHttpClientFactory httpClientFactory, ILogger<WorkflowHookRunner> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// 執行單一 hook。回傳 (是否阻擋, 處理後的文字, 錯誤訊息)。
    /// </summary>
    public async Task<HookResult> ExecuteAsync(Schema.WorkflowHook hook, HookContext context, CancellationToken ct = default)
    {
        try
        {
            // 阻擋檢查（BlockPattern 匹配時攔截）
            if (!string.IsNullOrEmpty(hook.BlockPattern))
            {
                var isBlocked = Regex.IsMatch(context.Input, hook.BlockPattern, RegexOptions.IgnoreCase, BlockPatternTimeout);
                if (isBlocked)
                {
                    var reason = hook.BlockMessage ?? $"Input blocked by hook pattern: {hook.BlockPattern}";
                    return HookResult.Blocked(reason);
                }
            }

            return hook switch
            {
                Schema.WebhookHook wh => await ExecuteWebhookAsync(wh, context, ct),
                Schema.CodeHook ch => ExecuteCode(ch, context),
                _ => HookResult.Ok(context.Input, $"Unknown hook type: {hook.GetType().Name}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hook execution failed: {Type}", hook.GetType().Name);
            return HookResult.Ok(context.Input, $"Hook error: {ex.Message}");
        }
    }

    private HookResult ExecuteCode(Schema.CodeHook hook, HookContext context)
    {
        var expandedExpression = ExpandTemplate(hook.Expression, context);
        var result = TransformHelper.ApplyTransform(
            hook.Kind,
            context.Input,
            expandedExpression,
            hook.Replacement,
            hook.MaxLength,
            hook.Delimiter,
            hook.SplitIndex);
        return HookResult.Ok(result);
    }

    private async Task<HookResult> ExecuteWebhookAsync(Schema.WebhookHook hook, HookContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hook.Url))
        {
            return HookResult.Ok(context.Input, "Webhook URL is empty, skipped.");
        }

        // SSRF 防護
        var (isSafe, ssrfError) = await SafeUrlValidator.ValidateAsync(hook.Url);
        if (!isSafe) return HookResult.Blocked(ssrfError ?? "SSRF blocked");

        var client = _httpClientFactory.CreateClient();
        client.Timeout = WebhookTimeout;

        var body = hook.BodyTemplate ?? JsonSerializer.Serialize(context, JsonOptions);
        body = ExpandTemplate(body, context);

        var methodString = hook.Method switch
        {
            Schema.HttpMethodKind.Get => "GET",
            Schema.HttpMethodKind.Post => "POST",
            Schema.HttpMethodKind.Put => "PUT",
            Schema.HttpMethodKind.Delete => "DELETE",
            Schema.HttpMethodKind.Patch => "PATCH",
            Schema.HttpMethodKind.Head => "HEAD",
            Schema.HttpMethodKind.Options => "OPTIONS",
            _ => "POST"
        };

        using var request = new HttpRequestMessage(new HttpMethod(methodString), hook.Url)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

        foreach (var header in hook.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Name, ExpandTemplate(header.Value, context));
        }

        try
        {
            using var response = await client.SendAsync(request, ct);
            _logger.LogDebug("Webhook {Url} responded {StatusCode}", hook.Url, response.StatusCode);
            return HookResult.Ok(context.Input, $"Webhook {response.StatusCode}");
        }
        catch (TaskCanceledException)
        {
            return HookResult.Ok(context.Input, "Webhook timeout, skipped.");
        }
    }

    /// <summary>
    /// 展開 HookContext 變數：{{input}}, {{output}}, {{agentName}}, {{agentId}}, {{workflowName}}, {{userId}}, {{error}}。
    /// </summary>
    private static string ExpandTemplate(string? template, HookContext context)
    {
        if (string.IsNullOrEmpty(template))
        {
            return "";
        }

        return template
            .Replace("{{input}}", context.Input)
            .Replace("{{output}}", context.Output ?? "")
            .Replace("{{agentName}}", context.AgentName)
            .Replace("{{agentId}}", context.AgentId)
            .Replace("{{workflowName}}", context.WorkflowName)
            .Replace("{{userId}}", context.UserId)
            .Replace("{{error}}", context.Error ?? "");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// Hook 執行結果。
/// </summary>
public record HookResult(bool IsBlocked, string TransformedInput, string? Message = null)
{
    public static HookResult Ok(string input, string? message = null) => new(false, input, message);
    public static HookResult Blocked(string reason) => new(true, "", reason);
}
