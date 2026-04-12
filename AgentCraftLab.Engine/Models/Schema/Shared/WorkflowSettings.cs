namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// Workflow 執行層級設定。
/// </summary>
public sealed record WorkflowSettings
{
    /// <summary>執行策略 — "auto"（自動偵測）/ "single" / "sequential" / "concurrent" / "handoff" / "imperative"。</summary>
    public string Strategy { get; init; } = "auto";

    public int MaxTurns { get; init; } = 10;

    /// <summary>Context 傳遞模式 — "previous-only"（僅前一節點輸出）/ "accumulated"（累積所有輸出）。</summary>
    public string ContextPassing { get; init; } = "previous-only";

    /// <summary>
    /// 啟用投機執行 — llm-judge Condition 節點評估時同時搶跑兩條分支的第一個節點。
    /// 預設關閉（opt-in），啟用後輸家分支會消耗額外 token。
    /// </summary>
    public bool SpeculativeExecution { get; init; }
}
