using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 簡單裁剪策略 — 保留 system message + 最近 N 條訊息。
/// 與 ImperativeWorkflowStrategy 原有的硬編碼邏輯完全一致（1:1 提取）。
/// </summary>
public sealed class SimpleTrimmingStrategy : IHistoryStrategy
{
    public string Name => "simple-trimming";

    public void TrimHistory(List<ChatMessage> history, int maxMessages)
    {
        HistoryTrimHelper.TrimToRecent(history, maxMessages);
    }
}

/// <summary>
/// 滑動視窗策略 — 在訊息數達到門檻的 80% 時提前裁剪，避免累積到上限才一次性大量丟棄。
/// 適用於長迴圈場景（Loop 50+ 輪）。
/// </summary>
public sealed class SlidingWindowStrategy : IHistoryStrategy
{
    private const double TriggerThreshold = 0.8;
    private const double KeepRatio = 0.6;

    public string Name => "sliding-window";

    public void TrimHistory(List<ChatMessage> history, int maxMessages)
    {
        var threshold = (int)(maxMessages * TriggerThreshold) + 1;
        if (history.Count <= threshold)
            return;

        HistoryTrimHelper.TrimToRecent(history, (int)(maxMessages * KeepRatio));
    }
}

/// <summary>
/// History 裁剪共用工具 — 保留 system message（history[0]）+ 最近 keepCount 條訊息。
/// </summary>
internal static class HistoryTrimHelper
{
    public static void TrimToRecent(List<ChatMessage> history, int keepCount)
    {
        // +1 排除 system message 的計數
        if (history.Count <= keepCount + 1)
            return;

        var systemMsg = history[0];
        var recent = history.Skip(history.Count - keepCount).ToList();
        history.Clear();
        history.Add(systemMsg);
        history.AddRange(recent);
    }
}
