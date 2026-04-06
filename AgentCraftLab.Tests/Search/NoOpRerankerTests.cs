using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Reranking;

namespace AgentCraftLab.Tests.Search;

public class NoOpRerankerTests
{
    private readonly NoOpReranker _reranker = new();

    [Fact]
    public async Task RerankAsync_EmptyResults_ReturnsEmpty()
    {
        var results = await _reranker.RerankAsync("query", [], 5);
        Assert.Empty(results);
    }

    [Fact]
    public async Task RerankAsync_SingleResult_ReturnsSame()
    {
        var input = new List<SearchResult>
        {
            new() { Id = "1", Content = "hello", Score = 0.9f }
        };

        var results = await _reranker.RerankAsync("query", input, 5);
        Assert.Single(results);
        Assert.Equal("1", results[0].Id);
    }

    [Fact]
    public async Task RerankAsync_PreservesOrder()
    {
        var input = new List<SearchResult>
        {
            new() { Id = "1", Content = "first", Score = 0.9f },
            new() { Id = "2", Content = "second", Score = 0.8f },
            new() { Id = "3", Content = "third", Score = 0.7f }
        };

        var results = await _reranker.RerankAsync("query", input, 10);
        Assert.Equal(3, results.Count);
        Assert.Equal("1", results[0].Id);
        Assert.Equal("2", results[1].Id);
        Assert.Equal("3", results[2].Id);
    }

    [Fact]
    public async Task RerankAsync_TruncatesToTopK()
    {
        var input = new List<SearchResult>
        {
            new() { Id = "1", Content = "a", Score = 0.9f },
            new() { Id = "2", Content = "b", Score = 0.8f },
            new() { Id = "3", Content = "c", Score = 0.7f },
            new() { Id = "4", Content = "d", Score = 0.6f },
            new() { Id = "5", Content = "e", Score = 0.5f }
        };

        var results = await _reranker.RerankAsync("query", input, 3);
        Assert.Equal(3, results.Count);
        Assert.Equal("1", results[0].Id);
        Assert.Equal("3", results[2].Id);
    }

    [Fact]
    public async Task RerankAsync_TopKGreaterThanCount_ReturnsAll()
    {
        var input = new List<SearchResult>
        {
            new() { Id = "1", Content = "x", Score = 0.5f },
            new() { Id = "2", Content = "y", Score = 0.3f }
        };

        var results = await _reranker.RerankAsync("query", input, 10);
        Assert.Equal(2, results.Count);
    }
}
