using AgentCraftLab.Autonomous.Flow.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Flow;

public class FlowPromptCacheTests
{
    [Fact]
    public void BuildAgentMessages_WithAnthropicProvider_AddsCacheControl()
    {
        var messages = FlowNodeRunner.BuildAgentMessages(
            "You are a research assistant.", ["web_search"], "Search for AI news", "anthropic");

        // 至少 2 條 system messages（static + dynamic）+ 1 條 user message
        Assert.True(messages.Count >= 2);
        // 第一條 system message 應有 cache_control
        var firstSystem = messages.First(m => m.Role == ChatRole.System);
        Assert.NotNull(firstSystem.AdditionalProperties);
        Assert.True(firstSystem.AdditionalProperties!.ContainsKey("cache_control"));
    }

    [Fact]
    public void BuildAgentMessages_WithoutProvider_NoCacheControl()
    {
        var messages = FlowNodeRunner.BuildAgentMessages(
            "You are a helper.", ["calculator"], "Calculate 2+2");

        // null provider → 不加 cache_control
        var systemMessages = messages.Where(m => m.Role == ChatRole.System).ToList();
        Assert.All(systemMessages, m => Assert.Null(m.AdditionalProperties));
    }

    [Fact]
    public void BuildAgentMessages_InstructionsOnly_SingleSystemMessage()
    {
        var messages = FlowNodeRunner.BuildAgentMessages(
            "Summarize the text.", tools: null, "Some long text...");

        // 無 tools → DynamicPart 為空 → 只有 1 條 system message
        var systemMessages = messages.Where(m => m.Role == ChatRole.System).ToList();
        Assert.Single(systemMessages);
        Assert.Contains("Summarize the text.", systemMessages[0].Text!);
    }

    [Fact]
    public void BuildAgentMessages_WithTools_SplitsStaticDynamic()
    {
        var messages = FlowNodeRunner.BuildAgentMessages(
            "Analyze data.", ["search", "calculator"], "Input data", "openai");

        // 有 tools → 靜態（instructions）+ 動態（tools hint）→ 2 條 system messages
        var systemMessages = messages.Where(m => m.Role == ChatRole.System).ToList();
        Assert.Equal(2, systemMessages.Count);
        Assert.Contains("Analyze data.", systemMessages[0].Text!);
        Assert.Contains("search", systemMessages[1].Text!);
    }

    [Fact]
    public void BuildAgentMessages_AlwaysHasUserMessage()
    {
        var messages = FlowNodeRunner.BuildAgentMessages(
            "Instructions.", ["tool1"], "User input", "anthropic");

        var userMessages = messages.Where(m => m.Role == ChatRole.User).ToList();
        Assert.Single(userMessages);
        Assert.Equal("User input", userMessages[0].Text);
    }
}
