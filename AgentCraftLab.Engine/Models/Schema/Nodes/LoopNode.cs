using System.ComponentModel;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// 迴圈節點 — 在條件成立前反覆執行 body agent。
/// </summary>
public sealed record LoopNode : NodeConfig
{
    [Description("結束條件 — 滿足時跳出迴圈")]
    public ConditionConfig Condition { get; init; } = new();

    [Description("迴圈 body — 每輪執行的 agent 設定")]
    public AgentNode BodyAgent { get; init; } = new();

    [Description("最大迴圈次數（安全上限）")]
    public int MaxIterations { get; init; } = 5;
}
