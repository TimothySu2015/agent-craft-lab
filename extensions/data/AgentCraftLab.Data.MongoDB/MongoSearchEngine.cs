using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Scoring;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

/// <summary>
/// MongoDB Atlas 搜尋引擎 — Atlas Vector Search + Atlas Search（全文）+ RRF 混合排序。
/// 需要 MongoDB Atlas 部署（自架 MongoDB 不支援 $vectorSearch / $search）。
/// </summary>
public class MongoSearchEngine : ISearchEngine
{
    private readonly IMongoDatabase _database;
    private readonly SearchEngineOptions _options;
    private readonly ILogger<MongoSearchEngine> _logger;

    private const string CollectionName = "search_chunks";
    private const string MetaCollectionName = "search_meta";
    private const string VectorIndexName = "vector_index";
    private const string SearchIndexName = "search_index";

    private bool _atlasSearchAvailable = true;
    private bool _atlasVectorSearchAvailable = true;

    public MongoSearchEngine(
        MongoDbContext dbContext,
        SearchEngineOptions options,
        ILogger<MongoSearchEngine> logger)
    {
        _database = dbContext.Database;
        _options = options;
        _logger = logger;
    }

    private IMongoCollection<ChunkDoc> Chunks => _database.GetCollection<ChunkDoc>(CollectionName);
    private IMongoCollection<MetaDoc> Metas => _database.GetCollection<MetaDoc>(MetaCollectionName);

    #region ISearchEngine 實作

    public async Task EnsureIndexAsync(string indexName, CancellationToken ct = default)
    {
        var existing = await Metas.Find(m => m.IndexName == indexName).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return;
        }

