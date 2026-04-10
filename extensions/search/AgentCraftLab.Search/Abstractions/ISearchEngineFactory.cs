namespace AgentCraftLab.Search.Abstractions;

/// <summary>
/// 搜尋引擎工廠介面 — 根據 DataSourceId 解析對應的 ISearchEngine 實例。
/// </summary>
public interface ISearchEngineFactory
{
    /// <summary>
    /// 解析 DataSourceId 對應的 ISearchEngine。
    /// null = 向下相容 legacy fallback（既有 KB 用，新建 KB 應明確指定 DataSourceId）。
    /// </summary>
    Task<ISearchEngine> ResolveAsync(string? dataSourceId, CancellationToken ct = default);

    /// <summary>清除指定 DataSource 的快取引擎（DataSource 更新/刪除時呼叫）。</summary>
    void Invalidate(string dataSourceId);
}

/// <summary>
/// 搜尋引擎 Provider — 負責根據 Provider 名稱和 ConfigJson 建立 ISearchEngine 實例。
/// 各搜尋引擎實作（SQLite / PgVector / Qdrant 等）透過 DI 註冊此介面，
/// SearchEngineFactory 依賴此集合解耦具體 Provider。
/// </summary>
public interface ISearchEngineProvider
{
    /// <summary>此 Provider 支援的名稱（例如 "sqlite"、"pgvector"、"qdrant"）。</summary>
    string ProviderName { get; }

    /// <summary>根據 ConfigJson 建立搜尋引擎實例。</summary>
    ISearchEngine Create(string configJson);
}
