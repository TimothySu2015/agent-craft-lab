using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Autonomous.Flow.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Flow;

public class FlowWorkingMemoryTests
{
    [Fact]
    public void WriteAndRead()
    {
        var memory = new FlowWorkingMemory();
        memory.Write("company", "TSMC");
        Assert.Equal("TSMC", memory.Read("company"));
    }

    [Fact]
    public void ReadMissing_ReturnsNull()
    {
        var memory = new FlowWorkingMemory();
        Assert.Null(memory.Read("nonexistent"));
    }

    [Fact]
    public void Snapshot_ReturnsAllEntries()
    {
        var memory = new FlowWorkingMemory();
        memory.Write("a", "1");
        memory.Write("b", "2");

        var snapshot = memory.Snapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal("1", snapshot["a"]);
    }

    [Fact]
    public void ToPromptSection_Empty_ReturnsEmpty()
    {
        var memory = new FlowWorkingMemory();
        Assert.Equal("", memory.ToPromptSection());
    }

    [Fact]
    public void ToPromptSection_WithData_ContainsEntries()
    {
        var memory = new FlowWorkingMemory();
        memory.Write("revenue", "$50B");
        memory.Write("employees", "50000");

        var section = memory.ToPromptSection();
        Assert.Contains("Working Memory", section);
        Assert.Contains("revenue: $50B", section);
        Assert.Contains("employees: 50000", section);
    }

    [Fact]
    public void BuildAgentMessages_WithMemory_InjectsInSystemPrompt()
    {
        var memory = new FlowWorkingMemory();
        memory.Write("key1", "value1");

        var messages = FlowNodeRunner.BuildAgentMessages(
            "Analyze the data.", ["search"], "input text", "openai", memory);

        var systemText = string.Join(" ", messages.Where(m => m.Role == ChatRole.System).Select(m => m.Text));
        Assert.Contains("key1: value1", systemText);
        Assert.Contains("Working Memory", systemText);
    }

    [Fact]
    public void BuildAgentMessages_EmptyMemory_NoInjection()
    {
        var memory = new FlowWorkingMemory();

        var messages = FlowNodeRunner.BuildAgentMessages(
            "Analyze.", null, "input", "openai", memory);

        var systemText = string.Join(" ", messages.Where(m => m.Role == ChatRole.System).Select(m => m.Text));
        Assert.DoesNotContain("Working Memory", systemText);
    }
}
