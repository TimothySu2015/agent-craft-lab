using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// Query Expansion — 用 LLM 生成查詢變體，提升 RAG 召回率。
/// 可選注入，未注入時不做擴展。
/// </summary>
public class QueryExpander
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<QueryExpander>? _logger;

    public QueryExpander(IChatClient chatClient, ILogger<QueryExpander>? logger = null)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    /// <summary>
    /// 根據原始查詢生成 2 個語意相近但措辭不同的查詢變體。
    /// </summary>
    public async Task<List<string>> ExpandAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var prompt = $"""
                Given this search query, generate 2 alternative phrasings that capture the same intent but use different words or language.
                If the query is in Chinese, include one English variant and one Chinese rephrasing.
                If the query is in English, include one synonym-based variant and one more specific variant.

                Query: {query}

                Respond with ONLY a JSON array of 2 strings. Example: ["variant 1", "variant 2"]
                """;

            var response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { Temperature = 0.3f },
                ct);

            var text = response.Text ?? "";
            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');
            if (start >= 0 && end > start)
            {
                var variants = JsonSerializer.Deserialize<List<string>>(text[start..(end + 1)]);
                if (variants is { Count: > 0 })
                {
                    return variants.Take(2).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "[QueryExpander] Failed to expand query, using original only");
        }

        return [];
    }
}
