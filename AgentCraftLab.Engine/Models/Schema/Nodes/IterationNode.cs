using System.ComponentModel;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// foreach 迴圈節點 — 對上游輸出的集合逐項執行 body agent（可並行）。
/// </summary>
public sealed record IterationNode : NodeConfig
{
    [Description("集合拆分模式 — JsonArray（解析 JSON 陣列）/ Delimiter（用分隔符切）")]
    public SplitModeKind Split { get; init; } = SplitModeKind.JsonArray;

    [Description("拆分分隔符（僅 Split = Delimiter 時使用）")]
    public string Delimiter { get; init; } = "\n";

    [Description("最大項目數（安全上限，預設 50）")]
    public int MaxItems { get; init; } = 50;

    [Description("最大並行數（1 = 順序執行，>1 = SemaphoreSlim 節流）")]
    public int MaxConcurrency { get; init; } = 1;

    [Description("每項執行的 body agent")]
    public AgentNode BodyAgent { get; init; } = new();
}

public enum SplitModeKind
{
    JsonArray,
    Delimiter
}
