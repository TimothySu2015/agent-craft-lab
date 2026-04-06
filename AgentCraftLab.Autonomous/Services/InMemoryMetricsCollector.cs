using System.Collections.Concurrent;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 記憶體內指標收集器 — 保留最近 100 次執行的統計資料。
/// 執行緒安全，適用於多個 ReactExecutor 同時上報。
/// </summary>
public sealed class InMemoryMetricsCollector : IMetricsCollector
{
    private readonly ConcurrentQueue<ExecutionMetric> _recentExecutions = new();
    private readonly ConcurrentDictionary<string, int> _toolCallCounts = new();
    private readonly ConcurrentDictionary<string, byte> _activeExecutions = new();

    /// <summary>保留的最大歷史執行紀錄數。</summary>
    private const int MaxRecentExecutions = 100;

    /// <inheritdoc/>
    public void RecordStep(string executionId, int iteration, long stepTokens, long stepDurationMs)
    {
        // 標記此執行為活躍中
        _activeExecutions.TryAdd(executionId, 0);
    }

    /// <inheritdoc/>
    public void RecordToolCall(string executionId, string toolName, bool success, long durationMs)
    {
        _toolCallCounts.AddOrUpdate(toolName, 1, (_, count) => count + 1);
    }

    /// <inheritdoc/>
    public void RecordSubAgentOperation(string executionId, string agentName, string operation, long durationMs)
    {
        // 目前只追蹤活躍狀態，未來可擴展為獨立統計
    }

    /// <inheritdoc/>
    public void RecordExecutionComplete(string executionId, bool success, int totalSteps, long totalTokens, long totalDurationMs)
    {
        // 移除活躍標記
        _activeExecutions.TryRemove(executionId, out _);

        // 記錄完成指標
        _recentExecutions.Enqueue(new ExecutionMetric(success, totalSteps, totalTokens, totalDurationMs, DateTime.UtcNow));

        // 保留最近 MaxRecentExecutions 筆
        while (_recentExecutions.Count > MaxRecentExecutions)
        {
            _recentExecutions.TryDequeue(out _);
        }
    }

    /// <inheritdoc/>
    public MetricsSummary GetSummary()
    {
        var executions = _recentExecutions.ToArray();
        return new MetricsSummary
        {
            TotalExecutions = executions.Length,
            SuccessCount = executions.Count(e => e.Success),
            FailCount = executions.Count(e => !e.Success),
            AvgSteps = executions.Length > 0 ? executions.Average(e => e.TotalSteps) : 0,
            AvgTokens = executions.Length > 0 ? executions.Average(e => e.TotalTokens) : 0,
            AvgDurationMs = executions.Length > 0 ? executions.Average(e => e.TotalDurationMs) : 0,
            ActiveExecutions = _activeExecutions.Count,
            ToolCallCounts = _toolCallCounts.ToDictionary(kv => kv.Key, kv => kv.Value)
        };
    }

    /// <summary>單次執行的完成指標紀錄。</summary>
    private record ExecutionMetric(bool Success, int TotalSteps, long TotalTokens, long TotalDurationMs, DateTime CompletedAt);
}
