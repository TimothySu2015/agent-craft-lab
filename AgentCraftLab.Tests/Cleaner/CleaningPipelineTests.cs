using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;
using AgentCraftLab.Cleaner.Pipeline;

namespace AgentCraftLab.Tests.Cleaner;

public class CleaningPipelineTests
{
    // ── Stub Partitioner ──
    private sealed class StubPartitioner : IPartitioner
    {
        private readonly IReadOnlyList<DocumentElement> _elements;
        public StubPartitioner(params DocumentElement[] elements) => _elements = elements;
        public bool CanPartition(string mimeType) => mimeType == "text/plain";
        public Task<IReadOnlyList<DocumentElement>> PartitionAsync(
            byte[] data, string fileName, PartitionOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult(_elements);
    }

    // ── Stub Rule ──
    private sealed class UpperCaseRule : ICleaningRule
    {
        public string Name => "uppercase";
        public int Order => 1;
        public bool ShouldApply(DocumentElement element) => true;
        public void Apply(DocumentElement element) => element.Text = element.Text.ToUpperInvariant();
    }

    // ── Stub Filter ──
    private sealed class RemoveShortFilter : IElementFilter
    {
        public string Name => "remove_short";
        public bool ShouldKeep(DocumentElement element) => element.Text.Length >= 5;
    }

    private static DocumentElement MakeElement(string text, ElementType type = ElementType.NarrativeText) =>
        new() { Type = type, Text = text, FileName = "test.txt", Index = 0 };

    [Fact]
    public async Task CleanAsync_RunsFullPipeline()
    {
        var partitioner = new StubPartitioner(
            MakeElement("Hello World"),
            MakeElement("Hi", ElementType.NarrativeText),  // will be removed by filter
            MakeElement("Page 1", ElementType.Footer));

        var pipeline = new CleaningPipeline(
            [partitioner],
            [new UpperCaseRule()],
            [new RemoveShortFilter()]);

        var result = await pipeline.CleanAsync(
            "dummy"u8.ToArray(), "test.txt", "text/plain");

        // "Hi" removed by filter (< 5 chars), "Page 1" kept (6 chars)
        Assert.Equal(2, result.Elements.Count);
        Assert.Equal("HELLO WORLD", result.Elements[0].Text);
        Assert.Equal("PAGE 1", result.Elements[1].Text);
    }

    [Fact]
    public async Task CleanAsync_RemovesEmptyElementsAfterCleaning()
    {
        var partitioner = new StubPartitioner(
            MakeElement("   "),  // will be empty after trim
            MakeElement("Valid text"));

        // Use no rules/filters — just relies on RemoveEmptyElements option
        var pipeline = new CleaningPipeline([partitioner], [], []);

        var result = await pipeline.CleanAsync(
            "dummy"u8.ToArray(), "test.txt", "text/plain");

        Assert.Single(result.Elements);
        Assert.Equal("Valid text", result.Elements[0].Text);
    }

    [Fact]
    public async Task CleanAsync_ExcludeElementTypes()
    {
        var partitioner = new StubPartitioner(
            MakeElement("Title", ElementType.Title),
            MakeElement("Body text"),
            MakeElement("Footer", ElementType.Footer));

        var pipeline = new CleaningPipeline([partitioner], [], []);

        var options = new CleaningOptions
        {
            ExcludeElementTypes = new HashSet<ElementType> { ElementType.Footer },
        };

        var result = await pipeline.CleanAsync(
            "dummy"u8.ToArray(), "test.txt", "text/plain", options);

        Assert.Equal(2, result.Elements.Count);
        Assert.DoesNotContain(result.Elements, e => e.Type == ElementType.Footer);
    }

    [Fact]
    public async Task CleanAsync_EnabledRulesFilter()
    {
        var partitioner = new StubPartitioner(MakeElement("hello"));

        var pipeline = new CleaningPipeline(
            [partitioner],
            [new UpperCaseRule()],
            []);

        // Only enable a rule that doesn't exist → UpperCaseRule should be skipped
        var options = new CleaningOptions
        {
            EnabledRules = new HashSet<string> { "nonexistent_rule" },
        };

        var result = await pipeline.CleanAsync(
            "dummy"u8.ToArray(), "test.txt", "text/plain", options);

        Assert.Equal("hello", result.Elements[0].Text);  // not uppercased
    }

    [Fact]
    public async Task CleanAsync_ThrowsForUnsupportedMimeType()
    {
        var pipeline = new CleaningPipeline([], [], []);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            pipeline.CleanAsync("data"u8.ToArray(), "file.xyz", "application/unknown"));
    }

    [Fact]
    public async Task CleanBatchAsync_ProcessesMultipleDocuments()
    {
        var partitioner = new StubPartitioner(MakeElement("Content"));
        var pipeline = new CleaningPipeline([partitioner], [], []);

        var docs = new[]
        {
            new RawDocument { Data = "a"u8.ToArray(), FileName = "a.txt", MimeType = "text/plain" },
            new RawDocument { Data = "b"u8.ToArray(), FileName = "b.txt", MimeType = "text/plain" },
        };

        var results = await pipeline.CleanBatchAsync(docs);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void CleanedDocument_GetFullText_JoinsElements()
    {
        var doc = new CleanedDocument
        {
            FileName = "test.txt",
            Elements =
            [
                MakeElement("First paragraph"),
                MakeElement("Second paragraph"),
            ],
        };

        var text = doc.GetFullText();
        Assert.Equal("First paragraph\n\nSecond paragraph", text);
    }

    [Fact]
    public void CleanedDocument_GetElements_FiltersByType()
    {
        var doc = new CleanedDocument
        {
            FileName = "test.txt",
            Elements =
            [
                MakeElement("Title", ElementType.Title),
                MakeElement("Body"),
                MakeElement("Another Title", ElementType.Title),
            ],
        };

        var titles = doc.GetElements(ElementType.Title).ToList();
        Assert.Equal(2, titles.Count);
    }
}
