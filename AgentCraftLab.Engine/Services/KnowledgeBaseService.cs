using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Chunking;
using AgentCraftLab.Search.Extraction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 知識庫管理服務：建立知識庫、上傳檔案（Ingest）、刪除檔案、清理過期軟刪除。
/// </summary>
public class KnowledgeBaseService
{
    private readonly IKnowledgeBaseStore _store;
    private readonly ISearchEngine _searchEngine;
    private readonly SearchEngineFactory _searchEngineFactory;
    private readonly DocumentExtractorFactory _extractorFactory;
    private readonly ITextChunker _chunker;
    private readonly StructuralChunker _structuralChunker;
    private readonly ILogger<KnowledgeBaseService> _logger;

    public KnowledgeBaseService(
        IKnowledgeBaseStore store,
        ISearchEngine searchEngine,
        SearchEngineFactory searchEngineFactory,
        DocumentExtractorFactory extractorFactory,
        ITextChunker chunker,
        ILogger<KnowledgeBaseService> logger)
    {
        _store = store;
        _searchEngine = searchEngine;
        _searchEngineFactory = searchEngineFactory;
        _extractorFactory = extractorFactory;
        _chunker = chunker;
        _structuralChunker = new StructuralChunker();
        _logger = logger;
    }

    private ITextChunker ResolveChunker(string? strategy) => strategy switch
    {
        "structural" => _structuralChunker,
        _ => _chunker
    };

    public async Task<KnowledgeBaseDocument> CreateAsync(string userId, string name, string description,
        string embeddingModel, int chunkSize, int chunkOverlap, CancellationToken cancellationToken = default,
        string? dataSourceId = null, string chunkStrategy = "fixed")
    {
        var kb = await _store.SaveAsync(userId, name, description, embeddingModel, chunkSize, chunkOverlap, dataSourceId, chunkStrategy);
        var engine = await _searchEngineFactory.ResolveAsync(kb.DataSourceId, cancellationToken);
        await engine.EnsureIndexAsync(kb.IndexName, cancellationToken);
        return kb;
    }

    /// <summary>
    /// 上傳檔案到知識庫：擷取 → 分塊 → embedding → 寫入搜尋引擎 → 記錄 ChunkIds。
    /// </summary>
    public async IAsyncEnumerable<ExecutionEvent> AddFileAsync(
        string knowledgeBaseId,
        string fileName,
        string mimeType,
        byte[] fileData,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var kb = await _store.GetAsync(knowledgeBaseId);
        if (kb is null || kb.IsDeleted)
        {
            yield return ExecutionEvent.Error("Knowledge base not found.");
            yield break;
        }

        // 同名檔案替換：刪除舊檔的 chunks 再重新 ingest
        var existingFiles = await _store.ListFilesAsync(knowledgeBaseId);
        var duplicate = existingFiles.FirstOrDefault(f =>
            string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            yield return ExecutionEvent.RagProcessing($"Replacing existing file {fileName}...");
            await RemoveFileAsync(knowledgeBaseId, duplicate.Id, cancellationToken);
        }

        yield return ExecutionEvent.RagProcessing($"Extracting text from {fileName}...");

        var extraction = await _extractorFactory.ExtractAsync(fileData, fileName, mimeType, cancellationToken);
        if (extraction is null || !extraction.HasContent)
        {
            yield return ExecutionEvent.Error($"Unsupported file format or no text could be extracted from {fileName}.");
            yield break;
        }

        yield return ExecutionEvent.RagProcessing($"Chunking text ({extraction.Text.Length} chars)...");
        var chunker = ResolveChunker(kb.ChunkStrategy);
        var chunks = chunker.Chunk(extraction.Text, kb.ChunkSize, kb.ChunkOverlap);
        yield return ExecutionEvent.RagProcessing($"Created {chunks.Count} chunks. Generating embeddings...");

        var engine = await _searchEngineFactory.ResolveAsync(kb.DataSourceId, cancellationToken);
        await engine.EnsureIndexAsync(kb.IndexName, cancellationToken);

        var batchSize = Defaults.EmbeddingBatchSize;
        var totalIngested = 0;
        var chunkTexts = chunks.Select(c => c.Text).ToList();
        var allChunkIds = new List<string>(chunkTexts.Count);

        for (int i = 0; i < chunkTexts.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = chunkTexts.Skip(i).Take(batchSize).ToList();
            var embeddings = await embeddingGenerator.GenerateAsync(batch,
                new EmbeddingGenerationOptions { Dimensions = Defaults.GetEmbeddingDimensions(kb.EmbeddingModel) },
                cancellationToken);

            var searchDocs = new List<SearchDocument>(batch.Count);
            for (int j = 0; j < batch.Count; j++)
            {
                var docId = Guid.NewGuid().ToString("N");
                allChunkIds.Add(docId);
                searchDocs.Add(new SearchDocument
                {
                    Id = docId,
                    Content = batch[j],
                    Vector = embeddings[j].Vector,
                    FileName = fileName,
                    ChunkIndex = i + j
                });
            }

            await engine.IndexDocumentsAsync(kb.IndexName, searchDocs, cancellationToken);
            totalIngested += batch.Count;
            yield return ExecutionEvent.RagProcessing($"Embedded {totalIngested}/{chunkTexts.Count} chunks...");
        }

        // 記錄檔案和 chunk IDs
        await _store.AddFileAsync(knowledgeBaseId, fileName, mimeType, fileData.Length, allChunkIds);

        // 更新統計
        var files = await _store.ListFilesAsync(knowledgeBaseId);
        var totalChunks = files.Sum(f => (long)f.ChunkCount);
        await _store.UpdateStatsAsync(knowledgeBaseId, files.Count, totalChunks);

        // 附帶前 3 個 chunk 預覽
        var previewChunks = chunkTexts.Take(3).Select((text, idx) =>
            text.Length > Defaults.ChunkPreviewLength ? text[..Defaults.ChunkPreviewLength] + "..." : text).ToList();
        yield return new ExecutionEvent
        {
            Type = EventTypes.RagReady,
            Text = $"Indexed {totalIngested} chunks from {fileName}",
            Metadata = new Dictionary<string, string>
            {
                ["chunkPreviews"] = System.Text.Json.JsonSerializer.Serialize(previewChunks),
                ["totalChunks"] = totalIngested.ToString(),
                ["fileName"] = fileName
            }
        };
    }

