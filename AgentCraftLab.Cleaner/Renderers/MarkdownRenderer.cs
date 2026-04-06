using System.Text;
using System.Text.Json;
using AgentCraftLab.Cleaner.Abstractions;

namespace AgentCraftLab.Cleaner.Renderers;

/// <summary>
/// Markdown 渲染器 — 將 Schema Mapper 產出的 JSON 轉為結構化 Markdown 文件。
/// 自動處理：標題層級、表格、清單、程式碼區塊等。
/// </summary>
public sealed class MarkdownRenderer : IOutputRenderer
{
    /// <summary>物件陣列欄位數 ≤ 此值且無巢狀 → 渲染為表格，否則渲染為子區塊</summary>
    private const int MaxPropertiesForTableRendering = 6;
    private const int MaxHeadingLevel = 6;

    public string Format => "markdown";

    public Task<string> RenderAsync(string json, SchemaDefinition schema, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            var root = doc.RootElement;

            if (root.TryGetProperty("document", out var docMeta) &&
                docMeta.TryGetProperty("title", out var title))
            {
                sb.AppendLine($"# {title.GetString()}");
                sb.AppendLine();
                RenderDocumentMeta(docMeta, sb);
            }
            else
            {
                sb.AppendLine($"# {schema.Name}");
                sb.AppendLine();
            }

            foreach (var prop in root.EnumerateObject())
            {
                ct.ThrowIfCancellationRequested();
                if (prop.Name == "document")
                {
                    continue;
                }

                RenderSection(prop.Name, prop.Value, sb, level: 2);
            }

            return Task.FromResult(sb.ToString());
        }
        catch (JsonException)
        {
            return Task.FromResult($"# {schema.Name}\n\n```json\n{json}\n```\n");
        }
    }

    private static void RenderDocumentMeta(JsonElement docMeta, StringBuilder sb)
    {
        (string Key, string Label)[] fields = [("version", "版本"), ("date", "日期"), ("author", "撰寫者")];
        foreach (var (key, label) in fields)
        {
            if (docMeta.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
            {
                sb.AppendLine($"- **{label}**：{val.GetString()}");
            }
        }

        if (docMeta.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
        {
            sb.Append("- **來源文件**：");
            sb.AppendJoin(", ", sources.EnumerateArray().Select(s => s.GetString()));
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void RenderSection(string name, JsonElement value, StringBuilder sb, int level)
    {
        var heading = new string('#', level);
        var displayName = FormatSectionName(name);

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                sb.AppendLine($"{heading} {displayName}");
                sb.AppendLine();
                RenderObject(value, sb, level);
                break;

            case JsonValueKind.Array:
                sb.AppendLine($"{heading} {displayName}");
                sb.AppendLine();
                RenderArray(name, value, sb, level);
                break;

            case JsonValueKind.String:
                sb.AppendLine($"{heading} {displayName}");
                sb.AppendLine();
                sb.AppendLine(value.GetString());
                sb.AppendLine();
                break;

            case JsonValueKind.Null:
                break;

            default:
                sb.AppendLine($"{heading} {displayName}");
                sb.AppendLine();
                sb.AppendLine(value.ToString());
                sb.AppendLine();
                break;
        }
    }

    private static void RenderObject(JsonElement obj, StringBuilder sb, int level)
    {
        var hasComplex = obj.EnumerateObject().Any(p =>
            p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array);

        if (!hasComplex)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                RenderSimpleField(prop.Name, prop.Value, sb);
            }
            sb.AppendLine();
            return;
        }

        // 先渲染簡單欄位，再渲染複雜欄位
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                RenderSimpleField(prop.Name, prop.Value, sb);
            }
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                RenderSection(prop.Name, prop.Value, sb, ClampLevel(level + 1));
            }
        }
    }

    private static void RenderArray(string sectionName, JsonElement arr, StringBuilder sb, int level)
    {
        var items = arr.EnumerateArray().ToList();
        if (items.Count == 0)
        {
            sb.AppendLine("（無資料）");
            sb.AppendLine();
            return;
        }

        if (items[0].ValueKind == JsonValueKind.String)
        {
            RenderStringArray(items, sb);
            return;
        }

        if (items[0].ValueKind == JsonValueKind.Object)
        {
            RenderObjectArray(sectionName, items, sb, level);
            return;
        }

        // 其他型別 → 簡單列出
        foreach (var item in items)
        {
            sb.AppendLine($"- {item}");
        }
        sb.AppendLine();
    }

    private static void RenderStringArray(List<JsonElement> items, StringBuilder sb)
    {
        foreach (var item in items)
        {
            sb.AppendLine($"- {item.GetString()}");
        }
        sb.AppendLine();
    }

    private static void RenderObjectArray(string sectionName, List<JsonElement> items, StringBuilder sb, int level)
    {
        var firstObj = items[0];
        var propCount = firstObj.EnumerateObject().Count();
        var hasNested = firstObj.EnumerateObject().Any(p =>
            p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array);

        if (propCount <= MaxPropertiesForTableRendering && !hasNested)
        {
            RenderTable(items, sb);
        }
        else
        {
            RenderSubSections(sectionName, items, sb, level);
        }
    }

    private static void RenderSubSections(string sectionName, List<JsonElement> items, StringBuilder sb, int level)
    {
        var subLevel = ClampLevel(level + 1);
        for (var i = 0; i < items.Count; i++)
        {
            var subHeading = new string('#', subLevel);
            sb.AppendLine($"{subHeading} {GetItemTitle(items[i], sectionName, i)}");
            sb.AppendLine();
            RenderObject(items[i], sb, subLevel);
        }
    }

    private static void RenderTable(List<JsonElement> items, StringBuilder sb)
    {
        var columns = items[0].EnumerateObject().Select(p => p.Name).ToList();

        sb.Append('|');
        foreach (var col in columns)
        {
            sb.Append($" {FormatSectionName(col)} |");
        }
        sb.AppendLine();

        sb.Append('|');
        foreach (var _ in columns)
        {
            sb.Append(" --- |");
        }
        sb.AppendLine();

        foreach (var item in items)
        {
            sb.Append('|');
            foreach (var col in columns)
            {
                var val = item.TryGetProperty(col, out var v) ? FormatValue(v) : "";
                sb.Append($" {val} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();
    }

    private static void RenderSimpleField(string name, JsonElement value, StringBuilder sb)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        sb.AppendLine($"- **{FormatSectionName(name)}**：{FormatValue(value)}");
    }

    private static string FormatValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.ToString(),
        JsonValueKind.True => "是",
        JsonValueKind.False => "否",
        JsonValueKind.Null => "—",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(FormatValue)),
        _ => value.ToString(),
    };

    private static string GetItemTitle(JsonElement item, string sectionName, int index)
    {
        string[] titleFields = ["title", "name", "id", "term", "description", "item"];
        foreach (var field in titleFields)
        {
            if (item.TryGetProperty(field, out var val) && val.ValueKind == JsonValueKind.String)
            {
                var text = val.GetString()!;
                if (field != "id" && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    return $"{id.GetString()} — {text}";
                }
                return text;
            }
        }

        return $"{FormatSectionName(sectionName)} {index + 1}";
    }

    private static string FormatSectionName(string name) =>
        name.Replace('_', ' ') switch
        {
            var s when s.Length > 0 => char.ToUpperInvariant(s[0]) + s[1..],
            var s => s,
        };

    private static int ClampLevel(int level) => Math.Min(level, MaxHeadingLevel);
}
