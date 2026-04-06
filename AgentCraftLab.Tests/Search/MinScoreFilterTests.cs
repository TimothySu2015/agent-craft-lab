using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Providers.InMemory;

namespace AgentCraftLab.Tests.Search;

public class MinScoreFilterTests
{
    private readonly InMemorySearchEngine _engine;

    public MinScoreFilterTests()
    {
        _engine = new InMemorySearchEngine(new SearchEngineOptions());
    }

    private async Task SeedDocuments(string indexName)
    {
        await _engine.EnsureIndexAsync(indexName);
        await _engine.IndexDocumentsAsync(indexName,
        [
            new SearchDocument { Id = "1", Content = "apple banana cherry", FileName = "fruits.txt", ChunkIndex = 0 },
            new SearchDocument { Id = "2", Content = "dog cat mouse", FileName = "animals.txt", ChunkIndex = 0 },
            new SearchDocument { Id = "3", Content = "apple pie recipe", FileName = "recipe.txt", ChunkIndex = 0 }
        ]);
    }

    [Fact]
    public async Task SearchAsync_NoMinScore_ReturnsAllMatches()
    {
        await SeedDocuments("idx1");

        var results = await _engine.SearchAsync("idx1", new SearchQuery
        {
            Text = "apple",
            Mode = SearchMode.FullText,
            TopK = 10,
            MinScore = null
        });

        Assert.Equal(2, results.Count); // "apple banana cherry" + "apple pie recipe"
    }

    [Fact]
    public async Task SearchAsync_HighMinScore_FiltersLowScoreResults()
    {
        await SeedDocuments("idx2");

        var results = await _engine.SearchAsync("idx2", new SearchQuery
        {
            Text = "apple banana",
            Mode = SearchMode.FullText,
            TopK = 10,
            MinScore = 0.9f
        });

        // "apple banana cherry" 匹配 2/2 關鍵字 = 1.0 → 通過
        // "apple pie recipe" 匹配 1/2 關鍵字 = 0.5 → 被過濾
        Assert.Single(results);
        Assert.Equal("1", results[0].Id);
    }

    [Fact]
    public async Task SearchAsync_MinScoreZero_ReturnsAllMatches()
    {
        await SeedDocuments("idx3");

        var results = await _engine.SearchAsync("idx3", new SearchQuery
        {
            Text = "apple",
            Mode = SearchMode.FullText,
            TopK = 10,
            MinScore = 0f
        });

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_MinScoreOne_OnlyPerfectMatches()
    {
        await SeedDocuments("idx4");

        var results = await _engine.SearchAsync("idx4", new SearchQuery
        {
            Text = "apple",
            Mode = SearchMode.FullText,
            TopK = 10,
            MinScore = 1.0f
        });

        // 單一關鍵字 "apple"，匹配 1/1 = 1.0 → 全部通過
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_VeryHighMinScore_ReturnsEmpty()
    {
        await SeedDocuments("idx5");

        var results = await _engine.SearchAsync("idx5", new SearchQuery
        {
            Text = "apple banana cherry dog",
            Mode = SearchMode.FullText,
            TopK = 10,
            MinScore = 0.99f
        });

        // 沒有文件匹配全部 4 個關鍵字 → 最高分 3/4 = 0.75 → 全被過濾
        Assert.Empty(results);
    }
}
