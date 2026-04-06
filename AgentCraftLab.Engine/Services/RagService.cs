using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Extraction;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// RAG 搜尋結果（含來源標注資訊）。
/// </summary>
public class RagChunk
{
    public required string Content { get; init; }
    public required string FileName { get; init; }
    public required int ChunkIndex { get; init; }
    public required float Score { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// RAG 管線服務：文字擷取、分塊、向量化、搜尋。
/// 協調 ISearchEngine（搜尋引擎）和 IEmbeddingGenerator（向量化），
/// 擷取與分塊委派給 CraftSearch 類別庫。
/// 若有註冊 IDocumentCleaner（CraftCleaner），擷取後會先清洗再分塊。
/// </summary>
public class RagService
{
    /// <summary>Reranker 候選擴展倍數（取 topK * 此倍數作為初步檢索量）。</summary>
    private const int RerankExpansionFactor = 3;

    private readonly ISearchEngine _searchEngine;
    private readonly DocumentExtractorFactory _extractorFactory;
    private readonly ITextChunker _chunker;
    private readonly IReranker _reranker;
    private readonly IDocumentCleaner? _documentCleaner;

    public RagService(
        ISearchEngine searchEngine,
        DocumentExtractorFactory extractorFactory,
        ITextChunker chunker,
        IReranker reranker,
        IDocumentCleaner? documentCleaner = null)
    {
        _searchEngine = searchEngine;
        _extractorFactory = extractorFactory;
        _chunker = chunker;
        _reranker = reranker;
        _documentCleaner = documentCleaner;
    }

    /// <summary>取得底層搜尋引擎（用於臨時索引管理，如 DocRefinery precise mode）。</summary>
    public ISearchEngine GetSearchEngine() => _searchEngine;

    /// <summary>取得文字分塊器（DocRefinery 檔案索引用）。</summary>
    public ITextChunker GetChunker() => _chunker;

    /// <summary>
    /// 完整 RAG Ingest 管線：擷取 → 分塊 → embedding → 寫入搜尋引擎。
    /// </summary>
    public async IAsyncEnumerable<ExecutionEvent> IngestAsync(
        FileAttachment file,
        RagSettings settings,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        string indexName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return ExecutionEvent.RagProcessing($"Extracting text from {file.FileName}...");

        // 使用 CraftSearch 擷取器（支援 PDF/DOCX/PPTX/HTML/純文字）
        var extraction = await _extractorFactory.ExtractAsync(
            file.Data, file.FileName, file.MimeType, cancellationToken);

        if (extraction is null || !extraction.HasContent)
        {
            yield return ExecutionEvent.Error($"Unsupported file format or no text could be extracted from {file.FileName}.");
            yield break;
        }

        // CraftCleaner 清洗（如有註冊）：去頁首頁尾、正規化空白、合併截斷段落等
        var textForChunking = extraction.Text;
        if (_documentCleaner is not null)
        {
            yield return ExecutionEvent.RagProcessing("Cleaning extracted text...");
            try
            {
                var cleaned = await _documentCleaner.CleanAsync(
                    file.Data, file.FileName, file.MimeType, ct: cancellationToken);
                var cleanedText = cleaned.GetFullText();
                if (!string.IsNullOrWhiteSpace(cleanedText))
                {
                    textForChunking = cleanedText;
                    // 合併清洗後的 metadata
                    foreach (var kv in cleaned.Metadata)
                    {
                        extraction.Metadata.TryAdd(kv.Key, kv.Value);
                    }
                }
            }
            catch (NotSupportedException)
            {
                // CraftCleaner 不支援此格式 → 用原始擷取結果
            }
        }

        yield return ExecutionEvent.RagProcessing($"Chunking text ({textForChunking.Length} chars)...");
        var chunks = _chunker.Chunk(textForChunking, settings.ChunkSize, settings.ChunkOverlap);
        yield return ExecutionEvent.RagProcessing($"Created {chunks.Count} chunks. Generating embeddings...");

        // 確保搜尋索引存在
        await _searchEngine.EnsureIndexAsync(indexName, cancellationToken);

        // 文件級 metadata（來自 Extractor 自動填充）+ chunk 級 metadata
        var docMetadata = extraction.Metadata;

        // 批次 embedding（OpenAI 支援最多 2048）
        var batchSize = Defaults.EmbeddingBatchSize;
        var totalIngested = 0;
        var chunkTexts = chunks.Select(c => c.Text).ToList();

        for (int i = 0; i < chunkTexts.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = chunkTexts.Skip(i).Take(batchSize).ToList();
            var embeddings = await embeddingGenerator.GenerateAsync(batch,
                new EmbeddingGenerationOptions { Dimensions = Defaults.GetEmbeddingDimensions(settings.EmbeddingModel) },
                cancellationToken);

            // 轉換為 SearchDocument 並寫入搜尋引擎（含文件 + chunk 級 metadata）
            var searchDocs = new List<SearchDocument>(batch.Count);
            for (int j = 0; j < batch.Count; j++)
            {
                var chunkMeta = new Dictionary<string, string>(docMetadata)
                {
                    ["chunk_index"] = (i + j).ToString(),
                    ["total_chunks"] = chunkTexts.Count.ToString()
                };

                searchDocs.Add(new SearchDocument
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Content = batch[j],
                    Vector = embeddings[j].Vector,
                    FileName = file.FileName,
                    ChunkIndex = i + j,
                    Metadata = chunkMeta
                });
            }

            await _searchEngine.IndexDocumentsAsync(indexName, searchDocs, cancellationToken);
            totalIngested += batch.Count;
            yield return ExecutionEvent.RagProcessing($"Embedded {totalIngested}/{chunkTexts.Count} chunks...");
        }

        yield return ExecutionEvent.RagReady($"RAG ready: {totalIngested} chunks indexed from {file.FileName}");
    }

    /// <summary>
    /// 搜尋相關片段（混合搜尋：全文 + 向量 RRF），回傳含來源標注的結果。
    /// </summary>
    /// <summary>最近一次 Query Expansion 生成的變體（供 UI 顯示）。</summary>
    public List<string>? LastExpandedQueries { get; private set; }

    public async Task<List<RagChunk>> SearchAsync(
        string query,
        int topK,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        string indexName,
        RagSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new RagSearchOptions();

        var mode = opts.SearchMode?.ToLowerInvariant() switch
        {
            "fulltext" => SearchMode.FullText,
            "vector" => SearchMode.Vector,
            _ => SearchMode.Hybrid
        };

        var dims = Defaults.GetEmbeddingDimensions(opts.EmbeddingModel);
        var expandedTopK = topK * RerankExpansionFactor;
        var scoreThreshold = opts.MinScore ?? SearchEngineOptions.DefaultRagMinScore;

        // 收集所有要搜尋的 queries（原始 + 擴展變體）
        var queries = new List<string> { query };
        LastExpandedQueries = null;

        if (opts.QueryExpansion && opts.QueryExpander is not null)
        {
            var variants = await opts.QueryExpander.ExpandAsync(query, cancellationToken);
            if (variants.Count > 0)
            {
                queries.AddRange(variants);
                LastExpandedQueries = variants;
            }
        }

        // 平行搜尋所有 queries
        var searchTasks = queries.Select(async q =>
        {
            var embedding = await embeddingGenerator.GenerateAsync(
                [q], new EmbeddingGenerationOptions { Dimensions = dims }, cancellationToken);
            return await _searchEngine.SearchAsync(indexName, new SearchQuery
            {
                Text = q,
                Vector = embedding[0].Vector,
                Mode = mode,
                TopK = expandedTopK,
                MinScore = scoreThreshold,
                FileNameFilter = opts.FileNameFilter
            }, cancellationToken);
        });

        var allResults = await Task.WhenAll(searchTasks);

        // 合併去重：同 Content 保留最高分
        var results = allResults
            .SelectMany(r => r)
            .GroupBy(r => r.Content)
            .Select(g => g.OrderByDescending(r => r.Score).First())
            .OrderByDescending(r => r.Score)
            .Take(expandedTopK)
            .ToList();

        // Rerank：重新排序並截斷至 topK
        var reranked = await _reranker.RerankAsync(query, results, topK, cancellationToken);

        return reranked
            .Where(r => r.Content is { Length: > 0 })
            .Select(r => new RagChunk
            {
                Content = r.Content,
                FileName = r.FileName,
                ChunkIndex = r.ChunkIndex,
                Score = r.Score,
                Metadata = r.Metadata
            })
            .ToList();
    }
}
