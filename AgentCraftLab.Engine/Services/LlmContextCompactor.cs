using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 基於 LLM 的上下文壓縮實作 — 當 content 超過 tokenBudget 時用 LLM 摘要壓縮。
/// 不超過 budget 就不壓，避免不必要的延遲和成本。
/// </summary>
public sealed class LlmContextCompactor : IContextCompactor
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<LlmContextCompactor>? _logger;

    public LlmContextCompactor(IChatClient chatClient, ILogger<LlmContextCompactor>? logger = null)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string?> CompressAsync(string content, string context, int tokenBudget, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var currentTokens = ModelPricing.EstimateTokens(content);
        if (currentTokens <= tokenBudget)
        {
            return null; // 不需壓縮
        }

        try
        {
            var prompt = BuildCompressionPrompt(content, context, tokenBudget);
            var response = await _chatClient.GetResponseAsync(
                prompt, new ChatOptions { Temperature = 0f }, ct);

            var compressed = response.Text;
            if (string.IsNullOrWhiteSpace(compressed))
            {
                return null;
            }

            var compressedTokens = ModelPricing.EstimateTokens(compressed);
            _logger?.LogInformation(
                "[Compactor] Compressed {OriginalTokens} → {CompressedTokens} tokens (budget: {Budget})",
                currentTokens, compressedTokens, tokenBudget);

            return compressed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "[Compactor] Compression failed, returning null");
            return null;
        }
    }

    private static List<ChatMessage> BuildCompressionPrompt(string content, string context, int tokenBudget)
    {
        var systemPrompt = """
            You are a context compression assistant. Your task is to create a concise summary
            that preserves all information relevant to the given context/query.
            Keep important facts, numbers, tool call results, and key findings.
            Use bullet points for structured data. Be brief but preserve all critical details.
            Output in the same language as the content.
            """;

        var userPrompt = $"""
            Context/Query: {context}

            Content to compress (target: ~{tokenBudget} tokens):

            {content}

            Compressed summary:
            """;

        return
        [
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userPrompt)
        ];
    }
}
