using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// Schema v2 序列化選項 — 所有 <see cref="WorkflowPayload"/> / <see cref="NodeConfig"/> 的
/// JSON round-trip 都應使用此共用設定。camelCase 屬性 + enum 序列化為字串，確保 LLM
/// 產出的 JSON 線格式與 C# 型別對齊。
/// </summary>
public static class SchemaJsonOptions
{
    public static JsonSerializerOptions Default { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
