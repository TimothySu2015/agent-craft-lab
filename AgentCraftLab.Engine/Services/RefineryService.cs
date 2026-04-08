using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;
using AgentCraftLab.Cleaner.Renderers;
using AgentCraftLab.Cleaner.SchemaMapper;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// DocRefinery 服務 — 管理精煉專案的完整生命週期：
/// 建立/更新/刪除專案、上傳檔案（清洗+快取）、產出結構化文件（Schema Mapper）。
/// </summary>
public class RefineryService
{
    private const string FileStorageDir = "Data/refinery-files";
    private const int PreciseModeChunkSize = 500;
    private const int PreciseModeChunkOverlap = 100;
    private const int EmbeddingBatchSize = 20;
    private const int EmbeddingRetryCount = 3;
    private const int EmbeddingRetryDelayMs = 2000;

    private readonly IRefineryStore _store;
    private readonly IDocumentCleaner _cleaner;
    private readonly ISchemaTemplateProvider _templateProvider;
    private readonly RagService _ragService;
    private readonly ILogger<RefineryService>? _logger;

    public RefineryService(
        IRefineryStore store,
        IDocumentCleaner cleaner,
        ISchemaTemplateProvider templateProvider,
        RagService ragService,
        ILogger<RefineryService>? logger = null)
    {
        _store = store;
        _cleaner = cleaner;
        _templateProvider = templateProvider;
        _ragService = ragService;
        _logger = logger;
    }

    // ── Project CRUD ──

    public async Task<RefineryProject> CreateAsync(string userId, string name, string description,
        string? schemaTemplateId, string? customSchemaJson,
        string provider, string model, string? outputLanguage,
        string extractionMode = "fast", bool enableChallenge = false,
        string imageProcessingMode = "skip")
    {
        var project = await _store.SaveAsync(userId, name, description,
            schemaTemplateId, customSchemaJson, provider, model, outputLanguage, extractionMode, enableChallenge,
            imageProcessingMode);

        // 建立搜尋索引（精準模式用，快速模式也先建好以便之後切換）
        await _ragService.GetSearchEngine().EnsureIndexAsync(project.IndexName);
        return project;
    }

    public Task<List<RefineryProject>> ListAsync(string userId) =>
        _store.ListAsync(userId);

    public Task<RefineryProject?> GetAsync(string id) =>
        _store.GetAsync(id);

