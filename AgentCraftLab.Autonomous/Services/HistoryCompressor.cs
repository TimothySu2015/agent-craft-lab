using AgentCraftLab.Engine.Services.Compression;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 歷史壓縮器 — 委派 Engine 層的壓縮積木（ToolResultTruncator + MessageDeduplicator）。
/// 保留原有 API 以維持 HybridHistoryManager 和 ReactExecutor 的向下相容性。
/// </summary>
internal static class HistoryCompressor
{
    /// <summary>
    /// 嘗試本地壓縮。回傳是否成功壓縮到目標數量以下。
    /// </summary>
    public static bool TryLocalCompress(List<ChatMessage> messages, int targetCount = 12, int shortMessageThreshold = 100)
        => MessageDeduplicator.TryCompress(messages, targetCount, shortMessageThreshold);

    /// <summary>
    /// Layer 1：截斷超長工具結果（零 token 成本，在壓縮之前呼叫）。
    /// 回傳被截斷的字元數（供 cachedMessageChars 調整）。
    /// </summary>
    public static long TruncateLongToolResults(List<ChatMessage> messages, int maxLength = 1500, CompressionState? state = null)
        => ToolResultTruncator.Truncate(messages, maxLength, state);
}
