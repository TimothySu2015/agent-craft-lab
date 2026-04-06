using System.Text.Json;
using AgentCraftLab.Autonomous.Flow.Models;

namespace AgentCraftLab.Tests.Flow;

public class FlowCheckpointTests
{
    [Fact]
    public void FlowCheckpointSnapshot_JsonRoundTrip()
    {
        var snapshot = new FlowCheckpointSnapshot
        {
            PlanJson = """{"nodes":[{"type":"agent","name":"test"}]}""",
            CompletedNodeIndex = 3,
            PreviousResult = "Node 3 output text",
            SkipIndices = [5, 7],
            AccumulatedTokens = 12500,
        };

        var json = JsonSerializer.Serialize(snapshot);
        var deserialized = JsonSerializer.Deserialize<FlowCheckpointSnapshot>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized!.CompletedNodeIndex);
        Assert.Equal("Node 3 output text", deserialized.PreviousResult);
        Assert.Contains(5, deserialized.SkipIndices);
        Assert.Contains(7, deserialized.SkipIndices);
        Assert.Equal(12500, deserialized.AccumulatedTokens);
        Assert.Contains("test", deserialized.PlanJson);
    }

    [Fact]
    public void FlowCheckpointSnapshot_DefaultValues()
    {
        var snapshot = new FlowCheckpointSnapshot
        {
            PlanJson = "{}",
            CompletedNodeIndex = 0,
            PreviousResult = "",
        };

        Assert.Empty(snapshot.SkipIndices);
        Assert.Equal(0, snapshot.AccumulatedTokens);
        Assert.True(snapshot.Timestamp <= DateTime.UtcNow);
    }
}
