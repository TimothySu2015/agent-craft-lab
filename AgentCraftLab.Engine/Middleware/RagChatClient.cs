using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// 在每次 LLM 呼叫前，自動搜尋相關文件片段並注入 system context。
/// 支援臨時上傳索引 + 知識庫索引的多索引並行搜尋。
/// </summary>
public class RagChatClient : DelegatingChatClient
{
    private readonly RagService _ragService;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly string _indexName;
    private readonly List<string> _knowledgeBaseIndexNames;
    private readonly Dictionary<string, string?> _indexDataSourceMap;
    private readonly int _topK;
    private readonly RagSearchOptions _searchOptions;
    private readonly Action<List<RagChunk>>? _onCitationsFound;
    private readonly ILogger<RagChatClient>? _logger;

    public RagChatClient(
        IChatClient innerClient,
        RagService ragService,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        string indexName,
        int topK,
        List<string>? knowledgeBaseIndexNames = null,
        ILogger<RagChatClient>? logger = null,
        RagSearchOptions? searchOptions = null,
        Action<List<RagChunk>>? onCitationsFound = null,
        Dictionary<string, string?>? indexDataSourceMap = null) : base(innerClient)
    {
        _ragService = ragService;
        _embeddingGenerator = embeddingGenerator;
        _indexName = indexName;
        _topK = topK;
        _knowledgeBaseIndexNames = knowledgeBaseIndexNames ?? [];
        _indexDataSourceMap = indexDataSourceMap ?? new();
        _logger = logger;
        _searchOptions = searchOptions ?? new RagSearchOptions();
        _onCitationsFound = onCitationsFound;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var enriched = await EnrichWithRagContext(messages, cancellationToken);
        return await base.GetResponseAsync(enriched, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enriched = await EnrichWithRagContext(messages, cancellationToken);
        await foreach (var update in base.GetStreamingResponseAsync(enriched, options, cancellationToken))
        {
            yield return update;
        }
    }

    private async Task<List<ChatMessage>> EnrichWithRagContext(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var msgList = messages.ToList();
        var lastUserMsg = msgList.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
        if (string.IsNullOrWhiteSpace(lastUserMsg))
        {
            return msgList;
        }

        try
        {
            // 收集所有要搜尋的索引（每個索引根據 DataSourceId 路由到對應搜尋引擎）
            var searchTasks = new List<Task<List<RagChunk>>>();

            if (!string.IsNullOrEmpty(_indexName))
            {
                _indexDataSourceMap.TryGetValue(_indexName, out var dsId);
                searchTasks.Add(_ragService.SearchAsync(
                    lastUserMsg, _topK, _embeddingGenerator, _indexName,
                    _searchOptions, cancellationToken, dsId));
            }

            foreach (var kbIndexName in _knowledgeBaseIndexNames)
            {
                _indexDataSourceMap.TryGetValue(kbIndexName, out var dsId);
                searchTasks.Add(_ragService.SearchAsync(
                    lastUserMsg, _topK, _embeddingGenerator, kbIndexName,
                    _searchOptions, cancellationToken, dsId));
            }

            if (searchTasks.Count == 0)
            {
                return msgList;
            }

            // 並行搜尋所有索引，依 Content 去重保留最高 Score
            var results = await Task.WhenAll(searchTasks);
            var allChunks = results
                .SelectMany(r => r)
                .GroupBy(c => c.Content)
                .Select(g => g.OrderByDescending(c => c.Score).First())
                .OrderByDescending(c => c.Score)
                .Take(_topK)
                .ToList();

            if (allChunks.Count == 0)
            {
                return msgList;
            }

            // 通知呼叫方找到的引用來源（保持原始排序供 Sources tab 顯示）
            _onCitationsFound?.Invoke(allChunks);

            // Reorder：最高分放頭尾，低分放中間（解決 Lost in the Middle 問題）
            if (allChunks.Count > 2)
            {
                allChunks = ReorderForAttention(allChunks);
            }

            // Context Compression：超過 token budget 時用 LLM 摘要壓縮
            string context;
            if (_searchOptions.ContextCompression && _searchOptions.ContextCompressor is not null)
            {
                var compressed = await _searchOptions.ContextCompressor.CompressIfNeededAsync(
                    lastUserMsg, allChunks, _searchOptions.TokenBudget, cancellationToken);
                if (compressed is not null)
                {
                    context = compressed;
                }
                else
                {
                    context = BuildContextString(allChunks);
                }
            }
            else
            {
                context = BuildContextString(allChunks);
            }

            var ragMessage = new ChatMessage(ChatRole.System,
                $"[RAG Context]\nThe following document excerpts may be relevant to the user's question.\nWhen citing information, reference the source document name.\n\n{context}\n[End RAG Context]");

            var enriched = new List<ChatMessage> { ragMessage };
            enriched.AddRange(msgList);
            return enriched;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RAG] Search failed, returning original messages: {Error}", ex.Message);
            return msgList;
        }
    }

    private static string BuildContextString(List<RagChunk> chunks)
    {
        var contextParts = chunks.Select(c =>
        {
            var sourceParts = new List<string>();
            if (!string.IsNullOrEmpty(c.FileName))
            {
                sourceParts.Add(c.FileName);
            }

            if (c.Metadata?.TryGetValue("title", out var title) == true && !string.IsNullOrEmpty(title))
            {
                sourceParts.Add($"Title: {title}");
            }

            sourceParts.Add($"Section {c.ChunkIndex + 1}");

            var source = sourceParts.Count > 0
                ? $"[Source: {string.Join(" | ", sourceParts)}]\n"
                : "";
            return $"{source}{c.Content}";
        });
        return string.Join("\n\n---\n\n", contextParts);
    }

    /// <summary>
    /// Lost in the Middle 重排序 — 最高分放頭尾（LLM 注意力最強），低分放中間。
    /// 輸入：[1st, 2nd, 3rd, 4th, 5th]（依分數高到低）
    /// 輸出：[1st, 3rd, 5th, 4th, 2nd]（頭尾高分，中間低分）
    /// </summary>
    private static List<RagChunk> ReorderForAttention(List<RagChunk> chunks)
    {
        // 輸入已依分數高到低排序：[1st, 2nd, 3rd, 4th, 5th]
        // 輸出：頭尾放高分，中間放低分
        // 策略：奇數 rank (1st, 3rd, 5th) 從頭開始放，偶數 rank (2nd, 4th) 從尾開始放
        var result = new RagChunk[chunks.Count];
        int head = 0;
        int tail = chunks.Count - 1;

        for (int i = 0; i < chunks.Count; i++)
        {
            if (i % 2 == 0)
            {
                result[head++] = chunks[i];
            }
            else
            {
                result[tail--] = chunks[i];
            }
        }

        return result.ToList();
    }
}
