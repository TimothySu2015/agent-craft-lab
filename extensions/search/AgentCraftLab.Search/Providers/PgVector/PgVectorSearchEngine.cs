using System.Text.Json;
using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Scoring;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;

namespace AgentCraftLab.Search.Providers.PgVector;

/// <summary>
/// PostgreSQL + pgvector 搜尋引擎。
/// 每個 indexName 對應一張 table: search_{sanitized_name}。
/// 支援 FullText（tsvector simple）+ Vector（cosine HNSW）+ Hybrid（RRF）。
/// </summary>
public class PgVectorSearchEngine : ISearchEngine
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly int _dimensions;
    private readonly SearchEngineOptions _options;

    public PgVectorSearchEngine(string connectionString, int dimensions = 1536, SearchEngineOptions? options = null)
    {
        _dimensions = dimensions;
        _options = options ?? new SearchEngineOptions();
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        _dataSource = builder.Build();
    }

    public PgVectorSearchEngine(PgVectorConfig config, int dimensions = 1536, SearchEngineOptions? options = null)
        : this(config.ToConnectionString(), dimensions, options) { }

    private static string TableName(string indexName) => $"search_{Sanitize(indexName)}";

    private static string Sanitize(string name) =>
        new(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

    // ─── ISearchEngine ───

    public async Task EnsureIndexAsync(string indexName, CancellationToken ct = default)
    {
        var table = TableName(indexName);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // 確保 pgvector 擴充已啟用
        await using (var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 建立表格
        var createTable = $"""
            CREATE TABLE IF NOT EXISTS {table} (
                id          TEXT PRIMARY KEY,
                content     TEXT NOT NULL DEFAULT '',
                embedding   vector({_dimensions}),
                file_name   TEXT DEFAULT '',
                chunk_index INTEGER DEFAULT 0,
                metadata    JSONB,
                created_at  TIMESTAMPTZ DEFAULT NOW()
            )
            """;
        await using (var cmd = new NpgsqlCommand(createTable, conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 全文搜尋索引（GIN + tsvector simple — 支援 CJK）
        await using (var cmd = new NpgsqlCommand(
            $"CREATE INDEX IF NOT EXISTS idx_{Sanitize(indexName)}_fts ON {table} USING gin(to_tsvector('simple', content))", conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 向量搜尋索引（HNSW cosine）
        await using (var cmd = new NpgsqlCommand(
            $"CREATE INDEX IF NOT EXISTS idx_{Sanitize(indexName)}_vec ON {table} USING hnsw(embedding vector_cosine_ops)", conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 記錄索引 metadata（用 comment）
        await using (var cmd = new NpgsqlCommand(
            $"COMMENT ON TABLE {table} IS 'CraftSearch index: {indexName}, created: {DateTime.UtcNow:O}'", conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task DeleteIndexAsync(string indexName, CancellationToken ct = default)
    {
        var table = TableName(indexName);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand($"DROP TABLE IF EXISTS {table}", conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IndexInfo?> GetIndexInfoAsync(string indexName, CancellationToken ct = default)
    {
        var table = TableName(indexName);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // 檢查表格是否存在
        await using var checkCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @table", conn);
        checkCmd.Parameters.AddWithValue("table", table);
        var exists = (long)(await checkCmd.ExecuteScalarAsync(ct))! > 0;
        if (!exists) return null;

        // 取得文件數量
        await using var countCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {table}", conn);
        var count = (long)(await countCmd.ExecuteScalarAsync(ct))!;

        return new IndexInfo
        {
            Name = indexName,
            DocumentCount = count,
            CreatedAt = DateTimeOffset.UtcNow // pgvector 不直接存 index creation time
        };
    }

    public async Task IndexDocumentsAsync(string indexName, IEnumerable<SearchDocument> documents, CancellationToken ct = default)
    {
        var table = TableName(indexName);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        foreach (var doc in documents)
        {
            var sql = $"""
                INSERT INTO {table} (id, content, embedding, file_name, chunk_index, metadata)
                VALUES (@id, @content, @embedding, @fileName, @chunkIndex, @metadata::jsonb)
                ON CONFLICT (id) DO UPDATE SET
                    content = EXCLUDED.content,
                    embedding = EXCLUDED.embedding,
                    file_name = EXCLUDED.file_name,
                    chunk_index = EXCLUDED.chunk_index,
                    metadata = EXCLUDED.metadata
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", doc.Id);
            cmd.Parameters.AddWithValue("content", doc.Content);
            cmd.Parameters.AddWithValue("embedding",
                doc.Vector.HasValue ? new Vector(doc.Vector.Value.ToArray()) : DBNull.Value);
            cmd.Parameters.AddWithValue("fileName", doc.FileName);
            cmd.Parameters.AddWithValue("chunkIndex", doc.ChunkIndex);
            cmd.Parameters.AddWithValue("metadata",
                doc.Metadata is not null ? JsonSerializer.Serialize(doc.Metadata) : "{}");
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task DeleteDocumentsAsync(string indexName, IEnumerable<string> documentIds, CancellationToken ct = default)
    {
        var ids = documentIds.ToList();
        if (ids.Count == 0) return;

        var table = TableName(indexName);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand($"DELETE FROM {table} WHERE id = ANY(@ids)", conn);
        cmd.Parameters.AddWithValue("ids", ids.ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string indexName, SearchQuery query, CancellationToken ct = default)
    {
        var mode = query.Mode;
        var topK = query.TopK > 0 ? query.TopK : _options.DefaultTopK;

        var results = mode switch
        {
            SearchMode.FullText => await FullTextSearchAsync(indexName, query.Text, topK, ct),
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
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name LIKE 'search_%'", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<IndexInfo>();
        while (await reader.ReadAsync(ct))
        {
            var tableName = reader.GetString(0);
            results.Add(new IndexInfo
            {
                Name = tableName.StartsWith("search_") ? tableName[7..] : tableName,
                DocumentCount = 0,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        return results;
    }

    public async Task<int> CleanupStaleIndexesAsync(TimeSpan ttl, CancellationToken ct = default)
    {
        // pgvector 不支援自動 TTL，需要外部機制
        // 這裡只清理沒有任何文件的空索引
        var indexes = await ListIndexesAsync(ct);
        var cleaned = 0;
        foreach (var idx in indexes)
        {
            var info = await GetIndexInfoAsync(idx.Name, ct);
            if (info is not null && info.DocumentCount == 0)
            {
                await DeleteIndexAsync(idx.Name, ct);
                cleaned++;
            }
        }
        return cleaned;
    }

    // ─── Private Search Methods ───

    private async Task<IReadOnlyList<SearchResult>> FullTextSearchAsync(
        string indexName, string queryText, int topK, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(queryText)) return [];

        var table = TableName(indexName);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var sql = $"""
            SELECT id, content, file_name, chunk_index,
                   ts_rank(to_tsvector('simple', content), plainto_tsquery('simple', @query)) AS score
            FROM {table}
            WHERE to_tsvector('simple', content) @@ plainto_tsquery('simple', @query)
            ORDER BY score DESC
            LIMIT @topK
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("query", queryText);
        cmd.Parameters.AddWithValue("topK", topK);

        return await ReadResults(cmd, ct);
    }

    private async Task<IReadOnlyList<SearchResult>> VectorSearchAsync(
        string indexName, ReadOnlyMemory<float>? queryVector, int topK, CancellationToken ct)
    {
        if (!queryVector.HasValue) return [];

        var table = TableName(indexName);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // cosine distance: <=> 回傳 0~2（0 = 完全相同），轉為 similarity = 1 - distance
        var sql = $"""
            SELECT id, content, file_name, chunk_index,
                   1 - (embedding <=> @query) AS score
            FROM {table}
            WHERE embedding IS NOT NULL
            ORDER BY embedding <=> @query
            LIMIT @topK
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("query", new Vector(queryVector.Value.ToArray()));
        cmd.Parameters.AddWithValue("topK", topK);

        return await ReadResults(cmd, ct);
    }

    private async Task<IReadOnlyList<SearchResult>> HybridSearchAsync(
        string indexName, SearchQuery query, int topK, CancellationToken ct)
    {
        // 分別取兩路結果
        var expandedTopK = topK * 3; // 取更多候選再 RRF 融合
        var ftsTask = FullTextSearchAsync(indexName, query.Text, expandedTopK, ct);
        var vecTask = VectorSearchAsync(indexName, query.Vector, expandedTopK, ct);

        await Task.WhenAll(ftsTask, vecTask);
        var ftsResults = await ftsTask;
        var vecResults = await vecTask;

        if (ftsResults.Count == 0) return vecResults.Take(topK).ToList();
        if (vecResults.Count == 0) return ftsResults.Take(topK).ToList();

        // RRF 融合
        var ftsIds = ftsResults.Select(r => r.Id).ToList();
        var vecIds = vecResults.Select(r => r.Id).ToList();

        var rrfScores = ReciprocalRankFusion.Fuse(
            [ftsIds, vecIds],
            [query.FullTextWeight, query.VectorWeight],
            _options.RrfK,
            topK);

        // 建立 id → result lookup
        var lookup = new Dictionary<string, SearchResult>();
        foreach (var r in ftsResults) lookup.TryAdd(r.Id, r);
        foreach (var r in vecResults) lookup.TryAdd(r.Id, r);

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

    private static async Task<IReadOnlyList<SearchResult>> ReadResults(NpgsqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<SearchResult>();
        while (await reader.ReadAsync(ct))
        {
            var colId = reader.GetOrdinal("id");
            var colContent = reader.GetOrdinal("content");
            var colFileName = reader.GetOrdinal("file_name");
            var colChunkIndex = reader.GetOrdinal("chunk_index");
            var colScore = reader.GetOrdinal("score");

            results.Add(new SearchResult
            {
                Id = reader.GetString(colId),
                Score = reader.GetFloat(colScore),
                Content = reader.GetString(colContent),
                FileName = reader.IsDBNull(colFileName) ? "" : reader.GetString(colFileName),
                ChunkIndex = reader.IsDBNull(colChunkIndex) ? 0 : reader.GetInt32(colChunkIndex)
            });
        }
        return results;
    }

    /// <summary>
    /// 測試連線是否正常（供 DataSource test 端點使用）。
    /// </summary>
    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync(ct);
    }
}
