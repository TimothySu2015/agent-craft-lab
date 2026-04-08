namespace AgentCraftLab.Search.Abstractions;

/// <summary>
/// 搜尋引擎核心介面 — 參考 Azure AI Search 設計，支援全文、向量、混合搜尋。
/// </summary>
public interface ISearchEngine
{
    /// <summary>確保索引存在（冪等操作）。</summary>
    Task EnsureIndexAsync(string indexName, CancellationToken ct = default);

    /// <summary>刪除索引及其所有文件。</summary>
    Task DeleteIndexAsync(string indexName, CancellationToken ct = default);

    /// <summary>取得索引資訊；索引不存在時回傳 null。</summary>
    Task<IndexInfo?> GetIndexInfoAsync(string indexName, CancellationToken ct = default);

    /// <summary>批次寫入或更新文件（upsert 語意）。</summary>
    Task IndexDocumentsAsync(string indexName, IEnumerable<SearchDocument> documents, CancellationToken ct = default);

    /// <summary>批次刪除文件。</summary>
    Task DeleteDocumentsAsync(string indexName, IEnumerable<string> documentIds, CancellationToken ct = default);

    /// <summary>執行搜尋（支援 FullText / Vector / Hybrid 三種模式）。</summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string indexName, SearchQuery query, CancellationToken ct = default);

    /// <summary>列出所有索引。</summary>
    Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(CancellationToken ct = default);

    /// <summary>清理過期索引（超過 TTL 且未更新的索引）。</summary>
    Task<int> CleanupStaleIndexesAsync(TimeSpan ttl, CancellationToken ct = default);
}