        var meta = new MetaDoc
        {
            Id = $"meta_{indexName}",
            IndexName = indexName,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await Metas.InsertOneAsync(meta, cancellationToken: ct);

        // 確保 chunks collection 的一般索引（indexName + 複合索引）
        await Chunks.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<ChunkDoc>(
                Builders<ChunkDoc>.IndexKeys.Ascending(c => c.IndexName)),
            new CreateIndexModel<ChunkDoc>(
                Builders<ChunkDoc>.IndexKeys
                    .Ascending(c => c.IndexName)
                    .Ascending(c => c.FileName))
        ], ct);
    }

    public async Task DeleteIndexAsync(string indexName, CancellationToken ct = default)
    {
        await Chunks.DeleteManyAsync(c => c.IndexName == indexName, ct);
        await Metas.DeleteOneAsync(m => m.IndexName == indexName, ct);
    }

    public async Task<IndexInfo?> GetIndexInfoAsync(string indexName, CancellationToken ct = default)
    {
        var meta = await Metas.Find(m => m.IndexName == indexName).FirstOrDefaultAsync(ct);
        if (meta is null)
        {
            return null;
        }

        var count = await Chunks.CountDocumentsAsync(c => c.IndexName == indexName, cancellationToken: ct);

        return new IndexInfo
        {
            Name = indexName,
            DocumentCount = count,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(meta.CreatedAt),
            LastUpdatedAt = meta.LastUpdatedAt is not null
                ? DateTimeOffset.FromUnixTimeMilliseconds(meta.LastUpdatedAt.Value) : null
        };
    }

    public async Task IndexDocumentsAsync(
        string indexName, IEnumerable<SearchDocument> documents, CancellationToken ct = default)
    {
        var docs = documents.Select(d => new ChunkDoc
        {
            Id = d.Id,
            IndexName = indexName,
            Content = d.Content,
            FileName = d.FileName,
            ChunkIndex = d.ChunkIndex,
            Vector = d.Vector is { Length: > 0 } ? d.Vector.Value.ToArray() : null,
            Metadata = d.Metadata
        }).ToList();

        if (docs.Count == 0)
        {
            return;
        }

        var bulkOps = docs.Select(d =>
            new ReplaceOneModel<ChunkDoc>(
                Builders<ChunkDoc>.Filter.Eq(c => c.Id, d.Id), d)
            { IsUpsert = true });

        await Chunks.BulkWriteAsync(bulkOps, cancellationToken: ct);

        // 更新 meta
        var count = await Chunks.CountDocumentsAsync(c => c.IndexName == indexName, cancellationToken: ct);
        await Metas.UpdateOneAsync(
            m => m.IndexName == indexName,
            Builders<MetaDoc>.Update
                .Set(m => m.DocumentCount, count)
                .Set(m => m.LastUpdatedAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            cancellationToken: ct);
    }

    public async Task DeleteDocumentsAsync(
        string indexName, IEnumerable<string> documentIds, CancellationToken ct = default)
    {
        var idList = documentIds.ToList();
        if (idList.Count == 0)
        {
            return;
        }

        await Chunks.DeleteManyAsync(
            Builders<ChunkDoc>.Filter.In(c => c.Id, idList) &
            Builders<ChunkDoc>.Filter.Eq(c => c.IndexName, indexName), ct);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string indexName, SearchQuery query, CancellationToken ct = default)
    {
        return query.Mode switch
        {
            SearchMode.Vector => await VectorSearchAsync(indexName, query, ct),
            SearchMode.FullText => await FullTextSearchAsync(indexName, query, ct),
            SearchMode.Hybrid => await HybridSearchAsync(indexName, query, ct),
            _ => []
        };
    }

    public async Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(CancellationToken ct = default)
    {
        var metas = await Metas.Find(_ => true).ToListAsync(ct);

        return metas.Select(m => new IndexInfo
        {
            Name = m.IndexName,
            DocumentCount = m.DocumentCount,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(m.CreatedAt),
            LastUpdatedAt = m.LastUpdatedAt is not null
                ? DateTimeOffset.FromUnixTimeMilliseconds(m.LastUpdatedAt.Value) : null
        }).ToList();
    }

    public async Task<int> CleanupStaleIndexesAsync(TimeSpan ttl, CancellationToken ct = default)
    {
        var cutoff = (DateTimeOffset.UtcNow - ttl).ToUnixTimeMilliseconds();

        var filter = Builders<MetaDoc>.Filter.Or(
            Builders<MetaDoc>.Filter.Lt(m => m.LastUpdatedAt, cutoff),
            Builders<MetaDoc>.Filter.And(
                Builders<MetaDoc>.Filter.Eq(m => m.LastUpdatedAt, null),
                Builders<MetaDoc>.Filter.Lt(m => m.CreatedAt, cutoff)));

        var staleMetas = await Metas.Find(filter).ToListAsync(ct);

        foreach (var meta in staleMetas)
        {
            try
            {
                await DeleteIndexAsync(meta.IndexName, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "清理過期索引 '{IndexName}' 失敗", meta.IndexName);
            }
        }

        return staleMetas.Count;
    }

    #endregion

    #region 向量搜尋（Atlas Vector Search）

    private async Task<IReadOnlyList<SearchResult>> VectorSearchAsync(
        string indexName, SearchQuery query, CancellationToken ct)
    {
        if (query.Vector is not { Length: > 0 } || !_atlasVectorSearchAvailable)
        {
            return [];
        }

        var vectorArray = query.Vector.Value.ToArray();

        var vectorSearchStage = new BsonDocument("$vectorSearch", new BsonDocument
        {
            { "index", VectorIndexName },
            { "path", "vector" },
            { "queryVector", new BsonArray(vectorArray.Select(v => (double)v)) },
            { "numCandidates", Math.Max(query.TopK * 10, 100) },
            { "limit", query.TopK },
            { "filter", new BsonDocument("indexName", indexName) }
        });

        var pipeline = new[] { vectorSearchStage, BuildProjectStage("vectorSearchScore") };

        try
        {
            return await RunAggregationAsync(pipeline, ct);
        }
        catch (MongoCommandException ex) when (ex.Message.Contains("vectorSearch"))
        {
            _atlasVectorSearchAvailable = false;
            _logger.LogWarning("Atlas Vector Search 不可用（需要 Atlas 部署 + vector_index）：{Message}", ex.Message);
            return [];
        }
    }

    #endregion

    #region 全文搜尋（Atlas Search 或 regex fallback）

    private async Task<IReadOnlyList<SearchResult>> FullTextSearchAsync(
        string indexName, SearchQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return [];
        }

        if (!_atlasSearchAvailable)
        {
            return await RegexFullTextSearchAsync(indexName, query, ct);
        }

        try
        {
            return await AtlasFullTextSearchAsync(indexName, query, ct);
        }
        catch (MongoCommandException)
        {
            _atlasSearchAvailable = false;
            _logger.LogWarning("Atlas Search 不可用，已降級為 regex 全文搜尋");
            return await RegexFullTextSearchAsync(indexName, query, ct);
        }
    }

    private async Task<IReadOnlyList<SearchResult>> AtlasFullTextSearchAsync(
        string indexName, SearchQuery query, CancellationToken ct)
    {
        var searchStage = new BsonDocument("$search", new BsonDocument
        {
            { "index", SearchIndexName },
            { "compound", new BsonDocument
                {
                    { "must", new BsonArray
                        {
                            new BsonDocument("text", new BsonDocument
                            {
                                { "query", query.Text.Trim() },
                                { "path", "content" }
                            })
                        }
                    },
                    { "filter", new BsonArray
                        {
                            new BsonDocument("equals", new BsonDocument
                            {
                                { "path", "indexName" },
                                { "value", indexName }
                            })
                        }
                    }
                }
            }
        });

        var limitStage = new BsonDocument("$limit", query.TopK);
        var pipeline = new[] { searchStage, BuildProjectStage("searchScore"), limitStage };

        return await RunAggregationAsync(pipeline, ct);
    }

    /// <summary>Regex fallback — 自架 MongoDB 沒有 Atlas Search 時使用。</summary>
    private async Task<IReadOnlyList<SearchResult>> RegexFullTextSearchAsync(
        string indexName, SearchQuery query, CancellationToken ct)
    {
        var filter = Builders<ChunkDoc>.Filter.Eq(c => c.IndexName, indexName) &
                     Builders<ChunkDoc>.Filter.Regex(c => c.Content,
                         new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(query.Text.Trim()), "i"));

        var docs = await Chunks.Find(filter)
            .Limit(query.TopK)
            .ToListAsync(ct);

        return docs.Select(d => new SearchResult
        {
            Id = d.Id,
            Score = 1.0f,
            Content = d.Content,
            FileName = d.FileName,
            ChunkIndex = d.ChunkIndex,
            Metadata = d.Metadata
        }).ToList();
    }

    #endregion

    #region 混合搜尋

    private async Task<IReadOnlyList<SearchResult>> HybridSearchAsync(
        string indexName, SearchQuery query, CancellationToken ct)
    {
        var expandedTopK = Math.Max(query.TopK * 3, 20);
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

        if (vectorResults.Count == 0)
        {
            return fullTextResults.Take(query.TopK).ToList();
        }

        if (fullTextResults.Count == 0)
        {
            return vectorResults.Take(query.TopK).ToList();
        }

        var rankedLists = new List<IReadOnlyList<string>>
        {
            vectorResults.Select(r => r.Id).ToList(),
            fullTextResults.Select(r => r.Id).ToList()
        };

        var weights = new List<float> { query.VectorWeight, query.FullTextWeight };
        var fused = ReciprocalRankFusion.Fuse(rankedLists, weights, _options.RrfK, query.TopK);

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
                    ChunkIndex = original.ChunkIndex,
                    Metadata = original.Metadata
                };
            })
            .ToList();
    }

    #endregion

    #region 輔助方法

    private static BsonDocument BuildProjectStage(string scoreMetaName)
    {
        return new BsonDocument("$project", new BsonDocument
        {
            { "_id", 1 },
            { "content", 1 },
            { "fileName", 1 },
            { "chunkIndex", 1 },
            { "metadata", 1 },
            { "score", new BsonDocument("$meta", scoreMetaName) }
        });
    }

    private async Task<IReadOnlyList<SearchResult>> RunAggregationAsync(
        BsonDocument[] pipeline, CancellationToken ct)
    {
        var collection = _database.GetCollection<BsonDocument>(CollectionName);
        var cursor = await collection.AggregateAsync<BsonDocument>(
            PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline),
            cancellationToken: ct);

        var results = new List<SearchResult>();
        await cursor.ForEachAsync(doc => results.Add(MapBsonToSearchResult(doc)), ct);
        return results;
    }

    private static SearchResult MapBsonToSearchResult(BsonDocument doc)
    {
        return new SearchResult
        {
            Id = doc["_id"].AsString,
            Score = (float)doc.GetValue("score", 0.0).AsDouble,
            Content = doc.GetValue("content", "").AsString,
            FileName = doc.GetValue("fileName", "").AsString,
            ChunkIndex = doc.GetValue("chunkIndex", 0).AsInt32,
            Metadata = doc.Contains("metadata") && doc["metadata"].IsBsonDocument
                ? doc["metadata"].AsBsonDocument.ToDictionary(
                    e => e.Name, e => e.Value.AsString)
                : null
        };
    }

    #endregion

    #region 內部文件模型

    internal class ChunkDoc
    {
        [BsonId]
        public string Id { get; set; } = "";
        [BsonElement("indexName")]
        public string IndexName { get; set; } = "";
        [BsonElement("content")]
        public string Content { get; set; } = "";
        [BsonElement("fileName")]
        public string FileName { get; set; } = "";
        [BsonElement("chunkIndex")]
        public int ChunkIndex { get; set; }
        [BsonElement("vector")]
        public float[]? Vector { get; set; }
        [BsonElement("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }
    }

    internal class MetaDoc
    {
        [BsonId]
        public string Id { get; set; } = "";
        [BsonElement("indexName")]
        public string IndexName { get; set; } = "";
        [BsonElement("documentCount")]
        public long DocumentCount { get; set; }
        [BsonElement("createdAt")]
        public long CreatedAt { get; set; }
        [BsonElement("lastUpdatedAt")]
        public long? LastUpdatedAt { get; set; }
    }

    #endregion
}
