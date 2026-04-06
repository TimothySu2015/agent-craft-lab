using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

/// <summary>
/// SkillRegistryService 完整性驗證 — 確保所有 Skill 正確註冊。
/// 新增 Skill 時如果忘了加到 AllSkillIds，測試會失敗。
/// </summary>
public sealed class SkillRegistryTests
{
    private readonly SkillRegistryService _registry = new();

    /// <summary>所有已註冊的 Skill ID — 新增 Skill 時加到這裡。</summary>
    public static readonly string[] AllSkillIds =
    [
        // Domain Knowledge
        "code_review", "legal_review",
        // Methodology
        "structured_reasoning", "swot_analysis", "debate_council",
        // Output Format
        "formal_writing", "technical_documentation",
        // Persona
        "customer_service", "senior_engineer",
        // Tool Preset
        "web_researcher", "data_analyst",
    ];

    // ═══════════════════════════════════════════════
    // 註冊完整性
    // ═══════════════════════════════════════════════

    [Fact]
    public void AllExpectedSkills_AreRegistered()
    {
        var registered = _registry.GetAvailableSkills().Select(s => s.Id).ToHashSet();

        foreach (var id in AllSkillIds)
        {
            Assert.True(registered.Contains(id),
                $"Skill '{id}' is listed in AllSkillIds but not registered in SkillRegistryService");
        }
    }

    [Fact]
    public void NoUnexpectedSkills_AreRegistered()
    {
        var registered = _registry.GetAvailableSkills().Select(s => s.Id).ToHashSet();
        var expected = AllSkillIds.ToHashSet();

        foreach (var id in registered)
        {
            Assert.True(expected.Contains(id),
                $"Skill '{id}' is registered but not listed in AllSkillIds — please add it to the test");
        }
    }

    [Fact]
    public void AllSkills_HaveUniqueIds()
    {
        var skills = _registry.GetAvailableSkills();
        var ids = skills.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void AllSkills_HaveNonEmptyFields()
    {
        var skills = _registry.GetAvailableSkills();
        foreach (var skill in skills)
        {
            Assert.False(string.IsNullOrWhiteSpace(skill.Id), "Skill has empty ID");
            Assert.False(string.IsNullOrWhiteSpace(skill.DisplayName), $"Skill '{skill.Id}' has empty DisplayName");
            Assert.False(string.IsNullOrWhiteSpace(skill.Description), $"Skill '{skill.Id}' has empty Description");
            Assert.False(string.IsNullOrWhiteSpace(skill.Instructions), $"Skill '{skill.Id}' has empty Instructions");
        }
    }

    [Fact]
    public void SkillCount_MatchesExpected()
    {
        var skills = _registry.GetAvailableSkills();
        Assert.Equal(AllSkillIds.Length, skills.Count);
    }

    // ═══════════════════════════════════════════════
    // Skill 查詢
    // ═══════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AllSkillIdData))]
    public void GetById_ReturnsDefinition(string skillId)
    {
        var skill = _registry.GetById(skillId, null);
        Assert.NotNull(skill);
        Assert.Equal(skillId, skill!.Id);
    }

    [Fact]
    public void GetById_UnknownId_ReturnsNull()
    {
        var skill = _registry.GetById("nonexistent_skill", null);
        Assert.Null(skill);
    }

    // ═══════════════════════════════════════════════
    // Tool Preset Skills 的工具引用驗證
    // ═══════════════════════════════════════════════

    [Fact]
    public void ToolPresetSkills_ReferenceValidToolIds()
    {
        var validToolIds = ToolRegistryTests.AllToolIds.ToHashSet();
        var skills = _registry.GetAvailableSkills()
            .Where(s => s.Tools is { Count: > 0 });

        foreach (var skill in skills)
        {
            foreach (var toolId in skill.Tools!)
            {
                Assert.True(validToolIds.Contains(toolId),
                    $"Skill '{skill.Id}' references tool '{toolId}' which is not in ToolRegistryTests.AllToolIds");
            }
        }
    }

    public static IEnumerable<object[]> AllSkillIdData()
        => AllSkillIds.Select(id => new object[] { id });
}
