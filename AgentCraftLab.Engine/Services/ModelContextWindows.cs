namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 模型 Context Window 大小對照表 — 用於動態設定壓縮門檻。
/// 資料來源：https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure
/// 查表命中：壓縮門檻 = contextWindow × CompressionRatio。
/// 查表未命中：使用 fallback 值。
/// </summary>
public static class ModelContextWindows
{
    /// <summary>壓縮觸發比率（參考 Claude Code 的 83.5%，保守取 80%）。</summary>
    public const double CompressionRatio = 0.80;

    /// <summary>模型名稱前綴 → context window（input tokens）。從長前綴到短前綴排列，確保精確匹配優先。</summary>
    private static readonly (string Prefix, int Tokens)[] KnownModels =
    [
        // ═══════════════════════════════════════════
        // OpenAI — GPT 系列
        // ═══════════════════════════════════════════
        ("gpt-5.4-mini", 272_000),
        ("gpt-5.4-nano", 272_000),
        ("gpt-5.4-pro", 1_050_000),
        ("gpt-5.4", 1_050_000),
        ("gpt-5.3-codex", 272_000),
        ("gpt-5.2-codex", 272_000),
        ("gpt-5.2-chat", 111_616),
        ("gpt-5.2", 272_000),
        ("gpt-5.1-codex-mini", 272_000),
        ("gpt-5.1-codex-max", 272_000),
        ("gpt-5.1-codex", 272_000),
        ("gpt-5.1-chat", 111_616),
        ("gpt-5.1", 272_000),
        ("gpt-5-codex", 272_000),
        ("gpt-5-chat", 128_000),
        ("gpt-5-mini", 272_000),
        ("gpt-5-nano", 272_000),
        ("gpt-5-pro", 272_000),
        ("gpt-5", 272_000),
        ("gpt-4.1-mini", 1_047_576),
        ("gpt-4.1-nano", 1_047_576),
        ("gpt-4.1", 1_047_576),
        ("gpt-4o-mini", 128_000),
        ("gpt-4o", 128_000),
        ("gpt-4-turbo", 128_000),
        ("gpt-4", 128_000),
        ("gpt-3.5-turbo", 16_385),
        ("gpt-oss-120b", 131_072),
        ("gpt-oss-20b", 131_072),

        // OpenAI — o 系列（reasoning）
        ("o4-mini", 200_000),
        ("o3-pro", 200_000),
        ("o3-mini", 200_000),
        ("o3", 200_000),
        ("o1-preview", 128_000),
        ("o1-mini", 128_000),
        ("o1", 200_000),

        // OpenAI — Codex
        ("codex-mini", 200_000),

        // ═══════════════════════════════════════════
        // Anthropic Claude
        // ═══════════════════════════════════════════
        ("claude-opus", 200_000),
        ("claude-sonnet", 200_000),
        ("claude-haiku", 200_000),
        ("claude-4", 200_000),
        ("claude-3.5", 200_000),
        ("claude-3", 200_000),

        // ═══════════════════════════════════════════
        // Google Gemini
        // ═══════════════════════════════════════════
        ("gemini-2.5-pro", 1_048_576),
        ("gemini-2.5-flash", 1_048_576),
        ("gemini-2.0", 1_048_576),
        ("gemini-1.5-pro", 2_097_152),
        ("gemini-1.5-flash", 1_048_576),

        // ═══════════════════════════════════════════
        // DeepSeek
        // ═══════════════════════════════════════════
        ("deepseek-r1", 163_840),
        ("deepseek-v3.2", 128_000),
        ("deepseek-v3.1", 131_072),
        ("deepseek-v3", 131_072),
        ("deepseek-chat", 128_000),

        // ═══════════════════════════════════════════
        // Meta Llama
        // ═══════════════════════════════════════════
        ("llama-4-maverick", 1_000_000),
        ("llama-4", 1_000_000),
        ("llama-3.3", 128_000),
        ("llama-3.2", 128_000),
        ("llama-3.1", 128_000),

        // ═══════════════════════════════════════════
        // Mistral
        // ═══════════════════════════════════════════
        ("mistral-large-3", 128_000),
        ("mistral-large", 128_000),
        ("mistral-small", 128_000),

        // ═══════════════════════════════════════════
        // Cohere
        // ═══════════════════════════════════════════
        ("cohere-command-a", 131_072),

        // ═══════════════════════════════════════════
        // xAI Grok
        // ═══════════════════════════════════════════
        ("grok-4.1", 128_000),
        ("grok-4", 262_000),
        ("grok-code", 256_000),
        ("grok-3-mini", 131_072),
        ("grok-3", 131_072),

        // ═══════════════════════════════════════════
        // Moonshot AI
        // ═══════════════════════════════════════════
        ("kimi-k2", 262_144),

        // ═══════════════════════════════════════════
        // Microsoft
        // ═══════════════════════════════════════════
        ("mai-ds-r1", 163_840),
    ];

    /// <summary>
    /// 查詢模型的 context window 大小。回傳 input token 數，或 null 表示未知模型。
    /// 使用前綴匹配，支援 Azure 部署名稱（如 "gpt-4o-2024-08-06"）和版本後綴。
    /// </summary>
    public static int? GetContextWindow(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return null;
        }

        var lower = modelName.ToLowerInvariant();
        foreach (var (prefix, tokens) in KnownModels)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                return tokens;
            }
        }

        // fallback：包含匹配（處理 Azure 部署名如 "my-gpt-4o-deployment"）
        foreach (var (prefix, tokens) in KnownModels)
        {
            if (lower.Contains(prefix, StringComparison.Ordinal))
            {
                return tokens;
            }
        }

        return null;
    }

    /// <summary>
    /// 計算壓縮觸發門檻（token 數）。
    /// 查表命中：contextWindow × 80%。
    /// 未命中：使用 fallback 值。
    /// </summary>
    public static long GetCompressionThreshold(string modelName, long fallbackTokens = 30_000)
    {
        var contextWindow = GetContextWindow(modelName);
        return contextWindow.HasValue
            ? (long)(contextWindow.Value * CompressionRatio)
            : fallbackTokens;
    }
}
