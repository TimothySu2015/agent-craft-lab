using AgentCraftLab.Autonomous.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Autonomous;

public class EntityExtractorTests
{
    private sealed class FakeChatClient(string response) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, response)]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? key = null) where TService : class => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Extract_EmptyResult_ReturnsEmpty()
    {
        var client = new FakeChatClient("[]");
        var result = await EntityExtractor.ExtractAsync(client, "test goal", "", CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Extract_ValidJson_ParsesEntities()
    {
        var json = """
            [
              {"name": "NVIDIA", "type": "organization", "facts": ["GPU manufacturer", "Revenue $39B"]},
              {"name": "TSMC", "type": "organization", "facts": ["Chip foundry"]}
            ]
            """;
        var client = new FakeChatClient(json);
        var result = await EntityExtractor.ExtractAsync(client, "Compare NVIDIA and TSMC", "NVIDIA makes GPUs...", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("NVIDIA", result[0].Name);
        Assert.Equal("organization", result[0].Type);
        Assert.Equal(2, result[0].Facts.Count);
    }

    [Fact]
    public async Task Extract_WithMarkdownFence_StillParses()
    {
        var json = """
            ```json
            [{"name": "Apple", "type": "organization", "facts": ["Tech company"]}]
            ```
            """;
        var client = new FakeChatClient(json);
        var result = await EntityExtractor.ExtractAsync(client, "Apple info", "Apple is a tech company", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Apple", result[0].Name);
    }

    [Fact]
    public async Task Extract_InvalidJson_ReturnsEmpty()
    {
        var client = new FakeChatClient("not valid json");
        var result = await EntityExtractor.ExtractAsync(client, "test", "some result", CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Extract_RespectsMaxEntities()
    {
        var entities = Enumerable.Range(0, 15)
            .Select(i => $"{{\"name\": \"Entity{i}\", \"type\": \"concept\", \"facts\": [\"fact\"]}}")
            .ToList();
        var json = $"[{string.Join(",", entities)}]";
        var client = new FakeChatClient(json);

        var result = await EntityExtractor.ExtractAsync(client, "test", "lots of entities", CancellationToken.None);
        Assert.True(result.Count <= 10);
    }
}
