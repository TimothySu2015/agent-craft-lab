using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Autonomous.Flow.Models;

/// <summary>
/// LLM 產生的 Flow 規劃結果 — Phase F 改用 <see cref="Schema.NodeConfig"/> 為 wire format。
/// LLM 直接輸出 Schema JSON（discriminator union），<see cref="System.Text.Json.JsonSerializer"/>
/// 透過 <see cref="Schema.SchemaJsonOptions.Default"/> 反序列化為強型別 sealed record 清單。
/// </summary>
public sealed class FlowPlan
{
    public List<Schema.NodeConfig> Nodes { get; init; } = [];
}
