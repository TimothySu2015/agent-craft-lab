using AgentCraftLab.Data;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

public class MongoExecutionMemoryStore(MongoDbContext db) : IExecutionMemoryStore
{
    private static string GenerateId() => $"mem-{Guid.NewGuid():N}"[..12];

    public async Task SaveAsync(ExecutionMemoryDocument memory)
    {
        if (string.IsNullOrEmpty(memory.Id))
        {
            memory.Id = GenerateId();
        }

        if (memory.CreatedAt == default)
        {
            memory.CreatedAt = DateTime.UtcNow;
        }

        await db.ExecutionMemories.InsertOneAsync(memory);
    }

    public async Task<List<ExecutionMemoryDocument>> SearchAsync(
        string userId, string goalKeywords, int limit = 5)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var candidates = await db.ExecutionMemories
            .Find(m => m.UserId == userId && m.CreatedAt > cutoff)
            .SortByDescending(m => m.CreatedAt)
            .Limit(50)
            .ToListAsync();

        if (string.IsNullOrWhiteSpace(goalKeywords) || candidates.Count == 0)
        {
            return candidates.Take(limit).ToList();
        }

        char[] separators = [' ', ',', '，', '。', '\n'];
        var queryWords = goalKeywords
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return candidates
            .Select(m =>
            {
                var docWords = m.GoalKeywords
                    .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var intersection = queryWords.Intersect(docWords).Count();
                var union = queryWords.Union(docWords).Count();
                var similarity = union > 0 ? (double)intersection / union : 0;
                return (Memory: m, Similarity: similarity);
            })
            .Where(x => x.Similarity > 0.1)
            .OrderByDescending(x => x.Similarity)
            .ThenByDescending(x => x.Memory.CreatedAt)
            .Take(limit)
            .Select(x => x.Memory)
            .ToList();
    }

    public async Task<int> CleanupAsync(string userId, int maxCount = 200, int maxAgeDays = 90)
    {
        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);

        var ageDeletion = await db.ExecutionMemories.DeleteManyAsync(
            m => m.UserId == userId && m.CreatedAt < cutoff);

        var totalDeleted = (int)ageDeletion.DeletedCount;

        var remainingCount = (int)await db.ExecutionMemories.CountDocumentsAsync(
            m => m.UserId == userId);

        if (remainingCount > maxCount)
        {
            var excessCount = remainingCount - maxCount;
            var excessIds = await db.ExecutionMemories
                .Find(m => m.UserId == userId)
                .SortBy(m => m.CreatedAt)
                .Limit(excessCount)
                .Project(m => m.Id)
                .ToListAsync();

            if (excessIds.Count > 0)
            {
                var excessDeletion = await db.ExecutionMemories.DeleteManyAsync(
                    m => excessIds.Contains(m.Id));
                totalDeleted += (int)excessDeletion.DeletedCount;
            }
        }

        return totalDeleted;
    }
}