    /// <summary>
    /// 從 URL 爬取網頁內容並加入知識庫。
    /// </summary>
    public async IAsyncEnumerable<ExecutionEvent> AddUrlAsync(
        string knowledgeBaseId,
        string url,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return ExecutionEvent.RagProcessing($"Fetching {url}...");

        // Fetch URL（不在 catch 裡 yield，改用錯誤旗標）
        byte[]? htmlData = null;
        string? fetchError = null;
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AgentCraftLab/1.0");
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            htmlData = await httpClient.GetByteArrayAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            fetchError = ex.Message;
        }

        if (fetchError is not null || htmlData is null)
        {
            yield return ExecutionEvent.Error($"Failed to fetch URL: {fetchError ?? "empty response"}");
            yield break;
        }

        // 從 URL 取得檔名（用 domain + path 簡化）
        var uri = new Uri(url);
        var fileName = $"{uri.Host}{uri.AbsolutePath}".Replace("/", "_").TrimEnd('_');
        if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
        {
            fileName += ".html";
        }

        // 委派給 AddFileAsync 處理 ingest
        await foreach (var evt in AddFileAsync(knowledgeBaseId, fileName, "text/html", htmlData, embeddingGenerator, cancellationToken))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// 刪除知識庫中的單個檔案及其 chunks。
    /// </summary>
    public async Task<bool> RemoveFileAsync(string knowledgeBaseId, string fileId, CancellationToken cancellationToken = default)
    {
        var kb = await _store.GetAsync(knowledgeBaseId);
        if (kb is null)
        {
            return false;
        }

        var file = await _store.GetFileAsync(knowledgeBaseId, fileId);
        if (file is null)
        {
            return false;
        }

        // 從搜尋引擎刪除 chunks
        var chunkIds = file.GetChunkIds();
        if (chunkIds.Count > 0)
        {
            var engine = await _searchEngineFactory.ResolveAsync(kb.DataSourceId, cancellationToken);
            await engine.DeleteDocumentsAsync(kb.IndexName, chunkIds, cancellationToken);
        }

        // 刪除檔案記錄
        await _store.RemoveFileAsync(knowledgeBaseId, fileId);

        // 更新統計
        var remainingFiles = await _store.ListFilesAsync(knowledgeBaseId);
        var totalChunks = remainingFiles.Sum(f => (long)f.ChunkCount);
        await _store.UpdateStatsAsync(knowledgeBaseId, remainingFiles.Count, totalChunks);

        return true;
    }

    /// <summary>
    /// 清理軟刪除超過 24 小時的知識庫（刪除搜尋索引 + 硬刪除記錄）。
    /// </summary>
    public async Task CleanupDeletedAsync(CancellationToken cancellationToken = default)
    {
        var pendingDeletions = await _store.GetPendingDeletionsAsync(TimeSpan.FromHours(24));
        foreach (var kb in pendingDeletions)
        {
            try
            {
                var engine = await _searchEngineFactory.ResolveAsync(kb.DataSourceId, cancellationToken);
                await engine.DeleteIndexAsync(kb.IndexName, cancellationToken);
                await _store.HardDeleteAsync(kb.Id);
                _logger.LogInformation("Cleaned up deleted knowledge base: {Id} ({Name})", kb.Id, kb.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup knowledge base: {Id}", kb.Id);
            }
        }
    }
}
