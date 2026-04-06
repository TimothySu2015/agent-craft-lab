namespace AgentCraftLab.Engine.Models;

/// <summary>
/// Workflow 節點欄位契約 — 所有需要序列化/反序列化 workflow 節點的 class 都應實作此介面。
/// 欄位名在此定義一次（Single Source of Truth），名稱不一致會在編譯時立刻失敗。
/// 實作者：WorkflowNode（Engine 輸入）、CrystallizedNode（Flow 輸出）。
/// </summary>
public interface IWorkflowNodeContract
{
    // 基礎
    string Type { get; }
    string Name { get; }

    // Agent
    string? Instructions { get; }
    List<string>? Tools { get; }
    string? Provider { get; }
    string? Model { get; }

    // Condition / Loop
    string? ConditionType { get; }
    string? ConditionExpression { get; }
    // MaxIterations 在 WorkflowNode 是 int（有預設值），CrystallizedNode 是 int?（nullable）
    // interface 不約束 nullability，靠屬性名保證一致

    // Code
    string? TransformType { get; }
    string? Pattern { get; }
    string? Replacement { get; }
    string? Template { get; }

    // Parallel
    string? Branches { get; }
    string? MergeStrategy { get; }

    // Iteration
    string? SplitMode { get; }
    string? IterationDelimiter { get; }

    // HTTP Request
    string? HttpApiId { get; }
    string? HttpArgsTemplate { get; }

    // 輸出格式
    string? OutputFormat { get; }
    string? OutputSchema { get; }
}

/// <summary>
/// Workflow 連線欄位契約。
/// 實作者：WorkflowConnection（Engine）、CrystallizedConnection（Flow）。
/// </summary>
public interface IWorkflowConnectionContract
{
    string? FromOutput { get; }
}
