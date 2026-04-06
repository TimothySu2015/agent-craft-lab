using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Middleware;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class ApplyMiddlewareTests
{
    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "OK")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "OK");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class StubContextCompactor : IContextCompactor
    {
        public bool WasInjected { get; private set; }

        public Task<string?> CompressAsync(string content, string context, int tokenBudget, CancellationToken ct = default)
        {
            WasInjected = true;
            return Task.FromResult<string?>("compressed");
        }
    }

    [Fact]
    public void ApplyMiddleware_WithRecovery_CreatesRecoveryChatClient()
    {
        var inner = new StubChatClient();

        var result = AgentContextBuilder.ApplyMiddleware(inner, "recovery");

        // result 應該是 RecoveryChatClient（包裝了 inner）
        Assert.IsType<RecoveryChatClient>(result);
    }

    [Fact]
    public void ApplyMiddleware_WithRecoveryAndCompactor_InjectsCompactor()
    {
        var inner = new StubChatClient();
        var compactor = new StubContextCompactor();

        var result = AgentContextBuilder.ApplyMiddleware(
            inner, "recovery", contextCompactor: compactor);

        // result 是 RecoveryChatClient
        Assert.IsType<RecoveryChatClient>(result);
    }

    [Fact]
    public void ApplyMiddleware_WithoutRecovery_NoRecoveryChatClient()
    {
        var inner = new StubChatClient();

        var result = AgentContextBuilder.ApplyMiddleware(inner, "logging,retry");

        // 不含 recovery → 不應該包裝 RecoveryChatClient
        Assert.IsNotType<RecoveryChatClient>(result);
    }

    [Fact]
    public void ApplyMiddleware_NullMiddleware_ReturnsOriginal()
    {
        var inner = new StubChatClient();

        var result = AgentContextBuilder.ApplyMiddleware(inner, null);

        Assert.Same(inner, result);
    }

    [Fact]
    public void ApplyMiddleware_WithContextCompactorButNoRecovery_CompactorIgnored()
    {
        var inner = new StubChatClient();
        var compactor = new StubContextCompactor();

        // contextCompactor 只在 "recovery" 啟用時有效
        var result = AgentContextBuilder.ApplyMiddleware(
            inner, "logging,retry", contextCompactor: compactor);

        Assert.IsNotType<RecoveryChatClient>(result);
    }
}

public class ReactExecutorConfigTests
{
    [Fact]
    public void DefaultMiddleware_ContainsRecovery()
    {
        var config = new AgentCraftLab.Autonomous.Models.ReactExecutorConfig();

        Assert.Contains("recovery", config.OrchestratorMiddleware);
        Assert.Contains("logging", config.OrchestratorMiddleware);
        Assert.Contains("retry", config.OrchestratorMiddleware);
    }

    [Fact]
    public void CustomMiddleware_Overrides()
    {
        var config = new AgentCraftLab.Autonomous.Models.ReactExecutorConfig
        {
            OrchestratorMiddleware = "logging,retry"
        };

        Assert.DoesNotContain("recovery", config.OrchestratorMiddleware);
    }
}
