using AgentCraftLab.Search.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Data.PostgreSQL;

/// <summary>
/// ExecutionMemory 的 PostgreSQL 實作 — 記錄 Autonomous Agent 執行記憶，供跨 Session 學習。
/// 可選接入 CraftSearch（ISearchEngine）提供語義搜索，否則 fallback 到 Jaccard 相似度。
/// </summary>
public class PgExecutionMemoryStore(
    IServiceScopeFactory scopeFactory,
    ISearchEngine? searchEngine = null,
    ILogger<PgExecutionMemoryStore>? logger = null) : IExecutionMemoryStore
{
    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task SaveAsync(ExecutionMemoryDocument memory)
    {
        var (db, scope) = CreateScope();
        await using var disposable = scope as IAsyncDisposable;

        db.ExecutionMemories.Add(memory);
        await db.SaveChangesAsync();

        // 背景索引到 CraftSearch（不阻塞主流程）
        if (searchEngine is not null)
        {
            _ = IndexToSearchEngineAsync(memory);
        }
    }

    public async Task<List<ExecutionMemoryDocument>> SearchAsync(
        string userId, string goalKeywords, int limit = 5)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        // 先按 userId + 30 天時間窗口過濾，再按時間倒序取出候選（減少記憶體使用）
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var candidates = await db.ExecutionMemories
            .Where(m => m.UserId == userId && m.CreatedAt > cutoff)
            .OrderByDescending(m => m.CreatedAt)
            .Take(50) // 50 筆足以找到相似項
            .ToListAsync();

        if (string.IsNullOrWhiteSpace(goalKeywords) || candidates.Count == 0)
        {
            return candidates.Take(limit).ToList();
        }

        // 用 Jaccard 相似度排序
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
            .Where(x => x.Similarity > 0.1) // 最低門檻
            .OrderByDescending(x => x.Similarity)
            .ThenByDescending(x => x.Memory.CreatedAt)
            .Take(limit)
            .Select(x => x.Memory)
            .ToList();
    }

    public async Task<int> CleanupAsync(string userId, int maxCount = 200, int maxAgeDays = 90)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);

        // 單次查詢：取回所有記錄的 Id + CreatedAt（輕量投影，避免載入完整文件）
        var allRecords = await db.ExecutionMemories
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Id, m.CreatedAt })
            .ToListAsync();

        // 標記要刪除的：過期 + 超額
        var idsToDelete = new HashSet<string>();

        // 1. 過期記錄
        foreach (var r in allRecords.Where(r => r.CreatedAt < cutoff))
        {
            idsToDelete.Add(r.Id);
        }

        // 2. 超額記錄（排除已標記過期的，從最舊開始刪）
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
            await db.ExecutionMemories
                .Where(m => idsToDelete.Contains(m.Id))
                .ExecuteDeleteAsync();
        }

        return idsToDelete.Count;
    }

    /// <summary>
    /// 語義搜索 — 有 CraftSearch 時用語義搜索，否則 fallback 到 Jaccard。
    /// </summary>
    public async Task<List<ExecutionMemoryDocument>> SemanticSearchAsync(
        string userId, string goalDescription, int limit = 5)
    {
        if (searchEngine is null)
        {
            return await SearchAsync(userId, goalDescription, limit);
        }

        try
        {
            var indexName = $"memory_{userId}_execution";
            var results = await searchEngine.SearchAsync(indexName, new SearchQuery
            {
                Text = goalDescription,
                Mode = SearchMode.FullText,
                TopK = limit
            });

            if (results.Count == 0)
            {
                return await SearchAsync(userId, goalDescription, limit);
            }

            // 用搜索結果的 ID 從 PostgreSQL 取回完整 document
            var ids = results.Select(r => r.Id).ToHashSet();
            var (db, scope) = CreateScope();
            await using var _ = scope as IAsyncDisposable;

            var documents = await db.ExecutionMemories
                .Where(m => ids.Contains(m.Id))
                .ToListAsync();

            // 按 CraftSearch BM25 排序
            var idOrder = results.Select(r => r.Id).ToList();
            return documents
                .OrderBy(d => idOrder.IndexOf(d.Id))
                .ToList();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[Memory] Semantic search failed, falling back to Jaccard");
            return await SearchAsync(userId, goalDescription, limit);
        }
    }

    /// <summary>背景將記憶索引到 CraftSearch，不阻塞主流程。</summary>
    private async Task IndexToSearchEngineAsync(ExecutionMemoryDocument memory)
    {
        try
        {
            var indexName = $"memory_{memory.UserId}_execution";
            await searchEngine!.EnsureIndexAsync(indexName);

            var content = $"{memory.GoalKeywords}\n{memory.Reflection}";
            if (!string.IsNullOrEmpty(memory.ResultSummary))
            {
                content += $"\n{memory.ResultSummary}";
            }

            await searchEngine.IndexDocumentsAsync(indexName,
            [
                new SearchDocument
                {
                    Id = memory.Id,
                    Content = content,
                    Metadata = new Dictionary<string, string>
                    {
                        ["succeeded"] = memory.Succeeded.ToString(),
                        ["createdAt"] = memory.CreatedAt.ToString("O"),
                        ["stepCount"] = memory.StepCount.ToString(),
                        ["tokensUsed"] = memory.TokensUsed.ToString()
                    }
                }
            ]);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[Memory] FTS indexing failed for {Id}", memory.Id);
        }
    }
}
