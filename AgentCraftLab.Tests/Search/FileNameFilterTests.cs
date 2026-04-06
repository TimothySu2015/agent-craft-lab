using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Providers.InMemory;

namespace AgentCraftLab.Tests.Search;

public class FileNameFilterTests
{
    private readonly InMemorySearchEngine _engine;

    public FileNameFilterTests()
    {
        _engine = new InMemorySearchEngine(new SearchEngineOptions());
    }

    private async Task SeedDocuments(string indexName)
    {
        await _engine.EnsureIndexAsync(indexName);
        await _engine.IndexDocumentsAsync(indexName,
        [
            new SearchDocument { Id = "1", Content = "quarterly revenue growth", FileName = "report.pdf", ChunkIndex = 0 },
            new SearchDocument { Id = "2", Content = "product roadmap details", FileName = "roadmap.docx", ChunkIndex = 0 },
            new SearchDocument { Id = "3", Content = "revenue forecast data", FileName = "forecast.pdf", ChunkIndex = 0 },
            new SearchDocument { Id = "4", Content = "team meeting notes", FileName = "notes.txt", ChunkIndex = 0 },
        ]);
    }

    [Fact]
    public async Task SearchAsync_NoFilter_ReturnsAllMatches()
    {
        await SeedDocuments("f1");
        var results = await _engine.SearchAsync("f1", new SearchQuery
        {
            Text = "revenue",
            Mode = SearchMode.FullText,
            TopK = 10,
            FileNameFilter = null
        });
        Assert.Equal(2, results.Count); // report.pdf + forecast.pdf
    }

    [Fact]
    public async Task SearchAsync_FilterByExtension_OnlyMatchingFiles()
    {
        await SeedDocuments("f2");
        var results = await _engine.SearchAsync("f2", new SearchQuery
        {
            Text = "revenue",
            Mode = SearchMode.FullText,
            TopK = 10,
            FileNameFilter = ".pdf"
        });
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.EndsWith(".pdf", r.FileName));
    }

    [Fact]
    public async Task SearchAsync_FilterByName_SubstringMatch()
    {
        await SeedDocuments("f3");
        var results = await _engine.SearchAsync("f3", new SearchQuery
        {
            Text = "revenue roadmap team",
            Mode = SearchMode.FullText,
            TopK = 10,
            FileNameFilter = "report"
        });
        Assert.Single(results);
        Assert.Equal("report.pdf", results[0].FileName);
    }

    [Fact]
    public async Task SearchAsync_FilterCaseInsensitive()
    {
        await SeedDocuments("f4");
        var results = await _engine.SearchAsync("f4", new SearchQuery
        {
            Text = "revenue",
            Mode = SearchMode.FullText,
            TopK = 10,
            FileNameFilter = ".PDF"
        });
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_FilterNoMatch_ReturnsEmpty()
    {
        await SeedDocuments("f5");
        var results = await _engine.SearchAsync("f5", new SearchQuery
        {
            Text = "revenue",
            Mode = SearchMode.FullText,
            TopK = 10,
            FileNameFilter = ".xlsx"
        });
        Assert.Empty(results);
    }
}
