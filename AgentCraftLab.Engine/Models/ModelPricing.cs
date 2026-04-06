namespace AgentCraftLab.Engine.Models;

/// <summary>
/// 模型成本估算 — 根據 token 數量計算 USD 費用。
/// 價格為 blended rate（input+output 加權平均，假設 40% input / 60% output）每 1M tokens。
/// 資料來源：各廠商官方定價頁面。
/// 最後更新：2026-03。
/// 更新方式：WebFetch OpenAI/Anthropic pricing page 後同步此檔案。
/// </summary>
public static class ModelPricing
{
    /// <summary>
    /// Blended rate per 1M tokens (USD)。
    /// 計算方式：input_price * 0.4 + output_price * 0.6
    /// </summary>
    private static readonly Dictionary<string, decimal> PricePerMillionTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI（來源：https://openai.com/api/pricing/）
        // gpt-4o: $2.50 input / $10.00 output → blended $7.00
        ["gpt-4o"] = 7.00m,
        // gpt-4o-mini: $0.15 input / $0.60 output → blended $0.42
        ["gpt-4o-mini"] = 0.42m,
        // gpt-4.1: $2.00 input / $8.00 output → blended $5.60
        ["gpt-4.1"] = 5.60m,
        // gpt-4.1-mini: $0.40 input / $1.60 output → blended $1.12
        ["gpt-4.1-mini"] = 1.12m,
        // gpt-4.1-nano: $0.10 input / $0.40 output → blended $0.28
        ["gpt-4.1-nano"] = 0.28m,
        // o3-mini: $1.10 input / $4.40 output → blended $3.08
        ["o3-mini"] = 3.08m,

        // Anthropic Claude（來源：https://platform.claude.com/docs/en/about-claude/pricing）
        // claude-sonnet-4: $3 input / $15 output → blended $10.20
        ["claude-sonnet-4-20250514"] = 10.20m,
        // claude-opus-4: $5 input / $25 output → blended $17.00
        ["claude-opus-4-20250514"] = 17.00m,
        // claude-haiku-4.5: $1 input / $5 output → blended $3.40
        ["claude-haiku-4-5-20251001"] = 3.40m,

        // AWS Bedrock — Anthropic（價格與直連相同）
        ["anthropic.claude-sonnet-4-20250514-v1:0"] = 10.20m,
        ["anthropic.claude-opus-4-20250514-v1:0"] = 17.00m,
        ["amazon.nova-pro-v1:0"] = 1.60m,

        // Ollama（本地模型，零成本）
        ["llama3.3"] = 0m,
        ["phi4"] = 0m,
        ["mistral"] = 0m,
        ["gemma2"] = 0m,
        ["qwen2.5"] = 0m,
        ["deepseek-r1"] = 0m,
    };

    /// <summary>找不到模型時的 fallback 費率（保守估計）</summary>
    private const decimal FallbackRate = 2.00m;

    /// <summary>
    /// 根據模型和 token 數估算 USD 成本。
    /// </summary>
    public static decimal EstimateCost(string model, long totalTokens)
    {
        var rate = PricePerMillionTokens.GetValueOrDefault(model, FallbackRate);
        return rate * totalTokens / 1_000_000m;
    }

    /// <summary>
    /// 格式化成本為字串（例如 "$0.0042"）。
    /// </summary>
    public static string FormatCost(decimal cost)
    {
        return cost < 0.01m ? $"${cost:F4}" : $"${cost:F2}";
    }

    /// <summary>
    /// 從字元數估算 token 數。
    /// ASCII 字元約 4 字元 = 1 token；CJK 字元約 1 字元 = 1.5 tokens。
    /// </summary>
    public static long EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        long asciiChars = 0, cjkChars = 0;
        foreach (var c in text)
        {
            if (c <= 0x7F)
                asciiChars++;
            else
                cjkChars++;
        }

        // ASCII: ~0.25 tokens/char, CJK: ~1.5 tokens/char
        return (asciiChars / 4) + (cjkChars * 3 / 2);
    }
}
