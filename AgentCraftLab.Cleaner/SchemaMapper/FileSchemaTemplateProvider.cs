using System.Text.Json;
using AgentCraftLab.Cleaner.Abstractions;

namespace AgentCraftLab.Cleaner.SchemaMapper;

/// <summary>
/// 檔案式 Schema 模板提供者 — 從 Data/schema-templates/ 載入 JSON 模板。
/// 新增模板只需放入 .json 檔案，零程式碼修改。
/// </summary>
public sealed class FileSchemaTemplateProvider : ISchemaTemplateProvider
{
    private readonly Dictionary<string, SchemaTemplateFile> _templates = new(StringComparer.OrdinalIgnoreCase);

    public FileSchemaTemplateProvider(string templatesDirectory)
    {
        if (!Directory.Exists(templatesDirectory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(templatesDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var template = JsonSerializer.Deserialize<SchemaTemplateFile>(json, JsonOptions);
                if (template?.Id is not null)
                {
                    _templates[template.Id] = template;
                }
            }
            catch (JsonException)
            {
                Console.WriteLine($"[CraftCleaner] Skipped invalid template: {file}");
            }
        }
    }

    public IReadOnlyList<SchemaTemplateSummary> ListTemplates() =>
        _templates.Values.Select(t => new SchemaTemplateSummary
        {
            Id = t.Id!,
            Name = t.Name ?? t.Id!,
            Description = t.Description ?? "",
            Category = t.Category ?? "General",
        }).ToList();

    public SchemaDefinition? GetTemplate(string templateId)
    {
        if (!_templates.TryGetValue(templateId, out var template))
        {
            return null;
        }

        return new SchemaDefinition
        {
            Name = template.Name ?? template.Id!,
            Description = template.Description ?? "",
            JsonSchema = template.JsonSchema.HasValue
                ? template.JsonSchema.Value.GetRawText()
                : "{}",
            ExtractionGuidance = template.ExtractionGuidance,
        };
    }

    // 內部反序列化模型
    private sealed class SchemaTemplateFile
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public string? ExtractionGuidance { get; set; }
        public JsonElement? JsonSchema { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}
