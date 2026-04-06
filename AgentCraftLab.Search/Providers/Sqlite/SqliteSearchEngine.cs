using System.Runtime.InteropServices;
using System.Text.Json;
using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Search.Providers.Sqlite;

/// <summary>
/// SQLite 搜尋引擎 — FTS5 全文搜尋 + BLOB 向量搜尋 + RRF 混合排序。
/// </summary>
public class SqliteSearchEngine : ISearchEngine
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SearchEngineOptions _options;
    private readonly ILogger<SqliteSearchEngine> _logger;

    public SqliteSearchEngine(
        IServiceScopeFactory scopeFactory,
        SearchEngineOptions options,
        ILogger<SqliteSearchEngine> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task EnsureIndexAsync(string indexName, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        bool exists = await db.SearchIndexes.AnyAsync(i => i.Name == indexName, ct);
        if (!exists)
        {
            db.SearchIndexes.Add(new SearchIndexEntity
            {
                Name = indexName,
                DocumentCount = 0,
                CreatedAtTicks = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task DeleteIndexAsync(string indexName, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        // 整個刪除操作包在 transaction 中，確保原子性
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // 先刪 FTS5，再刪主表
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM SearchChunksFts WHERE IndexName = {0}", indexName);

        await db.SearchChunks.Where(c => c.IndexName == indexName).ExecuteDeleteAsync(ct);
        await db.SearchIndexes.Where(i => i.Name == indexName).ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);
    }

    public async Task<IndexInfo?> GetIndexInfoAsync(string indexName, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        var entity = await db.SearchIndexes.FirstOrDefaultAsync(i => i.Name == indexName, ct);
        if (entity is null)
        {
            return null;
        }

        long docCount = await db.SearchChunks.CountAsync(c => c.IndexName == indexName, ct);

        return new IndexInfo
        {
            Name = entity.Name,
            DocumentCount = docCount,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entity.CreatedAtTicks),
            LastUpdatedAt = entity.LastUpdatedAtTicks is not null
                ? DateTimeOffset.FromUnixTimeMilliseconds(entity.LastUpdatedAtTicks.Value) : null
        };
    }

    public async Task IndexDocumentsAsync(string indexName, IEnumerable<SearchDocument> documents, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        var docList = documents.ToList();

        // 主表寫入 + FTS5 同步包在同一個 transaction 中，確保一致性
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // 批次查詢哪些 Id 已存在（一次 DB round-trip）
        var docIds = docList.Select(d => d.Id).ToList();
        var existingIds = await db.SearchChunks
            .Where(c => c.IndexName == indexName && docIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToHashSetAsync(ct);

        foreach (var doc in docList)
        {
            if (existingIds.Contains(doc.Id))
            {
                var existing = await db.SearchChunks.FirstAsync(
                    c => c.Id == doc.Id && c.IndexName == indexName, ct);
                existing.Content = doc.Content;
                existing.FileName = doc.FileName;
                existing.ChunkIndex = doc.ChunkIndex;
                existing.EmbeddingBlob = doc.Vector is { Length: > 0 } ? VectorToBytes(doc.Vector.Value) : null;
                existing.MetadataJson = doc.Metadata is not null ? JsonSerializer.Serialize(doc.Metadata) : null;
            }
            else
            {
                db.SearchChunks.Add(new SearchChunkEntity
                {
                    Id = doc.Id,
                    IndexName = indexName,
                    Content = doc.Content,
                    FileName = doc.FileName,
                    ChunkIndex = doc.ChunkIndex,
                    EmbeddingBlob = doc.Vector is { Length: > 0 } ? VectorToBytes(doc.Vector.Value) : null,
                    MetadataJson = doc.Metadata is not null ? JsonSerializer.Serialize(doc.Metadata) : null
                });
            }
        }

        await db.SaveChangesAsync(ct);

        // 同步 FTS5 獨立索引（先刪舊、再插新）
        foreach (var doc in docList)
        {
            // 移除舊的 FTS 記錄（若存在）
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM SearchChunksFts WHERE ChunkId = {0} AND IndexName = {1}",
                doc.Id, indexName);

            // 插入新的 FTS 記錄
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO SearchChunksFts(ChunkId, IndexName, Content) VALUES ({0}, {1}, {2})",
                doc.Id, indexName, doc.Content);
        }

        // 更新索引元資料
        var indexEntity = await db.SearchIndexes.FirstOrDefaultAsync(i => i.Name == indexName, ct);
        if (indexEntity is not null)
        {
            indexEntity.DocumentCount = await db.SearchChunks.CountAsync(c => c.IndexName == indexName, ct);
            indexEntity.LastUpdatedAtTicks = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task DeleteDocumentsAsync(string indexName, IEnumerable<string> documentIds, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        var ids = documentIds.ToList();

        // FTS5 刪除 + 主表刪除包在 transaction 中
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        foreach (string id in ids)
        {
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM SearchChunksFts WHERE ChunkId = {0} AND IndexName = {1}",
                id, indexName);
        }

        await db.SearchChunks
            .Where(c => c.IndexName == indexName && ids.Contains(c.Id))
            .ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string indexName, SearchQuery query, CancellationToken ct = default)
    {
        var results = query.Mode switch
        {
            SearchMode.Vector => await VectorSearchAsync(indexName, query, ct),
            SearchMode.FullText => await FullTextSearchAsync(indexName, query, ct),
            SearchMode.Hybrid => await HybridSearchAsync(indexName, query, ct),
            _ => []
        };

        if (query.MinScore is { } minScore)
        {
            results = results.Where(r => r.Score >= minScore).ToList();
        }

        if (!string.IsNullOrEmpty(query.FileNameFilter))
        {
            results = results.Where(r => r.FileName.Contains(query.FileNameFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return results;
    }

    #region 向量搜尋

    private async Task<IReadOnlyList<SearchResult>> VectorSearchAsync(
        string indexName, SearchQuery query, CancellationToken ct)
    {
        if (query.Vector is not { Length: > 0 })
        {
            return [];
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        // 第一步：只載入 Id + EmbeddingBlob（不載入 Content，節省記憶體）
        var vectors = await db.SearchChunks
            .Where(c => c.IndexName == indexName && c.EmbeddingBlob != null)
            .Select(c => new { c.Id, c.EmbeddingBlob })
            .ToListAsync(ct);

        if (vectors.Count == 0)
        {
            return [];
        }

        // SIMD 加速的暴力搜尋（零拷貝向量反序列化）
        var querySpan = query.Vector.Value.Span;
        var scored = new List<(int Index, float Score)>(vectors.Count);

        for (int i = 0; i < vectors.Count; i++)
        {
            var blob = vectors[i].EmbeddingBlob!;

            // 驗證 BLOB 長度（防止損壞資料導致 crash）
            if (blob.Length < sizeof(float) * 2)
            {
                continue;
            }

            // 零拷貝：直接將 byte[] 重新解釋為 float span，不分配新陣列
            var candidateSpan = MemoryMarshal.Cast<byte, float>(blob.AsSpan());
            if (candidateSpan.Length != querySpan.Length)
            {
                continue; // 維度不匹配，跳過
            }

            float score = Scoring.CosineSimilarity.Compute(querySpan, candidateSpan);
            scored.Add((i, score));
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        // 第二步：只對 top-K 回查完整資料（Content, FileName 等）
        var topIds = scored.Take(query.TopK).Select(s => vectors[s.Index].Id).ToList();
        var fullChunks = await db.SearchChunks
            .Where(c => topIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        return scored
            .Take(query.TopK)
            .Where(s => fullChunks.ContainsKey(vectors[s.Index].Id))
            .Select(s =>
            {
                var chunk = fullChunks[vectors[s.Index].Id];
                return new SearchResult
                {
                    Id = chunk.Id,
                    Score = s.Score,
                    Content = chunk.Content,
                    FileName = chunk.FileName,
                    ChunkIndex = chunk.ChunkIndex
                };
            })
            .ToList();
    }

    #endregion

    #region 全文搜尋

    private async Task<IReadOnlyList<SearchResult>> FullTextSearchAsync(
        string indexName, SearchQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return [];
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        // trigram tokenizer 使用子字串匹配（支援 CJK 中日韓文字）
        // 對於 < 3 字元的查詢（含 2 字元中文詞），trigram 無法匹配，
        // 改用 LIKE 回退搜尋以確保 CJK 短詞可被找到
        var queryText = query.Text.Trim();

        if (queryText.Length < 3)
        {
            return await LikeSearchFallbackAsync(db, indexName, queryText, query.TopK, ct);
        }

        // 轉義雙引號，用雙引號包裹確保作為字面值搜尋
        var safeQuery = $"\"{queryText.Replace("\"", "\"\"")}\"";

        try
        {
            var results = await db.Database
                .SqlQueryRaw<FtsSearchRow>("""
                    SELECT sc.Id, sc.Content, sc.FileName, sc.ChunkIndex, -fts.rank AS Score
                    FROM SearchChunksFts fts
                    JOIN SearchChunks sc ON sc.Id = fts.ChunkId
                    WHERE fts.Content MATCH {0}
                      AND fts.IndexName = {1}
                    ORDER BY fts.rank
                    LIMIT {2}
                    """, safeQuery, indexName, query.TopK)
                .ToListAsync(ct);

            return results.Select(r => new SearchResult
            {
                Id = r.Id,
                Score = (float)r.Score,
                Content = r.Content,
                FileName = r.FileName,
                ChunkIndex = r.ChunkIndex
            }).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // FTS5 MATCH 語法錯誤或 SQLITE_BUSY — 回退到 LIKE 搜尋
            _logger.LogWarning(ex, "FTS5 MATCH failed for query '{Query}', falling back to LIKE search", queryText);
            return await LikeSearchFallbackAsync(db, indexName, queryText, query.TopK, ct);
        }
    }

    /// <summary>
    /// LIKE 回退搜尋 — 用於 trigram 無法處理的短查詢（< 3 字元）或 FTS5 錯誤時。
    /// </summary>
    private static async Task<IReadOnlyList<SearchResult>> LikeSearchFallbackAsync(
        SearchDbContext db, string indexName, string queryText, int topK, CancellationToken ct)
    {
        var likePattern = $"%{queryText.Replace("%", "").Replace("_", "")}%";

        var results = await db.SearchChunks
            .Where(c => c.IndexName == indexName && EF.Functions.Like(c.Content, likePattern))
            .OrderByDescending(c => c.Content.Length) // 較短的 chunk 匹配密度更高
            .Take(topK)
            .Select(c => new SearchResult
            {
                Id = c.Id,
                Score = 1.0f,
                Content = c.Content,
                FileName = c.FileName,
                ChunkIndex = c.ChunkIndex
            })
            .ToListAsync(ct);

        return results;
    }

    #endregion

    #region 混合搜尋

    private async Task<IReadOnlyList<SearchResult>> HybridSearchAsync(
        string indexName, SearchQuery query, CancellationToken ct)
    {
        // 並行執行兩路搜尋，擴展 TopK 確保 RRF 有足夠候選
        int expandedTopK = Math.Max(query.TopK * 3, 20);
        var expandedQuery = new SearchQuery
        {
            Text = query.Text,
            Vector = query.Vector,
            Mode = query.Mode,
            TopK = expandedTopK,
            FullTextWeight = query.FullTextWeight,
            VectorWeight = query.VectorWeight
        };

        var vectorTask = VectorSearchAsync(indexName, expandedQuery, ct);
        var fullTextTask = FullTextSearchAsync(indexName, expandedQuery, ct);

        await Task.WhenAll(vectorTask, fullTextTask);

        var vectorResults = await vectorTask;
        var fullTextResults = await fullTextTask;

        // 只有一路有結果時直接回傳
        if (vectorResults.Count == 0)
        {
            return fullTextResults.Take(query.TopK).ToList();
        }

        if (fullTextResults.Count == 0)
        {
            return vectorResults.Take(query.TopK).ToList();
        }

        // RRF 融合
        var rankedLists = new List<IReadOnlyList<string>>
        {
            vectorResults.Select(r => r.Id).ToList(),
            fullTextResults.Select(r => r.Id).ToList()
        };

        var weights = new List<float> { query.VectorWeight, query.FullTextWeight };
        var fused = ReciprocalRankFusion.Fuse(rankedLists, weights, _options.RrfK, query.TopK);

        // 建立原始結果 lookup（優先用向量結果的內容）
        var contentLookup = vectorResults
            .Concat(fullTextResults)
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => g.First());

        return fused
            .Where(f => contentLookup.ContainsKey(f.Id))
            .Select(f =>
            {
                var original = contentLookup[f.Id];
                return new SearchResult
                {
                    Id = f.Id,
                    Score = f.Score,
                    Content = original.Content,
                    FileName = original.FileName,
                    ChunkIndex = original.ChunkIndex
                };
            })
            .ToList();
    }

    #endregion

    #region 索引管理

    public async Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        var indexes = await db.SearchIndexes.ToListAsync(ct);

        return indexes.Select(e => new IndexInfo
        {
            Name = e.Name,
            DocumentCount = e.DocumentCount,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(e.CreatedAtTicks),
            LastUpdatedAt = e.LastUpdatedAtTicks is not null
                ? DateTimeOffset.FromUnixTimeMilliseconds(e.LastUpdatedAtTicks.Value) : null
        }).ToList();
    }

    public async Task<int> CleanupStaleIndexesAsync(TimeSpan ttl, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        long cutoffTicks = (DateTimeOffset.UtcNow - ttl).ToUnixTimeMilliseconds();

        // SQL 端篩選過期索引（避免載入全部到記憶體）
        var staleNames = await db.SearchIndexes
            .Where(e => (e.LastUpdatedAtTicks != null && e.LastUpdatedAtTicks < cutoffTicks)
                     || (e.LastUpdatedAtTicks == null && e.CreatedAtTicks < cutoffTicks))
            .Select(e => e.Name)
            .ToListAsync(ct);

        foreach (string name in staleNames)
        {
            try
            {
                await DeleteIndexAsync(name, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to cleanup stale index '{IndexName}'", name);
            }
        }

        return staleNames.Count;
    }

    #endregion

    #region 向量序列化

    private static byte[] VectorToBytes(ReadOnlyMemory<float> vector)
    {
        return MemoryMarshal.AsBytes(vector.Span).ToArray();
    }

    #endregion
}

/// <summary>FTS5 查詢結果投影。</summary>
internal class FtsSearchRow
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public string FileName { get; set; } = "";
    public int ChunkIndex { get; set; }
    public double Score { get; set; }
}
