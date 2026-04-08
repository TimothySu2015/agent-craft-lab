using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Extraction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCraftLab.Tests.Engine;

public class KnowledgeBaseServiceTests
{
    // ─── In-Memory Fakes ───

    private class InMemoryKnowledgeBaseStore : IKnowledgeBaseStore
    {
        private readonly List<KnowledgeBaseDocument> _kbs = [];
        private readonly List<KbFileDocument> _files = [];

        public Task<KnowledgeBaseDocument> SaveAsync(string userId, string name, string description,
            string embeddingModel, int chunkSize, int chunkOverlap, string? dataSourceId = null,
            string chunkStrategy = "fixed")
        {
            var kb = new KnowledgeBaseDocument
            {
                Id = $"kb-{Guid.NewGuid():N}"[..11],
                UserId = userId,
                Name = name,
                Description = description,
                IndexName = $"{userId}_kb_{Guid.NewGuid():N}"[..20],
                EmbeddingModel = embeddingModel,
                ChunkSize = chunkSize,
                ChunkOverlap = chunkOverlap,
                ChunkStrategy = chunkStrategy,
                DataSourceId = dataSourceId,
            };
            _kbs.Add(kb);
            return Task.FromResult(kb);
        }

        public Task<List<KnowledgeBaseDocument>> ListAsync(string userId)
            => Task.FromResult(_kbs.Where(k => k.UserId == userId && !k.IsDeleted).ToList());

        public Task<KnowledgeBaseDocument?> GetAsync(string id)
            => Task.FromResult(_kbs.FirstOrDefault(k => k.Id == id));

        public Task<KnowledgeBaseDocument?> UpdateAsync(string userId, string id, string name, string description)
        {
            var kb = _kbs.FirstOrDefault(k => k.Id == id && k.UserId == userId);
            if (kb is null) return Task.FromResult<KnowledgeBaseDocument?>(null);
            kb.Name = name;
            kb.Description = description;
            return Task.FromResult<KnowledgeBaseDocument?>(kb);
        }

        public Task<bool> DeleteAsync(string userId, string id)
        {
            var kb = _kbs.FirstOrDefault(k => k.Id == id && k.UserId == userId);
            if (kb is null) return Task.FromResult(false);
            kb.IsDeleted = true;
            kb.DeletedAt = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task<KbFileDocument> AddFileAsync(string knowledgeBaseId, string fileName, string mimeType,
            long fileSize, List<string> chunkIds)
        {
            var file = new KbFileDocument
            {
                Id = $"kbf-{Guid.NewGuid():N}"[..11],
                KnowledgeBaseId = knowledgeBaseId,
                FileName = fileName,
                MimeType = mimeType,
                FileSize = fileSize,
                ChunkCount = chunkIds.Count,
            };
            file.SetChunkIds(chunkIds);
            _files.Add(file);
            return Task.FromResult(file);
        }

        public Task<KbFileDocument?> GetFileAsync(string knowledgeBaseId, string fileId)
            => Task.FromResult(_files.FirstOrDefault(f => f.KnowledgeBaseId == knowledgeBaseId && f.Id == fileId));

        public Task<List<KbFileDocument>> ListFilesAsync(string knowledgeBaseId)
            => Task.FromResult(_files.Where(f => f.KnowledgeBaseId == knowledgeBaseId).ToList());

        public Task<bool> RemoveFileAsync(string knowledgeBaseId, string fileId)
        {
            var idx = _files.FindIndex(f => f.KnowledgeBaseId == knowledgeBaseId && f.Id == fileId);
            if (idx < 0) return Task.FromResult(false);
            _files.RemoveAt(idx);
            return Task.FromResult(true);
        }

        public Task UpdateStatsAsync(string id, int fileCount, long totalChunks)
        {
            var kb = _kbs.FirstOrDefault(k => k.Id == id);
            if (kb is not null) { kb.FileCount = fileCount; kb.TotalChunks = totalChunks; }
            return Task.CompletedTask;
        }

        public Task<List<KnowledgeBaseDocument>> GetPendingDeletionsAsync(TimeSpan delay)
            => Task.FromResult(_kbs.Where(k => k.IsDeleted && k.DeletedAt < DateTime.UtcNow - delay).ToList());

        public Task HardDeleteAsync(string id)
        {
            _kbs.RemoveAll(k => k.Id == id);
            return Task.CompletedTask;
        }
    }

