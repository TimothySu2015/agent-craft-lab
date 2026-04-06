using AgentCraftLab.Search.Chunking;

namespace AgentCraftLab.Tests.Search;

public class FixedSizeChunkerTests
{
    private readonly FixedSizeChunker _chunker = new();

    [Fact]
    public void Chunk_EmptyText_ReturnsEmpty()
    {
        var result = _chunker.Chunk("", 100, 0);
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_NullText_ReturnsEmpty()
    {
        var result = _chunker.Chunk(null!, 100, 0);
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_ShortText_SingleChunk()
    {
        var result = _chunker.Chunk("Hello world", 100, 0);
        Assert.Single(result);
        Assert.Equal("Hello world", result[0].Text);
    }

    [Fact]
    public void Chunk_LongText_MultipleChunks()
    {
        var text = string.Join(". ", Enumerable.Range(1, 50).Select(i => $"Sentence number {i}"));
        var result = _chunker.Chunk(text, 100, 0);
        Assert.True(result.Count > 1);
    }

    [Fact]
    public void Chunk_OverlapWorks()
    {
        var text = string.Join(". ", Enumerable.Range(1, 50).Select(i => $"Sentence number {i}"));
        var result = _chunker.Chunk(text, 100, 20);
        Assert.True(result.Count > 1);
        // With overlap, chunks should be more than without
        var noOverlap = _chunker.Chunk(text, 100, 0);
        Assert.True(result.Count >= noOverlap.Count);
    }

    [Fact]
    public void Chunk_IndexesAreSequential()
    {
        var text = string.Join(". ", Enumerable.Range(1, 30).Select(i => $"Sentence {i}"));
        var result = _chunker.Chunk(text, 50, 0);
        for (var i = 0; i < result.Count; i++)
        {
            Assert.Equal(i, result[i].Index);
        }
    }

    [Fact]
    public void Chunk_EachChunkNotExceedsSize()
    {
        var text = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"word{i}"));
        var result = _chunker.Chunk(text, 100, 0);
        foreach (var chunk in result)
        {
            // 允許超過一點（因為會在句號/空格邊界斷）
            Assert.True(chunk.Text.Length <= 150, $"Chunk too large: {chunk.Text.Length}");
        }
    }

    [Fact]
    public void Chunk_ChineseText_Works()
    {
        var text = "這是第一句話。這是第二句話。這是第三句話。這是第四句話。這是第五句話。";
        var result = _chunker.Chunk(text, 20, 0);
        Assert.True(result.Count >= 1);
    }
}
