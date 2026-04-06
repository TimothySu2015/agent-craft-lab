using AgentCraftLab.Autonomous.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Autonomous;

public class SerializableChatMessageTests
{
    // ─── Text 訊息 round-trip ───

    [Fact]
    public void RoundTrip_SystemMessage()
    {
        var original = new ChatMessage(ChatRole.System, "You are helpful.");
        var serializable = SerializableChatMessage.FromChatMessage(original);
        var restored = serializable.ToChatMessage();

        Assert.Equal("system", restored.Role.Value);
        Assert.Equal("You are helpful.", restored.Text);
    }

    [Fact]
    public void RoundTrip_UserMessage()
    {
        var original = new ChatMessage(ChatRole.User, "Hello!");
        var serializable = SerializableChatMessage.FromChatMessage(original);
        var restored = serializable.ToChatMessage();

        Assert.Equal("user", restored.Role.Value);
        Assert.Equal("Hello!", restored.Text);
    }

    [Fact]
    public void RoundTrip_AssistantMessage()
    {
        var original = new ChatMessage(ChatRole.Assistant, "I can help.");
        var serializable = SerializableChatMessage.FromChatMessage(original);
        var restored = serializable.ToChatMessage();

        Assert.Equal("assistant", restored.Role.Value);
        Assert.Equal("I can help.", restored.Text);
    }

    // ─── FunctionCall round-trip ───

    [Fact]
    public void RoundTrip_FunctionCallContent()
    {
        var call = new FunctionCallContent("call_123", "WebSearch",
            new Dictionary<string, object?> { ["query"] = "AI news" });
        var original = new ChatMessage(ChatRole.Assistant, [call]);

        var serializable = SerializableChatMessage.FromChatMessage(original);
        Assert.Single(serializable.Contents);
        Assert.Equal("functionCall", serializable.Contents[0].Type);
        Assert.Equal("WebSearch", serializable.Contents[0].FunctionName);
        Assert.Equal("call_123", serializable.Contents[0].CallId);
        Assert.Contains("AI news", serializable.Contents[0].ArgumentsJson!);

        var restored = serializable.ToChatMessage();
        var restoredCall = restored.Contents.OfType<FunctionCallContent>().Single();
        Assert.Equal("WebSearch", restoredCall.Name);
        Assert.Equal("call_123", restoredCall.CallId);
    }

    // ─── FunctionResult round-trip ───

    [Fact]
    public void RoundTrip_FunctionResultContent()
    {
        var result = new FunctionResultContent("call_123", "Search results: AI is advancing");
        var original = new ChatMessage(ChatRole.Tool, [result]);

        var serializable = SerializableChatMessage.FromChatMessage(original);
        Assert.Single(serializable.Contents);
        Assert.Equal("functionResult", serializable.Contents[0].Type);
        Assert.Equal("call_123", serializable.Contents[0].CallId);

        var restored = serializable.ToChatMessage();
        var restoredResult = restored.Contents.OfType<FunctionResultContent>().Single();
        Assert.Equal("call_123", restoredResult.CallId);
    }

    // ─── 混合內容 ───

    [Fact]
    public void RoundTrip_MixedContents()
    {
        var contents = new AIContent[]
        {
            new TextContent("Let me search for that."),
            new FunctionCallContent("call_456", "Calculator",
                new Dictionary<string, object?> { ["expression"] = "2+2" })
        };
        var original = new ChatMessage(ChatRole.Assistant, contents);

        var serializable = SerializableChatMessage.FromChatMessage(original);
        Assert.Equal(2, serializable.Contents.Count);
        Assert.Equal("text", serializable.Contents[0].Type);
        Assert.Equal("functionCall", serializable.Contents[1].Type);

        var restored = serializable.ToChatMessage();
        Assert.Equal(2, restored.Contents.Count);
    }

    // ─── 邊界情況 ───

    [Fact]
    public void RoundTrip_EmptyMessage()
    {
        var original = new ChatMessage(ChatRole.User, "");
        var serializable = SerializableChatMessage.FromChatMessage(original);
        var restored = serializable.ToChatMessage();

        Assert.Equal("user", restored.Role.Value);
    }

    [Fact]
    public void RoundTrip_NullArguments()
    {
        var call = new FunctionCallContent("call_789", "GetDateTime", null);
        var original = new ChatMessage(ChatRole.Assistant, [call]);

        var serializable = SerializableChatMessage.FromChatMessage(original);
        Assert.Null(serializable.Contents[0].ArgumentsJson);

        var restored = serializable.ToChatMessage();
        var restoredCall = restored.Contents.OfType<FunctionCallContent>().Single();
        Assert.Equal("GetDateTime", restoredCall.Name);
    }

    // ─── List round-trip ───

    [Fact]
    public void RoundTrip_MessageList()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System prompt"),
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there"),
        };

        var serialized = SerializableChatMessage.FromList(messages);
        var restored = SerializableChatMessage.ToList(serialized);

        Assert.Equal(3, restored.Count);
        Assert.Equal("system", restored[0].Role.Value);
        Assert.Equal("Hello", restored[1].Text);
        Assert.Equal("Hi there", restored[2].Text);
    }

    // ─── JSON round-trip ───

    [Fact]
    public void JsonRoundTrip_FullSnapshot()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are an AI agent."),
            new(ChatRole.User, "Find NVIDIA stock price"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "WebSearch",
                new Dictionary<string, object?> { ["query"] = "NVIDIA stock" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "$850")]),
            new(ChatRole.Assistant, "NVIDIA stock is $850."),
        };

        var serialized = SerializableChatMessage.FromList(messages);
        var json = System.Text.Json.JsonSerializer.Serialize(serialized);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<List<SerializableChatMessage>>(json)!;
        var restored = SerializableChatMessage.ToList(deserialized);

        Assert.Equal(5, restored.Count);
        Assert.Equal("You are an AI agent.", restored[0].Text);
        Assert.IsType<FunctionCallContent>(restored[2].Contents[0]);
        Assert.IsType<FunctionResultContent>(restored[3].Contents[0]);
        Assert.Equal("NVIDIA stock is $850.", restored[4].Text);
    }
}
