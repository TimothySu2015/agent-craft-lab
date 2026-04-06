namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 執行指標收集器 — 集中記錄 Autonomous Agent 的關鍵效能指標。
/// </summary>
public interface IMetricsCollector
{
    /// <summary>記錄一個 ReAct 步驟完成。</summary>
    void RecordStep(string executionId, int iteration, long stepTokens, long stepDurationMs);

    /// <summary>記錄工具呼叫。</summary>
    void RecordToolCall(string executionId, string toolName, bool success, long durationMs);

    /// <summary>記錄 Sub-agent 操作。</summary>
    void RecordSubAgentOperation(string executionId, string agentName, string operation, long durationMs);

    /// <summary>記錄執行完成。</summary>
    void RecordExecutionComplete(string executionId, bool success, int totalSteps, long totalTokens, long totalDurationMs);

    /// <summary>取得最近的指標摘要（供健康檢查用）。</summary>
    MetricsSummary GetSummary();
}

/// <summary>指標摘要。</summary>
public record MetricsSummary
{
    /// <summary>總執行次數。</summary>
    public int TotalExecutions { get; init; }

    /// <summary>成功次數。</summary>
    public int SuccessCount { get; init; }

    /// <summary>失敗次數。</summary>
    public int FailCount { get; init; }

    /// <summary>平均步驟數。</summary>
    public double AvgSteps { get; init; }

    /// <summary>平均 Token 用量。</summary>
    public double AvgTokens { get; init; }

    /// <summary>平均執行時間（毫秒）。</summary>
    public double AvgDurationMs { get; init; }

    /// <summary>目前活躍的執行數。</summary>
    public int ActiveExecutions { get; init; }

    /// <summary>各工具呼叫次數統計。</summary>
    public Dictionary<string, int> ToolCallCounts { get; init; } = new();
}
