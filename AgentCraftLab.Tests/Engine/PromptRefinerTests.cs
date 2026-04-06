using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class PromptRefinerTests
{
    // ─── SkillPromptProvider ───

    [Fact]
    public void LoadPrompt_ReturnsCommonWhenNoSpecificFile()
    {
        var provider = new SkillPromptProvider("nonexistent-dir");
        var result = provider.LoadPrompt("prompt-refiner", "unknown-model");
        // Should fallback to built-in default
        Assert.Contains("Prompt Engineering", result);
    }

    [Fact]
    public void LoadPrompt_DefaultFallback_ContainsCoreGuide()
    {
        var provider = new SkillPromptProvider("nonexistent-dir");
        var result = provider.LoadPrompt("prompt-refiner", "gpt-4o", "openai");
        Assert.Contains("清晰指令", result);
    }

    /// <summary>取得專案根目錄的 Data/skill-prompts 路徑（測試工作目錄可能不同）。</summary>
    private static string GetSkillPromptsDir()
    {
        // 從測試 bin 目錄往上找到專案根目錄
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AgentFrameworkDemo.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir is not null
            ? Path.Combine(dir, "Data", "skill-prompts")
            : "Data/skill-prompts";
    }

    [Fact]
    public void LoadPrompt_WithRealFiles_LoadsCommon()
    {
        var provider = new SkillPromptProvider(GetSkillPromptsDir());
        var result = provider.LoadPrompt("prompt-refiner", "gpt-4o", "openai");
        Assert.NotEmpty(result);
        Assert.Contains("Prompt Engineering", result);
    }

    [Fact]
    public void LoadPrompt_WithProvider_MatchesCorrectFile()
    {
        var provider = new SkillPromptProvider(GetSkillPromptsDir());

        var gpt = provider.LoadPrompt("prompt-refiner", "gpt-4o", "openai");
        var claude = provider.LoadPrompt("prompt-refiner", "claude-sonnet-4-6", "anthropic");
        var gemini = provider.LoadPrompt("prompt-refiner", "gemini-2.0-flash", "google");

        // Each should have model-specific content (different lengths)
        Assert.NotEqual(gpt, claude);
        Assert.NotEqual(claude, gemini);
    }

    [Fact]
    public void LoadPrompt_AzureOpenAI_MapsToGpt()
    {
        var provider = new SkillPromptProvider(GetSkillPromptsDir());
        var azure = provider.LoadPrompt("prompt-refiner", "gpt-4o", "azure-openai");
        var openai = provider.LoadPrompt("prompt-refiner", "gpt-4o", "openai");
        Assert.Equal(azure, openai);
    }

    [Fact]
    public void LoadPrompt_NullProvider_FallsBackToModelMatch()
    {
        var provider = new SkillPromptProvider(GetSkillPromptsDir());
        var result = provider.LoadPrompt("prompt-refiner", "claude-sonnet-4-6");
        Assert.Contains("Claude", result);
    }

    [Fact]
    public void LoadPrompt_UnknownSkill_ReturnsEmpty()
    {
        var provider = new SkillPromptProvider(GetSkillPromptsDir());
        var result = provider.LoadPrompt("nonexistent-skill", "gpt-4o");
        Assert.Empty(result);
    }

    // ─── PromptRefinerService.ParseResult (via RefineAsync mock) ───
    // ParseResult is private, so we test through public API behavior.
    // These tests verify the JSON parsing logic using a stub IChatClient.

    private sealed class StubChatClient(string responseText) : Microsoft.Extensions.AI.IChatClient
    {
        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                new Microsoft.Extensions.AI.ChatMessage(
                    Microsoft.Extensions.AI.ChatRole.Assistant, responseText)));

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task RefineAsync_ValidJson_ParsesCorrectly()
    {
        var service = new PromptRefinerService(new SkillPromptProvider("nonexistent"));
        var client = new StubChatClient("""
            {"refined": "optimized prompt here", "changes": ["added role", "added constraints"]}
            """);

        var result = await service.RefineAsync(client, "original prompt", "gpt-4o", "openai", CancellationToken.None);

        Assert.Equal("original prompt", result.Original);
        Assert.Equal("optimized prompt here", result.Refined);
        Assert.Equal(2, result.Changes.Count);
        Assert.Contains("added role", result.Changes);
    }

    [Fact]
    public async Task RefineAsync_JsonWithMarkdownFence_StillParses()
    {
        var service = new PromptRefinerService(new SkillPromptProvider("nonexistent"));
        var client = new StubChatClient("""
            Here is the result:
            ```json
            {"refined": "better prompt", "changes": ["improved clarity"]}
            ```
            """);

        var result = await service.RefineAsync(client, "old prompt", "gpt-4o", "openai", CancellationToken.None);
        Assert.Equal("better prompt", result.Refined);
    }

    [Fact]
    public async Task RefineAsync_InvalidJson_FallsBackToRawText()
    {
        var service = new PromptRefinerService(new SkillPromptProvider("nonexistent"));
        var client = new StubChatClient("This is not JSON, just a refined prompt.");

        var result = await service.RefineAsync(client, "original", "gpt-4o", "openai", CancellationToken.None);

        Assert.Equal("original", result.Original);
        Assert.Equal("This is not JSON, just a refined prompt.", result.Refined);
        Assert.NotEmpty(result.Changes); // Should have fallback message
    }

    [Fact]
    public async Task RefineAsync_EmptyResponse_FallsBack()
    {
        var service = new PromptRefinerService(new SkillPromptProvider("nonexistent"));
        var client = new StubChatClient("");

        var result = await service.RefineAsync(client, "original", "gpt-4o", "openai", CancellationToken.None);

        Assert.Equal("original", result.Original);
    }

    [Fact]
    public async Task RefineAsync_JsonMissingRefined_FallsBack()
    {
        var service = new PromptRefinerService(new SkillPromptProvider("nonexistent"));
        var client = new StubChatClient("""{"changes": ["something"]}""");

        var result = await service.RefineAsync(client, "original", "gpt-4o", "openai", CancellationToken.None);
        // No "refined" key → fallback to raw text
        Assert.NotEmpty(result.Refined);
    }

    [Fact]
    public async Task RefineAsync_PreservesOriginal()
    {
        var service = new PromptRefinerService(new SkillPromptProvider("nonexistent"));
        var client = new StubChatClient("""{"refined": "new", "changes": ["x"]}""");

        var result = await service.RefineAsync(client, "my original prompt", "gpt-4o", "openai", CancellationToken.None);
        Assert.Equal("my original prompt", result.Original);
    }
}
