using System.Text;
using System.Text.RegularExpressions;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// SKILL.md 匯入/匯出轉換器 — 與 Microsoft Agent Framework Skills 格式互通。
/// </summary>
public static class SkillMdConverter
{
    private static readonly Regex FrontmatterRegex = new(@"^---\s*\n([\s\S]*?)\n---\s*\n([\s\S]*)", RegexOptions.Compiled);

    /// <summary>
    /// 匯出：SkillDefinition → SKILL.md 字串。
    /// </summary>
    public static string ToSkillMd(SkillDefinition skill)
    {
        var sb = new StringBuilder();

        // YAML frontmatter
        sb.AppendLine("---");
        sb.AppendLine($"name: {skill.Id}");
        sb.AppendLine($"description: {EscapeYamlString(skill.Description)}");

        // metadata（AgentCraftLab 擴展欄位）
        sb.AppendLine("metadata:");
        sb.AppendLine($"  displayName: {EscapeYamlString(skill.DisplayName)}");
        sb.AppendLine($"  category: {skill.Category}");

        if (skill.Icon != "&#x1F3AF;")
            sb.AppendLine($"  icon: \"{skill.Icon}\"");

        if (skill.Tools is { Count: > 0 })
        {
            sb.AppendLine("  tools:");
            foreach (var tool in skill.Tools)
                sb.AppendLine($"    - {tool}");
        }

        if (skill.FewShotExamples is { Count: > 0 })
        {
            sb.AppendLine("  fewShotExamples:");
            foreach (var ex in skill.FewShotExamples)
            {
                sb.AppendLine($"    - user: {EscapeYamlString(ex.User)}");
                sb.AppendLine($"      assistant: {EscapeYamlString(ex.Assistant)}");
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();

        // Instructions body
        if (!string.IsNullOrWhiteSpace(skill.Instructions))
            sb.Append(skill.Instructions.TrimEnd());

        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// 匯入：SKILL.md 字串 → SkillDefinition。
    /// </summary>
    public static SkillDefinition? FromSkillMd(string markdown, string? fallbackId = null)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return null;

        var match = FrontmatterRegex.Match(markdown.TrimStart());
        if (!match.Success)
            return null;

        var yamlBlock = match.Groups[1].Value;
        var body = match.Groups[2].Value.Trim();

        // 解析 YAML frontmatter
        var props = ParseYaml(yamlBlock);

        var name = GetValue(props, "name") ?? fallbackId ?? $"imported-{Guid.NewGuid():N}"[..16];
        var description = GetValue(props, "description") ?? "";
        var displayName = GetValue(props, "metadata.displayName") ?? name;
        var categoryStr = GetValue(props, "metadata.category") ?? "DomainKnowledge";
        var icon = GetValue(props, "metadata.icon") ?? "&#x1F3AF;";
        var tools = GetList(props, "metadata.tools");
        var fewShotExamples = ParseFewShotExamples(props);

        if (!Enum.TryParse<SkillCategory>(categoryStr, true, out var category))
            category = SkillCategory.DomainKnowledge;

        return new SkillDefinition(
            Id: name,
            DisplayName: displayName,
            Description: description,
            Instructions: body,
            Category: category,
            Icon: icon,
            Tools: tools.Count > 0 ? tools : null,
            FewShotExamples: fewShotExamples.Count > 0 ? fewShotExamples : null);
    }

    /// <summary>
    /// 簡易 YAML parser — 支援 key: value、巢狀 key、list（- item）。
    /// </summary>
    private static Dictionary<string, object> ParseYaml(string yaml)
    {
        var result = new Dictionary<string, object>();
        var lines = yaml.Split('\n');
        var currentPath = new Stack<(string Key, int Indent)>();
        var currentList = (List<object>?)null;
        var currentListKey = "";
        var currentListItemIndent = -1;
        var pendingMapItem = new Dictionary<string, string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var indent = line.Length - line.TrimStart().Length;
            var trimmed = line.TrimStart();

            // 調整 path stack
            while (currentPath.Count > 0 && currentPath.Peek().Indent >= indent)
            {
                // 如果離開 list context，flush pending
                if (currentList != null && indent <= currentListItemIndent - 2)
                {
                    FlushPendingMapItem(pendingMapItem, currentList);
                    currentList = null;
                    currentListKey = "";
                    currentListItemIndent = -1;
                }
                currentPath.Pop();
            }

            // List item（- value 或 - key: value）
            if (trimmed.StartsWith("- "))
            {
                var itemContent = trimmed[2..].Trim();

                if (currentList == null)
                {
                    // 找到 parent list key
                    currentList = [];
                    currentListKey = GetFullPath(currentPath);
                    result[currentListKey] = currentList;
                    currentListItemIndent = indent;
                }

                // key: value 在 list item 中
                var colonIdx = itemContent.IndexOf(':');
                if (colonIdx > 0 && colonIdx < itemContent.Length - 1)
                {
                    FlushPendingMapItem(pendingMapItem, currentList);
                    var k = itemContent[..colonIdx].Trim();
                    var v = UnescapeYamlString(itemContent[(colonIdx + 1)..].Trim());
                    pendingMapItem[k] = v;
                }
                else
                {
                    FlushPendingMapItem(pendingMapItem, currentList);
                    currentList.Add(UnescapeYamlString(itemContent));
                }
                continue;
            }

            // 續行 map item（如 assistant: value 在 list item 下一行）
            if (currentList != null && indent > currentListItemIndent && trimmed.Contains(':'))
            {
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx > 0)
                {
                    var k = trimmed[..colonIdx].Trim();
                    var v = UnescapeYamlString(trimmed[(colonIdx + 1)..].Trim());
                    pendingMapItem[k] = v;
                    continue;
                }
            }

            // Flush list if we're back to normal keys
            if (currentList != null)
            {
                FlushPendingMapItem(pendingMapItem, currentList);
                currentList = null;
                currentListKey = "";
                currentListItemIndent = -1;
            }

            // key: value
            var mainColon = trimmed.IndexOf(':');
            if (mainColon > 0)
            {
                var key = trimmed[..mainColon].Trim();
                var value = trimmed[(mainColon + 1)..].Trim();

                currentPath.Push((key, indent));

                if (string.IsNullOrEmpty(value))
                {
                    // 巢狀 object（下一行會有子 key）
                    continue;
                }

                var fullKey = GetFullPath(currentPath);
                result[fullKey] = UnescapeYamlString(value);
            }
        }

        FlushPendingMapItem(pendingMapItem, currentList);
        return result;
    }

