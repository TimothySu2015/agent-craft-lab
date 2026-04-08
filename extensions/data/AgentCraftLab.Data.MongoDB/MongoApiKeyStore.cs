using AgentCraftLab.Data;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

public class MongoApiKeyStore(MongoDbContext db) : IApiKeyStore
{
    private static string GenerateId() => $"ak-{Guid.NewGuid():N}"[..12];

    public async Task<ApiKeyDocument> SaveAsync(string userId, string name, string keyHash, string keyPrefix,
        string? scopedWorkflowIds = null, DateTime? expiresAt = null)
    {
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

        await db.ApiKeys.InsertOneAsync(doc);
        return doc;
    }

    public async Task<List<ApiKeyDocument>> ListAsync(string userId)
    {
        return await db.ApiKeys
            .Find(k => k.UserId == userId)
            .SortByDescending(k => k.CreatedAt)
            .ToListAsync();
    }

    public async Task<ApiKeyDocument?> GetAsync(string id)
    {
        return await db.ApiKeys.Find(k => k.Id == id).FirstOrDefaultAsync();
    }

    public async Task<ApiKeyDocument?> FindByHashAsync(string keyHash)
    {
        var now = DateTime.UtcNow;
        return await db.ApiKeys.Find(k =>
            k.KeyHash == keyHash &&
            !k.IsRevoked &&
            (!k.ExpiresAt.HasValue || k.ExpiresAt > now))
            .FirstOrDefaultAsync();
    }

    public async Task<bool> RevokeAsync(string userId, string id)
    {
        var result = await db.ApiKeys.UpdateOneAsync(
            k => k.Id == id && k.UserId == userId,
            Builders<ApiKeyDocument>.Update
                .Set(k => k.IsRevoked, true)
                .Set(k => k.UpdatedAt, DateTime.UtcNow));
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var result = await db.ApiKeys.DeleteOneAsync(k => k.Id == id && k.UserId == userId);
        return result.DeletedCount > 0;
    }

    public async Task UpdateLastUsedAsync(string id)
    {
        await db.ApiKeys.UpdateOneAsync(
            k => k.Id == id,
            Builders<ApiKeyDocument>.Update.Set(k => k.LastUsedAt, DateTime.UtcNow));
    }
}
