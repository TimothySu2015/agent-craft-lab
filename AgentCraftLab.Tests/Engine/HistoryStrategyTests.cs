using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class HistoryStrategyTests
{
    private static List<ChatMessage> CreateHistory(int userMessageCount)
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant.")
        };
        for (var i = 0; i < userMessageCount; i++)
        {
            history.Add(new ChatMessage(ChatRole.User, $"Question {i + 1}"));
            history.Add(new ChatMessage(ChatRole.Assistant, $"Answer {i + 1}"));
        }
        return history;
    }

    [Fact]
    public void SimpleTrimming_PreservesSystemMessage()
    {
        var strategy = new SimpleTrimmingStrategy();
        var history = CreateHistory(15); // 1 system + 30 messages = 31
        strategy.TrimHistory(history, 10);
        Assert.Equal(ChatRole.System, history[0].Role);
        Assert.Equal("You are a helpful assistant.", history[0].Text);
    }

    [Fact]
    public void SimpleTrimming_TrimsToRecentN()
    {
        var strategy = new SimpleTrimmingStrategy();
        var history = CreateHistory(15); // 31 total
        strategy.TrimHistory(history, 10);
        Assert.Equal(11, history.Count); // 1 system + 10 recent
    }

    [Fact]
    public void SimpleTrimming_ShortHistory_NoChange()
    {
        var strategy = new SimpleTrimmingStrategy();
        var history = CreateHistory(3); // 7 total
        strategy.TrimHistory(history, 20);
        Assert.Equal(7, history.Count);
    }

    [Fact]
    public void SlidingWindow_UnderThreshold_NoTrim()
    {
        var strategy = new SlidingWindowStrategy();
        var history = CreateHistory(5); // 11 total, threshold = 20*0.8+1 = 17
        strategy.TrimHistory(history, 20);
        Assert.Equal(11, history.Count);
    }

    [Fact]
    public void SlidingWindow_OverThreshold_TrimsTo60Pct()
    {
        var strategy = new SlidingWindowStrategy();
        var history = CreateHistory(15); // 31 total, threshold = 20*0.8+1 = 17
        strategy.TrimHistory(history, 20);
        var expectedKeep = (int)(20 * 0.6); // 12
        Assert.Equal(expectedKeep + 1, history.Count); // +1 for system
    }

    [Fact]
    public void SlidingWindow_PreservesSystemMessage()
    {
        var strategy = new SlidingWindowStrategy();
        var history = CreateHistory(15);
        strategy.TrimHistory(history, 20);
        Assert.Equal(ChatRole.System, history[0].Role);
    }

    [Fact]
    public void HistoryTrimHelper_ExactBoundary_NoTrim()
    {
        var strategy = new SimpleTrimmingStrategy();
        var history = CreateHistory(5); // 11 total
        strategy.TrimHistory(history, 10); // 10 + 1 = 11 = count → no trim
        Assert.Equal(11, history.Count);
    }
}
