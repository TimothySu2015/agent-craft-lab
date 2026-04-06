using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Partitioners;

namespace AgentCraftLab.Tests.Cleaner;

public class ImageDescriptionCacheTests
{
    [Fact]
    public void CacheMiss_ReturnsFalse()
    {
        var cache = new ImageDescriptionCache();

        var found = cache.TryGet("nonexistent", out var result);

        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void CacheHit_ReturnsCachedResult()
    {
        var cache = new ImageDescriptionCache();
        var description = new ImageDescriptionResult
        {
            Description = "A bar chart showing Q3 revenue",
            Confidence = 0.9f,
            InputTokens = 500,
            OutputTokens = 100,
        };

        cache.Set("abc123", description);
        var found = cache.TryGet("abc123", out var result);

        Assert.True(found);
        Assert.NotNull(result);
        Assert.Equal("A bar chart showing Q3 revenue", result.Description);
        Assert.Equal(0.9f, result.Confidence);
    }

    [Fact]
    public void DifferentKeys_IndependentResults()
    {
        var cache = new ImageDescriptionCache();
        var desc1 = new ImageDescriptionResult { Description = "Chart 1", Confidence = 0.8f };
        var desc2 = new ImageDescriptionResult { Description = "Chart 2", Confidence = 0.7f };

        cache.Set("hash1", desc1);
        cache.Set("hash2", desc2);

        cache.TryGet("hash1", out var result1);
        cache.TryGet("hash2", out var result2);

        Assert.Equal("Chart 1", result1!.Description);
        Assert.Equal("Chart 2", result2!.Description);
    }

    [Fact]
    public void DuplicateSet_FirstWins()
    {
        var cache = new ImageDescriptionCache();
        var desc1 = new ImageDescriptionResult { Description = "First", Confidence = 0.9f };
        var desc2 = new ImageDescriptionResult { Description = "Second", Confidence = 0.8f };

        cache.Set("same-hash", desc1);
        cache.Set("same-hash", desc2); // ConcurrentDictionary.TryAdd → first wins

        cache.TryGet("same-hash", out var result);
        Assert.Equal("First", result!.Description);
    }
}
