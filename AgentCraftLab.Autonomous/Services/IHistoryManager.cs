using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services.Compression;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>歷史壓縮結果。</summary>
public record HistoryCompressionResult(
    bool WasCompressed,
    List<ExecutionEvent> Events,
    bool ShouldResetBudgetReminderIndex);

/// <summary>
/// 歷史管理器 — 三層壓縮策略：截斷工具結果 → 本地壓縮 → LLM 摘要。
/// </summary>
public interface IHistoryManager
{
    /// <summary>訊息數門檻（向下相容）。</summary>
    int Threshold { get; }

    /// <summary>設定模型名稱，動態計算 context window 門檻。</summary>
    void SetModel(string modelName);

    /// <summary>判斷是否需要壓縮（token 或訊息數任一超過門檻）。</summary>
    bool ShouldCompress(List<ChatMessage> messages, long cachedMessageChars);

    /// <summary>嘗試壓縮對話歷史。可選 CompressionState 追蹤壓縮統計。</summary>
    Task<HistoryCompressionResult> CompressIfNeededAsync(
        List<ChatMessage> messages,
        IChatClient rawClient,
        TokenTracker tokenTracker,
        CancellationToken ct,
        CompressionState? compressionState = null);
}
