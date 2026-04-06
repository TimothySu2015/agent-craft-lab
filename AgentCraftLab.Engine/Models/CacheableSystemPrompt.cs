namespace AgentCraftLab.Engine.Models;

/// <summary>
/// 可快取的系統提示詞 — 將 system prompt 拆分為靜態和動態兩部分。
/// 靜態部分（技能指南、行為規範）可跨 session 緩存，動態部分（日期、記憶、RAG context）每輪重算。
/// 透過 API 的 prefix caching 機制，最大化緩存命中率以降低 token 成本。
/// </summary>
public sealed record CacheableSystemPrompt(string StaticPart, string DynamicPart = "")
{
    /// <summary>
    /// 合併為完整提示詞（向下相容，等同原本的 BuildInstructions 輸出）。
    /// StaticPart 保留原始尾部換行，直接串接 DynamicPart 確保完美重建。
    /// </summary>
    public string ToFullText() => string.IsNullOrWhiteSpace(DynamicPart)
        ? StaticPart
        : StaticPart + DynamicPart;

    /// <summary>靜態部分的預估 token 數（用於統計 cache 命中節省量）。</summary>
    public long EstimatedStaticTokens => ModelPricing.EstimateTokens(StaticPart);
}
