using AgentCraftLab.Engine.Services.Compression;

namespace AgentCraftLab.Tests.Engine;

public class CompressionStateTests
{
    [Fact]
    public void InitialState_AllZero()
    {
        var state = new CompressionState();

        Assert.Equal(0, state.CompressionsApplied);
        Assert.Equal(0, state.TotalTokensSaved);
        Assert.Null(state.ApiCachedTokenCount);
        Assert.Null(state.LastCompressionTime);
        Assert.Empty(state.TruncatedToolCallIds);
    }

    [Fact]
    public void RecordCompression_IncrementsCountAndTokens()
    {
        var state = new CompressionState();

        state.RecordCompression(500);
        Assert.Equal(1, state.CompressionsApplied);
        Assert.Equal(500, state.TotalTokensSaved);
        Assert.NotNull(state.LastCompressionTime);

        state.RecordCompression(300);
        Assert.Equal(2, state.CompressionsApplied);
        Assert.Equal(800, state.TotalTokensSaved);
    }

    [Fact]
    public void TruncatedToolCallIds_TracksIds()
    {
        var state = new CompressionState();

        state.TruncatedToolCallIds.Add("call-1");
        state.TruncatedToolCallIds.Add("call-2");
        state.TruncatedToolCallIds.Add("call-1"); // 重複

        Assert.Equal(2, state.TruncatedToolCallIds.Count);
        Assert.Contains("call-1", state.TruncatedToolCallIds);
        Assert.Contains("call-2", state.TruncatedToolCallIds);
    }
}
