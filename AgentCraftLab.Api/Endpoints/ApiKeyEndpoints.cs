using AgentCraftLab.Data;
using AgentCraftLab.Engine.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentCraftLab.Api.Endpoints;

public static class ApiKeyEndpoints
{
    public static void MapApiKeyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/keys");

        group.MapGet("/", async ([FromServices] IApiKeyStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var list = await store.ListAsync(userId);
            return Results.Ok(list.Select(k => ToInfo(k)).ToList());
        });

        group.MapPost("/", async (CreateApiKeyRequest req,
            [FromServices] ApiKeyService svc, IUserContext userCtx) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new ApiError("KEY_NAME_REQUIRED", "Name is required"));
            }

            var userId = await userCtx.GetUserIdAsync();
            DateTime? expiresAt = null;
            if (!string.IsNullOrEmpty(req.ExpiresAt) && DateTime.TryParse(req.ExpiresAt, out var parsed))
            {
                expiresAt = parsed.ToUniversalTime();
            }

            var (doc, rawKey) = await svc.CreateAsync(userId, req.Name, req.ScopedWorkflowIds, expiresAt);

            return Results.Created($"/api/keys/{doc.Id}", new
            {
                doc.Id,
                doc.Name,
                doc.KeyPrefix,
                doc.ScopedWorkflowIds,
                doc.IsRevoked,
                doc.LastUsedAt,
                doc.ExpiresAt,
                doc.CreatedAt,
                RawKey = rawKey,
            });
        });

        group.MapDelete("/{id}", async (string id,
            [FromServices] IApiKeyStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var ok = await store.RevokeAsync(userId, id);
            return ok ? Results.NoContent() : Results.NotFound();
        });
    }

    private static object ToInfo(ApiKeyDocument k) => new
    {
        k.Id,
        k.Name,
        k.KeyPrefix,
        k.ScopedWorkflowIds,
        k.IsRevoked,
        k.LastUsedAt,
        k.ExpiresAt,
        k.CreatedAt,
    };
}

public record CreateApiKeyRequest(string Name, string? ScopedWorkflowIds, string? ExpiresAt);
