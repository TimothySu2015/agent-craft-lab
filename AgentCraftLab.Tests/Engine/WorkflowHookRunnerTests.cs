using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCraftLab.Tests.Engine;

public class WorkflowHookRunnerTests
{
    private readonly WorkflowHookRunner _runner = new(new NoOpHttpClientFactory(), NullLogger<WorkflowHookRunner>.Instance);

    private static HookContext CreateContext(string input = "hello") => new()
    {
        Input = input,
        WorkflowName = "test",
        UserId = "u1"
    };

    [Fact]
    public async Task CodeHook_Template_ReturnsTransformedInput()
    {
        var hook = new CodeHook
        {
            Kind = TransformKind.Template,
            Expression = "Processed: {{input}}"
        };

        var result = await _runner.ExecuteAsync(hook, CreateContext("hello"));

        Assert.False(result.IsBlocked);
        Assert.Contains("Processed: hello", result.TransformedInput);
    }

    [Fact]
    public async Task CodeHook_Upper_ReturnsUpperCased()
    {
        var hook = new CodeHook { Kind = TransformKind.Upper };

        var result = await _runner.ExecuteAsync(hook, CreateContext("hello"));

        Assert.False(result.IsBlocked);
        Assert.Equal("HELLO", result.TransformedInput);
    }

    [Fact]
    public async Task CodeHook_BlockPattern_BlocksMatchingInput()
    {
        var hook = new CodeHook
        {
            Kind = TransformKind.Template,
            Expression = "{{input}}",
            BlockPattern = "forbidden"
        };

        var result = await _runner.ExecuteAsync(hook, CreateContext("this is forbidden"));

        Assert.True(result.IsBlocked);
    }

    [Fact]
    public async Task CodeHook_BlockPattern_AllowsNonMatchingInput()
    {
        var hook = new CodeHook
        {
            Kind = TransformKind.Template,
            Expression = "{{input}}",
            BlockPattern = "forbidden"
        };

        var result = await _runner.ExecuteAsync(hook, CreateContext("this is fine"));

        Assert.False(result.IsBlocked);
    }

    [Fact]
    public async Task WebhookHook_EmptyUrl_SkipsWithMessage()
    {
        var hook = new WebhookHook { Url = "" };

        var result = await _runner.ExecuteAsync(hook, CreateContext());

        Assert.False(result.IsBlocked);
        Assert.NotNull(result.Message);
        Assert.Contains("empty", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_NullBlockPattern_DoesNotBlock()
    {
        var hook = new CodeHook
        {
            Kind = TransformKind.Template,
            Expression = "{{input}}"
        };

        var result = await _runner.ExecuteAsync(hook, CreateContext("anything"));

        Assert.False(result.IsBlocked);
    }

    private class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
