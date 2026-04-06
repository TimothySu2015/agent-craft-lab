namespace AgentCraftLab.Search.Abstractions;

/// <summary>搜尋模式。</summary>
public enum SearchMode
{
    /// <summary>BM25 全文搜尋。</summary>
    FullText,

    /// <summary>向量相似度搜尋。</summary>
    Vector,

    /// <summary>全文 + 向量 RRF 混合搜尋（預設，品質最佳）。</summary>
    Hybrid
}

/// <summary>搜尋查詢。</summary>
public class SearchQuery
{
    /// <summary>全文搜尋關鍵字（FullText / Hybrid 模式必填）。</summary>
    public string Text { get; set; } = "";

    /// <summary>查詢向量（Vector / Hybrid 模式必填）。</summary>
    public ReadOnlyMemory<float>? Vector { get; set; }

    /// <summary>搜尋模式。</summary>
    public SearchMode Mode { get; set; } = SearchMode.Hybrid;

    /// <summary>回傳結果數量上限。</summary>
    public int TopK { get; set; } = 5;

    /// <summary>全文搜尋在 RRF 中的權重（預設 1.0）。</summary>
    public float FullTextWeight { get; set; } = 1.0f;

    /// <summary>向量搜尋在 RRF 中的權重（預設 1.0）。</summary>
    public float VectorWeight { get; set; } = 1.0f;

    /// <summary>最低分數門檻，低於此分數的結果會被過濾（null 表示不過濾）。</summary>
    public float? MinScore { get; set; }

    /// <summary>檔案名稱過濾（只回傳包含此子字串的檔案，null 表示不過濾）。</summary>
    public string? FileNameFilter { get; set; }
}

/// <summary>搜尋結果。</summary>
public class SearchResult
{
    /// <summary>文件 ID。</summary>
    public required string Id { get; init; }

    /// <summary>綜合分數（RRF 或單路分數）。</summary>
    public required float Score { get; init; }

    /// <summary>文件文字內容。</summary>
    public required string Content { get; init; }

    /// <summary>來源檔名。</summary>
    public string FileName { get; init; } = "";

    /// <summary>分塊索引（在原文中的位置）。</summary>
    public int ChunkIndex { get; init; }

    /// <summary>額外元資料。</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>索引中的文件（寫入用）。</summary>
public class SearchDocument
{
    /// <summary>唯一識別碼。</summary>
    public required string Id { get; init; }

    /// <summary>文字內容（用於全文索引）。</summary>
    public required string Content { get; init; }

    /// <summary>向量（用於向量索引）。</summary>
    public ReadOnlyMemory<float>? Vector { get; init; }

    /// <summary>來源檔名。</summary>
    public string FileName { get; init; } = "";

    /// <summary>分塊索引。</summary>
    public int ChunkIndex { get; init; }

    /// <summary>額外元資料。</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>索引資訊。</summary>
public class IndexInfo
{
    /// <summary>索引名稱。</summary>
    public required string Name { get; init; }

    /// <summary>文件數量。</summary>
    public required long DocumentCount { get; init; }

    /// <summary>建立時間。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>最後更新時間。</summary>
    public DateTimeOffset? LastUpdatedAt { get; init; }
}

/// <summary>搜尋引擎全域設定。</summary>
public class SearchEngineOptions
{
    /// <summary>RRF 平滑常數（預設 60，與 Azure AI Search 相同）。</summary>
    public int RrfK { get; set; } = 60;

    /// <summary>預設 TopK。</summary>
    public int DefaultTopK { get; set; } = 5;

    /// <summary>預設搜尋模式。</summary>
    public SearchMode DefaultSearchMode { get; set; } = SearchMode.Hybrid;

    /// <summary>索引自動清理 TTL（預設 24 小時）。設為 null 停用自動清理。</summary>
    public TimeSpan? IndexTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>索引數量上限（超過時觸發清理）。設為 0 不限。</summary>
    public int MaxIndexCount { get; set; } = 200;

    /// <summary>RAG 搜尋預設最低分數門檻（RRF 分數通常很小，此值用於過濾完全不相關的結果）。</summary>
    public const float DefaultRagMinScore = 0.005f;
}
