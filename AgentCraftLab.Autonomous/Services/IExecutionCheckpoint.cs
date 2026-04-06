namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 執行檢查點介面 — 儲存 ReAct 迴圈的中間狀態，供追蹤與未來中斷恢復使用。
/// </summary>
public interface IExecutionCheckpoint
{
    /// <summary>儲存檢查點（只記錄 metadata，不儲存完整 messages）。</summary>
    Task SaveAsync(string executionId, int iteration, int messageCount, long tokensUsed, CancellationToken ct);

    /// <summary>清除指定執行的所有檢查點。</summary>
    Task CleanupAsync(string executionId, CancellationToken ct);
}
