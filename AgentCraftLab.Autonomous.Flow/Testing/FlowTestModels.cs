using System.Text.Json.Serialization;

namespace AgentCraftLab.Autonomous.Flow.Testing;

/// <summary>
/// 測試情境定義 — 描述一個 Flow 自動化測試案例。
/// </summary>
public sealed record FlowTestScenario
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Goal { get; init; }
    public HashSet<string> ExpectedNodeTypes { get; init; } = [];
    public (int Min, int Max) ExpectedToolCallRange { get; init; } = (0, 100);
    public int MaxIterations { get; init; } = 15;
    public long MaxTotalTokens { get; init; } = 100_000;
    public List<string> RequiredTools { get; init; } = [];
    public bool ExpectError { get; init; }
}

/// <summary>
/// 單一情境的測試結果。
/// </summary>
public sealed class FlowTestResult
{
    public string ScenarioId { get; init; } = "";
    public string ScenarioName { get; init; } = "";
    public bool Passed { get; set; }
    public List<string> Failures { get; } = [];
    public int NodeCount { get; set; }
    public HashSet<string> ObservedNodeTypes { get; } = [];
    public int ToolCallCount { get; set; }
    public long TotalTokens { get; set; }
    public bool HasPlan { get; set; }
    public bool HasWorkflowCompleted { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
    public string FinalOutput { get; set; } = "";
}

/// <summary>
/// 全部情境的彙總報告。
/// </summary>
public sealed class FlowTestReport
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ExecutionMode { get; init; } = "flow";
    public string Model { get; init; } = "";
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public long TotalTokensUsed { get; set; }
    public List<FlowTestResult> Results { get; } = [];
}
