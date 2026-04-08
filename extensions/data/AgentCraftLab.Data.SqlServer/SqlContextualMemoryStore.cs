using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Data.SqlServer;

/// <summary>
/// ContextualMemory 的 SQL Server 實作 — 儲存使用者互動模式與偏好。
/// </summary>
public class SqlContextualMemoryStore(IServiceScopeFactory scopeFactory) : IContextualMemoryStore
{
    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task SaveAsync(ContextualMemoryDocument pattern)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        db.ContextualMemories.Add(pattern);
        await db.SaveChangesAsync();
    }

    public async Task<List<ContextualMemoryDocument>> GetPatternsAsync(string userId, int limit = 10)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.ContextualMemories
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.Confidence)
            .ThenByDescending(p => p.OccurrenceCount)
            .Take(limit)
            .ToListAsync();
    }

    public async Task UpsertPatternAsync(
        string userId, string patternType, string description, float confidence)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        // 尋找相似模式（同類型 + 描述包含匹配）
        var candidates = await db.ContextualMemories
            .Where(p => p.UserId == userId && p.PatternType == patternType)
            .ToListAsync();

        var descLower = description.ToLowerInvariant();
        var existing = candidates.FirstOrDefault(p =>
            ComputeOverlap(p.Description.ToLowerInvariant(), descLower) > 0.6);

        if (existing is not null)
        {
            // 更新既有模式
            existing.OccurrenceCount++;
            existing.Confidence = Math.Max(existing.Confidence, confidence);
            existing.UpdatedAt = DateTime.UtcNow;
            // 若新描述更具體（更長），更新描述
            if (description.Length > existing.Description.Length)
            {
                existing.Description = description;
            }
        }
        else
        {
            db.ContextualMemories.Add(new ContextualMemoryDocument
            {
                Id = $"ctx-{Guid.NewGuid():N}"[..16],
                UserId = userId,
                PatternType = patternType,
                Description = description,
                Confidence = confidence,
                OccurrenceCount = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task<int> CleanupAsync(string userId, int maxCount = 50, int maxAgeDays = 365)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
        var idsToDelete = new HashSet<string>();

        var allRecords = await db.ContextualMemories
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.UpdatedAt)
            .Select(p => new { p.Id, p.UpdatedAt })
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
            await db.ContextualMemories
                .Where(p => idsToDelete.Contains(p.Id))
                .ExecuteDeleteAsync();
        }

        return idsToDelete.Count;
    }

    /// <summary>簡易 token overlap 比率（用於判斷描述是否相似）。</summary>
    private static double ComputeOverlap(string a, string b)
    {
        var tokensA = a.Split([' ', ',', '.'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var tokensB = b.Split([' ', ',', '.'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return 0;
        }

        var intersection = tokensA.Intersect(tokensB).Count();
        var union = tokensA.Union(tokensB).Count();
        return union > 0 ? (double)intersection / union : 0;
    }
}
