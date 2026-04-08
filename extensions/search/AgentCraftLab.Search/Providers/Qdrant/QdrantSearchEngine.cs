using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Scoring;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AgentCraftLab.Search.Providers.Qdrant;

/// <summary>
/// Qdrant 向量搜尋引擎。
/// 每個 indexName 對應一個 Qdrant Collection。
/// Vector 搜尋為主要模式，FullText 用 payload keyword filter 近似。
/// </summary>
public class QdrantSearchEngine : ISearchEngine
{
    private readonly QdrantClient _client;
    private readonly int _dimensions;
    private readonly SearchEngineOptions _options;

    /// <summary>ContentFilterSearch 的 scroll 上限（Qdrant 無 FTS，需全量掃描）。</summary>
    private const uint ContentFilterScrollLimit = 1000;

    public QdrantSearchEngine(QdrantConfig config, int dimensions = 1536, SearchEngineOptions? options = null)
    {
        _dimensions = dimensions;
        _options = options ?? new SearchEngineOptions();

        var uri = new Uri(config.Url);
        _client = string.IsNullOrEmpty(config.ApiKey)
            ? new QdrantClient(uri.Host, uri.Port, https: uri.Scheme == "https")
            : new QdrantClient(uri.Host, uri.Port, https: uri.Scheme == "https", apiKey: config.ApiKey);
    }

    private static string CollectionName(string indexName) =>
        new(indexName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());

    // ─── ISearchEngine ───

    public async Task EnsureIndexAsync(string indexName, CancellationToken ct = default)
    {
        var name = CollectionName(indexName);

        try
        {
            await _client.GetCollectionInfoAsync(name, ct);
            // Collection 已存在
        }
        catch
        {
            // Collection 不存在，建立
            await _client.CreateCollectionAsync(name, new VectorParams
            {
                Size = (ulong)_dimensions,
                Distance = Distance.Cosine
            }, cancellationToken: ct);

            // 建立 payload index for file_name（加速 filter）
            await _client.CreatePayloadIndexAsync(name, "file_name",
                PayloadSchemaType.Keyword, cancellationToken: ct);
        }
    }

    public async Task DeleteIndexAsync(string indexName, CancellationToken ct = default)
    {
        var name = CollectionName(indexName);
        try
        {
            await _client.DeleteCollectionAsync(name, cancellationToken: ct);
        }
        catch
        {
            // Collection 不存在，忽略
        }
    }

