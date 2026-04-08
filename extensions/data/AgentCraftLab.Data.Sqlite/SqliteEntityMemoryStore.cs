using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Data.Sqlite;

/// <summary>
/// EntityMemory 的 SQLite 實作 — 儲存 Agent 執行中發現的實體及事實。
/// </summary>
public class SqliteEntityMemoryStore(IServiceScopeFactory scopeFactory) : IEntityMemoryStore
{
    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task SaveAsync(EntityMemoryDocument entity)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        db.EntityMemories.Add(entity);
        await db.SaveChangesAsync();
    }

    public async Task<EntityMemoryDocument?> FindByNameAsync(string userId, string entityName)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        // SQLite 預設 TEXT 比對為 case-sensitive，改用 ToLower 比對
        var nameLower = entityName.ToLowerInvariant();
        return await db.EntityMemories
            .FirstOrDefaultAsync(e =>
                e.UserId == userId &&
                e.EntityName.ToLower() == nameLower);
    }

    public async Task<List<EntityMemoryDocument>> SearchAsync(string userId, string query, int limit = 10)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        if (string.IsNullOrWhiteSpace(query))
        {
            return await db.EntityMemories
                .Where(e => e.UserId == userId)
                .OrderByDescending(e => e.UpdatedAt)
                .Take(limit)
                .ToListAsync();
        }

        // 簡單 keyword 匹配：實體名稱或事實包含任一查詢詞
        var queryTokens = query
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToList();

        var candidates = await db.EntityMemories
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.UpdatedAt)
            .Take(100)
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
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var nameLower = entityName.ToLowerInvariant();
        var existing = await db.EntityMemories
            .FirstOrDefaultAsync(e =>
                e.UserId == userId &&
                e.EntityName.ToLower() == nameLower);

        if (existing is not null)
        {
            // 合併事實：解析既有 + 新增 → 去重 → 保留最新 20 筆
            var currentFacts = ParseFacts(existing.Facts);
            var merged = currentFacts.Union(newFacts, StringComparer.OrdinalIgnoreCase)
                .TakeLast(20)
                .ToList();

            existing.Facts = JsonSerializer.Serialize(merged);
            existing.MergedCount++;
            existing.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(sourceExecutionId))
            {
                existing.SourceExecutionId = sourceExecutionId;
            }
        }
        else
        {
            db.EntityMemories.Add(new EntityMemoryDocument
            {
                Id = $"ent-{Guid.NewGuid():N}"[..16],
                UserId = userId,
                EntityName = entityName,
                EntityType = entityType,
                Facts = JsonSerializer.Serialize(newFacts.Take(20).ToList()),
                SourceExecutionId = sourceExecutionId,
                MergedCount = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task<int> CleanupAsync(string userId, int maxCount = 500, int maxAgeDays = 180)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
        var idsToDelete = new HashSet<string>();

        var allRecords = await db.EntityMemories
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.UpdatedAt)
            .Select(e => new { e.Id, e.UpdatedAt })
            .ToListAsync();

        foreach (var r in allRecords.Where(r => r.UpdatedAt < cutoff))
        {
            idsToDelete.Add(r.Id);
        }

        var remaining = allRecords.Where(r => !idsToDelete.Contains(r.Id)).ToList();
        if (remaining.Count > maxCount)
        {
            foreach (var r in remaining.Take(remaining.Count - maxCount))
            {
                idsToDelete.Add(r.Id);
            }
        }

        if (idsToDelete.Count > 0)
        {
            await db.EntityMemories
                .Where(e => idsToDelete.Contains(e.Id))
                .ExecuteDeleteAsync();
        }

        return idsToDelete.Count;
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