    public Task<RefineryProject?> UpdateAsync(string userId, string id, string name, string description,
        string? schemaTemplateId, string? customSchemaJson,
        string provider, string model, string? outputLanguage,
        string extractionMode = "fast", bool enableChallenge = false,
        string imageProcessingMode = "skip") =>
        _store.UpdateAsync(userId, id, name, description,
            schemaTemplateId, customSchemaJson, provider, model, outputLanguage, extractionMode, enableChallenge,
            imageProcessingMode);

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var result = await _store.DeleteAsync(userId, id);
        if (result)
        {
            // 嘗試刪除磁碟上的檔案目錄
            var dir = Path.Combine(FileStorageDir, id);
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (IOException ex)
                {
                    _logger?.LogWarning(ex, "Failed to delete refinery file directory: {Dir}", dir);
                }
            }
        }
        return result;
    }

    // ── File management ──

    public async IAsyncEnumerable<ExecutionEvent> AddFileAsync(
        string projectId,
        string fileName,
        string mimeType,
        byte[] fileData,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var project = await _store.GetAsync(projectId);
        if (project is null)
        {
            yield return ExecutionEvent.Error($"Project not found: {projectId}");
            yield break;
        }

        yield return ExecutionEvent.RagProcessing($"Cleaning {fileName}...");

        // Step 1: 清洗
        CleanedDocument? cleaned = null;
        string? cleanError = null;
        try
        {
            var cleaningOptions = new CleaningOptions
            {
                Partition = new PartitionOptions
                {
                    ImageMode = ParseImageMode(project.ImageProcessingMode),
                },
            };
            cleaned = await _cleaner.CleanAsync(fileData, fileName, mimeType, cleaningOptions, ct);
        }
        catch (NotSupportedException)
        {
            cleanError = $"Unsupported file format: {fileName}";
        }

        if (cleanError is not null)
        {
            yield return ExecutionEvent.Error(cleanError);
            yield break;
        }

        if (cleaned is null || cleaned.Elements.Count == 0)
        {
            yield return ExecutionEvent.Error($"No content extracted from {fileName}");
            yield break;
        }

        yield return ExecutionEvent.RagProcessing(
            $"Extracted {cleaned.Elements.Count} elements from {fileName}");

        // Step 2: 快取清洗結果 + 存檔
        var cleanedJson = SerializeElements(cleaned.Elements);
        var initialStatus = embeddingGenerator is not null ? "Pending" : "Skipped";
        var fileRecord = await _store.AddFileAsync(
            projectId, fileName, mimeType, fileData.Length, cleanedJson, cleaned.Elements.Count, initialStatus);

        var fileDir = Path.Combine(FileStorageDir, projectId);
        Directory.CreateDirectory(fileDir);
        var filePath = Path.Combine(fileDir, $"{fileRecord.Id}.bin");
        await File.WriteAllBytesAsync(filePath, fileData, ct);

        // Step 3: 建搜尋索引（如有 embeddingGenerator）
        if (embeddingGenerator is not null)
        {
            yield return ExecutionEvent.RagProcessing($"Indexing {fileName}...");
            await _store.UpdateFileIndexStatusAsync(fileRecord.Id, "Indexing");

            // 用 Channel 讓 embedding 進度能即時 yield 到 SSE
            var progressChannel = System.Threading.Channels.Channel.CreateUnbounded<string>();
            var indexTask = Task.Run(async () =>
            {
                var result = await BuildFileIndexAsync(
                    project.IndexName, fileName, cleaned.GetFullText(), embeddingGenerator, ct,
                    msg => progressChannel.Writer.TryWrite(msg));
                progressChannel.Writer.Complete();
                return result;
            }, ct);

            // 即時讀取 progress 並 yield（不等 indexTask 完成）
            await foreach (var msg in progressChannel.Reader.ReadAllAsync(ct))
            {
                yield return ExecutionEvent.RagProcessing(msg);
            }

            var (chunkIds, indexError) = await indexTask;

            if (indexError is not null)
            {
                await _store.UpdateFileIndexStatusAsync(fileRecord.Id, "Failed");
                yield return ExecutionEvent.RagProcessing($"Index failed for {fileName}: {indexError}");
            }
            else
            {
                await _store.UpdateFileIndexStatusAsync(fileRecord.Id, "Indexed",
                    string.Join(",", chunkIds), chunkIds.Count);
                yield return ExecutionEvent.RagProcessing($"Indexed {fileName}: {chunkIds.Count} chunks");
            }
        }

        // Step 4: 更新統計
        var files = await _store.ListFilesAsync(projectId);
        await _store.UpdateStatsAsync(projectId, files.Count);

        var typeSummary = cleaned.Elements
            .GroupBy(e => e.Type)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();

        yield return ExecutionEvent.RagReady(
            $"Cleaned {fileName}: {cleaned.Elements.Count} elements ({string.Join(", ", typeSummary)})");
    }

    public async Task<bool> RemoveFileAsync(string projectId, string fileId)
    {
        // 取得檔案資訊（需要 chunkIds 來刪除索引）
        var file = await _store.GetFileAsync(projectId, fileId);
        var result = await _store.RemoveFileAsync(projectId, fileId);
        if (result)
        {
            // 刪除磁碟檔案
            var filePath = Path.Combine(FileStorageDir, projectId, $"{fileId}.bin");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            // 刪除搜尋索引中的 chunks
            if (file is not null && !string.IsNullOrWhiteSpace(file.ChunkIds))
            {
                var project = await _store.GetAsync(projectId);
                if (project is not null && !string.IsNullOrWhiteSpace(project.IndexName))
                {
                    try
                    {
                        await _ragService.GetSearchEngine()
                            .DeleteDocumentsAsync(project.IndexName, file.GetChunkIds());
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to delete chunks for file {FileId}", fileId);
                    }
                }
            }

            var files = await _store.ListFilesAsync(projectId);
            await _store.UpdateStatsAsync(projectId, files.Count);
        }
        return result;
    }

    /// <summary>重新索引失敗的檔案（不需重新上傳/清洗）</summary>
    public async IAsyncEnumerable<ExecutionEvent> ReindexFileAsync(
        string projectId,
        string fileId,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var project = await _store.GetAsync(projectId);
        var file = await _store.GetFileAsync(projectId, fileId);

        if (project is null || file is null)
        {
            yield return ExecutionEvent.Error("Project or file not found");
            yield break;
        }

        var cleaned = DeserializeCleanedDocument(file.FileName, file.CleanedJson);
        if (cleaned is null)
        {
            yield return ExecutionEvent.Error("No cleaned data for this file");
            yield break;
        }

        yield return ExecutionEvent.RagProcessing($"Re-indexing {file.FileName}...");
        await _store.UpdateFileIndexStatusAsync(fileId, "Indexing");

        var (chunkIds, indexError) = await BuildFileIndexAsync(
            project.IndexName, file.FileName, cleaned.GetFullText(), embeddingGenerator, ct);

        if (indexError is not null)
        {
            await _store.UpdateFileIndexStatusAsync(fileId, "Failed");
            yield return ExecutionEvent.Error($"Re-index failed: {indexError}");
        }
        else
        {
            await _store.UpdateFileIndexStatusAsync(fileId, "Indexed",
                string.Join(",", chunkIds), chunkIds.Count);
            yield return ExecutionEvent.RagReady($"Re-indexed {file.FileName}: {chunkIds.Count} chunks");
        }
    }

    public Task<List<RefineryFile>> ListFilesAsync(string projectId) =>
        _store.ListFilesAsync(projectId);

    public async Task<string?> PreviewFileAsync(string projectId, string fileId)
    {
        var file = await _store.GetFileAsync(projectId, fileId);
        return file?.CleanedJson;
    }

    // ── Generate output ──

    public async IAsyncEnumerable<ExecutionEvent> GenerateOutputAsync(
        string projectId,
        string userId,
        ILlmProvider llmProvider,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var project = await _store.GetAsync(projectId);
        if (project is null)
        {
            yield return ExecutionEvent.Error($"Project not found: {projectId}");
            yield break;
        }

        var schema = ResolveSchema(project);
        if (schema is null)
        {
            yield return ExecutionEvent.Error("No schema configured. Set a template or custom schema in Settings.");
            yield break;
        }

        var files = await _store.ListFilesAsync(projectId);
        if (files.Count == 0)
        {
            yield return ExecutionEvent.Error("No files uploaded. Upload files first.");
            yield break;
        }

        yield return ExecutionEvent.RagProcessing(
            $"Loading {files.Count} cleaned files...");

        var includedFiles = files.Where(f => f.IsIncluded).ToList();
        if (includedFiles.Count < files.Count)
        {
            yield return ExecutionEvent.RagProcessing(
                $"Using {includedFiles.Count}/{files.Count} selected files");
        }

        var cleanedDocs = includedFiles
            .Select(f => DeserializeCleanedDocument(f.FileName, f.CleanedJson))
            .Where(d => d is not null)
            .Cast<CleanedDocument>()
            .ToList();

        if (cleanedDocs.Count == 0)
        {
            yield return ExecutionEvent.Error("No valid cleaned data found.");
            yield break;
        }

        var options = new SchemaMapperOptions
        {
            OutputLanguage = project.OutputLanguage,
            IncludeSourceReferences = true,
            EnableChallenge = project.EnableChallenge,
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        SchemaMapperResult result;

        if (project.ExtractionMode == "precise" && embeddingGenerator is not null)
        {
            // ── 精準模式：多層 Agent + Search（使用已建好的索引）──
            var indexedCount = files.Count(f => f.IndexStatus == "Indexed");
            var totalFiles = files.Count;

            if (indexedCount == 0)
            {
                yield return ExecutionEvent.Error("No indexed files. Upload files first or retry failed indexing.");
                yield break;
            }

            if (indexedCount < totalFiles)
            {
                yield return ExecutionEvent.RagProcessing(
                    $"Warning: {indexedCount}/{totalFiles} files indexed. Using indexed files only.");
            }

            yield return ExecutionEvent.RagProcessing(
                $"Precise mode: {indexedCount} indexed files → {schema.Name}");

            SearchCallback searchCallback = async (query, topK, cancelToken) =>
            {
                var chunks = await _ragService.SearchAsync(
                    query, topK, embeddingGenerator, project.IndexName, cancellationToken: cancelToken);
                return chunks.Select(c => c.Content).ToList();
            };

            var progressMessages = new List<string>();
            var mapper = new MultiLayerSchemaMapper(llmProvider, searchCallback,
                msg => progressMessages.Add(msg));
            result = await mapper.MapAsync(cleanedDocs, schema, options, ct);

            // 回傳所有 progress messages
            foreach (var msg in progressMessages)
            {
                yield return ExecutionEvent.RagProcessing(msg);
            }
        }
        else
        {
            // ── 快速模式：一次性 LLM ──
            yield return ExecutionEvent.RagProcessing(
                $"Fast mode: {cleanedDocs.Count} files → {schema.Name}");

            var progressMessages = new List<string>();
            var mapper = new LlmSchemaMapper(llmProvider, msg => progressMessages.Add(msg));
            result = await mapper.MapAsync(cleanedDocs, schema, options, ct);

            foreach (var msg in progressMessages)
            {
                yield return ExecutionEvent.RagProcessing(msg);
            }
        }

        sw.Stop();

        // Render Markdown
        var renderer = new MarkdownRenderer();
        var markdown = await renderer.RenderAsync(result.Json, schema, ct);

        // Save output version
        var latest = await _store.GetLatestOutputAsync(projectId);
        var nextVersion = (latest?.Version ?? 0) + 1;

        var missingJson = JsonSerializer.Serialize(result.MissingFields);
        var questionsJson = JsonSerializer.Serialize(result.OpenQuestions);
        var challengesJson = JsonSerializer.Serialize(result.Challenges,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
        var sourceFilesJson = JsonSerializer.Serialize(cleanedDocs.Select(d => d.FileName).ToList());

        await _store.AddOutputAsync(
            projectId, nextVersion,
            project.SchemaTemplateId, schema.Name,
            result.Json, markdown,
            missingJson, questionsJson,
            challengesJson, result.OverallConfidence,
            sourceFilesJson, cleanedDocs.Count);

        var mode = project.ExtractionMode == "precise" ? "Precise" : "Fast";
        var elapsed = sw.Elapsed.TotalSeconds;
        var tokenInfo = result.TotalTokens > 0
            ? $" | {result.TotalInputTokens:N0} in + {result.TotalOutputTokens:N0} out = {result.TotalTokens:N0} tokens"
            : "";
        yield return ExecutionEvent.RagReady(
            $"✅ Generated v{nextVersion} ({mode}) | {elapsed:F1}s{tokenInfo}");
    }

    // ── Output retrieval ──

    public Task<List<RefineryOutput>> ListOutputsAsync(string projectId) =>
        _store.ListOutputsAsync(projectId);

    public Task<RefineryOutput?> GetOutputAsync(string projectId, int version) =>
        _store.GetOutputAsync(projectId, version);

    public Task<RefineryOutput?> GetLatestOutputAsync(string projectId) =>
        _store.GetLatestOutputAsync(projectId);

    // ── Cleanup ──

    public async Task CleanupDeletedAsync()
    {
        try
        {
            var pending = await _store.GetPendingDeletionsAsync(TimeSpan.FromHours(24));
            foreach (var project in pending)
            {
                var dir = Path.Combine(FileStorageDir, project.Id);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }

                await _store.HardDeleteAsync(project.Id);
                _logger?.LogInformation("Hard deleted refinery project: {Id}", project.Id);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Refinery cleanup failed");
        }
    }

    // ── Helpers ──

    /// <summary>共用索引邏輯 — AddFileAsync 和 ReindexFileAsync 都用</summary>
    private async Task<(List<string> ChunkIds, string? Error)> BuildFileIndexAsync(
        string indexName, string fileName, string text,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        CancellationToken ct,
        Action<string>? onProgress = null)
    {
        try
        {
            var chunks = _ragService.GetChunker().Chunk(text, PreciseModeChunkSize, PreciseModeChunkOverlap);
            var chunkTexts = chunks.Select(c => c.Text).ToList();
            var searchDocs = new List<Search.Abstractions.SearchDocument>();
            var chunkIds = new List<string>();

            var totalBatches = (chunkTexts.Count + EmbeddingBatchSize - 1) / EmbeddingBatchSize;
            var batchNum = 0;

            for (var i = 0; i < chunkTexts.Count; i += EmbeddingBatchSize)
            {
                batchNum++;
                var batch = chunkTexts.Skip(i).Take(EmbeddingBatchSize).ToList();
                onProgress?.Invoke($"Embedding {fileName}: batch {batchNum}/{totalBatches} ({chunkIds.Count + batch.Count}/{chunkTexts.Count} chunks)");

                // Retry with delay for rate limit (429)
                GeneratedEmbeddings<Embedding<float>>? embeddings = null;
                for (var attempt = 0; attempt < EmbeddingRetryCount; attempt++)
                {
                    try
                    {
                        embeddings = await embeddingGenerator.GenerateAsync(batch, cancellationToken: ct);
                        break;
                    }
                    catch (Exception ex) when (ex.Message.Contains("429") || ex.Message.Contains("RateLimitReached"))
                    {
                        if (attempt < EmbeddingRetryCount - 1)
                        {
                            var waitSec = EmbeddingRetryDelayMs * (attempt + 1) / 1000;
                            onProgress?.Invoke($"Rate limited, retrying in {waitSec}s...");
                            await Task.Delay(EmbeddingRetryDelayMs * (attempt + 1), ct);
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                for (var j = 0; j < batch.Count; j++)
                {
                    var docId = Guid.NewGuid().ToString("N");
                    chunkIds.Add(docId);
                    searchDocs.Add(new Search.Abstractions.SearchDocument
                    {
                        Id = docId,
                        Content = batch[j],
                        Vector = embeddings![j].Vector,
                        FileName = fileName,
                        ChunkIndex = i + j,
                    });
                }

                // 批次間延遲，避免連續請求觸發 rate limit
                if (i + EmbeddingBatchSize < chunkTexts.Count)
                {
                    await Task.Delay(200, ct);
                }
            }

            await _ragService.GetSearchEngine().IndexDocumentsAsync(indexName, searchDocs, ct);
            return (chunkIds, null);
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }
    }

    private SchemaDefinition? ResolveSchema(RefineryProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.SchemaTemplateId))
        {
            return _templateProvider.GetTemplate(project.SchemaTemplateId);
        }

        if (!string.IsNullOrWhiteSpace(project.CustomSchemaJson))
        {
            try
            {
                var custom = JsonSerializer.Deserialize<CustomSchemaDto>(project.CustomSchemaJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (custom is not null)
                {
                    return new SchemaDefinition
                    {
                        Name = custom.Name ?? "Custom Schema",
                        Description = custom.Description ?? "",
                        JsonSchema = custom.JsonSchema ?? "{}",
                        ExtractionGuidance = custom.ExtractionGuidance,
                    };
                }
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "Failed to parse custom schema for project {Id}", project.Id);
            }
        }

        return null;
    }

    private static string SerializeElements(IReadOnlyList<DocumentElement> elements)
    {
        var simplified = elements.Select(e => new
        {
            type = e.Type.ToString(),
            text = e.Text,
            fileName = e.FileName,
            pageNumber = e.PageNumber,
            index = e.Index,
            metadata = e.Metadata,
        });
        return JsonSerializer.Serialize(simplified);
    }

    private static CleanedDocument? DeserializeCleanedDocument(string fileName, string json)
    {
        try
        {
            var elements = JsonSerializer.Deserialize<List<SerializedElement>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (elements is null || elements.Count == 0)
            {
                return null;
            }

            return new CleanedDocument
            {
                FileName = fileName,
                Elements = elements.Select((e, i) => new DocumentElement
                {
                    Type = Enum.TryParse<ElementType>(e.Type, true, out var t) ? t : ElementType.UncategorizedText,
                    Text = e.Text ?? "",
                    FileName = e.FileName ?? fileName,
                    PageNumber = e.PageNumber,
                    Index = e.Index ?? i,
                    Metadata = e.Metadata ?? [],
                }).ToList(),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record CustomSchemaDto(string? Name, string? Description, string? JsonSchema, string? ExtractionGuidance);

    private sealed class SerializedElement
    {
        public string? Type { get; set; }
        public string? Text { get; set; }
        public string? FileName { get; set; }
        public int? PageNumber { get; set; }
        public int? Index { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    private static ImageProcessingMode ParseImageMode(string mode) => mode switch
    {
        "ocr" => ImageProcessingMode.Ocr,
        "ai-describe" => ImageProcessingMode.AiDescribe,
        "hybrid" => ImageProcessingMode.Hybrid,
        _ => ImageProcessingMode.Skip,
    };
}
