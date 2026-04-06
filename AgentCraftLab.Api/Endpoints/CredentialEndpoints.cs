using System.Collections.Concurrent;
using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Api.Endpoints;

public static class CredentialEndpoints
{
    /// <summary>runtime-keys 速率限制：每 IP 每分鐘最多 10 次。</summary>
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Window)> _runtimeKeyRateLimit = new();
    private const int RuntimeKeyMaxPerMinute = 10;

    public static void MapCredentialEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/credentials");

        group.MapPost("/", async (SaveCredentialRequest req, ICredentialStore store, IUserContext userCtx) =>
        {
            if (string.IsNullOrWhiteSpace(req.Provider))
            {
                return Results.BadRequest(new ApiError("CREDENTIAL_PROVIDER_REQUIRED", "Provider is required"));
            }

            var userId = await userCtx.GetUserIdAsync();
            var doc = await store.SaveAsync(userId, req.Provider, req.Name ?? req.Provider, req.ApiKey ?? "", req.Endpoint ?? "", req.Model ?? "");
            return Results.Created($"/api/credentials/{doc.Id}", ToSafeResponse(doc));
        });

        group.MapGet("/", async (ICredentialStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var list = await store.ListAsync(userId);
            var safe = list.Select(c => new
            {
                c.Id,
                c.Provider,
                c.Name,
                hasApiKey = !string.IsNullOrEmpty(c.EncryptedApiKey),
                c.Endpoint,
                c.Model,
                c.CreatedAt,
                c.UpdatedAt,
            }).ToList();
            return Results.Ok(safe);
        });

        group.MapPut("/{id}", async (string id, SaveCredentialRequest req, ICredentialStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var doc = await store.UpdateAsync(userId, id, req.Name ?? req.Provider ?? "", req.ApiKey ?? "", req.Endpoint ?? "", req.Model ?? "");
            return doc is not null
                ? Results.Ok(ToSafeResponse(doc))
                : Results.NotFound(new ApiError("CREDENTIAL_NOT_FOUND", Params: new() { ["id"] = id }));
        });

        group.MapDelete("/{id}", async (string id, ICredentialStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var ok = await store.DeleteAsync(userId, id);
            return ok
                ? Results.NoContent()
                : Results.NotFound(new ApiError("CREDENTIAL_NOT_FOUND", Params: new() { ["id"] = id }));
        });

        // 內部端點 — Runtime 用，回傳解密後的 credentials（僅 localhost + 速率限制 + 審計日誌）
        group.MapGet("/runtime-keys", async (HttpContext ctx, ICredentialStore store, IUserContext userCtx, ILogger<Program> logger) =>
        {
            // 安全層 1：僅允許 localhost 存取
            var remote = ctx.Connection.RemoteIpAddress;
            if (remote is not null && !System.Net.IPAddress.IsLoopback(remote))
            {
                logger.LogWarning("runtime-keys 拒絕非 localhost 存取: {RemoteIp}", remote);
                return Results.Forbid();
            }

            // 安全層 2：速率限制（每 IP 每分鐘 10 次）
            var clientIp = remote?.ToString() ?? "unknown";
            var now = DateTime.UtcNow;
            var entry = _runtimeKeyRateLimit.AddOrUpdate(clientIp,
                _ => (1, now),
                (_, prev) => now - prev.Window > TimeSpan.FromMinutes(1) ? (1, now) : (prev.Count + 1, prev.Window));
            if (entry.Count > RuntimeKeyMaxPerMinute)
            {
                logger.LogWarning("runtime-keys 速率限制觸發: {ClientIp}, count={Count}", clientIp, entry.Count);
                return Results.StatusCode(429);
            }

            // 定期清理過期記錄，避免長期運行記憶體增長
            if (entry.Count == 1 && _runtimeKeyRateLimit.Count > 100)
            {
                foreach (var key in _runtimeKeyRateLimit
                    .Where(kv => now - kv.Value.Window > TimeSpan.FromMinutes(5))
                    .Select(kv => kv.Key).ToList())
                {
                    _runtimeKeyRateLimit.TryRemove(key, out _);
                }
            }

            var userId = await userCtx.GetUserIdAsync();
            logger.LogInformation("runtime-keys 存取: userId={UserId}, clientIp={ClientIp}", userId, clientIp);

            var creds = await store.GetDecryptedCredentialsAsync(userId);
            // 只回傳 provider 名稱清單，不回傳完整 apiKey（降低暴露面）
            var keys = creds.Select(kv => new
            {
                provider = kv.Key,
                apiKey = kv.Value.ApiKey,
                endpoint = kv.Value.Endpoint,
                model = kv.Value.Model,
            }).ToList();
            return Results.Ok(keys);
        });
    }

    private static object ToSafeResponse(CredentialDocument c) => new
    {
        c.Id, c.Provider, c.Name,
        hasApiKey = !string.IsNullOrEmpty(c.EncryptedApiKey),
        c.Endpoint, c.Model, c.CreatedAt, c.UpdatedAt,
    };
}

public record SaveCredentialRequest(string? Provider, string? Name, string? ApiKey, string? Endpoint, string? Model);
