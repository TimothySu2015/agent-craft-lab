using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class SkillMdConverterTests
{
    [Fact]
    public void RoundTrip_FullSkill()
    {
        var skill = new SkillDefinition(
            "code_review",
            "程式碼審查",
            "提供專業的程式碼審查能力",
            "你具備專業的程式碼審查能力。\n\n1. 安全性\n2. 效能",
            SkillCategory.DomainKnowledge,
            "&#x1F50D;",
            ["search_web", "read_file"],
            [new FewShotExample("請幫我審查", "好的，讓我檢查")]);

        var md = SkillMdConverter.ToSkillMd(skill);
        var parsed = SkillMdConverter.FromSkillMd(md);

        Assert.NotNull(parsed);
        Assert.Equal("code_review", parsed.Id);
        Assert.Equal("程式碼審查", parsed.DisplayName);
        Assert.Equal("提供專業的程式碼審查能力", parsed.Description);
        Assert.Contains("安全性", parsed.Instructions);
        Assert.Equal(SkillCategory.DomainKnowledge, parsed.Category);
        Assert.Equal("&#x1F50D;", parsed.Icon);
        Assert.Equal(2, parsed.Tools!.Count);
        Assert.Contains("search_web", parsed.Tools);
        Assert.Contains("read_file", parsed.Tools);
        Assert.NotNull(parsed.FewShotExamples);
        Assert.Single(parsed.FewShotExamples);
        Assert.Equal("請幫我審查", parsed.FewShotExamples![0].User);
        Assert.Equal("好的，讓我檢查", parsed.FewShotExamples[0].Assistant);
    }

    [Fact]
    public void RoundTrip_MinimalSkill()
    {
        var skill = new SkillDefinition(
            "simple",
            "Simple Skill",
            "A simple skill",
            "Do something useful.",
            SkillCategory.Persona);

        var md = SkillMdConverter.ToSkillMd(skill);
        var parsed = SkillMdConverter.FromSkillMd(md);

        Assert.NotNull(parsed);
        Assert.Equal("simple", parsed.Id);
        Assert.Equal("Simple Skill", parsed.DisplayName);
        Assert.Equal("A simple skill", parsed.Description);
        Assert.Equal("Do something useful.", parsed.Instructions);
        Assert.Equal(SkillCategory.Persona, parsed.Category);
        Assert.Null(parsed.Tools);
        Assert.Null(parsed.FewShotExamples);
    }

    [Fact]
    public void ToSkillMd_ContainsFrontmatter()
    {
        var skill = new SkillDefinition("test", "Test", "Desc", "Body", SkillCategory.Methodology);
        var md = SkillMdConverter.ToSkillMd(skill);

        Assert.StartsWith("---", md);
        Assert.Contains("name: test", md);
        Assert.Contains("description: Desc", md);
        Assert.Contains("category: Methodology", md);
        Assert.Contains("Body", md);
    }

    [Fact]
    public void FromSkillMd_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(SkillMdConverter.FromSkillMd(""));
        Assert.Null(SkillMdConverter.FromSkillMd(null!));
        Assert.Null(SkillMdConverter.FromSkillMd("  "));
    }

    [Fact]
    public void FromSkillMd_NoFrontmatter_ReturnsNull()
    {
        var md = "# Just a markdown file\n\nNo frontmatter here.";
        Assert.Null(SkillMdConverter.FromSkillMd(md));
    }

    [Fact]
    public void FromSkillMd_MissingName_UsesFallbackId()
    {
        var md = "---\ndescription: test\n---\nBody";
        var parsed = SkillMdConverter.FromSkillMd(md, fallbackId: "fallback-id");

        Assert.NotNull(parsed);
        Assert.Equal("fallback-id", parsed.Id);
    }

    [Fact]
    public void FromSkillMd_UnknownCategory_DefaultsToDomainKnowledge()
    {
        var md = "---\nname: test\nmetadata:\n  category: UnknownCategory\n---\nBody";
        var parsed = SkillMdConverter.FromSkillMd(md);

        Assert.NotNull(parsed);
        Assert.Equal(SkillCategory.DomainKnowledge, parsed.Category);
    }

    [Fact]
    public void RoundTrip_ToolPresetCategory()
    {
        var skill = new SkillDefinition(
            "web_researcher",
            "Web Researcher",
            "網路研究員",
            "你是一位網路研究員。",
            SkillCategory.ToolPreset,
            Tools: ["search_web", "browse_url"]);

        var md = SkillMdConverter.ToSkillMd(skill);
        var parsed = SkillMdConverter.FromSkillMd(md);

        Assert.NotNull(parsed);
        Assert.Equal(SkillCategory.ToolPreset, parsed.Category);
        Assert.Equal(2, parsed.Tools!.Count);
    }

    [Fact]
    public void FromSkillMd_ExternalFormat_BasicCompat()
    {
        // 模擬 Framework 標準格式（沒有 metadata 擴展）
        var md = """
            ---
            name: external-skill
            description: A skill from the community
            ---

            ## Instructions

            Follow these steps:
            1. Step one
            2. Step two
            """;

        var parsed = SkillMdConverter.FromSkillMd(md);

        Assert.NotNull(parsed);
        Assert.Equal("external-skill", parsed.Id);
        Assert.Equal("A skill from the community", parsed.Description);
        Assert.Equal("external-skill", parsed.DisplayName); // fallback to name
        Assert.Equal(SkillCategory.DomainKnowledge, parsed.Category); // default
        Assert.Contains("Step one", parsed.Instructions);
    }

    [Fact]
    public void ToSkillMd_DescriptionWithSpecialChars_Escaped()
    {
        var skill = new SkillDefinition(
            "test",
            "Test: Special \"chars\"",
            "Description with: colon and #hash",
            "Body",
            SkillCategory.DomainKnowledge);

        var md = SkillMdConverter.ToSkillMd(skill);
        Assert.Contains("\"Description with: colon and #hash\"", md);

        var parsed = SkillMdConverter.FromSkillMd(md);
        Assert.NotNull(parsed);
        Assert.Equal("Description with: colon and #hash", parsed.Description);
    }
}
