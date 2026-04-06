using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AgentCraftLab.Tests.Engine;

public class LlmClientFactoryTests
{
    private readonly DefaultLlmClientFactory _factory = new();

    [Fact]
    public void CreateClient_NoCredentials_ReturnsError()
    {
        var credentials = new Dictionary<string, ProviderCredential>();
        var (client, error) = _factory.CreateClient(credentials, "openai", "gpt-4o");
        Assert.Null(client);
        Assert.Contains("No API key", error);
    }

    [Fact]
    public void CreateClient_EmptyApiKey_ReturnsError()
    {
        var credentials = new Dictionary<string, ProviderCredential>
        {
            ["openai"] = new() { ApiKey = "" }
        };
        var (client, error) = _factory.CreateClient(credentials, "openai", "gpt-4o");
        Assert.Null(client);
        Assert.Contains("No API key", error);
    }

    [Fact]
    public void CreateClient_FallbackToOpenAI()
    {
        var credentials = new Dictionary<string, ProviderCredential>
        {
            ["openai"] = new() { ApiKey = "test-key" }
        };
        // 請求 azure-openai 但沒有該 credential → fallback 到 openai
        var (client, error) = _factory.CreateClient(credentials, "azure-openai", "gpt-4o");
        Assert.NotNull(client);
        Assert.Null(error);
    }

    [Fact]
    public void CreateClient_ProviderNormalization()
    {
        var credentials = new Dictionary<string, ProviderCredential>
        {
            ["openai"] = new() { ApiKey = "test-key" }
        };
        // "OpenAI" 正規化為 "openai"
        var (client, error) = _factory.CreateClient(credentials, "OpenAI", "gpt-4o");
        Assert.NotNull(client);
        Assert.Null(error);
    }

    [Fact]
    public void CreateClient_ValidCredentials_ReturnsClient()
    {
        var credentials = new Dictionary<string, ProviderCredential>
        {
            ["openai"] = new() { ApiKey = "sk-test-key-123" }
        };
        var (client, error) = _factory.CreateClient(credentials, "openai", "gpt-4o");
        Assert.NotNull(client);
        Assert.Null(error);
    }

    [Fact]
    public void CreateClient_AzureOpenAI_WithEndpoint()
    {
        var credentials = new Dictionary<string, ProviderCredential>
        {
            ["azure-openai"] = new() { ApiKey = "test-key", Endpoint = "https://test.openai.azure.com/" }
        };
        var (client, error) = _factory.CreateClient(credentials, "azure-openai", "gpt-4o");
        Assert.NotNull(client);
        Assert.Null(error);
    }

    // ── CreateLlmClient / GetChatClientFromBase 多型分派測試 ──

    [Fact]
    public void CreateLlmClient_OpenAI_ReturnsOpenAIClient()
    {
        var client = AgentContextBuilder.CreateLlmClient(
            Providers.OpenAI, "sk-test", "", TimeSpan.FromSeconds(30));
        Assert.IsType<OpenAIClient>(client);
    }

    [Fact]
    public void CreateLlmClient_Anthropic_ReturnsAnthropicClient()
    {
        var client = AgentContextBuilder.CreateLlmClient(
            Providers.Anthropic, "sk-ant-test", "", TimeSpan.FromSeconds(30));
        Assert.IsType<AnthropicClient>(client);
    }

    [Fact]
    public void CreateLlmClient_AzureOpenAI_ReturnsAzureOpenAIClient()
    {
        var client = AgentContextBuilder.CreateLlmClient(
            Providers.AzureOpenAI, "key", "https://test.openai.azure.com/", TimeSpan.FromSeconds(30));
        Assert.IsAssignableFrom<OpenAIClient>(client);
    }

    [Fact]
    public void GetChatClientFromBase_OpenAIClient_ReturnsIChatClient()
    {
        var openai = new OpenAIClient("sk-test");
        var chatClient = AgentContextBuilder.GetChatClientFromBase(openai, "gpt-4o");
        Assert.IsAssignableFrom<IChatClient>(chatClient);
    }

    [Fact]
    public void GetChatClientFromBase_AnthropicClient_ReturnsIChatClient()
    {
        var anthropic = new AnthropicClient { ApiKey = "sk-ant-test" };
        var chatClient = AgentContextBuilder.GetChatClientFromBase(anthropic, "claude-sonnet-4-20250514");
        Assert.IsAssignableFrom<IChatClient>(chatClient);
    }

    [Fact]
    public void GetChatClientFromBase_UnsupportedType_Throws()
    {
        Assert.Throws<NotSupportedException>(
            () => AgentContextBuilder.GetChatClientFromBase("not-a-client", "model"));
    }

    [Fact]
    public void StaticCreateChatClient_Anthropic_ReturnsIChatClient()
    {
        var client = AgentContextBuilder.CreateChatClient(
            Providers.Anthropic, "sk-ant-test", "", "claude-sonnet-4-20250514");
        Assert.IsAssignableFrom<IChatClient>(client);
    }

    [Fact]
    public void StaticCreateChatClient_WithCache_ReusesSameBaseClient()
    {
        var cache = new Dictionary<string, object>();
        AgentContextBuilder.CreateChatClient(Providers.OpenAI, "sk-test", "", "gpt-4o", cache);
        AgentContextBuilder.CreateChatClient(Providers.OpenAI, "sk-test", "", "gpt-4o-mini", cache);
        Assert.Single(cache);
    }

    [Fact]
    public void StaticCreateChatClient_DifferentProviders_SeparateCacheEntries()
    {
        var cache = new Dictionary<string, object>();
        AgentContextBuilder.CreateChatClient(Providers.OpenAI, "sk-test", "", "gpt-4o", cache);
        AgentContextBuilder.CreateChatClient(Providers.Anthropic, "sk-ant-test", "", "claude-sonnet-4-20250514", cache);
        Assert.Equal(2, cache.Count);
    }
}
