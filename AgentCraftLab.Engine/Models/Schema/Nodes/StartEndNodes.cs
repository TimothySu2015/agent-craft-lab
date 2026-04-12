namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// 起始節點（Meta）— 標示 workflow 進入點。
/// </summary>
public sealed record StartNode : NodeConfig;

/// <summary>
/// 結束節點（Meta）— 標示 workflow 終點。
/// </summary>
public sealed record EndNode : NodeConfig;
