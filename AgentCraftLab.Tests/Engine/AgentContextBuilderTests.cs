using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Strategies;

namespace AgentCraftLab.Tests.Engine;

public class AgentContextBuilderTests
{
    // ── BuildMiddlewareSpec ──

    [Fact]
    public void BuildMiddlewareSpec_NullBindings_ReturnsDefaults()
    {
        var (middleware, config) = AgentContextBuilder.BuildMiddlewareSpec(null);
        Assert.Equal("logging,retry,recovery", middleware);
        Assert.Null(config);
    }

    [Fact]
    public void BuildMiddlewareSpec_EmptyBindings_ReturnsDefaults()
    {
        var (middleware, config) = AgentContextBuilder.BuildMiddlewareSpec([]);
        Assert.Equal("logging,retry,recovery", middleware);
        Assert.Null(config);
    }

    [Fact]
    public void BuildMiddlewareSpec_WithBindings_JoinsKeysAndMapsOptions()
    {
        var bindings = new List<MiddlewareBinding>
        {
            new() { Key = "logging", Options = new Dictionary<string, string>() },
            new() { Key = "pii", Options = new Dictionary<string, string> { ["locales"] = "TW,US" } },
        };
        var (middleware, config) = AgentContextBuilder.BuildMiddlewareSpec(bindings);
        Assert.Equal("logging,pii", middleware);
        Assert.NotNull(config);
        Assert.True(config!.ContainsKey("pii"));
        Assert.Equal("TW,US", config["pii"]["locales"]);
    }

    [Fact]
    public void BuildMiddlewareSpec_SingleBinding_NoExtraComma()
    {
        var bindings = new List<MiddlewareBinding>
        {
            new() { Key = "guardrails", Options = new Dictionary<string, string> { ["blockedTerms"] = "spam" } },
        };
        var (middleware, config) = AgentContextBuilder.BuildMiddlewareSpec(bindings);
        Assert.Equal("guardrails", middleware);
        Assert.NotNull(config);
        Assert.Single(config!);
        Assert.Equal("spam", config["guardrails"]["blockedTerms"]);
    }

    // ── BuildInstructions ──

    [Fact]
    public void BuildInstructions_NullInput_ReturnsDefaultAssistant()
    {
        var result = AgentContextBuilder.BuildInstructions(null);
        Assert.Contains("helpful assistant", result);
    }

    [Fact]
    public void BuildInstructions_WithInstructions_IncludesText()
    {
        var result = AgentContextBuilder.BuildInstructions("Analyze data");
        Assert.Contains("Analyze data", result);
    }

    [Fact]
    public void BuildInstructions_JsonFormat_AddsJsonPrompt()
    {
        var result = AgentContextBuilder.BuildInstructions("Do something", "json");
        Assert.Contains("JSON format", result);
    }

    [Fact]
    public void BuildInstructions_JsonSchemaFormat_AddsJsonPrompt()
    {
        var result = AgentContextBuilder.BuildInstructions("Do something", "json_schema");
        Assert.Contains("JSON format", result);
    }

    [Fact]
    public void BuildInstructions_TextFormat_NoJsonPrompt()
    {
        var result = AgentContextBuilder.BuildInstructions("Do something", "text");
        Assert.DoesNotContain("JSON format", result);
    }

    [Fact]
    public void BuildInstructions_IncludesCurrentDate()
    {
        var result = AgentContextBuilder.BuildInstructions("test");
        Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), result);
    }

    [Fact]
    public void BuildInstructions_InstructionsContainJson_NoDoubleJsonPrompt()
    {
        var result = AgentContextBuilder.BuildInstructions("Return valid JSON output", "json");
        // Should NOT append extra "Respond in JSON format." because instructions already mention json
        Assert.DoesNotContain("Respond in JSON format.", result);
    }

    // ── NormalizeProvider ──

    [Theory]
    [InlineData("openai", "openai")]
    [InlineData("azureopenai", "azure-openai")]
    [InlineData("azure-openai", "azure-openai")]
    [InlineData("azure_openai", "azure-openai")]
    [InlineData("", "openai")]
    [InlineData("anthropic", "anthropic")]
    [InlineData("custom-provider", "custom-provider")]
    public void NormalizeProvider_MapsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, AgentContextBuilder.NormalizeProvider(input));
    }

    [Fact]
    public void NormalizeProvider_IsCaseInsensitive()
    {
        Assert.Equal("azure-openai", AgentContextBuilder.NormalizeProvider("AzureOpenAI"));
        Assert.Equal("openai", AgentContextBuilder.NormalizeProvider("OpenAI"));
    }

    // ── NormalizeCredential ──

    [Fact]
    public void NormalizeCredential_KeyOptionalProvider_DefaultsApiKey()
    {
        var (apiKey, _) = AgentContextBuilder.NormalizeCredential("ollama", "", "http://localhost:11434");
        Assert.False(string.IsNullOrWhiteSpace(apiKey));
        Assert.Equal(Providers.DefaultLocalApiKey, apiKey);
    }

    [Fact]
    public void NormalizeCredential_NonKeyOptionalProvider_PreservesEmptyKey()
    {
        var (apiKey, _) = AgentContextBuilder.NormalizeCredential("openai", "", "");
        Assert.Equal("", apiKey);
    }

    [Fact]
    public void NormalizeCredential_PreservesExistingApiKey()
    {
        var (apiKey, _) = AgentContextBuilder.NormalizeCredential("ollama", "my-key", "http://localhost:11434");
        Assert.Equal("my-key", apiKey);
    }
}
