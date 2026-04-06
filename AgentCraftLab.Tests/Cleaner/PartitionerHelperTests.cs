using AgentCraftLab.Cleaner;
using AgentCraftLab.Cleaner.Elements;
using AgentCraftLab.Cleaner.Partitioners;

namespace AgentCraftLab.Tests.Cleaner;

public class PartitionerHelperTests
{
    // ── ToMarkdownTable ──

    [Fact]
    public void ToMarkdownTable_BasicTable()
    {
        var rows = new[]
        {
            new[] { "Name", "Age" },
            new[] { "Alice", "30" },
            new[] { "Bob", "25" },
        };

        var result = PartitionerHelper.ToMarkdownTable(rows);

        Assert.Contains("| Name | Age |", result);
        Assert.Contains("| --- | --- |", result);
        Assert.Contains("| Alice | 30 |", result);
        Assert.Contains("| Bob | 25 |", result);
    }

    [Fact]
    public void ToMarkdownTable_SingleRow()
    {
        var rows = new[] { new[] { "Header1", "Header2" } };
        var result = PartitionerHelper.ToMarkdownTable(rows);

        Assert.Contains("| Header1 | Header2 |", result);
        Assert.Contains("| --- |", result);
    }

    [Fact]
    public void ToMarkdownTable_EmptyRows_ReturnsEmpty()
    {
        var result = PartitionerHelper.ToMarkdownTable([]);
        Assert.Equal("", result);
    }

    [Fact]
    public void ToMarkdownTable_TrimsWhitespace()
    {
        var rows = new[] { new[] { "  padded  ", "  text  " } };
        var result = PartitionerHelper.ToMarkdownTable(rows);
        Assert.Contains("| padded | text |", result);
    }

    // ── CreateElement ──

    [Fact]
    public void CreateElement_SetsAllProperties()
    {
        var index = 0;
        var metadata = new Dictionary<string, string> { ["key"] = "value" };

        var el = PartitionerHelper.CreateElement(
            ElementType.Title, "Hello", "test.docx", ref index, metadata, pageNumber: 5);

        Assert.Equal(ElementType.Title, el.Type);
        Assert.Equal("Hello", el.Text);
        Assert.Equal("test.docx", el.FileName);
        Assert.Equal(0, el.Index);
        Assert.Equal(5, el.PageNumber);
        Assert.Equal("value", el.Metadata["key"]);
        Assert.Equal(1, index); // index incremented
    }

    [Fact]
    public void CreateElement_IncrementsIndex()
    {
        var index = 10;
        var metadata = new Dictionary<string, string>();

        PartitionerHelper.CreateElement(ElementType.NarrativeText, "A", "f.txt", ref index, metadata);
        PartitionerHelper.CreateElement(ElementType.NarrativeText, "B", "f.txt", ref index, metadata);

        Assert.Equal(12, index);
    }

    [Fact]
    public void CreateElement_CopiesMetadata_NotSharesReference()
    {
        var index = 0;
        var metadata = new Dictionary<string, string> { ["x"] = "1" };

        var el = PartitionerHelper.CreateElement(ElementType.NarrativeText, "T", "f.txt", ref index, metadata);
        metadata["y"] = "2";

        Assert.False(el.Metadata.ContainsKey("y")); // 不共享引用
    }

    [Fact]
    public void CreateElement_DefaultPageNumber_IsNull()
    {
        var index = 0;
        var el = PartitionerHelper.CreateElement(
            ElementType.NarrativeText, "T", "f.txt", ref index, new Dictionary<string, string>());

        Assert.Null(el.PageNumber);
    }
}