    private class FakeDocumentExtractor : IDocumentExtractor
    {
        private readonly ExtractionResult? _result;
        public FakeDocumentExtractor(ExtractionResult? result = null) => _result = result;
        public bool CanExtract(string mimeType) => _result is not null;
        public Task<ExtractionResult> ExtractAsync(byte[] data, string fileName, CancellationToken ct = default)
            => Task.FromResult(_result ?? new ExtractionResult { Text = "", FileName = fileName });
    }

    private class FakeChunker : ITextChunker
    {
        public IReadOnlyList<ChunkResult> Chunk(string text, int chunkSize, int overlap)
            => [new ChunkResult { Text = text, Index = 0, StartPosition = 0 }];
    }

    private class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken ct = default)
        {
            var list = values.Select(_ => new Embedding<float>(new float[] { 0.1f })).ToList();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list));
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? key = null) where TService : class => null;
        public void Dispose() { }
    }

    private class TrackingSearchEngine : ISearchEngine
    {
        public List<string> EnsuredIndexes { get; } = [];
        public List<string> DeletedIndexes { get; } = [];
        public List<(string Index, List<string> DocIds)> DeletedDocs { get; } = [];
        public bool ThrowOnDeleteIndex { get; set; }

        public Task EnsureIndexAsync(string indexName, CancellationToken ct = default)
        { EnsuredIndexes.Add(indexName); return Task.CompletedTask; }

        public Task IndexDocumentsAsync(string indexName, IEnumerable<SearchDocument> documents, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteDocumentsAsync(string indexName, IEnumerable<string> documentIds, CancellationToken ct = default)
        { DeletedDocs.Add((indexName, documentIds.ToList())); return Task.CompletedTask; }

        public Task DeleteIndexAsync(string indexName, CancellationToken ct = default)
        {
            if (ThrowOnDeleteIndex) throw new InvalidOperationException("delete failed");
            DeletedIndexes.Add(indexName);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(string indexName, SearchQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SearchResult>>([]);
        public Task<IndexInfo?> GetIndexInfoAsync(string indexName, CancellationToken ct = default) => Task.FromResult<IndexInfo?>(null);
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IndexInfo>>([]);
        public Task<int> CleanupStaleIndexesAsync(TimeSpan ttl, CancellationToken ct = default) => Task.FromResult(0);
    }

    // ─── Helpers ───

    private static KnowledgeBaseService CreateService(
        InMemoryKnowledgeBaseStore? store = null,
        TrackingSearchEngine? engine = null,
        IDocumentExtractor? extractor = null)
    {
        store ??= new InMemoryKnowledgeBaseStore();
        engine ??= new TrackingSearchEngine();
        var dsStore = new InMemoryDataSourceStore();
        var factory = new SearchEngineFactory(engine, dsStore, NullLogger<SearchEngineFactory>.Instance);
        var ext = extractor ?? new FakeDocumentExtractor(new ExtractionResult { Text = "test content", FileName = "f.txt" });
        return new KnowledgeBaseService(store, engine, factory, new DocumentExtractorFactory([ext]), new FakeChunker(),
            NullLogger<KnowledgeBaseService>.Instance);
    }

    private class InMemoryDataSourceStore : IDataSourceStore
    {
        public Task<DataSourceDocument> SaveAsync(DataSourceDocument doc) => Task.FromResult(doc);
        public Task<List<DataSourceDocument>> ListAsync(string userId) => Task.FromResult<List<DataSourceDocument>>([]);
        public Task<DataSourceDocument?> GetAsync(string id) => Task.FromResult<DataSourceDocument?>(null);
        public Task<DataSourceDocument?> UpdateAsync(string userId, string id, string name, string description, string provider, string configJson) => Task.FromResult<DataSourceDocument?>(null);
        public Task<bool> DeleteAsync(string userId, string id) => Task.FromResult(false);
        public Task<int> CountKbReferencesAsync(string id) => Task.FromResult(0);
    }

    private static async Task<List<ExecutionEvent>> CollectEvents(IAsyncEnumerable<ExecutionEvent> stream)
    {
        var events = new List<ExecutionEvent>();
        await foreach (var e in stream) events.Add(e);
        return events;
    }

    // ─── CreateAsync ───

    [Fact]
    public async Task CreateAsync_ReturnsDocumentAndCreatesIndex()
    {
        var store = new InMemoryKnowledgeBaseStore();
        var engine = new TrackingSearchEngine();
        var svc = CreateService(store, engine);

        var kb = await svc.CreateAsync("user1", "My KB", "desc", "text-embedding-3-small", 500, 50);

        Assert.Equal("user1", kb.UserId);
        Assert.Equal("My KB", kb.Name);
        Assert.Contains(kb.IndexName, engine.EnsuredIndexes);
    }

    // ─── AddFileAsync ───

    [Fact]
    public async Task AddFileAsync_KbNotFound_YieldsError()
    {
        var svc = CreateService();
        var events = await CollectEvents(
            svc.AddFileAsync("nonexistent", "file.txt", "text/plain", [1, 2, 3], new FakeEmbeddingGenerator()));

        Assert.Single(events);
        Assert.Equal(EventTypes.Error, events[0].Type);
        Assert.Contains("not found", events[0].Text);
    }

    [Fact]
    public async Task AddFileAsync_KbDeleted_YieldsError()
    {
        var store = new InMemoryKnowledgeBaseStore();
        var svc = CreateService(store);
        var kb = await store.SaveAsync("u1", "kb", "", "model", 100, 10);
        kb.IsDeleted = true;

        var events = await CollectEvents(
            svc.AddFileAsync(kb.Id, "file.txt", "text/plain", [1], new FakeEmbeddingGenerator()));

        Assert.Single(events);
        Assert.Equal(EventTypes.Error, events[0].Type);
    }

    [Fact]
    public async Task AddFileAsync_UnsupportedFormat_YieldsError()
    {
        var store = new InMemoryKnowledgeBaseStore();
        var svc = CreateService(store, extractor: new FakeDocumentExtractor(null));
        var kb = await store.SaveAsync("u1", "kb", "", "model", 100, 10);

        var events = await CollectEvents(
            svc.AddFileAsync(kb.Id, "file.bin", "application/octet-stream", [1], new FakeEmbeddingGenerator()));

        Assert.Contains(events, e => e.Type == EventTypes.Error && e.Text.Contains("Unsupported"));
    }

    [Fact]
    public async Task AddFileAsync_NormalFile_IndexesAndUpdatesStats()
    {
        var store = new InMemoryKnowledgeBaseStore();
        var engine = new TrackingSearchEngine();
        var svc = CreateService(store, engine);
        var kb = await store.SaveAsync("u1", "kb", "", "model", 100, 10);

        var events = await CollectEvents(
            svc.AddFileAsync(kb.Id, "doc.txt", "text/plain", [1, 2], new FakeEmbeddingGenerator()));

        Assert.Equal(EventTypes.RagReady, events.Last().Type);

        // 檔案已記錄
        var files = await store.ListFilesAsync(kb.Id);
        Assert.Single(files);
        Assert.Equal("doc.txt", files[0].FileName);

        // 統計已更新
        var updatedKb = await store.GetAsync(kb.Id);
        Assert.Equal(1, updatedKb!.FileCount);
    }

    // ─── File Replacement ───

    [Fact]
    public async Task AddFileAsync_DuplicateFileName_ReplacesOldFile()
    {
        var store = new InMemoryKnowledgeBaseStore();
        var engine = new TrackingSearchEngine();
        var svc = CreateService(store, engine);
        var kb = await store.SaveAsync("u1", "kb", "", "model", 100, 10);

        // 第一次上傳
        await CollectEvents(
            svc.AddFileAsync(kb.Id, "doc.txt", "text/plain", [1, 2], new FakeEmbeddingGenerator()));
        var files1 = await store.ListFilesAsync(kb.Id);
        Assert.Single(files1);

        // 第二次上傳同名檔案
        var events = await CollectEvents(
            svc.AddFileAsync(kb.Id, "doc.txt", "text/plain", [3, 4, 5], new FakeEmbeddingGenerator()));

        // 應有 Replacing 訊息
        Assert.Contains(events, e => e.Text.Contains("Replacing"));

        // 仍然只有一個檔案（舊的被刪、新的被加）
        var files2 = await store.ListFilesAsync(kb.Id);
        Assert.Single(files2);
        // 新檔案的 ID 不同於舊的
        Assert.NotEqual(files1[0].Id, files2[0].Id);
    }

    [Fact]
    public async Task AddFileAsync_DifferentFileName_NoReplacement()
    {
        var store = new InMemoryKnowledgeBaseStore();
        var svc = CreateService(store);
        var kb = await store.SaveAsync("u1", "kb", "", "model", 100, 10);

        await CollectEvents(
            svc.AddFileAsync(kb.Id, "doc1.txt", "text/plain", [1], new FakeEmbeddingGenerator()));
        await CollectEvents(
            svc.AddFileAsync(kb.Id, "doc2.txt", "text/plain", [2], new FakeEmbeddingGenerator()));

        var files = await store.ListFilesAsync(kb.Id);
        Assert.Equal(2, files.Count);
    }

    // ─── RemoveFileAsync ───

    [Fact]
    public async Task RemoveFileAsync_KbNotFound_ReturnsFalse()
    {
        var svc = CreateService();
        Assert.False(await svc.RemoveFileAsync("nonexistent", "file-id"));
    }

    [Fact]
    public async Task RemoveFileAsync_FileNotFound_ReturnsFalse()
    {
        var store = new InMemoryKnowledgeBaseStore();
        var svc = CreateService(store);
        var kb = await store.SaveAsync("u1", "kb", "", "model", 100, 10);

        Assert.False(await svc.RemoveFileAsync(kb.Id, "nonexistent-file"));
    }

    [Fact]
    public async Task RemoveFileAsync_WithChunks_DeletesFromSearchEngine()
    {
        var store = new InMemoryKnowledgeBaseStore();
        var engine = new TrackingSearchEngine();
        var svc = CreateService(store, engine);
        var kb = await store.SaveAsync("u1", "kb", "", "model", 100, 10);

        // 手動加入檔案記錄（模擬 ingest 完成）
        var file = await store.AddFileAsync(kb.Id, "doc.txt", "text/plain", 100, ["chunk1", "chunk2"]);

        var result = await svc.RemoveFileAsync(kb.Id, file.Id);

        Assert.True(result);
        Assert.Single(engine.DeletedDocs);
        Assert.Equal(2, engine.DeletedDocs[0].DocIds.Count);
    }

    [Fact]
    public async Task RemoveFileAsync_EmptyChunks_SkipsSearchEngineDelete()
    {
        var store = new InMemoryKnowledgeBaseStore();
        var engine = new TrackingSearchEngine();
        var svc = CreateService(store, engine);
        var kb = await store.SaveAsync("u1", "kb", "", "model", 100, 10);

        var file = await store.AddFileAsync(kb.Id, "empty.txt", "text/plain", 0, []);

        var result = await svc.RemoveFileAsync(kb.Id, file.Id);

        Assert.True(result);
        Assert.Empty(engine.DeletedDocs);
    }

    [Fact]
    public async Task RemoveFileAsync_UpdatesStats()
    {
        var store = new InMemoryKnowledgeBaseStore();
        var svc = CreateService(store);
        var kb = await store.SaveAsync("u1", "kb", "", "model", 100, 10);

        await store.AddFileAsync(kb.Id, "a.txt", "text/plain", 10, ["c1", "c2"]);
        var fileB = await store.AddFileAsync(kb.Id, "b.txt", "text/plain", 20, ["c3"]);
        await store.UpdateStatsAsync(kb.Id, 2, 3);

        await svc.RemoveFileAsync(kb.Id, fileB.Id);

        var updated = await store.GetAsync(kb.Id);
        Assert.Equal(1, updated!.FileCount);
        Assert.Equal(2, updated.TotalChunks);
    }

    // ─── CleanupDeletedAsync ───

    [Fact]
    public async Task CleanupDeletedAsync_DeletesIndexAndHardDeletes()
    {
        var store = new InMemoryKnowledgeBaseStore();
        var engine = new TrackingSearchEngine();
        var svc = CreateService(store, engine);

        var kb = await store.SaveAsync("u1", "old-kb", "", "model", 100, 10);
        kb.IsDeleted = true;
        kb.DeletedAt = DateTime.UtcNow.AddHours(-25); // 超過 24 小時

        await svc.CleanupDeletedAsync();

        Assert.Contains(kb.IndexName, engine.DeletedIndexes);
        Assert.Null(await store.GetAsync(kb.Id));
    }

    [Fact]
    public async Task CleanupDeletedAsync_OneThrows_OtherStillProcessed()
    {
        var store = new InMemoryKnowledgeBaseStore();
        var engine = new TrackingSearchEngine { ThrowOnDeleteIndex = true };
        var svc = CreateService(store, engine);

        var kb1 = await store.SaveAsync("u1", "kb1", "", "m", 100, 10);
        kb1.IsDeleted = true;
        kb1.DeletedAt = DateTime.UtcNow.AddHours(-25);

        var kb2 = await store.SaveAsync("u1", "kb2", "", "m", 100, 10);
        kb2.IsDeleted = true;
        kb2.DeletedAt = DateTime.UtcNow.AddHours(-25);

        // 不應拋出例外（內部 catch）
        await svc.CleanupDeletedAsync();

        // 兩個都沒被 hard delete（因為 DeleteIndexAsync 拋出 → catch → 跳過 HardDeleteAsync）
        Assert.NotNull(await store.GetAsync(kb1.Id));
        Assert.NotNull(await store.GetAsync(kb2.Id));
    }
}
