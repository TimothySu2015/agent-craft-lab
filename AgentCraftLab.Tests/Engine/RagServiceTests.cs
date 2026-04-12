using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Extraction;
using AgentCraftLab.Search.Reranking;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCraftLab.Tests.Engine;

public class RagServiceTests
{
    // ─── Fakes ───

    private class FakeDocumentExtractor : IDocumentExtractor
    {
        private readonly ExtractionResult? _result;

        public FakeDocumentExtractor(ExtractionResult? result) => _result = result;

        public bool CanExtract(string mimeType) => true;

        public Task<ExtractionResult> ExtractAsync(byte[] data, string fileName, CancellationToken ct = default)
            => Task.FromResult(_result ?? new ExtractionResult { Text = "", FileName = fileName });
    }

    private class NullDocumentExtractor : IDocumentExtractor
    {
        public bool CanExtract(string mimeType) => false;
        public Task<ExtractionResult> ExtractAsync(byte[] data, string fileName, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private class FakeChunker : ITextChunker
    {
        public IReadOnlyList<ChunkResult> Chunk(string text, int chunkSize, int overlap)
        {
            // 簡單分割：每 chunkSize 字元一段
            var chunks = new List<ChunkResult>();
            for (int i = 0; i < text.Length; i += chunkSize)
            {
                var t = text.Substring(i, Math.Min(chunkSize, text.Length - i));
                chunks.Add(new ChunkResult { Text = t, Index = chunks.Count, StartPosition = i });
            }
            return chunks.Count > 0 ? chunks : [new ChunkResult { Text = text, Index = 0, StartPosition = 0 }];
        }
    }

    private class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public int CallCount { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var list = values.Select(_ => new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f })).ToList();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? key = null) where TService : class => null;

