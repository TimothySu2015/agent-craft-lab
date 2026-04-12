using System.ComponentModel;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// 條件分支節點 — 評估上游輸出後往兩條輸出 port（true / false）之一分派。
/// </summary>
public sealed record ConditionNode : NodeConfig
{
    [Description("條件判斷設定 — kind 可為 contains / regex / llmJudge / expression")]
    public ConditionConfig Condition { get; init; } = new();

    [Description("當 kind = llmJudge 時使用的模型設定（非 llmJudge 時忽略）")]
    public ModelConfig? JudgeModel { get; init; }
}
