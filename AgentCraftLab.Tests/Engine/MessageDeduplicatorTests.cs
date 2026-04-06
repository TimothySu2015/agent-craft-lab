using AgentCraftLab.Engine.Services.Compression;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class MessageDeduplicatorTests
{
    [Fact]
    public void RemovesDuplicateToolResults()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            // 第一次呼叫 search("TSMC")
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "search", new Dictionary<string, object?> { ["q"] = "TSMC" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "result 1")]),
            // 第二次呼叫 search("TSMC") — 相同工具+相同參數
            new(ChatRole.Assistant, [new FunctionCallContent("c2", "search", new Dictionary<string, object?> { ["q"] = "TSMC" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c2", "result 2")]),
        };

        MessageDeduplicator.RemoveRedundantToolResults(messages);

        // 只保留最後一次的 tool result（c2），c1 的 tool result 被移除
        var toolMessages = messages.Where(m => m.Role == ChatRole.Tool).ToList();
        Assert.Single(toolMessages);
        var resultText = toolMessages[0].Contents.OfType<FunctionResultContent>().First().Result?.ToString();
        Assert.Equal("result 2", resultText);
    }

    [Fact]
    public void MergesConsecutiveShortMessages()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.User, "hi"),
            new(ChatRole.User, "there"),
            new(ChatRole.Assistant, "hello"),
        };

        MessageDeduplicator.MergeConsecutiveMessages(messages, shortMessageThreshold: 100);

        // "hi" + "there" 合併為一則
        Assert.Equal(3, messages.Count);
        Assert.Equal("hi\nthere", messages[1].Text);
    }

    [Fact]
    public void PreservesFunctionCallContent()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "search", new Dictionary<string, object?> { ["q"] = "test" })]),
            new(ChatRole.Assistant, "short text"),
        };

        // FunctionCallContent 不應被合併
        MessageDeduplicator.MergeConsecutiveMessages(messages, shortMessageThreshold: 100);

        Assert.Equal(3, messages.Count); // 沒有合併
    }

    [Fact]
    public void TryCompress_UnderTarget_ReturnsTrue()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.User, "hello"),
        };

        var result = MessageDeduplicator.TryCompress(messages, targetCount: 12);

        Assert.True(result);
    }
}
