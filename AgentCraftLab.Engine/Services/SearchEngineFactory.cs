using System.Collections.Concurrent;
using AgentCraftLab.Data;
using AgentCraftLab.Search.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 根據 DataSource 設定解析或快取對應的 ISearchEngine 實例。
/// 透過 ISearchEngineProvider 集合解耦具體 Provider — 新增 Provider 只需註冊 ISearchEngineProvider，不需改此類。
/// DataSourceId 為 null 時，為向下相容既有 KB，自動建立指向預設路徑的 SQLite 引擎。
/// </summary>
public class SearchEngineFactory : ISearchEngineFactory
{
    private readonly IDataSourceStore _store;
    private readonly ILogger<SearchEngineFactory> _logger;
    private readonly Dictionary<string, ISearchEngineProvider> _providers;
    private readonly ISearchEngine? _overrideEngine;
    private readonly ConcurrentDictionary<string, ISearchEngine> _cache = new();

    /// <summary>Legacy fallback 用的快取 key。</summary>
    private const string LegacyFallbackKey = "__legacy_sqlite__";

    public SearchEngineFactory(
        IDataSourceStore store,
        IEnumerable<ISearchEngineProvider> providers,
        ILogger<SearchEngineFactory> logger)
    {
        _store = store;
        _logger = logger;
        _providers = providers.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 測試用建構子 — 傳入 overrideEngine 作為 legacy fallback 的替代。
    /// </summary>
    internal SearchEngineFactory(
        IDataSourceStore store,
        IEnumerable<ISearchEngineProvider> providers,
        ILogger<SearchEngineFactory> logger,
        ISearchEngine overrideEngine)
        : this(store, providers, logger)
    {
        _overrideEngine = overrideEngine;
    }

    /// <inheritdoc />
    public async Task<ISearchEngine> ResolveAsync(string? dataSourceId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(dataSourceId))
        {
            return GetOrCreateLegacyEngine();
        }

        if (_cache.TryGetValue(dataSourceId, out var cached))
        {
            return cached;
        }

        var ds = await _store.GetAsync(dataSourceId);
        if (ds is null)
        {
            throw new InvalidOperationException(
                $"DataSource '{dataSourceId}' not found. Please check your Data Source settings.");
        }

        var engine = CreateEngine(ds);
        _cache.TryAdd(dataSourceId, engine);
        return engine;
    }

    /// <inheritdoc />
    public void Invalidate(string dataSourceId)
    {
        _cache.TryRemove(dataSourceId, out _);
    }

    private ISearchEngine CreateEngine(DataSourceDocument ds)
    {
        if (!_providers.TryGetValue(ds.Provider, out var provider))
        {
            var supported = string.Join(", ", _providers.Keys.Order());
            throw new InvalidOperationException(
                $"Unsupported search provider: '{ds.Provider}'. Supported: {supported}.");
        }

        return provider.Create(ds.ConfigJson);
    }

    /// <summary>
    /// 向下相容：既有 KB 的 DataSourceId 為 null 時，使用 sqlite provider 建立預設引擎。
    /// </summary>
    private ISearchEngine GetOrCreateLegacyEngine()
    {
        if (_overrideEngine is not null)
        {
            return _overrideEngine;
        }

        return _cache.GetOrAdd(LegacyFallbackKey, _ =>
        {
            _logger.LogWarning(
                "Using legacy SQLite search engine (DataSourceId is null). " +
                "Please create a DataSource in Settings and bind your Knowledge Bases to it.");

            if (!_providers.TryGetValue("sqlite", out var sqliteProvider))
            {
                throw new InvalidOperationException(
                    "No SQLite search engine provider registered. Cannot create legacy fallback engine.");
            }

            return sqliteProvider.Create("{}");
        });
    }
}
