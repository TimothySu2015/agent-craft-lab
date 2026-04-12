namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// Workflow 的頂層序列化格式（v2.0）—  Nodes / Connections / Variables / Settings / Hooks / Resources。
/// 取代舊的 <see cref="Models.WorkflowPayload"/>。無 schemaVersion migrator — 直接 hard-cut。
/// </summary>
public sealed record WorkflowPayload
{
    /// <summary>Schema 版本標記（資訊用，不做 migration 邏輯）。</summary>
    public string Version { get; init; } = "2.0";

    public IReadOnlyList<NodeConfig> Nodes { get; init; } = [];
    public IReadOnlyList<Connection> Connections { get; init; } = [];
    public IReadOnlyList<VariableDef> Variables { get; init; } = [];
    public WorkflowSettings Settings { get; init; } = new();
    public WorkflowHooks Hooks { get; init; } = new();
    public WorkflowResources Resources { get; init; } = new();
    public IReadOnlyList<MiddlewareBinding> Middleware { get; init; } = [];
}

/// <summary>
/// Workflow 可用的資源清單（MCP / A2A / HTTP API / Skills 集中處）。
/// 型別沿用既有定義（<see cref="Models.McpServerDefinition"/> 等），Schema v2 會在後續階段整併。
/// </summary>
public sealed record WorkflowResources
{
    public IReadOnlyList<Models.McpServerDefinition> McpServers { get; init; } = [];
    public IReadOnlyList<Models.A2AAgentDefinition> A2AAgents { get; init; } = [];
    public IReadOnlyDictionary<string, Models.HttpApiDefinition> HttpApis { get; init; }
        = new Dictionary<string, Models.HttpApiDefinition>();
    public IReadOnlyList<string> Skills { get; init; } = [];
}
