using System.ComponentModel;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// Autonomous ReAct 節點 — 進入 AgentCraftLab.Autonomous 的 ReAct 迴圈執行自主任務。
/// </summary>
public sealed record AutonomousNode : NodeConfig
{
    [Description("目標任務描述 — 傳給 ReAct planner")]
    public string Instructions { get; init; } = "";

    [Description("LLM 模型設定 — 建議用強模型（如 gpt-4o）")]
    public ModelConfig Model { get; init; } = new();

    [Description("最大 ReAct 迴圈步數")]
    public int MaxIterations { get; init; } = 10;

    public IReadOnlyList<string> Tools { get; init; } = [];
    public IReadOnlyList<string> McpServers { get; init; } = [];
    public IReadOnlyList<string> A2AAgents { get; init; } = [];
    public IReadOnlyList<string> HttpApis { get; init; } = [];
    public IReadOnlyList<string> Skills { get; init; } = [];
}
