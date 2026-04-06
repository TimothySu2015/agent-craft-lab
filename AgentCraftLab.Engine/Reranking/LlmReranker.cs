using System.Text.Json;
using AgentCraftLab.Search.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Reranking;

/// <summary>
/// 用 LLM 對搜尋結果進行 pointwise 相關性評分並重排序。
/// 不需要額外 API Key，直接使用現有 ChatClient。
/// 放在 Engine 層（而非 Search）因為依賴 Microsoft.Extensions.AI 的 IChatClient。
/// </summary>
public class LlmReranker : IReranker
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<LlmReranker>? _logger;

    public LlmReranker(IChatClient chatClient, ILogger<LlmReranker>? logger = null)
    {
        _chatClient = chatClient;
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
            var prompt = BuildRerankPrompt(query, results);
            var response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { Temperature = 0f },
                ct);

            var scores = ParseScores(response.Text, results.Count);

            if (scores is null || scores.Count != results.Count)
            {
                _logger?.LogWarning("[Reranker] LLM 回應格式不正確，使用原始排序");
                return results.Take(topK).ToList();
            }

            return results
                .Select((r, i) => (Result: r, RelevanceScore: scores[i]))
                .OrderByDescending(x => x.RelevanceScore)
                .Take(topK)
                .Select(x => new SearchResult
                {
                    Id = x.Result.Id,
                    Score = x.RelevanceScore,
                    Content = x.Result.Content,
                    FileName = x.Result.FileName,
                    ChunkIndex = x.Result.ChunkIndex,
                    Metadata = x.Result.Metadata
                })
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "[Reranker] LLM rerank failed, returning original order");
            return results.Take(topK).ToList();
        }
    }

    private static string BuildRerankPrompt(string query, IReadOnlyList<SearchResult> results)
    {
        var docs = string.Join("\n\n", results.Select((r, i) =>
            $"[Document {i}]\n{Truncate(r.Content, 500)}"));

        return $"""
            You are a relevance scoring system. Given a query and a list of documents,
            score each document's relevance to the query on a scale of 0.0 to 1.0.

            Query: {query}

            Documents:
            {docs}

            Respond with ONLY a JSON array of numbers (scores for each document in order).
            Example: [0.9, 0.3, 0.7]
            """;
    }

    private static List<float>? ParseScores(string? responseText, int expectedCount)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        var start = responseText.IndexOf('[');
        var end = responseText.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return null;
        }

        var jsonArray = responseText[start..(end + 1)];

        try
        {
            var scores = JsonSerializer.Deserialize<List<float>>(jsonArray);
            if (scores is null || scores.Count != expectedCount)
            {
                return null;
            }

            return scores.Select(s => Math.Clamp(s, 0f, 1f)).ToList();
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
