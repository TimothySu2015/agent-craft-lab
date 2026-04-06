namespace AgentCraftLab.Cleaner.Abstractions;

/// <summary>LLM 回應（含 token 用量）</summary>
public sealed record LlmResponse(string Text, int InputTokens = 0, int OutputTokens = 0);

/// <summary>
/// LLM 提供者介面 — 讓 SchemaMapper 不直接依賴 MEAI 或任何特定 LLM SDK。
/// 外部可透過 adapter 將 IChatClient 橋接到此介面。
/// </summary>
public interface ILlmProvider
{
    /// <summary>呼叫 LLM 取得回應</summary>
    Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
