using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Strategies;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class CacheableSystemPromptTests
{
    [Fact]
    public void ToFullText_CombinesStaticAndDynamic()
    {
        var prompt = new CacheableSystemPrompt("Static part\n\n", "Current date: 2026-04-02.");
        Assert.Equal("Static part\n\nCurrent date: 2026-04-02.", prompt.ToFullText());
    }

    [Fact]
    public void ToFullText_DynamicEmpty_ReturnsStaticOnly()
    {
        var prompt = new CacheableSystemPrompt("Static only");
        Assert.Equal("Static only", prompt.ToFullText());
    }

    [Fact]
    public void ToChatMessages_SinglePart_ReturnsSingleMessage()
    {
        var prompt = new CacheableSystemPrompt("System instructions");
        var messages = prompt.ToChatMessages();

        Assert.Single(messages);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal("System instructions", messages[0].Text);
    }

    [Fact]
    public void ToChatMessages_TwoParts_ReturnsTwoSystemMessages()
    {
        var prompt = new CacheableSystemPrompt("Static\n\n", "Dynamic");
        var messages = prompt.ToChatMessages();

        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Equal(ChatRole.System, m.Role));
        Assert.Equal("Static\n\n", messages[0].Text);
        Assert.Equal("Dynamic", messages[1].Text);
    }

    [Fact]
    public void ToChatMessages_Anthropic_AddsCacheControl()
    {
        var prompt = new CacheableSystemPrompt("Static\n\n", "Dynamic");
        var messages = prompt.ToChatMessages(provider: "anthropic");

        Assert.NotNull(messages[0].AdditionalProperties);
        Assert.True(messages[0].AdditionalProperties!.ContainsKey("cache_control"));
    }

    [Fact]
    public void ToChatMessages_NonAnthropic_NoCacheControl()
    {
        var prompt = new CacheableSystemPrompt("Static\n\n", "Dynamic");
        var messages = prompt.ToChatMessages(provider: "openai");

        Assert.Null(messages[0].AdditionalProperties);
    }

    [Fact]
    public void BuildCacheableInstructions_Simple_DateInDynamic()
    {
        var result = AgentContextBuilder.BuildCacheableInstructions("You are a helper.");

        Assert.DoesNotContain("Current date:", result.StaticPart);
        Assert.StartsWith("Current date: ", result.DynamicPart);
    }

    [Fact]
    public void BuildCacheableInstructions_Simple_InstructionsInStatic()
    {
        var result = AgentContextBuilder.BuildCacheableInstructions("You are a helper.");

        Assert.Contains("You are a helper.", result.StaticPart);
    }

    [Fact]
    public void BuildCacheableInstructions_JsonFormat_InStaticPart()
    {
        // instructions 不含 "json" → 會附加 "Respond in JSON format."
        var result = AgentContextBuilder.BuildCacheableInstructions("Summarize the data.", "json");

        // JSON 格式指令應在 DynamicPart 之後（因為原始碼在 date 之後追加）
        Assert.Contains("Respond in JSON format.", result.ToFullText());
    }

    [Fact]
    public void BuildCacheableInstructions_MatchesOriginalBuildInstructions()
    {
        // 確認 ToFullText() 完美重建原始 BuildInstructions 輸出
        var instructions = "You are a helpful AI assistant.";
        var original = AgentContextBuilder.BuildInstructions(instructions);
        var cacheable = AgentContextBuilder.BuildCacheableInstructions(instructions);

        Assert.Equal(original, cacheable.ToFullText());
    }

    [Fact]
    public void BuildCacheableInstructions_WithLanguage_MatchesOriginal()
    {
        // 含語言強制的情境
        var instructions = "請用英文回答所有問題 English";
        var original = AgentContextBuilder.BuildInstructions(instructions);
        var cacheable = AgentContextBuilder.BuildCacheableInstructions(instructions);

        Assert.Equal(original, cacheable.ToFullText());
    }

    [Fact]
    public void BuildCacheableInstructions_WithJsonAndLanguage_MatchesOriginal()
    {
        var instructions = "Analyze data in Japanese 日文";
        var original = AgentContextBuilder.BuildInstructions(instructions, "json");
        var cacheable = AgentContextBuilder.BuildCacheableInstructions(instructions, "json");

        Assert.Equal(original, cacheable.ToFullText());
    }

    [Fact]
    public void EstimatedStaticTokens_ReturnsPositiveValue()
    {
        var prompt = new CacheableSystemPrompt("This is a test prompt with some content.");
        Assert.True(prompt.EstimatedStaticTokens > 0);
    }
}
