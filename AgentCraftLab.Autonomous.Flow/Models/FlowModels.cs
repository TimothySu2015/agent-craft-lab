using Schema = AgentCraftLab.Engine.Models.Schema;

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

    /// <summary>
    /// 節點配置 — Step 2 起統一用 Schema.NodeConfig 強型別，LLM 的 PlannedNode 會在
    /// Phase F 後 LLM 直接輸出 Schema.NodeConfig JSON（nested discriminator union）。
    /// </summary>
    public required Schema.NodeConfig Config { get; init; }

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
