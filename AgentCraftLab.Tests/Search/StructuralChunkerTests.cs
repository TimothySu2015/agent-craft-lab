using AgentCraftLab.Search.Chunking;

namespace AgentCraftLab.Tests.Search;

public class StructuralChunkerTests
{
    private readonly StructuralChunker _chunker = new();

    [Fact]
    public void Chunk_EmptyText_ReturnsEmpty()
    {
        var result = _chunker.Chunk("", 500, 0);
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_NullText_ReturnsEmpty()
    {
        var result = _chunker.Chunk(null!, 500, 0);
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_ShortText_SingleChunk()
    {
        var result = _chunker.Chunk("Hello world", 500, 0);
        Assert.Single(result);
        Assert.Equal("Hello world", result[0].Text);
    }

    [Fact]
    public void Chunk_MarkdownHeadings_SplitsAtHeadings()
    {
        var text = "# Introduction\nThis is the introduction section with enough content.\n\n## Methods\nThis is the methods section with enough content.\n\n## Results\nThis is the results section with enough content.";

        // chunkSize 夠小才會在 heading 邊界分割，而非合併
        var result = _chunker.Chunk(text, 80, 0);
        Assert.True(result.Count >= 3, $"Expected >= 3 chunks, got {result.Count}");
    }

    [Fact]
    public void Chunk_DoubleNewlines_SplitsAtParagraphs()
    {
        var text = "First paragraph content.\n\nSecond paragraph content.\n\nThird paragraph content.";
        var result = _chunker.Chunk(text, 500, 0);
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public void Chunk_LongSection_FallsBackToFixedSize()
    {
        // 一個很長的段落（沒有 heading），超過 chunkSize
        var longText = string.Join(". ", Enumerable.Range(1, 100).Select(i => $"Sentence number {i}"));
        var result = _chunker.Chunk(longText, 200, 0);
        Assert.True(result.Count > 1, "Long text should be split into multiple chunks");
    }

    [Fact]
    public void Chunk_ShortSections_MergedTogether()
    {
        // 多個非常短的段落，應該被合併
        var text = "A.\n\nB.\n\nC.\n\nD.\n\nE.";
        var result = _chunker.Chunk(text, 500, 0);
        // 所有段落加起來遠小於 500，應合併為一段
        Assert.Single(result);
        Assert.Contains("A.", result[0].Text);
        Assert.Contains("E.", result[0].Text);
    }

    [Fact]
    public void Chunk_IndexesAreSequential()
    {
        var text = "# Part 1\nContent 1.\n\n# Part 2\nContent 2.\n\n# Part 3\nContent 3.";
        var result = _chunker.Chunk(text, 50, 0);
        for (var i = 0; i < result.Count; i++)
        {
            Assert.Equal(i, result[i].Index);
        }
    }

    [Fact]
    public void Chunk_StartPositionIsNonNegative()
    {
        var text = "# Title\nSome text.\n\n## Section\nMore text here.\n\n## Another\nFinal text.";
        var result = _chunker.Chunk(text, 50, 0);
        foreach (var chunk in result)
        {
            Assert.True(chunk.StartPosition >= 0, $"StartPosition should be >= 0, got {chunk.StartPosition}");
        }
    }

    [Fact]
    public void Chunk_ChineseText_Works()
    {
        var text = "# 簡介\n這是第一段。\n\n## 方法\n這是第二段。\n\n## 結果\n這是第三段。";
        var result = _chunker.Chunk(text, 500, 0);
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public void Chunk_HtmlHeadings_SplitsAtHeadings()
    {
        var text = "<h1>Title</h1>\nIntro text with enough content to exceed chunk size limit.\n\n<h2>Section</h2>\nSection content with enough text to be a separate chunk.";
        var result = _chunker.Chunk(text, 60, 0);
        Assert.True(result.Count >= 2, $"Expected >= 2 chunks for HTML headings, got {result.Count}");
    }

    [Fact]
    public void Chunk_MixedContent_HandlesGracefully()
    {
        var text = "# Heading\nNormal paragraph text here with some details.\n\nAnother paragraph with more content.\n\n## Sub heading\nMore content under sub heading with additional text.";

        var result = _chunker.Chunk(text, 70, 0);
        Assert.True(result.Count >= 2, $"Expected >= 2 chunks, got {result.Count}");
        foreach (var chunk in result)
        {
            Assert.True(chunk.Text.Length > 0);
        }
    }
}
