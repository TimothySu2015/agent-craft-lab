using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentCraftLab.Search.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Search.Reranking;

/// <summary>
/// Cohere Rerank API 實作 — 使用專用 cross-encoder 模型重排序，效果最佳。
/// 需要 Cohere API Key。
/// </summary>
public class CohereReranker : IReranker
{
    private const int MaxDocumentLength = 4096;

    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _endpoint;
    private readonly ILogger<CohereReranker>? _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <param name="httpClient">已設定 Authorization header 的 HttpClient。</param>
    /// <param name="model">Rerank 模型名稱（預設 rerank-v3.5）。</param>
    /// <param name="endpoint">Rerank API endpoint（預設 Cohere 官方，可替換為代理或自建端點）。</param>
    /// <param name="logger">Logger。</param>
    public CohereReranker(
        HttpClient httpClient,
        string model = "rerank-v3.5",
        string endpoint = "https://api.cohere.com/v2/rerank",
        ILogger<CohereReranker>? logger = null)
    {
        _httpClient = httpClient;
        _model = model;
        _endpoint = endpoint;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchResult>> RerankAsync(
        string query,
        IReadOnlyList<SearchResult> results,
        int topK,
        CancellationToken ct = default)
    {
        if (results.Count <= 1)
        {
            return results;
        }

        try
        {
            var request = new CohereRerankRequest
            {
                Model = _model,
                Query = query,
                Documents = results.Select(r => Truncate(r.Content, MaxDocumentLength)).ToList(),
                TopN = topK
            };

            var response = await _httpClient.PostAsJsonAsync(
                _endpoint, request, JsonOptions, ct);

            response.EnsureSuccessStatusCode();

            var cohereResponse = await response.Content.ReadFromJsonAsync<CohereRerankResponse>(JsonOptions, ct);

            if (cohereResponse?.Results is null)
            {
                _logger?.LogWarning("[Reranker] Cohere 回應為空，使用原始排序");
                return results.Take(topK).ToList();
            }

            return cohereResponse.Results
                .OrderByDescending(r => r.RelevanceScore)
                .Where(r => r.Index < results.Count)
                .Select(r => new SearchResult
                {
                    Id = results[r.Index].Id,
                    Score = r.RelevanceScore,
                    Content = results[r.Index].Content,
                    FileName = results[r.Index].FileName,
                    ChunkIndex = results[r.Index].ChunkIndex,
                    Metadata = results[r.Index].Metadata
                })
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "[Reranker] Cohere rerank failed, returning original order");
            return results.Take(topK).ToList();
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private sealed class CohereRerankRequest
    {
        public string Model { get; init; } = "";
        public string Query { get; init; } = "";
        public List<string> Documents { get; init; } = [];
        public int TopN { get; init; }
    }

    private sealed class CohereRerankResponse
    {
        public List<CohereRerankResult>? Results { get; init; }
    }

    private sealed class CohereRerankResult
    {
        public int Index { get; init; }
        public float RelevanceScore { get; init; }
    }
}
