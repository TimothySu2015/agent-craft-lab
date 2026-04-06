using AgentCraftLab.Engine.Services.Compression;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class MessageSerializerTests
{
    [Fact]
    public void SerializesMessagesWithRoles()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What is AI?"),
            new(ChatRole.Assistant, "AI is artificial intelligence."),
        };

        var result = MessageSerializer.Serialize(messages);

        Assert.Contains("user: What is AI?", result);
        Assert.Contains("assistant: AI is artificial intelligence.", result);
    }

    [Fact]
    public void TruncatesLongMessages()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, new string('A', 500)),
        };

        var result = MessageSerializer.Serialize(messages, maxPerMessage: 100);

        // 應被截斷到 100 字元 + "..."
        Assert.Contains("...", result);
        Assert.True(result.Length < 500);
    }

    [Fact]
    public void SerializesFunctionCallContent()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "search", new Dictionary<string, object?> { ["q"] = "test" })]),
        };

        var result = MessageSerializer.Serialize(messages);

        Assert.Contains("[Called search(", result);
    }

    [Fact]
    public void SerializesFunctionResultContent()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("c1", "search result data")]),
        };

        var result = MessageSerializer.Serialize(messages);

        Assert.Contains("[Result: search result data]", result);
    }

    [Fact]
    public void WrapAsCompressedHistory_Format()
    {
        var msg = MessageSerializer.WrapAsCompressedHistory("summary text", 15);

        Assert.Equal(ChatRole.System, msg.Role);
        Assert.Contains("[Compressed history of previous 15 messages]", msg.Text);
        Assert.Contains("summary text", msg.Text);
    }
}