    public async Task<IndexInfo?> GetIndexInfoAsync(string indexName, CancellationToken ct = default)
    {
        var name = CollectionName(indexName);
        try
        {
            var info = await _client.GetCollectionInfoAsync(name, ct);
            return new IndexInfo
            {
                Name = indexName,
                DocumentCount = (long)info.PointsCount,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task IndexDocumentsAsync(string indexName, IEnumerable<SearchDocument> documents, CancellationToken ct = default)
    {
        var name = CollectionName(indexName);
        var points = documents.Select(doc =>
        {
            var point = new PointStruct
            {
                Id = new PointId { Uuid = ToUuid(doc.Id) },
                Vectors = doc.Vector.HasValue ? doc.Vector.Value.ToArray() : Array.Empty<float>(),
            };

            point.Payload.Add("content", doc.Content);
            point.Payload.Add("file_name", doc.FileName);
            point.Payload.Add("chunk_index", doc.ChunkIndex);

            if (doc.Metadata is not null)
            {
                foreach (var kv in doc.Metadata)
                {
                    point.Payload.Add($"meta_{kv.Key}", kv.Value);
                }
            }

            return point;
        }).ToList();

        await _client.UpsertAsync(name, points, cancellationToken: ct);
    }

    public async Task DeleteDocumentsAsync(string indexName, IEnumerable<string> documentIds, CancellationToken ct = default)
    {
        var name = CollectionName(indexName);
        var ids = documentIds.Select(id => new PointId { Uuid = ToUuid(id) }).ToList();
        if (ids.Count == 0) return;

        await _client.DeleteAsync(name, ids, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string indexName, SearchQuery query, CancellationToken ct = default)
    {
        var topK = query.TopK > 0 ? query.TopK : _options.DefaultTopK;

        var results = query.Mode switch
        {
            SearchMode.FullText => await ContentFilterSearchAsync(indexName, query.Text, topK, ct),
            SearchMode.Vector => await VectorSearchAsync(indexName, query.Vector, topK, ct),
            SearchMode.Hybrid => await HybridSearchAsync(indexName, query, topK, ct),
            _ => await HybridSearchAsync(indexName, query, topK, ct)
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

    public async Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(CancellationToken ct = default)
    {
        var collections = await _client.ListCollectionsAsync(ct);
        return collections.Select(c => new IndexInfo
        {
            Name = c,
            DocumentCount = 0,
            CreatedAt = DateTimeOffset.UtcNow
        }).ToList();
    }

    public async Task<int> CleanupStaleIndexesAsync(TimeSpan ttl, CancellationToken ct = default)
    {
        var collections = await _client.ListCollectionsAsync(ct);
        var cleaned = 0;
        foreach (var c in collections)
        {
            var info = await GetIndexInfoAsync(c, ct);
            if (info is not null && info.DocumentCount == 0)
            {
                await DeleteIndexAsync(c, ct);
                cleaned++;
            }
        }
        return cleaned;
    }

    /// <summary>
    /// 測試連線是否正常（供 DataSource test 端點使用）。
    /// </summary>
    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        await _client.ListCollectionsAsync(ct);
    }

    // ─── Private Search Methods ───

    /// <summary>
    /// Vector 搜尋 — Qdrant 核心能力。
    /// </summary>
    private async Task<IReadOnlyList<SearchResult>> VectorSearchAsync(
        string indexName, ReadOnlyMemory<float>? queryVector, int topK, CancellationToken ct)
    {
        if (!queryVector.HasValue) return [];

        var name = CollectionName(indexName);
        var results = await _client.SearchAsync(name,
            queryVector.Value.ToArray(),
            limit: (ulong)topK,
            payloadSelector: true,
            cancellationToken: ct);

        return results.Select(r => new SearchResult
        {
            Id = r.Id.Uuid ?? r.Id.Num.ToString(),
            Score = r.Score,
            Content = r.Payload.TryGetValue("content", out var c) ? c.StringValue : "",
            FileName = r.Payload.TryGetValue("file_name", out var f) ? f.StringValue : "",
            ChunkIndex = r.Payload.TryGetValue("chunk_index", out var ci) ? (int)ci.IntegerValue : 0
        }).ToList();
    }

    /// <summary>
    /// 內容過濾搜尋 — Qdrant 無內建 FTS，用 Scroll + 關鍵字匹配近似。
    /// 對於少量資料可用；大規模應搭配 Hybrid 模式。
    /// </summary>
    private async Task<IReadOnlyList<SearchResult>> ContentFilterSearchAsync(
        string indexName, string queryText, int topK, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(queryText)) return [];

        var name = CollectionName(indexName);
        // Qdrant 不支援全文搜尋，使用 scroll 取所有 points 後本地篩選
        // 對大量資料效能不佳，但 FullText-only 模式在 Qdrant 上本就不是最佳選擇
        var scrollResponse = await _client.ScrollAsync(name,
            limit: ContentFilterScrollLimit,
            payloadSelector: true,
            cancellationToken: ct);

        var keywords = queryText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return scrollResponse.Result.Select(r =>
            {
                var content = r.Payload.TryGetValue("content", out var c) ? c.StringValue : "";
                // 簡易評分：計算命中的關鍵字數量
                var matchCount = keywords.Count(k => content.Contains(k, StringComparison.OrdinalIgnoreCase));
                return new { Point = r, Content = content, MatchCount = matchCount };
            })
            .Where(x => x.MatchCount > 0)
            .OrderByDescending(x => x.MatchCount)
            .Take(topK)
            .Select(x => new SearchResult
            {
                Id = x.Point.Id.Uuid ?? x.Point.Id.Num.ToString(),
                Score = (float)x.MatchCount / keywords.Length,
                Content = x.Content,
                FileName = x.Point.Payload.TryGetValue("file_name", out var f) ? f.StringValue : "",
                ChunkIndex = x.Point.Payload.TryGetValue("chunk_index", out var ci) ? (int)ci.IntegerValue : 0
            })
            .ToList();
    }

    /// <summary>
    /// Hybrid 搜尋 — Vector 為主 + Content Filter 輔助 + RRF 融合。
    /// </summary>
    private async Task<IReadOnlyList<SearchResult>> HybridSearchAsync(
        string indexName, SearchQuery query, int topK, CancellationToken ct)
    {
        var expandedTopK = topK * 3;

        // Vector 搜尋一定要做（Qdrant 強項）
        var vecResults = await VectorSearchAsync(indexName, query.Vector, expandedTopK, ct);

        // 如果沒有文字查詢，直接回傳向量結果
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return vecResults.Take(topK).ToList();
        }

        var ftsResults = await ContentFilterSearchAsync(indexName, query.Text, expandedTopK, ct);

        if (ftsResults.Count == 0) return vecResults.Take(topK).ToList();

        // RRF 融合
        var vecIds = vecResults.Select(r => r.Id).ToList();
        var ftsIds = ftsResults.Select(r => r.Id).ToList();

        var rrfScores = ReciprocalRankFusion.Fuse(
            [vecIds, ftsIds],
            [query.VectorWeight, query.FullTextWeight],
            _options.RrfK,
            topK);

        var lookup = new Dictionary<string, SearchResult>();
        foreach (var r in vecResults) lookup.TryAdd(r.Id, r);
        foreach (var r in ftsResults) lookup.TryAdd(r.Id, r);

        return rrfScores
            .Where(s => lookup.ContainsKey(s.Id))
            .Select(s =>
            {
                var orig = lookup[s.Id];
                return new SearchResult
                {
                    Id = s.Id,
                    Score = s.Score,
                    Content = orig.Content,
                    FileName = orig.FileName,
                    ChunkIndex = orig.ChunkIndex,
                    Metadata = orig.Metadata
                };
            })
            .ToList();
    }

    /// <summary>
    /// 將任意字串 ID 轉為合法 UUID（Qdrant 要求 UUID 或 uint64）。
    /// 使用 MD5 hash 確保確定性映射。
    /// </summary>
    private static string ToUuid(string id)
    {
        // 如果已經是合法 UUID，直接用
        if (Guid.TryParse(id, out var guid))
        {
            return guid.ToString();
        }

        // 否則用 MD5 hash 產生確定性 UUID
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(id));
        return new Guid(bytes).ToString();
    }
}
