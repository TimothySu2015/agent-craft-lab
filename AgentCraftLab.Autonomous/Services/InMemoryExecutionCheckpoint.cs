using System.Collections.Concurrent;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 記憶體內檢查點實作 — 記錄執行進度，支持監控和未來恢復。
/// </summary>
public sealed class InMemoryExecutionCheckpoint : IExecutionCheckpoint
{
    private readonly ConcurrentDictionary<string, CheckpointData> _checkpoints = new();

    /// <summary>儲存檢查點（覆寫同一執行 ID 的舊資料）。</summary>
    public Task SaveAsync(string executionId, int iteration, int messageCount, long tokensUsed, CancellationToken ct)
    {
        _checkpoints[executionId] = new CheckpointData(iteration, messageCount, tokensUsed, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    /// <summary>清除指定執行的檢查點。</summary>
    public Task CleanupAsync(string executionId, CancellationToken ct)
    {
        _checkpoints.TryRemove(executionId, out _);
        return Task.CompletedTask;
    }

    /// <summary>查詢檢查點（供監控用）。</summary>
    public CheckpointData? GetCheckpoint(string executionId) =>
        _checkpoints.TryGetValue(executionId, out var data) ? data : null;

    /// <summary>檢查點資料 — 記錄迴圈進度快照。</summary>
    public record CheckpointData(int Iteration, int MessageCount, long TokensUsed, DateTime SavedAt);
}
