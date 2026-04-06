using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Engine.Data;

/// <summary>
/// Checkpoint 的 SQLite 實作 — 儲存 ReAct 迴圈的完整執行狀態快照。
/// </summary>
public class SqliteCheckpointStore(IServiceScopeFactory scopeFactory) : ICheckpointStore
{
    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task SaveAsync(CheckpointDocument checkpoint)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        // 同一 execution + iteration 的 checkpoint 存在時覆蓋
        var existing = await db.Checkpoints
            .FirstOrDefaultAsync(c => c.ExecutionId == checkpoint.ExecutionId && c.Iteration == checkpoint.Iteration);

        if (existing is not null)
        {
            existing.StateJson = checkpoint.StateJson;
            existing.StateSizeBytes = checkpoint.StateSizeBytes;
            existing.MessageCount = checkpoint.MessageCount;
            existing.TokensUsed = checkpoint.TokensUsed;
            existing.CreatedAt = checkpoint.CreatedAt;
        }
        else
        {
            db.Checkpoints.Add(checkpoint);
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<CheckpointDocument>> ListAsync(string executionId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Checkpoints
            .Where(c => c.ExecutionId == executionId)
            .OrderBy(c => c.Iteration)
            .ToListAsync();
    }

    public async Task<List<CheckpointDocument>> ListMetadataAsync(string executionId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Checkpoints
            .Where(c => c.ExecutionId == executionId)
            .OrderBy(c => c.Iteration)
            .Select(c => new CheckpointDocument
            {
                Id = c.Id,
                ExecutionId = c.ExecutionId,
                Iteration = c.Iteration,
                MessageCount = c.MessageCount,
                TokensUsed = c.TokensUsed,
                StateSizeBytes = c.StateSizeBytes,
                CreatedAt = c.CreatedAt
                // StateJson 不載入
            })
            .ToListAsync();
    }

    public async Task<CheckpointDocument?> GetAsync(string executionId, int iteration)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Checkpoints
            .FirstOrDefaultAsync(c => c.ExecutionId == executionId && c.Iteration == iteration);
    }

    public async Task<CheckpointDocument?> GetLatestAsync(string executionId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Checkpoints
            .Where(c => c.ExecutionId == executionId)
            .OrderByDescending(c => c.Iteration)
            .FirstOrDefaultAsync();
    }

    public async Task CleanupAsync(string executionId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        await db.Checkpoints
            .Where(c => c.ExecutionId == executionId)
            .ExecuteDeleteAsync();
    }

    public async Task CleanupOlderThanAsync(TimeSpan maxAge)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var cutoff = DateTime.UtcNow - maxAge;
        await db.Checkpoints
            .Where(c => c.CreatedAt < cutoff)
            .ExecuteDeleteAsync();
    }
}
