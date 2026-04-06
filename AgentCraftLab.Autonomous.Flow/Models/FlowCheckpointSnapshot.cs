namespace AgentCraftLab.Autonomous.Flow.Models;

/// <summary>
/// Flow 執行的 Checkpoint 快照 — 記錄已完成的節點位置和狀態，供 Phase B Resume 使用。
/// 比 ReAct 的 CheckpointSnapshot 簡單得多（不需要 messages / trackers / sub-agents）。
/// </summary>
public record FlowCheckpointSnapshot
{
    /// <summary>完整計劃的 JSON（不可變，Resume 時直接還原）。</summary>
    public required string PlanJson { get; init; }

    /// <summary>已完成到哪個節點的 index。</summary>
    public required int CompletedNodeIndex { get; init; }

    /// <summary>最後一個節點的輸出（Resume 時作為下一個節點的 input）。</summary>
    public required string PreviousResult { get; init; }

    /// <summary>Condition 分支跳過的節點 index 集合。</summary>
    public HashSet<int> SkipIndices { get; init; } = [];

    /// <summary>每個已完成節點的輸出（name → output），供 {{node:step_name}} 跨節點引用和 Resume 恢復。</summary>
    public Dictionary<string, string> NodeOutputs { get; init; } = new();

    /// <summary>累計使用的 token 數。</summary>
    public long AccumulatedTokens { get; init; }

    /// <summary>快照建立時間。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
