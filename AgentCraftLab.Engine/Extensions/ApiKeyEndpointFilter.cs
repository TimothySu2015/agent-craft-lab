using AgentCraftLab.Engine.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentCraftLab.Engine.Extensions;

/// <summary>
/// HttpContext.Items 中 API Key 驗證結果的 key 常數。
/// </summary>
public static class ApiKeyItemKeys
{
    public const string UserId = "ApiKeyUserId";
    public const string ApiKeyId = "ApiKeyId";
    public const string ApiKeyName = "ApiKeyName";
}

/// <summary>
/// 端點 Filter：驗證 API Key（X-Api-Key header 或 Authorization: Bearer）。
/// 驗證成功後將 userId/apiKeyId 存入 HttpContext.Items。
/// </summary>
public class ApiKeyEndpointFilter(ApiKeyService apiKeyService) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // 從 header 擷取 API Key
        var rawKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(rawKey))
        {
            var auth = httpContext.Request.Headers.Authorization.FirstOrDefault();
            if (auth is not null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                rawKey = auth["Bearer ".Length..].Trim();
            }
        }

        if (string.IsNullOrEmpty(rawKey))
        {
            return Results.Json(new { error = "API Key required. Use X-Api-Key header or Authorization: Bearer." }, statusCode: 401);
        }

        // 從路由取得 workflow key
        var workflowKey = httpContext.GetRouteValue("key")?.ToString();

        var result = await apiKeyService.ValidateAsync(rawKey, workflowKey);
        if (result is null)
        {
            return Results.Json(new { error = "Invalid or expired API Key." }, statusCode: 403);
        }

        // 將驗證結果存入 HttpContext.Items 供下游使用
        httpContext.Items[ApiKeyItemKeys.UserId] = result.UserId;
        httpContext.Items[ApiKeyItemKeys.ApiKeyId] = result.ApiKeyId;
        httpContext.Items[ApiKeyItemKeys.ApiKeyName] = result.ApiKeyName;

        return await next(context);
    }
}
