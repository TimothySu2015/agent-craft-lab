namespace AgentCraftLab.Autonomous.Flow.Models;

/// <summary>
/// 執行軌跡 — 記錄 FlowExecutor 每一步的節點操作，用於 Crystallize。
/// </summary>
public sealed class ExecutionTrace
{
    public string ExecutionId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string Goal { get; init; } = "";
    public List<TraceStep> Steps { get; } = [];
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public bool Succeeded { get; set; }
}

/// <summary>
/// 執行軌跡中的單一步驟 — 對應一個節點執行。
/// </summary>
public sealed class TraceStep
{
    /// <summary>步驟序號（從 1 開始）</summary>
    public required int Sequence { get; init; }

    /// <summary>節點類型（agent / condition / code / parallel / iteration / loop / http-request）</summary>
    public required string NodeType { get; init; }

    /// <summary>節點名稱（LLM 指定或自動生成）</summary>
    public required string NodeName { get; init; }

    /// <summary>節點配置（依 NodeType 不同內容不同）</summary>
    public required NodeConfig Config { get; init; }

    /// <summary>節點輸入</summary>
    public string Input { get; init; } = "";

    /// <summary>節點輸出</summary>
    public string Output { get; set; } = "";

    /// <summary>執行耗時</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>此步驟消耗的 token 數</summary>
    public long TokensUsed { get; set; }

    /// <summary>輸出埠（condition/loop 等分支節點使用）</summary>
    public string? OutputPort { get; set; }
}

/// <summary>
/// 節點配置 — 攜帶節點執行所需的所有參數。
/// 不同 NodeType 使用不同欄位子集。
/// PlannedNode 和 TraceStep 共用此型別（Single Source of Truth）。
/// </summary>
public sealed class NodeConfig
{
    // Agent 節點
    public string? Instructions { get; init; }
    public List<string>? Tools { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }

    // Condition / Loop 節點
    public string? ConditionType { get; init; }
    public string? ConditionValue { get; init; }

    // Loop 節點
    public int? MaxIterations { get; init; }

    // Code 節點
    public string? TransformType { get; init; }
    public string? TransformPattern { get; init; }
    public string? TransformReplacement { get; init; }

    // Parallel 節點
    public List<ParallelBranchConfig>? Branches { get; init; }
    public string? MergeStrategy { get; init; }

    // Iteration 節點
    public string? SplitMode { get; init; }
    public string? Delimiter { get; init; }
    public int? MaxItems { get; init; }
    public int? MaxConcurrency { get; init; }

    // HTTP Request 節點 — catalog 模式
    public string? HttpApiId { get; init; }
    public string? HttpArgsTemplate { get; init; }

    // HTTP Request 節點 — inline 模式
    public string? HttpUrl { get; init; }
    public string? HttpMethod { get; init; }
    public string? HttpHeaders { get; init; }
    public string? HttpBodyTemplate { get; init; }
    public string? HttpContentType { get; init; }
    public int? HttpTimeoutSeconds { get; init; }
    public string? HttpAuthMode { get; init; }
    public string? HttpAuthCredential { get; init; }
    public string? HttpAuthKeyName { get; init; }
    public int? HttpRetryCount { get; init; }
    public int? HttpRetryDelayMs { get; init; }
    public string? HttpResponseFormat { get; init; }
    public string? HttpResponseJsonPath { get; init; }
    public int? HttpResponseMaxLength { get; init; }

    // 輸出格式（text / json / json_schema）
    public string? OutputFormat { get; init; }
    public string? OutputSchema { get; init; }

    // Router 節點
    public string? Routes { get; init; }
}

/// <summary>
/// Parallel 節點的分支配置。
/// </summary>
public sealed class ParallelBranchConfig
{
    public required string Name { get; init; }
    public required string Goal { get; init; }
    public List<string>? Tools { get; init; }
}

/// <summary>
/// FlowExecutor 的特有配置 — 透過 GoalExecutionRequest.Options 傳遞。
/// </summary>
public static class FlowOptions
{
    /// <summary>強制最終輸出格式（text / json / json_schema）</summary>
    public const string OutputFormat = "flow:outputFormat";

    /// <summary>json_schema 模式的 JSON Schema 定義</summary>
    public const string OutputSchema = "flow:outputSchema";

    /// <summary>覆蓋規劃用模型（預設 gpt-4o）</summary>
    public const string PlannerModel = "flow:plannerModel";
}
