using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Tests.Engine;

public class ImperativeResumeTests
{
    [Fact]
    public void Checkpoint_CanRestoreForRerun_WithModifiedNodeResults()
    {
        // Simulate: A → B → C completed, want to rerun C
        var checkpoint = new ImperativeCheckpointSnapshot
        {
            CompletedNodeIds = ["node-a", "node-b"],
            PreviousResult = "B's output",
            NextNodeId = "node-c",
            NodeResults = new() { ["node-a"] = "A result", ["node-b"] = "B result" },
            LoopCounters = new(),
            OriginalUserMessage = "Hello",
            ContextPassing = "accumulate",
        };

        // Serialize → Deserialize (simulates checkpoint store round-trip)
        var json = checkpoint.Serialize();
        var restored = ImperativeCheckpointSnapshot.Deserialize(json);

        Assert.NotNull(restored);
        // Verify: PreviousResult = B's output (will be C's input)
        Assert.Equal("B's output", restored!.PreviousResult);
        // Verify: NextNodeId = C (where to resume)
        Assert.Equal("node-c", restored.NextNodeId);
        // Verify: CompletedNodeIds does NOT contain C
        Assert.DoesNotContain("node-c", restored.CompletedNodeIds);
        Assert.Contains("node-b", restored.CompletedNodeIds);
        // Verify: NodeResults preserved for accumulate mode
        Assert.Equal(2, restored.NodeResults.Count);
        Assert.Equal("A result", restored.NodeResults["node-a"]);
    }

    [Fact]
    public void Checkpoint_OriginalUserMessage_PreservedAcrossRerun()
    {
        var checkpoint = new ImperativeCheckpointSnapshot
        {
            CompletedNodeIds = ["node-a"],
            PreviousResult = "A's output",
            NextNodeId = "node-b",
            OriginalUserMessage = "Summarize this article",
            ContextPassing = "with-original",
        };

        var json = checkpoint.Serialize();
        var restored = ImperativeCheckpointSnapshot.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal("Summarize this article", restored!.OriginalUserMessage);
        Assert.Equal("with-original", restored.ContextPassing);
    }

    [Fact]
    public void Checkpoint_LoopCounters_PreservedForRerun()
    {
        var checkpoint = new ImperativeCheckpointSnapshot
        {
            CompletedNodeIds = ["loop-1"],
            PreviousResult = "iteration 3 result",
            NextNodeId = "node-after-loop",
            LoopCounters = new() { ["loop-1"] = 3 },
        };

        var json = checkpoint.Serialize();
        var restored = ImperativeCheckpointSnapshot.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(3, restored!.LoopCounters["loop-1"]);
    }

    [Fact]
    public void Checkpoint_FindCorrectSnapshot_ForRerunNode()
    {
        // Simulate multiple checkpoints: after A, after B, after C
        var snapshots = new List<ImperativeCheckpointSnapshot>
        {
            new()
            {
                CompletedNodeIds = ["node-a"],
                PreviousResult = "A output",
                NextNodeId = "node-b",
            },
            new()
            {
                CompletedNodeIds = ["node-a", "node-b"],
                PreviousResult = "B output",
                NextNodeId = "node-c",
            },
            new()
            {
                CompletedNodeIds = ["node-a", "node-b", "node-c"],
                PreviousResult = "C output",
                NextNodeId = "node-d",
            },
        };

        // Want to rerun node-c: find last snapshot where NextNodeId == "node-c"
        var target = snapshots
            .AsEnumerable().Reverse()
            .FirstOrDefault(s => s.NextNodeId == "node-c");

        Assert.NotNull(target);
        Assert.Equal("B output", target!.PreviousResult);
        Assert.DoesNotContain("node-c", target.CompletedNodeIds);
    }
}
