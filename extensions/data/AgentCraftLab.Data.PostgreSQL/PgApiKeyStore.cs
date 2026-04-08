using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Data.PostgreSQL;

public class PgApiKeyStore(IServiceScopeFactory scopeFactory) : IApiKeyStore
{
    private static string GenerateId() => $"ak-{Guid.NewGuid():N}"[..12];

    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task<ApiKeyDocument> SaveAsync(string userId, string name, string keyHash, string keyPrefix,
        string? scopedWorkflowIds = null, DateTime? expiresAt = null)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = new ApiKeyDocument
        {
            Id = GenerateId(),
            UserId = userId,
            Name = name,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            ScopedWorkflowIds = scopedWorkflowIds ?? "",
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.ApiKeys.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<List<ApiKeyDocument>> ListAsync(string userId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.ApiKeys
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();
    }

    public async Task<ApiKeyDocument?> GetAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.ApiKeys.FindAsync(id);
    }

    public async Task<ApiKeyDocument?> FindByHashAsync(string keyHash)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var now = DateTime.UtcNow;
        return await db.ApiKeys.FirstOrDefaultAsync(k =>
            k.KeyHash == keyHash &&
            !k.IsRevoked &&
            (k.ExpiresAt == null || k.ExpiresAt > now));
    }

    public async Task<bool> RevokeAsync(string userId, string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
        if (doc is null)
        {
            return false;
        }

        doc.IsRevoked = true;
        doc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
        if (doc is null)
        {
            return false;
        }

        db.ApiKeys.Remove(doc);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task UpdateLastUsedAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.ApiKeys.FindAsync(id);
        if (doc is not null)
        {
            doc.LastUsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
}
