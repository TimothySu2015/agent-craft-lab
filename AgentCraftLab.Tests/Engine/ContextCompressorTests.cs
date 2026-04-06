using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class ContextCompressorTests
{
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

    private static List<RagChunk> MakeChunks(int count, int charsEach)
    {
        return Enumerable.Range(0, count).Select(i => new RagChunk
        {
            Content = new string('x', charsEach),
            FileName = $"file{i}.txt",
            ChunkIndex = i,
            Score = 0.5f
        }).ToList();
    }

    [Fact]
    public async Task CompressIfNeeded_UnderBudget_ReturnsNull()
    {
        var compressor = new ContextCompressor(new FakeChatClient("should not be called"));
        // 3 short chunks (~30 tokens total) with budget 1000
        var chunks = MakeChunks(3, 50);

        var result = await compressor.CompressIfNeededAsync("query", chunks, 1000);

        Assert.Null(result); // 不需要壓縮
    }

    [Fact]
    public async Task CompressIfNeeded_OverBudget_ReturnsCompressed()
    {
        var compressor = new ContextCompressor(new FakeChatClient("This is the compressed summary."));
        // 10 large chunks (~2500 tokens) with budget 500
        var chunks = MakeChunks(10, 1000);

        var result = await compressor.CompressIfNeededAsync("query", chunks, 500);

        Assert.NotNull(result);
        Assert.Equal("This is the compressed summary.", result);
    }

    [Fact]
    public async Task CompressIfNeeded_LlmReturnsEmpty_ReturnsNull()
    {
        var compressor = new ContextCompressor(new FakeChatClient(""));
        var chunks = MakeChunks(10, 1000);

        var result = await compressor.CompressIfNeededAsync("query", chunks, 500);

        Assert.Null(result); // fallback
    }

    [Fact]
    public async Task CompressIfNeeded_LlmThrows_ReturnsNull()
    {
        var compressor = new ContextCompressor(new ThrowingChatClient());
        var chunks = MakeChunks(10, 1000);

        var result = await compressor.CompressIfNeededAsync("query", chunks, 500);

        Assert.Null(result); // graceful fallback
    }

    private class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => throw new HttpRequestException("API error");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? key = null) where TService : class => null;
        public void Dispose() { }
    }
}
