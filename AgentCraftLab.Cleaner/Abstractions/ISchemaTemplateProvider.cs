namespace AgentCraftLab.Cleaner.Abstractions;

/// <summary>
/// Schema 模板提供者 — 管理內建和自訂的 Schema 模板。
/// 內建模板從 Data/schema-templates/ 載入，零程式碼新增。
/// </summary>
public interface ISchemaTemplateProvider
{
    /// <summary>列出所有可用模板</summary>
    IReadOnlyList<SchemaTemplateSummary> ListTemplates();

    /// <summary>依 ID 取得完整模板</summary>
    SchemaDefinition? GetTemplate(string templateId);
}

/// <summary>模板摘要（用於列表顯示）</summary>
public sealed class SchemaTemplateSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
}
