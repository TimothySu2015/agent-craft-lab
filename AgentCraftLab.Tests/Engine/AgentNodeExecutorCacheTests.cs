using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Strategies;
using AgentCraftLab.Engine.Strategies.NodeExecutors;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

/// <summary>
/// AgentNodeExecutor Prompt Cache 相關測試 — BuildSystemMessages + ReplaceSystemMessages。
/// </summary>
public class AgentNodeExecutorCacheTests
{
    // ════════════════════════════════════════
    // BuildSystemMessages
    // ════════════════════════════════════════

    [Fact]
    public void BuildSystemMessages_WithDate_ReturnsTwoMessages()
    {
        var instructions = "You are a helper.\n\nCurrent date: 2026-04-04. Always use the current year when searching.";
        var messages = AgentNodeExecutor.BuildSystemMessages(instructions, null, null);

        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Equal(ChatRole.System, m.Role));
        Assert.Contains("You are a helper.", messages[0].Text);
        Assert.Contains("Current date:", messages[1].Text);
    }

    [Fact]
    public void BuildSystemMessages_WithoutDate_ReturnsSingleMessage()
    {
        var instructions = "You are a helper.";
        var messages = AgentNodeExecutor.BuildSystemMessages(instructions, null, null);

        Assert.Single(messages);
        Assert.Equal("You are a helper.", messages[0].Text);
    }

    [Fact]
    public void BuildSystemMessages_Anthropic_HasCacheControl()
    {
        var instructions = "You are a helper.\n\nCurrent date: 2026-04-04.";
        var messages = AgentNodeExecutor.BuildSystemMessages(instructions, null, "anthropic");

        Assert.Equal(2, messages.Count);
        Assert.NotNull(messages[0].AdditionalProperties);
        Assert.True(messages[0].AdditionalProperties!.ContainsKey("cache_control"));
    }

    [Fact]
    public void BuildSystemMessages_OpenAI_NoCacheControl()
    {
        var instructions = "You are a helper.\n\nCurrent date: 2026-04-04.";
        var messages = AgentNodeExecutor.BuildSystemMessages(instructions, null, "openai");

        Assert.Equal(2, messages.Count);
        Assert.Null(messages[0].AdditionalProperties);
    }

    [Fact]
    public void BuildSystemMessages_AzureOpenAI_NoCacheControl()
    {
        var instructions = "You are a helper.\n\nCurrent date: 2026-04-04.";
        var messages = AgentNodeExecutor.BuildSystemMessages(instructions, null, "azure-openai");

        Assert.Equal(2, messages.Count);
        Assert.Null(messages[0].AdditionalProperties);
    }

    [Fact]
    public void BuildSystemMessages_WithContextPrefix_InDynamicPart()
    {
        var instructions = "You are a helper.\n\nCurrent date: 2026-04-04.";
        var contextPrefix = "[Context] Previous step output: data";
        var messages = AgentNodeExecutor.BuildSystemMessages(instructions, contextPrefix, null);

        Assert.Equal(2, messages.Count);
        // Static part should NOT contain contextPrefix
        Assert.DoesNotContain("[Context]", messages[0].Text);
        // Dynamic part should contain contextPrefix
        Assert.Contains("[Context]", messages[1].Text);
        // Dynamic part should also still contain the date
        Assert.Contains("Current date:", messages[1].Text);
    }

    [Fact]
    public void BuildSystemMessages_WithContextPrefix_NoDate_ReturnsTwoMessages()
    {
        var instructions = "You are a helper.";
        var contextPrefix = "[Context] some context";
        var messages = AgentNodeExecutor.BuildSystemMessages(instructions, contextPrefix, null);

        // Even without date split, contextPrefix forces a DynamicPart
        Assert.Equal(2, messages.Count);
        Assert.Contains("You are a helper.", messages[0].Text);
        Assert.Contains("[Context]", messages[1].Text);
    }

    [Fact]
    public void BuildSystemMessages_PreservesFullContent()
    {
        var instructions = "You are a helper.\n\nCurrent date: 2026-04-04. Always use the current year.";
        var messages = AgentNodeExecutor.BuildSystemMessages(instructions, null, null);

        // Concatenating all messages should equal the original
        var combined = string.Join("", messages.Select(m => m.Text));
        Assert.Equal(instructions, combined);
    }

    [Fact]
    public void BuildSystemMessages_WithContextPrefix_Anthropic_CacheOnStatic()
    {
        var instructions = "You are a helper.\n\nCurrent date: 2026-04-04.";
        var contextPrefix = "[Context] data";
        var messages = AgentNodeExecutor.BuildSystemMessages(instructions, contextPrefix, "anthropic");

        // Static part has cache_control
        Assert.NotNull(messages[0].AdditionalProperties);
        Assert.True(messages[0].AdditionalProperties!.ContainsKey("cache_control"));
        // Dynamic part should NOT have cache_control
        Assert.Null(messages[1].AdditionalProperties);
    }

    // ════════════════════════════════════════
    // ReplaceSystemMessages
    // ════════════════════════════════════════

    [Fact]
    public void ReplaceSystemMessages_SingleToTwo()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "old system"),
            new(ChatRole.User, "hello"),
            new(ChatRole.Assistant, "hi")
        };
        var newSystemMessages = new List<ChatMessage>
        {
            new(ChatRole.System, "static part"),
            new(ChatRole.System, "dynamic part")
        };

        AgentNodeExecutor.ReplaceSystemMessages(history, newSystemMessages);

        Assert.Equal(4, history.Count);
        Assert.Equal("static part", history[0].Text);
        Assert.Equal("dynamic part", history[1].Text);
        Assert.Equal(ChatRole.User, history[2].Role);
        Assert.Equal(ChatRole.Assistant, history[3].Role);
    }

    [Fact]
    public void ReplaceSystemMessages_TwoToTwo()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "old static"),
            new(ChatRole.System, "old dynamic"),
            new(ChatRole.User, "hello")
        };
        var newSystemMessages = new List<ChatMessage>
        {
            new(ChatRole.System, "new static"),
            new(ChatRole.System, "new dynamic")
        };

        AgentNodeExecutor.ReplaceSystemMessages(history, newSystemMessages);

        Assert.Equal(3, history.Count);
        Assert.Equal("new static", history[0].Text);
        Assert.Equal("new dynamic", history[1].Text);
        Assert.Equal(ChatRole.User, history[2].Role);
    }

    [Fact]
    public void ReplaceSystemMessages_EmptyHistory()
    {
        var history = new List<ChatMessage>();
        var newSystemMessages = new List<ChatMessage>
        {
            new(ChatRole.System, "static"),
            new(ChatRole.System, "dynamic")
        };

        AgentNodeExecutor.ReplaceSystemMessages(history, newSystemMessages);

        Assert.Equal(2, history.Count);
        Assert.Equal("static", history[0].Text);
        Assert.Equal("dynamic", history[1].Text);
    }

    [Fact]
    public void ReplaceSystemMessages_SingleToSingle()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "old"),
            new(ChatRole.User, "test")
        };
        var newSystemMessages = new List<ChatMessage>
        {
            new(ChatRole.System, "new")
        };

        AgentNodeExecutor.ReplaceSystemMessages(history, newSystemMessages);

        Assert.Equal(2, history.Count);
        Assert.Equal("new", history[0].Text);
        Assert.Equal(ChatRole.User, history[1].Role);
    }

    [Fact]
    public void ReplaceSystemMessages_NoSystemMessages_InsertsAtFront()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "hello"),
            new(ChatRole.Assistant, "hi")
        };
        var newSystemMessages = new List<ChatMessage>
        {
            new(ChatRole.System, "injected")
        };

        AgentNodeExecutor.ReplaceSystemMessages(history, newSystemMessages);

        Assert.Equal(3, history.Count);
        Assert.Equal(ChatRole.System, history[0].Role);
        Assert.Equal("injected", history[0].Text);
    }

    // ════════════════════════════════════════
    // BuildInstructions → Split 整合
    // ════════════════════════════════════════

    [Fact]
    public void BuildSystemMessages_FromBuildInstructions_MatchesOriginal()
    {
        var fullInstructions = AgentContextBuilder.BuildInstructions("Analyze data carefully.", "json");
        var messages = AgentNodeExecutor.BuildSystemMessages(fullInstructions, null, null);

        // Combined messages should equal original
        var combined = string.Join("", messages.Select(m => m.Text));
        Assert.Equal(fullInstructions, combined);
    }

    [Fact]
    public void BuildSystemMessages_FromBuildInstructions_DateInDynamic()
    {
        var fullInstructions = AgentContextBuilder.BuildInstructions("Analyze data.", null);
        var messages = AgentNodeExecutor.BuildSystemMessages(fullInstructions, null, null);

        Assert.Equal(2, messages.Count);
        Assert.DoesNotContain("Current date:", messages[0].Text);
        Assert.Contains("Current date:", messages[1].Text);
    }
}
