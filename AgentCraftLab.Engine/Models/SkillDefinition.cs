namespace AgentCraftLab.Engine.Models;

/// <summary>
/// Skill 分類。
/// </summary>
public enum SkillCategory
{
    DomainKnowledge,
    Methodology,
    OutputFormat,
    Persona,
    ToolPreset
}

/// <summary>
/// SkillCategory 顯示名稱。
/// </summary>
public static class SkillCategoryExtensions
{
    public static string ToLabel(this SkillCategory cat) => cat switch
    {
        SkillCategory.DomainKnowledge => "Domain Knowledge",
        SkillCategory.Methodology => "Methodology",
        SkillCategory.OutputFormat => "Output Format",
        SkillCategory.Persona => "Persona",
        SkillCategory.ToolPreset => "Tool Preset",
        _ => cat.ToString()
    };
}

/// <summary>
/// Skill 定義：可重用的「領域知識包」，掛載到 Agent 節點或 Flow 上。
/// </summary>
public record SkillDefinition(
    string Id,
    string DisplayName,
    string Description,
    string Instructions,
    SkillCategory Category,
    string Icon = "&#x1F3AF;",
    List<string>? Tools = null,
    List<FewShotExample>? FewShotExamples = null);

/// <summary>
/// Few-shot 範例對話。
/// </summary>
public record FewShotExample(string User, string Assistant);
