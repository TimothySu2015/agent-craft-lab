using System.ComponentModel;
using AgentCraftLab.Autonomous.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Autonomous;

public class ToolDelegationTests
{
    private readonly SafeWhitelistToolDelegation _strategy = new();

    private static AITool CreateMockTool(string name)
    {
        return AIFunctionFactory.Create(
            [Description("test")] () => "ok",
            name);
    }

    [Fact]
    public void ResolveTools_ExactMatch_ReturnsMatched()
    {
        var tools = new List<AITool> { CreateMockTool("AzureWebSearch"), CreateMockTool("Calculator") };
        var result = _strategy.ResolveTools(tools, ["AzureWebSearch"]);
        Assert.Single(result);
    }

    [Fact]
    public void ResolveTools_FunctionsPrefix_StillMatches()
    {
        var tools = new List<AITool> { CreateMockTool("AzureWebSearch"), CreateMockTool("Calculator") };
        var result = _strategy.ResolveTools(tools, ["functions.AzureWebSearch"]);
        Assert.Single(result);
    }

    [Fact]
    public void ResolveTools_CaseInsensitive_Matches()
    {
        var tools = new List<AITool> { CreateMockTool("AzureWebSearch") };
        var result = _strategy.ResolveTools(tools, ["azurewebsearch"]);
        Assert.Single(result);
    }

    [Fact]
    public void ResolveTools_NonExistent_ReturnsEmpty()
    {
        var tools = new List<AITool> { CreateMockTool("AzureWebSearch") };
        var result = _strategy.ResolveTools(tools, ["NonExistentTool"]);
        Assert.Empty(result);
    }

    [Fact]
    public void ResolveTools_NoToolsRequested_ReturnsSafeDefaults()
    {
        var tools = new List<AITool>
        {
            CreateMockTool("AzureWebSearch"),
            CreateMockTool("send_email"),
            CreateMockTool("calculator")
        };
        var result = _strategy.ResolveTools(tools, []);

        // AzureWebSearch → azure_web_search naming mismatch, safe list uses snake_case IDs
        // calculator is in safe list
        Assert.Contains(result, t => t is AIFunction f && f.Name == "calculator");
        Assert.DoesNotContain(result, t => t is AIFunction f && f.Name == "send_email");
    }

    [Fact]
    public void ResolveTools_MultipleFunctionsPrefixed_AllMatch()
    {
        var tools = new List<AITool>
        {
            CreateMockTool("AzureWebSearch"),
            CreateMockTool("Calculator"),
            CreateMockTool("WikiSearch")
        };
        var result = _strategy.ResolveTools(tools,
            ["functions.AzureWebSearch", "functions.Calculator"]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ResolveTools_DuplicateRequested_BothResolve()
    {
        var tools = new List<AITool> { CreateMockTool("AzureWebSearch") };
        var result = _strategy.ResolveTools(tools,
            ["AzureWebSearch", "functions.AzureWebSearch"]);
        // Both normalize to "AzureWebSearch" — LINQ returns same tool twice (no dedup)
        Assert.Equal(2, result.Count);
        Assert.All(result, t => Assert.Equal("AzureWebSearch", ((AIFunction)t).Name));
    }
}
