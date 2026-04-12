using System.ComponentModel;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// Parallel fan-out/fan-in 節點 — 同時執行 N 條分支後合併結果。
/// 取代舊 schema 的 CSV "Branch1,Branch2" 字串編碼。
/// </summary>
public sealed record ParallelNode : NodeConfig
{
    [Description("並行分支清單 — 每條分支獨立執行後由 Merge 策略合併")]
    public IReadOnlyList<BranchConfig> Branches { get; init; } = [];

    [Description("合併策略 — Labeled（附標籤串接）/ Join（換行串接）/ Json（JSON 物件）")]
    public MergeStrategyKind Merge { get; init; } = MergeStrategyKind.Labeled;
}
