using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Autonomous.Services;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Autonomous;

public class SystemPromptBuilderTests
{
    [Theory]
    [InlineData("比較 NVIDIA 和 Tesla 的股價走勢", true)]
    [InlineData("分析 AWS、Azure、GCP 三個平台的 AI 推論成本", true)]
    [InlineData("research and compare the top 5 AI chips in 2026", true)]
    [InlineData("evaluate tradeoff between cost and performance", true)]
    [InlineData("什麼是 TCP/IP？", false)]
    [InlineData("hello", false)]
    [InlineData("現在幾點", false)]
    public void IsComplexGoal_CorrectlyClassifies(string goal, bool expected)
    {
        var result = SystemPromptBuilder.IsComplexGoal(goal);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsComplexGoal_LongText_IsComplex()
    {
        // 超過 20 個字，即使沒有複雜關鍵字也是複雜
        var longGoal = string.Join(" ", Enumerable.Repeat("word", 25));
        Assert.True(SystemPromptBuilder.IsComplexGoal(longGoal));
    }

    [Fact]
    public void IsComplexGoal_ShortWithoutKeywords_IsSimple()
    {
        Assert.False(SystemPromptBuilder.IsComplexGoal("查股價"));
    }

    // ─── Tool Search 模式 prompt 差異 ───

    [Fact]
    public void Build_WithSearchableTools_ContainsSearchGuidance()
    {
        var builder = new SystemPromptBuilder(new SkillRegistryService());
        var request = new AutonomousRequest
        {
            Goal = "test",
            MaxIterations = 10,
            ToolLimits = new ToolCallLimits(),
            Credentials = new()
        };

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(() => "ok", "Calculator", "math tool")
        };

        var prompt = builder.Build(request, tools, false, searchableToolCount: 25);

        Assert.Contains("25 additional tools", prompt);
        Assert.Contains("search_tools", prompt);
        Assert.Contains("load_tools", prompt);
    }

    [Fact]
    public void Build_WithoutSearchableTools_NoSearchGuidance()
    {
        var builder = new SystemPromptBuilder(new SkillRegistryService());
        var request = new AutonomousRequest
        {
            Goal = "test",
            MaxIterations = 10,
            ToolLimits = new ToolCallLimits(),
            Credentials = new()
        };

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(() => "ok", "Calculator", "math tool")
        };

        var prompt = builder.Build(request, tools, false, searchableToolCount: 0);

        Assert.DoesNotContain("search_tools", prompt);
        Assert.Contains("Calculator", prompt);
    }
}
