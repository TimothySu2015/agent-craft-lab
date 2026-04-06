using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class QueryExpanderTests
{
    /// <summary>Fake ChatClient 回傳預設 JSON 陣列。</summary>
    private class FakeChatClient : IChatClient
    {
        private readonly string _response;
        public FakeChatClient(string response) => _response = response;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, _response)]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? key = null) where TService : class => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task ExpandAsync_ValidJsonResponse_ReturnsTwoVariants()
    {
        var client = new FakeChatClient("""["revenue growth rate", "本季財務表現"]""");
        var expander = new QueryExpander(client);

        var result = await expander.ExpandAsync("營收成長多少");

        Assert.Equal(2, result.Count);
        Assert.Equal("revenue growth rate", result[0]);
        Assert.Equal("本季財務表現", result[1]);
    }

    [Fact]
    public async Task ExpandAsync_JsonWithSurroundingText_ParsesCorrectly()
    {
        var client = new FakeChatClient("""Here are the variants: ["variant one", "variant two"] hope this helps!""");
        var expander = new QueryExpander(client);

        var result = await expander.ExpandAsync("test query");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ExpandAsync_InvalidResponse_ReturnsEmptyList()
    {
        var client = new FakeChatClient("I don't know how to do that.");
        var expander = new QueryExpander(client);

        var result = await expander.ExpandAsync("test query");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExpandAsync_EmptyArray_ReturnsEmptyList()
    {
        var client = new FakeChatClient("[]");
        var expander = new QueryExpander(client);

        var result = await expander.ExpandAsync("test query");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExpandAsync_MoreThanTwo_TakesOnlyTwo()
    {
        var client = new FakeChatClient("""["a", "b", "c", "d"]""");
        var expander = new QueryExpander(client);

        var result = await expander.ExpandAsync("test");

        Assert.Equal(2, result.Count);
    }
}
