using System.Collections.Concurrent;
using System.Text.Json;
using AgentCraftLab.Engine.Data;
using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Providers.PgVector;
using AgentCraftLab.Search.Providers.Qdrant;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 根據 DataSource 設定解析或快取對應的 ISearchEngine 實例。
/// DataSourceId 為 null 時回傳全域預設 SQLite。
/// Phase 5/6 實作 pgvector/qdrant 時，在 CreateEngine 加入對應分支即可。
/// </summary>
public class SearchEngineFactory
{
    private readonly ISearchEngine _defaultEngine;
    private readonly IDataSourceStore _store;
    private readonly ILogger<SearchEngineFactory> _logger;
    private readonly ConcurrentDictionary<string, ISearchEngine> _cache = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public SearchEngineFactory(
        ISearchEngine defaultEngine,
        IDataSourceStore store,
        ILogger<SearchEngineFactory> logger)
    {
        _defaultEngine = defaultEngine;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// 解析 DataSourceId 對應的 ISearchEngine。null = 預設 SQLite。
    /// </summary>
    public async Task<ISearchEngine> ResolveAsync(string? dataSourceId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(dataSourceId))
        {
            return _defaultEngine;
        }

        if (_cache.TryGetValue(dataSourceId, out var cached))
        {
            return cached;
        }

        var ds = await _store.GetAsync(dataSourceId);
        if (ds is null)
        {
            _logger.LogWarning("DataSource {Id} not found, falling back to default", dataSourceId);
            return _defaultEngine;
        }

        var engine = CreateEngine(ds);
        _cache.TryAdd(dataSourceId, engine);
        return engine;
    }

    /// <summary>清除快取（DataSource 更新/刪除時呼叫）。</summary>
    public void Invalidate(string dataSourceId)
    {
        _cache.TryRemove(dataSourceId, out _);
    }

    private ISearchEngine CreateEngine(DataSourceDocument ds)
    {
        return ds.Provider switch
        {
            "pgvector" => CreatePgVector(ds.ConfigJson),
            "qdrant" => CreateQdrant(ds.ConfigJson),
            _ => _defaultEngine  // sqlite 或未知 Provider → 用預設
        };
    }

    private static PgVectorSearchEngine CreatePgVector(string configJson)
    {
        var config = JsonSerializer.Deserialize<PgVectorConfig>(configJson, JsonOpts) ?? new PgVectorConfig();
        return new PgVectorSearchEngine(config);
    }

    private static QdrantSearchEngine CreateQdrant(string configJson)
    {
        var config = JsonSerializer.Deserialize<QdrantConfig>(configJson, JsonOpts) ?? new QdrantConfig();
        return new QdrantSearchEngine(config);
    }
}
