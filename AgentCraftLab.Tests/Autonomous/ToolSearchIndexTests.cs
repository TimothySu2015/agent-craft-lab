using AgentCraftLab.Autonomous.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Autonomous;

public class ToolSearchIndexTests
{
    // ─── 輔助方法 ───

    private static AITool MakeTool(string name, string description = "")
    {
        return AIFunctionFactory.Create(() => "ok", name, description);
    }

    private static ToolSearchIndex BuildIndex(params (string Name, string Desc)[] tools)
    {
        return new ToolSearchIndex(tools.Select(t => MakeTool(t.Name, t.Desc)));
    }

    // ─── Search 基本功能 ───

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var index = BuildIndex(("WebSearch", "Search the web"));
        Assert.Empty(index.Search(""));
        Assert.Empty(index.Search("   "));
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsEmpty()
    {
        var index = new ToolSearchIndex([]);
        Assert.Empty(index.Search("web"));
    }

    [Fact]
    public void Search_ExactNameMatch_RanksHighest()
    {
        var index = BuildIndex(
            ("WebSearch", "Search the web using DuckDuckGo"),
            ("Calculator", "Calculate math expressions"),
            ("UrlFetch", "Fetch content from a URL"));

        var results = index.Search("WebSearch");
        Assert.NotEmpty(results);
        Assert.Equal("WebSearch", results[0].Name);
    }

    [Fact]
    public void Search_DescriptionKeyword_FindsTool()
    {
        var index = BuildIndex(
            ("AzureWebSearch", "Bing-powered web search"),
            ("Calculator", "Calculate math expressions"));

        var results = index.Search("Bing");
        Assert.Single(results);
        Assert.Equal("AzureWebSearch", results[0].Name);
    }

    [Fact]
    public void Search_RespectsMaxResults()
    {
        var index = BuildIndex(
            ("Search1", "web search tool"),
            ("Search2", "web search tool"),
            ("Search3", "web search tool"),
            ("Search4", "web search tool"),
            ("Search5", "web search tool"));

        var results = index.Search("search", maxResults: 2);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Search_CaseInsensitive()
    {
        var index = BuildIndex(("WebSearch", "Search the WEB"));
        var results = index.Search("WEBSEARCH");
        Assert.NotEmpty(results);
    }

    // ─── FindByName ───

    [Fact]
    public void FindByName_ExactMatch_ReturnsTool()
    {
        var index = BuildIndex(("Calculator", "math"));
        Assert.NotNull(index.FindByName("Calculator"));
    }

    [Fact]
    public void FindByName_CaseInsensitive()
    {
        var index = BuildIndex(("Calculator", "math"));
        Assert.NotNull(index.FindByName("calculator"));
    }

    [Fact]
    public void FindByName_NotFound_ReturnsNull()
    {
        var index = BuildIndex(("Calculator", "math"));
        Assert.Null(index.FindByName("NonExistent"));
    }

    // ─── FindByNames ───

    [Fact]
    public void FindByNames_ReturnsMatchingTools()
    {
        var index = BuildIndex(
            ("Tool1", "desc1"),
            ("Tool2", "desc2"),
            ("Tool3", "desc3"));

        var found = index.FindByNames(["Tool1", "Tool3"]);
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void FindByNames_IgnoresUnknownNames()
    {
        var index = BuildIndex(("Tool1", "desc1"));
        var found = index.FindByNames(["Tool1", "Unknown"]);
        Assert.Single(found);
    }

    // ─── Count / ListAllNames ───

    [Fact]
    public void Count_ReturnsCorrectNumber()
    {
        var index = BuildIndex(("A", "a"), ("B", "b"));
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void ListAllNames_ReturnsAllToolNames()
    {
        var index = BuildIndex(("Alpha", "a"), ("Beta", "b"));
        var names = index.ListAllNames();
        Assert.Contains("Alpha", names);
        Assert.Contains("Beta", names);
    }
}