        public void Dispose() { }
    }

    private class TrackingSearchEngine : ISearchEngine
    {
        public List<string> EnsuredIndexes { get; } = [];
        public List<(string Index, int DocCount)> IndexedBatches { get; } = [];
        public List<SearchResult> SearchResults { get; set; } = [];

        public Task EnsureIndexAsync(string indexName, CancellationToken ct = default)
        {
            EnsuredIndexes.Add(indexName);
            return Task.CompletedTask;
        }

        public Task IndexDocumentsAsync(string indexName, IEnumerable<SearchDocument> documents, CancellationToken ct = default)
        {
            var docs = documents.ToList();
            IndexedBatches.Add((indexName, docs.Count));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(string indexName, SearchQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SearchResult>>(SearchResults);

        public Task DeleteIndexAsync(string indexName, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IndexInfo?> GetIndexInfoAsync(string indexName, CancellationToken ct = default) => Task.FromResult<IndexInfo?>(null);
        public Task DeleteDocumentsAsync(string indexName, IEnumerable<string> documentIds, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IndexInfo>>([]);
        public Task<int> CleanupStaleIndexesAsync(TimeSpan ttl, CancellationToken ct = default) => Task.FromResult(0);
    }

    // ─── Helpers ───

    private static SearchEngineFactory CreateFactory(TrackingSearchEngine? engine = null)
    {
        var e = engine ?? new TrackingSearchEngine();
        return new SearchEngineFactory(new NullDataSourceStore(), [], NullLogger<SearchEngineFactory>.Instance, e);
    }

    private static RagService CreateService(IDocumentExtractor extractor, TrackingSearchEngine? engine = null)
    {
        var se = engine ?? new TrackingSearchEngine();
        var factory = new DocumentExtractorFactory([extractor]);
        var seFactory = CreateFactory(se);
        return new RagService(seFactory, factory, new FakeChunker(), new NoOpReranker());
    }

    private static FileAttachment MakeFile(string name = "test.txt", string content = "hello world")
        => new() { FileName = name, MimeType = "text/plain", Data = System.Text.Encoding.UTF8.GetBytes(content) };

    private static AgentCraftLab.Engine.Models.Schema.RagConfig DefaultSettings => new() { ChunkSize = 10, ChunkOverlap = 0 };

    private static async Task<List<ExecutionEvent>> CollectEvents(IAsyncEnumerable<ExecutionEvent> stream)
    {
        var events = new List<ExecutionEvent>();
        await foreach (var e in stream) events.Add(e);
        return events;
    }

    // ─── IngestAsync Tests ───

    [Fact]
    public async Task IngestAsync_ExtractionFails_YieldsError()
    {
        var svc = CreateService(new NullDocumentExtractor());
        var events = await CollectEvents(
            svc.IngestAsync(MakeFile(), DefaultSettings, new FakeEmbeddingGenerator(), "idx"));

        Assert.Contains(events, e => e.Type == EventTypes.Error);
        Assert.DoesNotContain(events, e => e.Type == EventTypes.RagReady);
    }

    [Fact]
    public async Task IngestAsync_EmptyContent_YieldsError()
    {
        var extractor = new FakeDocumentExtractor(new ExtractionResult { Text = "", FileName = "test.txt" });
        var svc = CreateService(extractor);
        var events = await CollectEvents(
            svc.IngestAsync(MakeFile(), DefaultSettings, new FakeEmbeddingGenerator(), "idx"));

        Assert.Contains(events, e => e.Type == EventTypes.Error);
    }

    [Fact]
    public async Task IngestAsync_NormalFile_IndexesChunksAndYieldsRagReady()
    {
        var text = "abcdefghijklmnopqrstuvwxyz"; // 26 chars → 3 chunks with chunkSize=10
        var extractor = new FakeDocumentExtractor(new ExtractionResult { Text = text, FileName = "test.txt" });
        var engine = new TrackingSearchEngine();
        var embedGen = new FakeEmbeddingGenerator();
        var svc = CreateService(extractor, engine);

        var events = await CollectEvents(
            svc.IngestAsync(MakeFile("test.txt", text), new AgentCraftLab.Engine.Models.Schema.RagConfig { ChunkSize = 10, ChunkOverlap = 0 },
                embedGen, "my-index"));

        // 最後一個事件是 RagReady
        Assert.Equal(EventTypes.RagReady, events.Last().Type);
        Assert.Contains("3", events.Last().Text); // "3 chunks indexed"

        // 索引已建立
        Assert.Contains("my-index", engine.EnsuredIndexes);

        // 文件已寫入
        var totalDocs = engine.IndexedBatches.Sum(b => b.DocCount);
        Assert.Equal(3, totalDocs);
    }

    [Fact]
    public async Task IngestAsync_MultipleBatches_EmbeddingCalledMultipleTimes()
    {
        // 產生超過 EmbeddingBatchSize (100) 的 chunks
        var longText = new string('x', 150 * 10); // 1500 chars → 150 chunks with chunkSize=10
        var extractor = new FakeDocumentExtractor(new ExtractionResult { Text = longText, FileName = "big.txt" });
        var engine = new TrackingSearchEngine();
        var embedGen = new FakeEmbeddingGenerator();
        var svc = CreateService(extractor, engine);

        await CollectEvents(
            svc.IngestAsync(MakeFile("big.txt", longText), new AgentCraftLab.Engine.Models.Schema.RagConfig { ChunkSize = 10, ChunkOverlap = 0 },
                embedGen, "idx"));

        // 150 chunks / 100 batch = 2 batches
        Assert.Equal(2, embedGen.CallCount);
        Assert.Equal(150, engine.IndexedBatches.Sum(b => b.DocCount));
    }

    [Fact]
    public async Task IngestAsync_EventSequence_StartsWithProcessingEndsWithReady()
    {
        var extractor = new FakeDocumentExtractor(new ExtractionResult { Text = "some content", FileName = "f.txt" });
        var svc = CreateService(extractor);
        var events = await CollectEvents(
            svc.IngestAsync(MakeFile(), DefaultSettings, new FakeEmbeddingGenerator(), "idx"));

        Assert.Equal(EventTypes.RagProcessing, events.First().Type);
        Assert.Equal(EventTypes.RagReady, events.Last().Type);
        // 中間事件全是 RagProcessing
        foreach (var e in events.Skip(1).SkipLast(1))
            Assert.Equal(EventTypes.RagProcessing, e.Type);
    }

    // ─── SearchAsync Tests ───

    [Fact]
    public async Task SearchAsync_FiltersEmptyContent()
    {
        var engine = new TrackingSearchEngine
        {
            SearchResults =
            [
                new SearchResult { Id = "1", Content = "valid result", Score = 0.9f },
                new SearchResult { Id = "2", Content = "", Score = 0.8f },
                new SearchResult { Id = "3", Content = "another", Score = 0.7f },
            ]
        };
        var svc = new RagService(CreateFactory(engine), new DocumentExtractorFactory([]), new FakeChunker(), new NoOpReranker());

        var results = await svc.SearchAsync("query", 5, new FakeEmbeddingGenerator(), "idx");

        Assert.Equal(2, results.Count);
        Assert.Equal("valid result", results[0].Content);
        Assert.Equal("another", results[1].Content);
    }

    [Fact]
    public async Task SearchAsync_NullContent_IsFilteredOut()
    {
        var engine = new TrackingSearchEngine
        {
            SearchResults = [new SearchResult { Id = "x", Content = null!, Score = 0.5f }]
        };
        var svc = new RagService(CreateFactory(engine), new DocumentExtractorFactory([]), new FakeChunker(), new NoOpReranker());

        var results = await svc.SearchAsync("q", 3, new FakeEmbeddingGenerator(), "idx");
        Assert.Empty(results);
    }

    // ─── Multi-Provider Routing Tests ───

    [Fact]
    public void GetSearchEngine_Null_ReturnsOverrideEngine()
    {
        var engine = new TrackingSearchEngine();
        var svc = new RagService(CreateFactory(engine), new DocumentExtractorFactory([]), new FakeChunker(), new NoOpReranker());

        var resolved = svc.GetSearchEngine();
        Assert.Same(engine, resolved);
    }

    [Fact]
    public void GetSearchEngine_WithUnknownDataSourceId_ThrowsInvalidOperation()
    {
        var svc = new RagService(CreateFactory(), new DocumentExtractorFactory([]), new FakeChunker(), new NoOpReranker());

        // NullDataSourceStore 回傳 null → factory throws
        Assert.Throws<InvalidOperationException>(() => svc.GetSearchEngine("ds-nonexistent"));
    }

    [Fact]
    public async Task SearchAsync_WithDataSourceId_Null_UsesOverrideEngine()
    {
        var engine = new TrackingSearchEngine { SearchResults = [new SearchResult { Id = "default", Content = "from default", Score = 0.5f }] };
        var svc = new RagService(CreateFactory(engine), new DocumentExtractorFactory([]), new FakeChunker(), new NoOpReranker());

        // dataSourceId=null → override engine
        var results = await svc.SearchAsync("test", 3, new FakeEmbeddingGenerator(), "idx", dataSourceId: null);
        Assert.Single(results);
        Assert.Equal("from default", results[0].Content);
    }

    [Fact]
    public async Task GetSearchEngineAsync_Null_ReturnsOverrideEngine()
    {
        var engine = new TrackingSearchEngine();
        var svc = new RagService(CreateFactory(engine), new DocumentExtractorFactory([]), new FakeChunker(), new NoOpReranker());

        var resolved = await svc.GetSearchEngineAsync(null);
        Assert.Same(engine, resolved);
    }

    [Fact]
    public async Task IngestAsync_WithNullDataSourceId_UsesOverrideEngine()
    {
        var engine = new TrackingSearchEngine();
        var extractor = new FakeDocumentExtractor(new ExtractionResult { Text = "hello world", FileName = "test.txt" });
        var svc = new RagService(CreateFactory(engine), new DocumentExtractorFactory([extractor]), new FakeChunker(), new NoOpReranker());

        await foreach (var _ in svc.IngestAsync(MakeFile(), DefaultSettings, new FakeEmbeddingGenerator(), "test-idx")) { }

        Assert.Contains("test-idx", engine.EnsuredIndexes);
        Assert.True(engine.IndexedBatches.Count > 0);
    }
}

file class FakeDataSourceStore(string targetId, DataSourceDocument doc) : IDataSourceStore
{
    public Task<DataSourceDocument> SaveAsync(DataSourceDocument d) => Task.FromResult(d);
    public Task<List<DataSourceDocument>> ListAsync(string userId) => Task.FromResult(new List<DataSourceDocument>());
    public Task<DataSourceDocument?> GetAsync(string id) => Task.FromResult<DataSourceDocument?>(id == targetId ? doc : null);
    public Task<DataSourceDocument?> UpdateAsync(string userId, string id, string name, string description, string provider, string configJson) => Task.FromResult<DataSourceDocument?>(null);
    public Task<bool> DeleteAsync(string userId, string id) => Task.FromResult(false);
    public Task<int> CountKbReferencesAsync(string id) => Task.FromResult(0);
}

file class NullDataSourceStore : IDataSourceStore
{
    public Task<DataSourceDocument> SaveAsync(DataSourceDocument doc) => Task.FromResult(doc);
    public Task<List<DataSourceDocument>> ListAsync(string userId) => Task.FromResult(new List<DataSourceDocument>());
    public Task<DataSourceDocument?> GetAsync(string id) => Task.FromResult<DataSourceDocument?>(null);
    public Task<DataSourceDocument?> UpdateAsync(string userId, string id, string name, string description, string provider, string configJson) => Task.FromResult<DataSourceDocument?>(null);
    public Task<bool> DeleteAsync(string userId, string id) => Task.FromResult(false);
    public Task<int> CountKbReferencesAsync(string id) => Task.FromResult(0);
}
