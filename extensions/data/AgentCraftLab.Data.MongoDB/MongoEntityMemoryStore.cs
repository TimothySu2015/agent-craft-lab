using System.Text.Json;
using System.Text.RegularExpressions;
using AgentCraftLab.Data;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

public class MongoEntityMemoryStore(MongoDbContext db) : IEntityMemoryStore
{
    private static string GenerateId() => $"ent-{Guid.NewGuid():N}"[..12];

    public async Task SaveAsync(EntityMemoryDocument entity)
    {
        if (string.IsNullOrEmpty(entity.Id))
        {
            entity.Id = GenerateId();
        }

        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.EntityMemories.InsertOneAsync(entity);
    }

    public async Task<EntityMemoryDocument?> FindByNameAsync(string userId, string entityName)
    {
        var filter = Builders<EntityMemoryDocument>.Filter.Eq(e => e.UserId, userId)
                   & Builders<EntityMemoryDocument>.Filter.Regex(e => e.EntityName,
                       new BsonRegularExpression($"^{Regex.Escape(entityName)}$", "i"));
        return await db.EntityMemories.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<List<EntityMemoryDocument>> SearchAsync(string userId, string query, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await db.EntityMemories
                .Find(e => e.UserId == userId)
                .SortByDescending(e => e.UpdatedAt)
                .Limit(limit)
                .ToListAsync();
        }

        var queryTokens = query
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToList();

        var candidates = await db.EntityMemories
            .Find(e => e.UserId == userId)
            .SortByDescending(e => e.UpdatedAt)
            .Limit(100)
            .ToListAsync();

        return candidates
            .Select(e =>
            {
                var nameScore = queryTokens.Count(t =>
                    e.EntityName.Contains(t, StringComparison.OrdinalIgnoreCase));
                var factScore = queryTokens.Count(t =>
                    e.Facts.Contains(t, StringComparison.OrdinalIgnoreCase));
                return (Entity: e, Score: nameScore * 3 + factScore);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entity.MergedCount)
            .Take(limit)
            .Select(x => x.Entity)
            .ToList();
    }

    public async Task MergeFactsAsync(
        string userId, string entityName, List<string> newFacts,
        string entityType = "concept", string sourceExecutionId = "")
    {
        var existing = await FindByNameAsync(userId, entityName);

        if (existing is not null)
        {
            var currentFacts = ParseFacts(existing.Facts);
            var merged = currentFacts.Union(newFacts, StringComparer.OrdinalIgnoreCase)
                .TakeLast(20)
                .ToList();

            var update = Builders<EntityMemoryDocument>.Update
                .Set(e => e.Facts, JsonSerializer.Serialize(merged))
                .Inc(e => e.MergedCount, 1)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            if (!string.IsNullOrEmpty(sourceExecutionId))
            {
                update = update.Set(e => e.SourceExecutionId, sourceExecutionId);
            }

            await db.EntityMemories.UpdateOneAsync(e => e.Id == existing.Id, update);
        }
        else
        {
            var entity = new EntityMemoryDocument
            {
                Id = GenerateId(),
                UserId = userId,
                EntityName = entityName,
                EntityType = entityType,
                Facts = JsonSerializer.Serialize(newFacts.Take(20).ToList()),
                SourceExecutionId = sourceExecutionId,
                MergedCount = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await db.EntityMemories.InsertOneAsync(entity);
        }
    }

    public async Task<int> CleanupAsync(string userId, int maxCount = 500, int maxAgeDays = 180)
    {
        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);

        var ageDeletion = await db.EntityMemories.DeleteManyAsync(
            e => e.UserId == userId && e.UpdatedAt < cutoff);

        var totalDeleted = (int)ageDeletion.DeletedCount;

        var remainingCount = (int)await db.EntityMemories.CountDocumentsAsync(
            e => e.UserId == userId);

        if (remainingCount > maxCount)
        {
            var excessCount = remainingCount - maxCount;
            var excessIds = await db.EntityMemories
                .Find(e => e.UserId == userId)
                .SortBy(e => e.UpdatedAt)
                .Limit(excessCount)
                .Project(e => e.Id)
                .ToListAsync();

            if (excessIds.Count > 0)
            {
                var excessDeletion = await db.EntityMemories.DeleteManyAsync(
                    e => excessIds.Contains(e.Id));
                totalDeleted += (int)excessDeletion.DeletedCount;
            }
        }

        return totalDeleted;
    }

    private static List<string> ParseFacts(string factsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(factsJson) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
