using AgentCraftLab.Data;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

public class MongoContextualMemoryStore(MongoDbContext db) : IContextualMemoryStore
{
    private static string GenerateId() => $"ctx-{Guid.NewGuid():N}"[..12];

    public async Task SaveAsync(ContextualMemoryDocument pattern)
    {
        if (string.IsNullOrEmpty(pattern.Id))
        {
            pattern.Id = GenerateId();
        }

        pattern.CreatedAt = DateTime.UtcNow;
        pattern.UpdatedAt = DateTime.UtcNow;
        await db.ContextualMemories.InsertOneAsync(pattern);
    }

    public async Task<List<ContextualMemoryDocument>> GetPatternsAsync(string userId, int limit = 10)
    {
        return await db.ContextualMemories
            .Find(p => p.UserId == userId)
            .SortByDescending(p => p.Confidence)
            .ThenByDescending(p => p.OccurrenceCount)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task UpsertPatternAsync(
        string userId, string patternType, string description, float confidence)
    {
        var candidates = await db.ContextualMemories
            .Find(p => p.UserId == userId && p.PatternType == patternType)
            .ToListAsync();

        var descLower = description.ToLowerInvariant();
        var existing = candidates.FirstOrDefault(p =>
            ComputeOverlap(p.Description.ToLowerInvariant(), descLower) > 0.6);

        if (existing is not null)
        {
            var update = Builders<ContextualMemoryDocument>.Update
                .Inc(p => p.OccurrenceCount, 1)
                .Set(p => p.Confidence, Math.Max(existing.Confidence, confidence))
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            if (description.Length > existing.Description.Length)
            {
                update = update.Set(p => p.Description, description);
            }

            await db.ContextualMemories.UpdateOneAsync(p => p.Id == existing.Id, update);
        }
        else
        {
            var pattern = new ContextualMemoryDocument
            {
                Id = GenerateId(),
                UserId = userId,
                PatternType = patternType,
                Description = description,
                Confidence = confidence,
                OccurrenceCount = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await db.ContextualMemories.InsertOneAsync(pattern);
        }
    }

    public async Task<int> CleanupAsync(string userId, int maxCount = 50, int maxAgeDays = 365)
    {
        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);

        var ageDeletion = await db.ContextualMemories.DeleteManyAsync(
            p => p.UserId == userId && p.UpdatedAt < cutoff);

        var totalDeleted = (int)ageDeletion.DeletedCount;

        var remainingCount = (int)await db.ContextualMemories.CountDocumentsAsync(
            p => p.UserId == userId);

        if (remainingCount > maxCount)
        {
            var excessCount = remainingCount - maxCount;
            var excessIds = await db.ContextualMemories
                .Find(p => p.UserId == userId)
                .SortBy(p => p.UpdatedAt)
                .Limit(excessCount)
                .Project(p => p.Id)
                .ToListAsync();

            if (excessIds.Count > 0)
            {
                var excessDeletion = await db.ContextualMemories.DeleteManyAsync(
                    p => excessIds.Contains(p.Id));
                totalDeleted += (int)excessDeletion.DeletedCount;
            }
        }

        return totalDeleted;
    }

    private static double ComputeOverlap(string a, string b)
    {
        var tokensA = a.Split([' ', ',', '.', '，', '。', '\n'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var tokensB = b.Split([' ', ',', '.', '，', '。', '\n'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return 0;
        }

        var intersection = tokensA.Intersect(tokensB).Count();
        var union = tokensA.Union(tokensB).Count();
        return union > 0 ? (double)intersection / union : 0;
    }
}
