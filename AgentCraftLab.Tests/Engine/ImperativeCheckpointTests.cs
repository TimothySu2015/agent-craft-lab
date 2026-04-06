using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Tests.Engine;

public class ImperativeCheckpointTests
{
    [Fact]
    public void Snapshot_JsonRoundTrip()
    {
        var snapshot = new ImperativeCheckpointSnapshot
        {
            CompletedNodeIds = ["node-1", "node-2", "node-3"],
            PreviousResult = "Node 3 output text",
            NextNodeId = "node-4",
            NodeResults = new() { ["node-1"] = "result-1", ["node-2"] = "result-2" },
            LoopCounters = new() { ["loop-1"] = 3 },
            OriginalUserMessage = "Hello",
            ContextPassing = "accumulate",
        };

        var json = snapshot.Serialize();
        var deserialized = ImperativeCheckpointSnapshot.Deserialize(json);

        Assert.NotNull(deserialized);
        Assert.Equal(["node-1", "node-2", "node-3"], deserialized!.CompletedNodeIds);
        Assert.Equal("Node 3 output text", deserialized.PreviousResult);
        Assert.Equal("node-4", deserialized.NextNodeId);
        Assert.Equal("result-1", deserialized.NodeResults["node-1"]);
        Assert.Equal("result-2", deserialized.NodeResults["node-2"]);
        Assert.Equal(3, deserialized.LoopCounters["loop-1"]);
        Assert.Equal("Hello", deserialized.OriginalUserMessage);
        Assert.Equal("accumulate", deserialized.ContextPassing);
    }

    [Fact]
    public void Snapshot_DefaultValues()
    {
        var snapshot = new ImperativeCheckpointSnapshot
        {
            CompletedNodeIds = [],
            PreviousResult = "",
            NextNodeId = "",
        };

        Assert.Empty(snapshot.NodeResults);
        Assert.Empty(snapshot.LoopCounters);
        Assert.Equal("", snapshot.OriginalUserMessage);
        Assert.Equal("previous-only", snapshot.ContextPassing);
        Assert.True(snapshot.Timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void Snapshot_WithNodeResults_Accumulate()
    {
        var results = new Dictionary<string, string>
        {
            ["agent-a"] = "Research findings about AI",
            ["agent-b"] = "Summary of the research",
            ["code-1"] = """{"formatted": true}""",
        };

        var snapshot = new ImperativeCheckpointSnapshot
        {
            CompletedNodeIds = ["agent-a", "agent-b", "code-1"],
            PreviousResult = results["code-1"],
            NextNodeId = "agent-c",
            NodeResults = results,
            ContextPassing = "accumulate",
        };

        var json = snapshot.Serialize();
        var restored = ImperativeCheckpointSnapshot.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(3, restored!.NodeResults.Count);
        Assert.Equal("Research findings about AI", restored.NodeResults["agent-a"]);
        Assert.Contains("formatted", restored.NodeResults["code-1"]);
    }

    [Fact]
    public void Serialize_ProducesCamelCase()
    {
        var snapshot = new ImperativeCheckpointSnapshot
        {
            CompletedNodeIds = ["n1"],
            PreviousResult = "test",
            NextNodeId = "n2",
        };

        var json = snapshot.Serialize();

        Assert.Contains("completedNodeIds", json);
        Assert.Contains("previousResult", json);
        Assert.Contains("nextNodeId", json);
        Assert.DoesNotContain("CompletedNodeIds", json);
    }

    [Fact]
    public void Deserialize_InvalidJson_ReturnsNull()
    {
        var result = ImperativeCheckpointSnapshot.Deserialize("not valid json");
        Assert.Null(result);
    }
}
