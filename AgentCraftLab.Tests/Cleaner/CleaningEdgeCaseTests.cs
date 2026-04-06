using System.Text;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;
using AgentCraftLab.Cleaner.Extensions;
using AgentCraftLab.Cleaner.Partitioners;
using AgentCraftLab.Cleaner.Pipeline;
using AgentCraftLab.Cleaner.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Tests.Cleaner;

public class CleaningEdgeCaseTests
{
    private static DocumentElement MakeElement(string text, ElementType type = ElementType.NarrativeText) =>
        new() { Type = type, Text = text, FileName = "test.txt", Index = 0 };

    // ── Pipeline: Rule Order ──

    [Fact]
    public async Task Pipeline_RulesExecuteInOrder()
    {
        var executionLog = new List<string>();

        var rule1 = new TrackingRule("first", 100, executionLog);
        var rule2 = new TrackingRule("second", 200, executionLog);
        var rule3 = new TrackingRule("third", 50, executionLog);

        var partitioner = new StubPartitioner(MakeElement("text"));
        var pipeline = new CleaningPipeline([partitioner], [rule1, rule2, rule3], []);

        await pipeline.CleanAsync("x"u8.ToArray(), "t.txt", "text/plain");

        Assert.Equal(["third", "first", "second"], executionLog);
    }

