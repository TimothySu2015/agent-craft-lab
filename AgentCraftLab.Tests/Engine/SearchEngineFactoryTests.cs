using AgentCraftLab.Data;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Search.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCraftLab.Tests.Engine;

public class SearchEngineFactoryTests
{
    // ─── Fakes ───

    private class FakeSearchEngine : ISearchEngine
    {
        public string Label { get; }

        public FakeSearchEngine(string label = "fake") => Label = label;

        public Task EnsureIndexAsync(string indexName, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteIndexAsync(string indexName, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IndexInfo?> GetIndexInfoAsync(string indexName, CancellationToken ct = default) => Task.FromResult<IndexInfo?>(null);
        public Task IndexDocumentsAsync(string indexName, IEnumerable<SearchDocument> documents, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDocumentsAsync(string indexName, IEnumerable<string> documentIds, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<SearchResult>> SearchAsync(string indexName, SearchQuery query, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SearchResult>>([]);
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IndexInfo>>([]);
        public Task<int> CleanupStaleIndexesAsync(TimeSpan ttl, CancellationToken ct = default) => Task.FromResult(0);
    }

    private class FakeProvider : ISearchEngineProvider
    {
        public string ProviderName { get; }
        public FakeSearchEngine LastCreated { get; private set; } = null!;

        public FakeProvider(string name) => ProviderName = name;

        public ISearchEngine Create(string configJson)
        {
            LastCreated = new FakeSearchEngine(ProviderName);
            return LastCreated;
        }
    }

    private class InMemoryDataSourceStore : IDataSourceStore
    {
        private readonly Dictionary<string, DataSourceDocument> _docs = new();

        public void Add(DataSourceDocument doc) => _docs[doc.Id] = doc;

        public Task<DataSourceDocument> SaveAsync(DataSourceDocument doc) { _docs[doc.Id] = doc; return Task.FromResult(doc); }
        public Task<List<DataSourceDocument>> ListAsync(string userId) => Task.FromResult(_docs.Values.ToList());
        public Task<DataSourceDocument?> GetAsync(string id) => Task.FromResult(_docs.GetValueOrDefault(id));
        public Task<DataSourceDocument?> UpdateAsync(string userId, string id, string name, string description, string provider, string configJson) => Task.FromResult<DataSourceDocument?>(null);
        public Task<bool> DeleteAsync(string userId, string id) => Task.FromResult(_docs.Remove(id));
        public Task<int> CountKbReferencesAsync(string id) => Task.FromResult(0);
    }

    // ─── Helpers ───

    private static SearchEngineFactory CreateFactory(
        IDataSourceStore? store = null,
        ISearchEngineProvider[]? providers = null,
        ISearchEngine? overrideEngine = null)
    {
        store ??= new InMemoryDataSourceStore();
        providers ??= [];
        return overrideEngine is not null
            ? new SearchEngineFactory(store, providers, NullLogger<SearchEngineFactory>.Instance, overrideEngine)
            : new SearchEngineFactory(store, providers, NullLogger<SearchEngineFactory>.Instance);
    }

    // ─── ResolveAsync — Registry Routing ───

    [Fact]
    public async Task ResolveAsync_RoutesToCorrectProvider()
    {
        var sqliteProvider = new FakeProvider("sqlite");
        var pgProvider = new FakeProvider("pgvector");
        var store = new InMemoryDataSourceStore();
        store.Add(new DataSourceDocument { Id = "ds-1", Provider = "sqlite", ConfigJson = "{}", Name = "Local", UserId = "u" });
        store.Add(new DataSourceDocument { Id = "ds-2", Provider = "pgvector", ConfigJson = "{}", Name = "PG", UserId = "u" });

        var factory = CreateFactory(store, [sqliteProvider, pgProvider]);

        var engine1 = await factory.ResolveAsync("ds-1");
        Assert.Same(sqliteProvider.LastCreated, engine1);

        var engine2 = await factory.ResolveAsync("ds-2");
        Assert.Same(pgProvider.LastCreated, engine2);
    }

    [Fact]
    public async Task ResolveAsync_CachesEnginePerDataSourceId()
    {
        var provider = new FakeProvider("sqlite");
        var store = new InMemoryDataSourceStore();
        store.Add(new DataSourceDocument { Id = "ds-1", Provider = "sqlite", ConfigJson = "{}", Name = "A", UserId = "u" });

        var factory = CreateFactory(store, [provider]);

        var first = await factory.ResolveAsync("ds-1");
        var second = await factory.ResolveAsync("ds-1");

        Assert.Same(first, second); // 快取命中，同一實例
    }

    [Fact]
    public async Task ResolveAsync_UnsupportedProvider_ThrowsWithSupportedList()
    {
        var store = new InMemoryDataSourceStore();
        store.Add(new DataSourceDocument { Id = "ds-1", Provider = "cosmos", ConfigJson = "{}", Name = "X", UserId = "u" });

        var factory = CreateFactory(store, [new FakeProvider("sqlite"), new FakeProvider("qdrant")]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.ResolveAsync("ds-1"));

        Assert.Contains("cosmos", ex.Message);
        Assert.Contains("sqlite", ex.Message);
        Assert.Contains("qdrant", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_DataSourceNotFound_Throws()
    {
        var factory = CreateFactory(providers: [new FakeProvider("sqlite")]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.ResolveAsync("ds-nonexistent"));

        Assert.Contains("ds-nonexistent", ex.Message);
    }

    // ─── ResolveAsync — Legacy Fallback (null DataSourceId) ───

    [Fact]
    public async Task ResolveAsync_Null_WithOverride_ReturnsOverrideEngine()
    {
        var overrideEngine = new FakeSearchEngine("override");
        var factory = CreateFactory(overrideEngine: overrideEngine);

        var result = await factory.ResolveAsync(null);

        Assert.Same(overrideEngine, result);
    }

    [Fact]
    public async Task ResolveAsync_Null_WithoutOverride_UsesSqliteProvider()
    {
        var sqliteProvider = new FakeProvider("sqlite");
        var factory = CreateFactory(providers: [sqliteProvider]);

        var result = await factory.ResolveAsync(null);

        Assert.NotNull(result);
        Assert.Same(sqliteProvider.LastCreated, result);
    }

    [Fact]
    public async Task ResolveAsync_Null_NoSqliteProvider_Throws()
    {
        var factory = CreateFactory(providers: [new FakeProvider("qdrant")]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.ResolveAsync(null));
    }

    // ─── Invalidate ───

    [Fact]
    public async Task Invalidate_RemovesCachedEngine()
    {
        var provider = new FakeProvider("sqlite");
        var store = new InMemoryDataSourceStore();
        store.Add(new DataSourceDocument { Id = "ds-1", Provider = "sqlite", ConfigJson = "{}", Name = "A", UserId = "u" });

        var factory = CreateFactory(store, [provider]);

        var first = await factory.ResolveAsync("ds-1");
        factory.Invalidate("ds-1");
        var second = await factory.ResolveAsync("ds-1");

        Assert.NotSame(first, second); // 快取已清除，重新建立
    }
}
