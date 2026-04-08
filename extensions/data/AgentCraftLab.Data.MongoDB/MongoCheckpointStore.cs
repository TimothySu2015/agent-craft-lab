using AgentCraftLab.Data;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

public class MongoCheckpointStore(MongoDbContext db) : ICheckpointStore
{
    private static string GenerateId() => $"ckpt-{Guid.NewGuid():N}"[..14];

    public async Task SaveAsync(CheckpointDocument checkpoint)
    {
        if (string.IsNullOrEmpty(checkpoint.Id))
        {
            checkpoint.Id = GenerateId();
        }

        checkpoint.CreatedAt = DateTime.UtcNow;

        await db.Checkpoints.ReplaceOneAsync(
            c => c.ExecutionId == checkpoint.ExecutionId && c.Iteration == checkpoint.Iteration,
            checkpoint,
            new ReplaceOptions { IsUpsert = true });
    }

    public async Task<List<CheckpointDocument>> ListAsync(string executionId)
    {
        return await db.Checkpoints
            .Find(c => c.ExecutionId == executionId)
            .SortBy(c => c.Iteration)
            .ToListAsync();
    }

    public async Task<List<CheckpointDocument>> ListMetadataAsync(string executionId)
    {
        return await db.Checkpoints
            .Find(c => c.ExecutionId == executionId)
            .SortBy(c => c.Iteration)
            .Project(c => new CheckpointDocument
            {
                Id = c.Id,
                ExecutionId = c.ExecutionId,
                Iteration = c.Iteration,
                MessageCount = c.MessageCount,
                TokensUsed = c.TokensUsed,
                StateSizeBytes = c.StateSizeBytes,
                CreatedAt = c.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<CheckpointDocument?> GetAsync(string executionId, int iteration)
    {
        return await db.Checkpoints
            .Find(c => c.ExecutionId == executionId && c.Iteration == iteration)
            .FirstOrDefaultAsync();
    }

    public async Task<CheckpointDocument?> GetLatestAsync(string executionId)
    {
        return await db.Checkpoints
            .Find(c => c.ExecutionId == executionId)
            .SortByDescending(c => c.Iteration)
            .FirstOrDefaultAsync();
    }

    public async Task CleanupAsync(string executionId)
    {
        await db.Checkpoints.DeleteManyAsync(c => c.ExecutionId == executionId);
    }

    public async Task CleanupOlderThanAsync(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        await db.Checkpoints.DeleteManyAsync(c => c.CreatedAt < cutoff);
    }
}
