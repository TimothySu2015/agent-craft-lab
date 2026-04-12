using System.Text.Json.Serialization;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// Workflow 節點配置的抽象基底 — 所有具體節點類型繼承此 record。
/// 使用 System.Text.Json 原生 discriminator union（"type" 欄位分派子型別）。
/// 新增節點型別步驟：(1) 建立 sealed record 繼承 NodeConfig (2) 在此加一行 [JsonDerivedType]。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AgentNode), "agent")]
[JsonDerivedType(typeof(A2AAgentNode), "a2a-agent")]
[JsonDerivedType(typeof(AutonomousNode), "autonomous")]
[JsonDerivedType(typeof(ConditionNode), "condition")]
[JsonDerivedType(typeof(LoopNode), "loop")]
[JsonDerivedType(typeof(RouterNode), "router")]
[JsonDerivedType(typeof(HumanNode), "human")]
[JsonDerivedType(typeof(CodeNode), "code")]
[JsonDerivedType(typeof(IterationNode), "iteration")]
[JsonDerivedType(typeof(ParallelNode), "parallel")]
[JsonDerivedType(typeof(HttpRequestNode), "http-request")]
[JsonDerivedType(typeof(RagNode), "rag")]
[JsonDerivedType(typeof(StartNode), "start")]
[JsonDerivedType(typeof(EndNode), "end")]
public abstract record NodeConfig
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public NodePosition? Position { get; init; }
    public IReadOnlyDictionary<string, string>? Meta { get; init; }
}

public sealed record NodePosition(double X, double Y);
