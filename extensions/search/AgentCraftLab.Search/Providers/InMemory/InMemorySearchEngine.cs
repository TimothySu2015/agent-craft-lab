using System.Collections.Concurrent;
using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Scoring;

namespace AgentCraftLab.Search.Providers.InMemory;

/// <summary>
/// 記憶體搜尋引擎實作 — 用於測試和向下相容。重啟後資料消失。
/// </summary>
public class InMemorySearchEngine : ISearchEngine
{
    private readonly ConcurrentDictionary<string, InMemoryIndex> _indexes = new();
    private readonly SearchEngineOptions _options;

    public InMemorySearchEngine(SearchEngineOptions options)
    {
        _options = options;
    }

    public Task EnsureIndexAsync(string indexName, CancellationToken ct = default)
    {
        _indexes.GetOrAdd(indexName, _ => new InMemoryIndex(indexName));
        return Task.CompletedTask;
    }

    public Task DeleteIndexAsync(string indexName, CancellationToken ct = default)
    {
        _indexes.TryRemove(indexName, out _);
        return Task.CompletedTask;
    }

    public Task<IndexInfo?> GetIndexInfoAsync(string indexName, CancellationToken ct = default)
    {
        if (!_indexes.TryGetValue(indexName, out var index))
        {
            return Task.FromResult<IndexInfo?>(null);
        }

        return Task.FromResult<IndexInfo?>(new IndexInfo
        {
            Name = indexName,
            DocumentCount = index.Documents.Count,
            CreatedAt = index.CreatedAt,
            LastUpdatedAt = index.LastUpdatedAt
        });
    }

    public Task IndexDocumentsAsync(string indexName, IEnumerable<SearchDocument> documents, CancellationToken ct = default)
    {
        var index = _indexes.GetOrAdd(indexName, _ => new InMemoryIndex(indexName));

        foreach (var doc in documents)
        {
            index.Documents[doc.Id] = doc;
        }

        index.LastUpdatedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task DeleteDocumentsAsync(string indexName, IEnumerable<string> documentIds, CancellationToken ct = default)
    {
        if (!_indexes.TryGetValue(indexName, out var index))
        {
            return Task.CompletedTask;
        }

        foreach (string id in documentIds)
        {
            index.Documents.TryRemove(id, out _);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(CancellationToken ct = default)
    {
        var list = _indexes.Values.Select(idx => new IndexInfo
        {
            Name = idx.Name,
            DocumentCount = idx.Documents.Count,
            CreatedAt = idx.CreatedAt,
            LastUpdatedAt = idx.LastUpdatedAt
        }).ToList();

        return Task.FromResult<IReadOnlyList<IndexInfo>>(list);
    }

    public Task<int> CleanupStaleIndexesAsync(TimeSpan ttl, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - ttl;
        var staleKeys = _indexes
            .Where(kv =>
            {
                var lastActivity = kv.Value.LastUpdatedAt ?? kv.Value.CreatedAt;
                return lastActivity < cutoff;
            })
            .Select(kv => kv.Key)
            .ToList();

        foreach (string key in staleKeys)
        {
            _indexes.TryRemove(key, out _);
        }

        return Task.FromResult(staleKeys.Count);
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string indexName, SearchQuery query, CancellationToken ct = default)
    {
        if (!_indexes.TryGetValue(indexName, out var index) || index.Documents.IsEmpty)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }

        var docs = index.Documents.Values.ToList();
        IReadOnlyList<SearchResult> results = query.Mode switch
        {
            SearchMode.Vector => VectorSearch(docs, query),
            SearchMode.FullText => FullTextSearch(docs, query),
            SearchMode.Hybrid => HybridSearch(docs, query),
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

        return Task.FromResult(results);
    }

    private static IReadOnlyList<SearchResult> VectorSearch(List<SearchDocument> docs, SearchQuery query)
    {
        if (query.Vector is null)
        {
            return [];
        }

        var vectors = docs
            .Where(d => d.Vector is { Length: > 0 })
            .ToList();

        if (vectors.Count == 0)
        {
            return [];
        }

        var candidateVectors = vectors.Select(d => d.Vector!.Value).ToList();
        var topK = CosineSimilarity.SearchTopK(query.Vector.Value, candidateVectors, query.TopK);

        return topK.Select(r => ToSearchResult(vectors[r.Index], r.Score)).ToList();
    }

    private static IReadOnlyList<SearchResult> FullTextSearch(List<SearchDocument> docs, SearchQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return [];
        }

        var keywords = query.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var scored = docs
            .Select(d =>
            {
                int matchCount = keywords.Count(k =>
                    d.Content.Contains(k, StringComparison.OrdinalIgnoreCase));
                return (Doc: d, Score: (float)matchCount / keywords.Length);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(query.TopK)
            .ToList();

        return scored.Select(x => ToSearchResult(x.Doc, x.Score)).ToList();
    }

    private IReadOnlyList<SearchResult> HybridSearch(List<SearchDocument> docs, SearchQuery query)
    {
        // 分別取得兩路排名
        var vectorResults = VectorSearch(docs, query);
        var textResults = FullTextSearch(docs, query);

        if (vectorResults.Count == 0)
        {
            return textResults;
        }

        if (textResults.Count == 0)
        {
            return vectorResults;
        }

        // RRF 融合
        var rankedLists = new List<IReadOnlyList<string>>
        {
            vectorResults.Select(r => r.Id).ToList(),
            textResults.Select(r => r.Id).ToList()
        };

        var weights = new List<float> { query.VectorWeight, query.FullTextWeight };
        var fused = ReciprocalRankFusion.Fuse(rankedLists, weights, _options.RrfK, query.TopK);

        // 建立 ID → 原始內容的 lookup
        var docLookup = docs.ToDictionary(d => d.Id);

        return fused
            .Where(f => docLookup.ContainsKey(f.Id))
            .Select(f => ToSearchResult(docLookup[f.Id], f.Score))
            .ToList();
    }

    private static SearchResult ToSearchResult(SearchDocument doc, float score) => new()
    {
        Id = doc.Id,
        Score = score,
        Content = doc.Content,
        FileName = doc.FileName,
        ChunkIndex = doc.ChunkIndex
    };

    private sealed class InMemoryIndex(string name)
    {
        public string Name { get; } = name;
        public ConcurrentDictionary<string, SearchDocument> Documents { get; } = new();
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastUpdatedAt { get; set; }
    }
}