    [Fact]
    public async Task Pipeline_CancellationToken_ThrowsOnCancel()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var partitioner = new StubPartitioner(MakeElement("text"));
        var pipeline = new CleaningPipeline([partitioner], [], []);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.CleanAsync("x"u8.ToArray(), "t.txt", "text/plain", ct: cts.Token));
    }

    // ── GroupBrokenParagraphs: CJK ──

    [Fact]
    public void GroupBrokenParagraphs_CjkSentenceEnders()
    {
        var rule = new GroupBrokenParagraphsRule();
        var el = MakeElement("第一段。\n第二段。");
        rule.Apply(el);
        Assert.Equal("第一段。\n第二段。", el.Text); // CJK period → no merge
    }

    [Fact]
    public void GroupBrokenParagraphs_CjkTruncatedLine_Merges()
    {
        var rule = new GroupBrokenParagraphsRule();
        var el = MakeElement("這是一段很長的文字被\nPDF 截斷了");
        rule.Apply(el);
        Assert.Equal("這是一段很長的文字被 PDF 截斷了", el.Text);
    }

    // ── PlainText: Edge Cases ──

    [Fact]
    public async Task PlainText_EmptyFile_ReturnsEmpty()
    {
        var data = ""u8.ToArray();
        var partitioner = new PlainTextPartitioner();
        var elements = await partitioner.PartitionAsync(data, "empty.txt");
        Assert.Empty(elements);
    }

    [Fact]
    public async Task PlainText_MarkdownCodeFence()
    {
        var text = "Intro\n\n```csharp\nvar x = 1;\n```";
        var data = Encoding.UTF8.GetBytes(text);
        var partitioner = new PlainTextPartitioner();
        var elements = await partitioner.PartitionAsync(data, "test.md");

        Assert.Contains(elements, e => e.Type == ElementType.CodeSnippet);
    }

    [Fact]
    public async Task PlainText_HorizontalRule_ClassifiesAsPageBreak()
    {
        var text = "Before\n\n---\n\nAfter";
        var data = Encoding.UTF8.GetBytes(text);
        var partitioner = new PlainTextPartitioner();
        var elements = await partitioner.PartitionAsync(data, "test.md");

        Assert.Contains(elements, e => e.Type == ElementType.PageBreak);
    }

    [Fact]
    public async Task PlainText_JsonFile_SingleCodeSnippet()
    {
        var json = """{"key": "value"}""";
        var data = Encoding.UTF8.GetBytes(json);
        var partitioner = new PlainTextPartitioner();
        var elements = await partitioner.PartitionAsync(data, "data.json");

        Assert.Single(elements);
        Assert.Equal(ElementType.CodeSnippet, elements[0].Type);
    }

    [Fact]
    public async Task PlainText_YamlFile_SingleCodeSnippet()
    {
        var yaml = "key: value\nlist:\n  - item1";
        var data = Encoding.UTF8.GetBytes(yaml);
        var partitioner = new PlainTextPartitioner();
        var elements = await partitioner.PartitionAsync(data, "config.yaml");

        Assert.Single(elements);
        Assert.Equal(ElementType.CodeSnippet, elements[0].Type);
    }

    // ── DI Registration ──

    [Fact]
    public void AddCraftCleaner_RegistersAllServices()
    {
        var services = new ServiceCollection();
        services.AddCraftCleaner();
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IDocumentCleaner>());
        Assert.NotEmpty(provider.GetServices<IPartitioner>());
        Assert.NotEmpty(provider.GetServices<ICleaningRule>());
        Assert.NotEmpty(provider.GetServices<IElementFilter>());
    }

    [Fact]
    public void AddCraftCleaner_Registers7Partitioners()
    {
        var services = new ServiceCollection();
        services.AddCraftCleaner();
        var provider = services.BuildServiceProvider();

        var partitioners = provider.GetServices<IPartitioner>().ToList();
        Assert.Equal(7, partitioners.Count);
    }

    [Fact]
    public void AddCraftCleaner_Registers7Rules()
    {
        var services = new ServiceCollection();
        services.AddCraftCleaner();
        var provider = services.BuildServiceProvider();

        var rules = provider.GetServices<ICleaningRule>().ToList();
        Assert.Equal(7, rules.Count);
    }

    [Fact]
    public void AddPartitioner_AddsCustomPartitioner()
    {
        var services = new ServiceCollection();
        services.AddCraftCleaner();
        services.AddPartitioner<StubPartitioner>();
        var provider = services.BuildServiceProvider();

        // Count registered IPartitioner types (including ImagePartitioner that may need IOcrProvider)
        var partitioners = provider.GetServices<IPartitioner>().ToList();
        Assert.True(partitioners.Count >= 7); // 7 built-in (some may need optional deps) + 1 custom
        Assert.Contains(partitioners, p => p is StubPartitioner);
    }

    [Fact]
    public void AddCleaningRule_AddsCustomRule()
    {
        var services = new ServiceCollection();
        services.AddCraftCleaner();
        services.AddCleaningRule<TrackingRule>();
        var provider = services.BuildServiceProvider();

        var rules = provider.GetServices<ICleaningRule>().ToList();
        Assert.Equal(8, rules.Count); // 7 built-in + 1 custom
    }

    // ── CleanedDocument ──

    [Fact]
    public void CleanedDocument_GetFullText_SkipsEmptyElements()
    {
        var doc = new CleanedDocument
        {
            FileName = "test.txt",
            Elements =
            [
                new DocumentElement { Type = ElementType.Title, Text = "Title", FileName = "t", Index = 0 },
                new DocumentElement { Type = ElementType.NarrativeText, Text = "  ", FileName = "t", Index = 1 },
                new DocumentElement { Type = ElementType.NarrativeText, Text = "Body", FileName = "t", Index = 2 },
            ],
        };

        var text = doc.GetFullText();
        Assert.Equal("Title\n\nBody", text);
    }

    [Fact]
    public void CleanedDocument_GetElements_MultipleTypes()
    {
        var doc = new CleanedDocument
        {
            FileName = "test.txt",
            Elements =
            [
                new DocumentElement { Type = ElementType.Title, Text = "T1", FileName = "t", Index = 0 },
                new DocumentElement { Type = ElementType.Table, Text = "Tab", FileName = "t", Index = 1 },
                new DocumentElement { Type = ElementType.Title, Text = "T2", FileName = "t", Index = 2 },
                new DocumentElement { Type = ElementType.ListItem, Text = "L1", FileName = "t", Index = 3 },
            ],
        };

        var titlesAndTables = doc.GetElements(ElementType.Title, ElementType.Table).ToList();
        Assert.Equal(3, titlesAndTables.Count);
    }

    // ── Helpers ──

    internal sealed class StubPartitioner : IPartitioner
    {
        private readonly DocumentElement[] _elements;
        public StubPartitioner() : this([]) { }
        public StubPartitioner(params DocumentElement[] elements) => _elements = elements;
        public bool CanPartition(string mimeType) => mimeType == "text/plain";
        public Task<IReadOnlyList<DocumentElement>> PartitionAsync(
            byte[] data, string fileName, PartitionOptions? options = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DocumentElement>>(_elements);
        }
    }

    private sealed class TrackingRule : ICleaningRule
    {
        private readonly List<string>? _log;
        public TrackingRule(string name, int order, List<string>? log = null)
        {
            Name = name;
            Order = order;
            _log = log;
        }

        public TrackingRule() : this("tracking", 999) { }

        public string Name { get; }
        public int Order { get; }
        public bool ShouldApply(DocumentElement element) => true;
        public void Apply(DocumentElement element) => _log?.Add(Name);
    }
}
