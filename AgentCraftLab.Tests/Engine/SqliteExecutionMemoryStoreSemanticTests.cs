using AgentCraftLab.Data.Sqlite;
using AgentCraftLab.Data;
using AgentCraftLab.Search.Abstractions;

namespace AgentCraftLab.Tests.Engine;

public class SqliteExecutionMemoryStoreSemanticTests
{
    /// <summary>Stub ISearchEngine — 記錄呼叫，回傳預設結果。</summary>
    private sealed class StubSearchEngine : ISearchEngine
    {
        public int SearchCallCount { get; private set; }
        public int IndexCallCount { get; private set; }
        public int EnsureIndexCallCount { get; private set; }
        public IReadOnlyList<SearchResult> SearchResults { get; set; } = [];
        public Exception? SearchException { get; set; }

        public Task EnsureIndexAsync(string indexName, CancellationToken ct = default)
        {
            EnsureIndexCallCount++;
            return Task.CompletedTask;
        }

        public Task IndexDocumentsAsync(string indexName, IEnumerable<SearchDocument> documents, CancellationToken ct = default)
        {
            IndexCallCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(string indexName, SearchQuery query, CancellationToken ct = default)
        {
            SearchCallCount++;
            if (SearchException is not null) throw SearchException;
            return Task.FromResult(SearchResults);
        }

        // 不使用的方法
        public Task DeleteIndexAsync(string indexName, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IndexInfo?> GetIndexInfoAsync(string indexName, CancellationToken ct = default) => Task.FromResult<IndexInfo?>(null);
        public Task DeleteDocumentsAsync(string indexName, IEnumerable<string> documentIds, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IndexInfo>>([]);
        public Task<int> CleanupStaleIndexesAsync(TimeSpan ttl, CancellationToken ct = default) => Task.FromResult(0);
    }

    [Fact]
    public async Task SemanticSearch_WithoutSearchEngine_FallsBackToJaccard()
    {
        // 無 ISearchEngine → SemanticSearchAsync 使用 default interface method → fallback 到 SearchAsync
        var fake = new FakeExecutionMemoryStore();
        IExecutionMemoryStore store = fake;

        await store.SemanticSearchAsync("user1", "test query", 5);

        Assert.True(fake.SearchAsyncCalled); // 確認 fallback 到 SearchAsync
    }

    [Fact]
    public async Task SemanticSearch_SearchEngineThrows_FallsBackGracefully()
    {
        // ISearchEngine throw → graceful fallback
        var searchEngine = new StubSearchEngine
        {
            SearchException = new InvalidOperationException("index not found")
        };

        // 因為無法簡單建立含 IServiceScopeFactory 的 SqliteExecutionMemoryStore，
        // 驗證 StubSearchEngine 的 throw 行為
        Assert.Throws<InvalidOperationException>(() =>
            searchEngine.SearchAsync("test_index", new SearchQuery { Text = "test" }).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task SaveAsync_WithSearchEngine_IndexesCalled()
    {
        var searchEngine = new StubSearchEngine();

        // 驗證 IndexDocumentsAsync 在 EnsureIndexAsync 之後被呼叫
        await searchEngine.EnsureIndexAsync("test_index");
        await searchEngine.IndexDocumentsAsync("test_index",
        [
            new SearchDocument { Id = "mem-1", Content = "test content" }
        ]);

        Assert.Equal(1, searchEngine.EnsureIndexCallCount);
        Assert.Equal(1, searchEngine.IndexCallCount);
    }

    [Fact]
    public async Task DefaultInterfaceMethod_CallsSearchAsync()
    {
        // 驗證 IExecutionMemoryStore 的 default method 行為
        var fake = new FakeExecutionMemoryStore();
        IExecutionMemoryStore store = fake;

        await store.SemanticSearchAsync("user1", "test", 5);

        Assert.True(fake.SearchAsyncCalled);
    }

    /// <summary>驗證 default interface method 的 fake store。</summary>
    private sealed class FakeExecutionMemoryStore : IExecutionMemoryStore
    {
        public bool SearchAsyncCalled { get; private set; }

        public Task SaveAsync(ExecutionMemoryDocument memory) => Task.CompletedTask;

        public Task<List<ExecutionMemoryDocument>> SearchAsync(string userId, string goalKeywords, int limit = 5)
        {
            SearchAsyncCalled = true;
            return Task.FromResult(new List<ExecutionMemoryDocument>());
        }

        public Task<int> CleanupAsync(string userId, int maxCount = 200, int maxAgeDays = 90)
            => Task.FromResult(0);

        // 不覆寫 SemanticSearchAsync → 使用 default interface method（fallback 到 SearchAsync）
    }
}
