using AgentCraftLab.Autonomous.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Autonomous;

public class DynamicToolSetTests
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

    // ─── GetActiveTools ───

    [Fact]
    public void GetActiveTools_OnlyAlwaysAvailable_WhenNothingLoaded()
    {
        var always = new[] { MakeTool("Calculator"), MakeTool("WebSearch") };
        var set = new DynamicToolSet(always);

        var active = set.GetActiveTools();
        Assert.Equal(2, active.Count);
        Assert.Equal(0, set.LoadedCount);
    }

    [Fact]
    public void GetActiveTools_IncludesLoadedTools()
    {
        var always = new[] { MakeTool("Calculator") };
        var set = new DynamicToolSet(always);
        var index = BuildIndex(("NewTool", "a new tool"));

        set.LoadTools(["NewTool"], index);

        var active = set.GetActiveTools();
        Assert.Equal(2, active.Count);
        Assert.Equal(1, set.LoadedCount);
    }

    // ─── LoadTools ───

    [Fact]
    public void LoadTools_ReturnsNewlyLoadedNames()
    {
        var set = new DynamicToolSet([MakeTool("Safe")]);
        var index = BuildIndex(("Extra1", "d1"), ("Extra2", "d2"));

        var loaded = set.LoadTools(["Extra1", "Extra2"], index);
        Assert.Equal(2, loaded.Count);
        Assert.Contains("Extra1", loaded);
        Assert.Contains("Extra2", loaded);
    }

    [Fact]
    public void LoadTools_SkipsDuplicateLoad()
    {
        var set = new DynamicToolSet([]);
        var index = BuildIndex(("Tool1", "desc"));

        set.LoadTools(["Tool1"], index);
        var secondLoad = set.LoadTools(["Tool1"], index);

        Assert.Empty(secondLoad);
        Assert.Equal(1, set.LoadedCount);
    }

    [Fact]
    public void LoadTools_SkipsAlwaysAvailable()
    {
        var set = new DynamicToolSet([MakeTool("Calculator")]);
        var index = BuildIndex(("Calculator", "already available"));

        var loaded = set.LoadTools(["Calculator"], index);
        Assert.Empty(loaded);
    }

    [Fact]
    public void LoadTools_SkipsUnknownNames()
    {
        var set = new DynamicToolSet([]);
        var index = BuildIndex(("Tool1", "desc"));

        var loaded = set.LoadTools(["NonExistent"], index);
        Assert.Empty(loaded);
    }

    // ─── Unload ───

    [Fact]
    public void Unload_RemovesLoadedTool()
    {
        var set = new DynamicToolSet([]);
        var index = BuildIndex(("Tool1", "desc"));

        set.LoadTools(["Tool1"], index);
        Assert.Equal(1, set.LoadedCount);

        Assert.True(set.Unload("Tool1"));
        Assert.Equal(0, set.LoadedCount);
    }

    [Fact]
    public void Unload_ReturnsFalse_WhenNotLoaded()
    {
        var set = new DynamicToolSet([]);
        Assert.False(set.Unload("NonExistent"));
    }

    // ─── IsAvailable ───

    [Fact]
    public void IsAvailable_TrueForAlwaysAvailable()
    {
        var set = new DynamicToolSet([MakeTool("Calculator")]);
        Assert.True(set.IsAvailable("Calculator"));
    }

    [Fact]
    public void IsAvailable_TrueForLoaded()
    {
        var set = new DynamicToolSet([]);
        var index = BuildIndex(("Tool1", "desc"));
        set.LoadTools(["Tool1"], index);
        Assert.True(set.IsAvailable("Tool1"));
    }

    [Fact]
    public void IsAvailable_FalseForUnknown()
    {
        var set = new DynamicToolSet([MakeTool("Calculator")]);
        Assert.False(set.IsAvailable("Unknown"));
    }

    // ─── LoadCreatedTool ───

    [Fact]
    public void LoadCreatedTool_AddsToActiveTools()
    {
        var set = new DynamicToolSet([MakeTool("Base")]);
        var created = MakeTool("my_custom_tool");

        Assert.True(set.LoadCreatedTool("my_custom_tool", created));
        Assert.Equal(1, set.LoadedCount);
        Assert.True(set.IsAvailable("my_custom_tool"));

        var active = set.GetActiveTools();
        Assert.Equal(2, active.Count); // Base + my_custom_tool
    }

    [Fact]
    public void LoadCreatedTool_DuplicateName_ReturnsFalse()
    {
        var set = new DynamicToolSet([]);
        var tool = MakeTool("dup_tool");

        Assert.True(set.LoadCreatedTool("dup_tool", tool));
        Assert.False(set.LoadCreatedTool("dup_tool", tool));
        Assert.Equal(1, set.LoadedCount);
    }

    // ─── Thread Safety ───

    [Fact]
    public async Task ConcurrentLoadAndRead_DoesNotThrow()
    {
        var set = new DynamicToolSet([MakeTool("Base")]);
        var tools = Enumerable.Range(0, 50)
            .Select(i => ($"Tool{i}", $"desc{i}"))
            .ToArray();
        var index = BuildIndex(tools);

        var loadTask = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                set.LoadTools([$"Tool{i}"], index);
            }
        });

        var readTask = Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                _ = set.GetActiveTools();
            }
        });

        await Task.WhenAll(loadTask, readTask);
        Assert.True(set.LoadedCount > 0);
    }
}
