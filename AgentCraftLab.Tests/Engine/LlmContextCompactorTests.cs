using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class LlmContextCompactorTests
{
    private sealed class StubChatClient(string responseText) : IChatClient
    {
        public int CallCount { get; private set; }
        public Exception? ExceptionToThrow { get; set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, responseText);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task UnderBudget_ReturnsNull()
    {
        var inner = new StubChatClient("compressed");
        var compactor = new LlmContextCompactor(inner);

        // 短文字不需壓縮（budget 很大）
        var result = await compactor.CompressAsync("short text", "query", 10000);

        Assert.Null(result);
        Assert.Equal(0, inner.CallCount); // 不呼叫 LLM
    }

    [Fact]
    public async Task OverBudget_ReturnsCompressed()
    {
        var inner = new StubChatClient("summary of content");
        var compactor = new LlmContextCompactor(inner);

        // 長文字超過 budget → LLM 壓縮
        var longContent = new string('A', 5000); // ~1250 tokens (ASCII / 4)
        var result = await compactor.CompressAsync(longContent, "query", 100);

        Assert.NotNull(result);
        Assert.Equal("summary of content", result);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task LlmThrows_ReturnsNull()
    {
        var inner = new StubChatClient("") { ExceptionToThrow = new InvalidOperationException("API error") };
        var compactor = new LlmContextCompactor(inner);

        var longContent = new string('A', 5000);
        var result = await compactor.CompressAsync(longContent, "query", 100);

        Assert.Null(result); // graceful degradation
    }

    [Fact]
    public async Task LlmReturnsEmpty_ReturnsNull()
    {
        var inner = new StubChatClient("");
        var compactor = new LlmContextCompactor(inner);

        var longContent = new string('A', 5000);
        var result = await compactor.CompressAsync(longContent, "query", 100);

        Assert.Null(result);
    }

    [Fact]
    public async Task EmptyContent_ReturnsNull()
    {
        var inner = new StubChatClient("compressed");
        var compactor = new LlmContextCompactor(inner);

        var result = await compactor.CompressAsync("", "query", 100);

        Assert.Null(result);
        Assert.Equal(0, inner.CallCount);
    }
}
