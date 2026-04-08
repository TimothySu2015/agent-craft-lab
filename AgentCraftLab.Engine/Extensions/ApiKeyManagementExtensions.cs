using AgentCraftLab.Data;
using AgentCraftLab.Engine.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentCraftLab.Engine.Extensions;

/// <summary>
/// API Key 管理端點（受 FallbackPolicy 保護）。
/// </summary>
public static class ApiKeyManagementExtensions
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/keys");

        // 建立 API Key
        group.MapPost("", async (HttpContext ctx, IUserContext userCtx, ApiKeyService keyService) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var body = await ctx.Request.ReadFromJsonAsync<CreateApiKeyRequest>();
            if (body is null || string.IsNullOrWhiteSpace(body.Name))
            {
                return Results.BadRequest(new { error = "name is required" });
            }

            var (doc, rawKey) = await keyService.CreateAsync(userId, body.Name, body.ScopedWorkflowIds, body.ExpiresAt);
            return Results.Ok(new
            {
                doc.Id,
                doc.Name,
                doc.KeyPrefix,
                doc.ScopedWorkflowIds,
                doc.ExpiresAt,
                doc.CreatedAt,
                rawKey
            });
        });

        // 列出使用者的 API Keys
        group.MapGet("", async (IUserContext userCtx, IApiKeyStore store) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var keys = await store.ListAsync(userId);
            return Results.Ok(keys.Select(k => new
            {
                k.Id,
                k.Name,
                k.KeyPrefix,
                k.ScopedWorkflowIds,
                k.IsRevoked,
                k.LastUsedAt,
                k.ExpiresAt,
                k.CreatedAt
            }));
        });

        // 撤銷 API Key
        group.MapDelete("{id}", async (string id, IUserContext userCtx, IApiKeyStore store) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var revoked = await store.RevokeAsync(userId, id);
            return revoked ? Results.Ok(new { success = true }) : Results.NotFound();
        });

        return app;
    }

    private record CreateApiKeyRequest(string Name, string? ScopedWorkflowIds = null, DateTime? ExpiresAt = null);
}
