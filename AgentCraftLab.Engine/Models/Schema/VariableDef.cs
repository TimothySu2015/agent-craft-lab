namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// Workflow 層級變數定義 — 由使用者在畫布設定，執行時可被 {{var:name}} 引用。
/// </summary>
public sealed record VariableDef
{
    public string Name { get; init; } = "";
    public VariableType Type { get; init; } = VariableType.String;
    public string DefaultValue { get; init; } = "";
    public string? Description { get; init; }
    public VariableScope Scope { get; init; } = VariableScope.Workflow;
}

public enum VariableType
{
    String,
    Number,
    Boolean,
    Json
}

/// <summary>
/// 變數來源範圍 — IVariableResolver 用此 enum 決定從哪個字典查表。
/// </summary>
public enum VariableScope
{
    /// <summary>系統變數（readonly），例如 user_id / run_id / now。</summary>
    System,
    /// <summary>Workflow 定義的變數（由 VariableDef 初始化，Code 節點可寫回）。</summary>
    Workflow,
    /// <summary>執行時傳入的變數（覆蓋 Workflow 預設值）。</summary>
    Runtime,
    /// <summary>環境變數（AGENTCRAFTLAB_ 前綴 allowlist）。</summary>
    Environment,
    /// <summary>上游節點輸出（{{node:name}}）。</summary>
    NodeOutput
}
