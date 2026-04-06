using AgentCraftLab.Engine.Services.Compression;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class ToolResultTruncatorTests
{
    [Fact]
    public void TruncatesLongResults()
    {
        var longResult = new string('X', 3000);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.Tool, [new FunctionResultContent("call1", longResult)])
        };

        var charsSaved = ToolResultTruncator.Truncate(messages, maxLength: 1500);

        Assert.True(charsSaved > 0);
        var resultText = messages[1].Contents.OfType<FunctionResultContent>().First().Result?.ToString();
        Assert.NotNull(resultText);
        Assert.True(resultText!.Length < 3000);
        Assert.Contains("truncated", resultText);
    }

    [Fact]
    public void PreservesShortResults()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.Tool, [new FunctionResultContent("call1", "short result")])
        };

        var charsSaved = ToolResultTruncator.Truncate(messages, maxLength: 1500);

        Assert.Equal(0, charsSaved);
        var resultText = messages[1].Contents.OfType<FunctionResultContent>().First().Result?.ToString();
        Assert.Equal("short result", resultText);
    }

    [Fact]
    public void SkipsSystemPrompt()
    {
        // index 0 是 system prompt，不應被截斷
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, new string('X', 3000)),
            new(ChatRole.User, "hello")
        };

        var charsSaved = ToolResultTruncator.Truncate(messages, maxLength: 100);

        Assert.Equal(0, charsSaved); // system prompt 不動
    }

    [Fact]
    public void WithCompressionState_RecordsToolIds()
    {
        var state = new CompressionState();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.Tool, [new FunctionResultContent("call-abc", new string('X', 3000))])
        };

        ToolResultTruncator.Truncate(messages, maxLength: 1500, state);

        Assert.Contains("call-abc", state.TruncatedToolCallIds);
    }

    [Fact]
    public void WithCompressionState_SkipsAlreadyTruncated()
    {
        var state = new CompressionState();
        state.TruncatedToolCallIds.Add("call-abc"); // 已截斷過

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.Tool, [new FunctionResultContent("call-abc", new string('X', 3000))])
        };

        var charsSaved = ToolResultTruncator.Truncate(messages, maxLength: 1500, state);

        Assert.Equal(0, charsSaved); // 跳過已截斷的
    }
}
