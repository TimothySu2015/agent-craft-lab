using System.Text.Json;
using AgentCraftLab.Api.Endpoints;

namespace AgentCraftLab.Tests.Api;

public class AgUiEndpointHelpersTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void ExtractCredentials_WithValidJsonElement_ReturnsParsedCredentials()
    {
        var json = """
        {
            "openai": { "apiKey": "sk-123", "endpoint": "", "model": "gpt-4o" }
        }
        """;
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        var props = new Dictionary<string, object> { ["credentials"] = element };
        var result = AgUiEndpoints.ExtractCredentials(props, JsonOptions);

        Assert.Single(result);
        Assert.True(result.ContainsKey("openai"));
        Assert.Equal("sk-123", result["openai"].ApiKey);
        Assert.Equal("gpt-4o", result["openai"].Model);
    }

    [Fact]
    public void ExtractCredentials_WithEmptyProps_ReturnsEmptyDict()
    {
        var props = new Dictionary<string, object>();
        var result = AgUiEndpoints.ExtractCredentials(props, JsonOptions);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractString_WithValidKey_ReturnsString()
    {
        var props = new Dictionary<string, object> { ["name"] = "hello" };
        var result = AgUiEndpoints.ExtractString(props, "name");

        Assert.Equal("hello", result);
    }

    [Fact]
    public void ExtractString_WithMissingKey_ReturnsNull()
    {
        var props = new Dictionary<string, object> { ["other"] = "value" };
        var result = AgUiEndpoints.ExtractString(props, "name");

        Assert.Null(result);
    }

    [Fact]
    public void ExtractString_WithJsonElementValue_ExtractsString()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("\"test-value\"");
        var props = new Dictionary<string, object> { ["key"] = element };

        var result = AgUiEndpoints.ExtractString(props, "key");

        Assert.Equal("test-value", result);
    }
}
