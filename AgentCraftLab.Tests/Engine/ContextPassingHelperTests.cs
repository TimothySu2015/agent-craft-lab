using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;
using AgentCraftLab.Engine.Strategies.NodeExecutors;

namespace AgentCraftLab.Tests.Engine;

public class ContextPassingHelperTests
{
    private static ImperativeExecutionState CreateState(
        string contextPassing,
        string originalMessage,
        Dictionary<string, string>? nodeResults = null,
        Dictionary<string, NodeConfig>? nodeMap = null)
    {
        return new ImperativeExecutionState
        {
            ContextPassing = contextPassing,
            OriginalUserMessage = originalMessage,
            NodeResults = nodeResults ?? new(),
            NodeMap = nodeMap ?? new(),
            Adjacency = new(),
            Agents = new(),
            ChatClients = new(),
            ChatHistories = new(),
            LoopCounters = new(),
            AgentContext = AgentExecutionContext.Empty,
            Request = new WorkflowExecutionRequest(),
            HistoryStrategy = new SimpleTrimmingStrategy(),
        };
    }

    [Fact]
    public void PreviousOnly_ReturnsEmpty()
    {
        var state = CreateState(ContextPassingModes.PreviousOnly, "Hello");
        var result = ContextPassingHelper.BuildContextPrefix(state, "node1");
        Assert.Equal("", result);
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        var state = CreateState("", "Hello");
        var result = ContextPassingHelper.BuildContextPrefix(state, "node1");
        Assert.Equal("", result);
    }

    [Fact]
    public void WithOriginal_ContainsOriginalMessage()
    {
        var state = CreateState(ContextPassingModes.WithOriginal, "Translate this to Japanese");
        var result = ContextPassingHelper.BuildContextPrefix(state, "node1");
        Assert.Contains("Translate this to Japanese", result);
        Assert.Contains("do not fabricate information", result);
    }

    [Fact]
    public void WithOriginal_ContainsContextMarker()
    {
        var state = CreateState(ContextPassingModes.WithOriginal, "Hello");
        var result = ContextPassingHelper.BuildContextPrefix(state, "node1");
        Assert.Contains(ContextPassingHelper.ContextMarker, result);
    }

    [Fact]
    public void Accumulate_NoResults_OnlyShowsOriginal()
    {
        var state = CreateState(ContextPassingModes.Accumulate, "Hello");
        var result = ContextPassingHelper.BuildContextPrefix(state, "node1");
        Assert.Contains("Hello", result);
        Assert.DoesNotContain("[Previous Step Results]", result);
    }

    [Fact]
    public void Accumulate_ShowsPreviousResults()
    {
        var nodeMap = new Dictionary<string, NodeConfig>
        {
            ["n1"] = new AgentNode { Id = "n1", Name = "Researcher" },
            ["n2"] = new AgentNode { Id = "n2", Name = "Writer" },
        };
        var nodeResults = new Dictionary<string, string>
        {
            ["n1"] = "Research output here",
            ["n2"] = "Writer output here",
        };
        var state = CreateState(ContextPassingModes.Accumulate, "Original", nodeResults, nodeMap);
        var result = ContextPassingHelper.BuildContextPrefix(state, "n3");

        Assert.Contains("[Previous Step Results]", result);
        Assert.Contains("Researcher", result);
        Assert.Contains("Research output here", result);
        Assert.Contains("Writer", result);
        Assert.Contains("Writer output here", result);
    }

    [Fact]
    public void Accumulate_ExcludesCurrentNode()
    {
        var nodeMap = new Dictionary<string, NodeConfig>
        {
            ["n1"] = new AgentNode { Id = "n1", Name = "Researcher" },
            ["n2"] = new AgentNode { Id = "n2", Name = "Writer" },
        };
        var nodeResults = new Dictionary<string, string>
        {
            ["n1"] = "Research output",
            ["n2"] = "Writer output",
        };
        var state = CreateState(ContextPassingModes.Accumulate, "Original", nodeResults, nodeMap);

        // n2 is the current node — should not appear in results
        var result = ContextPassingHelper.BuildContextPrefix(state, "n2");

        Assert.Contains("Researcher", result);
        Assert.Contains("Research output", result);
        Assert.DoesNotContain("Writer", result);
    }

    [Fact]
    public void Accumulate_TruncatesLongOutput()
    {
        var longOutput = new string('x', 600);
        var nodeResults = new Dictionary<string, string>
        {
            ["n1"] = longOutput,
        };
        var nodeMap = new Dictionary<string, NodeConfig>
        {
            ["n1"] = new AgentNode { Id = "n1", Name = "LongAgent" },
        };
        var state = CreateState(ContextPassingModes.Accumulate, "Original", nodeResults, nodeMap);
        var result = ContextPassingHelper.BuildContextPrefix(state, "n2");

        Assert.Contains("...", result);
        // 500 chars + "..." should be present, not the full 600
        Assert.DoesNotContain(longOutput, result);
    }

    [Fact]
    public void Accumulate_RespectsMaxTotalChars()
    {
        var nodeResults = new Dictionary<string, string>();
        var nodeMap = new Dictionary<string, NodeConfig>();

        // Create many nodes with 400-char outputs — only a few should fit within 2000 total
        for (var i = 0; i < 20; i++)
        {
            var id = $"n{i}";
            nodeResults[id] = new string((char)('a' + (i % 26)), 400);
            nodeMap[id] = new AgentNode { Id = id, Name = $"Agent{i}" };
        }

        var state = CreateState(ContextPassingModes.Accumulate, "Original", nodeResults, nodeMap);
        var result = ContextPassingHelper.BuildContextPrefix(state, "current");

        // Should not contain all 20 nodes (20 * ~410 chars = ~8200 > 2000 limit)
        var agentCount = 0;
        for (var i = 0; i < 20; i++)
        {
            if (result.Contains($"Agent{i}"))
            {
                agentCount++;
            }
        }

        Assert.True(agentCount > 0, "Should include at least one node");
        Assert.True(agentCount < 20, "Should not include all 20 nodes due to max total chars limit");
    }
}