    private static void FlushPendingMapItem(Dictionary<string, string> pending, List<object>? list)
    {
        if (pending.Count > 0 && list != null)
        {
            list.Add(new Dictionary<string, string>(pending));
            pending.Clear();
        }
    }

    private static string GetFullPath(Stack<(string Key, int Indent)> path) =>
        string.Join(".", path.Reverse().Select(p => p.Key));

    private static string? GetValue(Dictionary<string, object> props, string key) =>
        props.TryGetValue(key, out var v) && v is string s ? s : null;

    private static List<string> GetList(Dictionary<string, object> props, string key) =>
        props.TryGetValue(key, out var v) && v is List<object> list
            ? list.OfType<string>().ToList()
            : [];

    private static List<FewShotExample> ParseFewShotExamples(Dictionary<string, object> props)
    {
        if (!props.TryGetValue("metadata.fewShotExamples", out var v) || v is not List<object> list)
            return [];

        return list
            .OfType<Dictionary<string, string>>()
            .Where(d => d.ContainsKey("user") && d.ContainsKey("assistant"))
            .Select(d => new FewShotExample(d["user"], d["assistant"]))
            .ToList();
    }

    private static string EscapeYamlString(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.Contains(':') || value.Contains('#') || value.Contains('"') ||
            value.Contains('\n') || value.StartsWith(' ') || value.EndsWith(' '))
            return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        return value;
    }

    private static string UnescapeYamlString(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        return value;
    }
}
