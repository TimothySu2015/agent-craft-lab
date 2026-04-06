using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// RAG Context Compression — 當 RAG context 超過 token budget 時壓縮。
/// 內部委派給 <see cref="IContextCompactor"/>，外部 API 完全不變（向下相容）。
/// </summary>
public class ContextCompressor
{
    private readonly IContextCompactor _compactor;
    private readonly ILogger<ContextCompressor>? _logger;

    /// <summary>透過 IContextCompactor 建構（推薦）。</summary>
    public ContextCompressor(IContextCompactor compactor, ILogger<ContextCompressor>? logger = null)
    {
        _compactor = compactor;
        _logger = logger;
    }

    /// <summary>透過 IChatClient 建構（向下相容，內部建立 LlmContextCompactor）。</summary>
    public ContextCompressor(IChatClient chatClient, ILogger<ContextCompressor>? logger = null)
    {
        _compactor = new LlmContextCompactor(chatClient);
        _logger = logger;
    }

    /// <summary>
    /// 如果 chunks 的總 token 超過 budget，用 LLM 摘要壓縮。
    /// </summary>
    public async Task<string?> CompressIfNeededAsync(
        string query,
        List<RagChunk> chunks,
        int tokenBudget,
        CancellationToken ct = default)
    {
        // 串接 chunks 為 string（保留來源標注），由 IContextCompactor 統一判斷是否需要壓縮
        var content = FormatChunks(chunks);

        var compressed = await _compactor.CompressAsync(content, query, tokenBudget, ct);
        if (compressed is not null)
        {
            _logger?.LogInformation(
                "[ContextCompressor] Compressed → ~{CompressedTokens} tokens",
                ModelPricing.EstimateTokens(compressed));
        }

        return compressed;
    }

    /// <summary>將 RAG chunks 串接為可壓縮的文字（保留來源標注）。</summary>
    private static string FormatChunks(List<RagChunk> chunks)
    {
        return string.Join("\n\n---\n\n", chunks.Select(c =>
        {
            var source = !string.IsNullOrEmpty(c.FileName) ? $"[{c.FileName}]" : "";
            return $"{source}\n{c.Content}";
        }));
    }
}
