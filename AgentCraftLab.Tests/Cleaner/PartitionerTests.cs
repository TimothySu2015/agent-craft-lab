using System.Text;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;
using AgentCraftLab.Cleaner.Partitioners;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AgentCraftLab.Tests.Cleaner;

public class PartitionerTests
{
    // ═══════════════════════════════════════
    // DocxPartitioner
    // ═══════════════════════════════════════

    [Fact]
    public async Task Docx_ClassifiesHeadingsAndParagraphs()
    {
        var data = CreateDocx(doc =>
        {
            var body = doc.MainDocumentPart!.Document!.Body!;

            // Heading 段落
            var heading = new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                new Run(new Text("Chapter One")));
            body.Append(heading);

            // 一般段落
            body.Append(new Paragraph(new Run(new Text("This is body text."))));

            // ListParagraph
            var listItem = new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "ListParagraph" }),
                new Run(new Text("Item A")));
            body.Append(listItem);
        });

        var partitioner = new DocxPartitioner();
        Assert.True(partitioner.CanPartition(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));

        var elements = await partitioner.PartitionAsync(data, "test.docx");

        Assert.Equal(3, elements.Count);
        Assert.Equal(ElementType.Title, elements[0].Type);
        Assert.Equal("Chapter One", elements[0].Text);
        Assert.Equal(ElementType.NarrativeText, elements[1].Type);
        Assert.Equal(ElementType.ListItem, elements[2].Type);
    }

    [Fact]
    public async Task Docx_ExtractsTable()
    {
        var data = CreateDocx(doc =>
        {
            var body = doc.MainDocumentPart!.Document!.Body!;
            var table = new Table(
                new TableRow(
                    new TableCell(new Paragraph(new Run(new Text("Name")))),
                    new TableCell(new Paragraph(new Run(new Text("Age"))))),
                new TableRow(
                    new TableCell(new Paragraph(new Run(new Text("Alice")))),
                    new TableCell(new Paragraph(new Run(new Text("30"))))));
            body.Append(table);
        });

        var partitioner = new DocxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "test.docx");

        Assert.Single(elements);
        Assert.Equal(ElementType.Table, elements[0].Type);
        Assert.Contains("Name", elements[0].Text);
        Assert.Contains("Alice", elements[0].Text);
        Assert.Contains("|", elements[0].Text);   // Markdown 格式
        Assert.Contains("---", elements[0].Text);  // 分隔線
    }

    [Fact]
    public async Task Docx_EmptyBody_ReturnsEmpty()
    {
        var data = CreateDocx(_ => { });
        var partitioner = new DocxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "empty.docx");
        Assert.Empty(elements);
    }

    [Fact]
    public async Task Docx_NumberingProperties_ClassifiesAsListItem()
    {
        var data = CreateDocx(doc =>
        {
            var body = doc.MainDocumentPart!.Document!.Body!;
            var listPara = new Paragraph(
                new ParagraphProperties(
                    new NumberingProperties(
                        new NumberingLevelReference { Val = 0 },
                        new NumberingId { Val = 1 })),
                new Run(new Text("Numbered item")));
            body.Append(listPara);
        });

        var partitioner = new DocxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "test.docx");

        Assert.Single(elements);
        Assert.Equal(ElementType.ListItem, elements[0].Type);
    }

    // ═══════════════════════════════════════
    // PlainTextPartitioner
    // ═══════════════════════════════════════

    [Fact]
    public async Task PlainText_ClassifiesMarkdownHeading()
    {
        var text = "# Title\n\nSome body text.\n\n## Subtitle";
        var data = Encoding.UTF8.GetBytes(text);
        var partitioner = new PlainTextPartitioner();

        var elements = await partitioner.PartitionAsync(data, "test.md");

        Assert.Equal(3, elements.Count);
        Assert.Equal(ElementType.Title, elements[0].Type);
        Assert.Equal(ElementType.NarrativeText, elements[1].Type);
        Assert.Equal(ElementType.Title, elements[2].Type);
    }

    [Fact]
    public async Task PlainText_ClassifiesBulletList()
    {
        var text = "Intro paragraph.\n\n- Item 1\n- Item 2";
        var data = Encoding.UTF8.GetBytes(text);
        var partitioner = new PlainTextPartitioner();

        var elements = await partitioner.PartitionAsync(data, "test.md");

        Assert.Equal(2, elements.Count);
        Assert.Equal(ElementType.NarrativeText, elements[0].Type);
        Assert.Equal(ElementType.ListItem, elements[1].Type);
    }

    [Fact]
    public async Task PlainText_CodeFile_SingleCodeSnippet()
    {
        var code = "public class Foo\n{\n    public int Bar { get; set; }\n}";
        var data = Encoding.UTF8.GetBytes(code);
        var partitioner = new PlainTextPartitioner();

        var elements = await partitioner.PartitionAsync(data, "Foo.cs");

        Assert.Single(elements);
        Assert.Equal(ElementType.CodeSnippet, elements[0].Type);
    }

    [Fact]
    public async Task PlainText_CsvFile_SingleTable()
    {
        var csv = "Name,Age\nAlice,30\nBob,25";
        var data = Encoding.UTF8.GetBytes(csv);
        var partitioner = new PlainTextPartitioner();

        Assert.True(partitioner.CanPartition("text/csv"));
        var elements = await partitioner.PartitionAsync(data, "data.csv");

        Assert.Single(elements);
        Assert.Equal(ElementType.Table, elements[0].Type);
    }

    [Fact]
    public async Task PlainText_MarkdownTable()
    {
        var text = "Intro\n\n| Col1 | Col2 |\n| --- | --- |\n| a | b |";
        var data = Encoding.UTF8.GetBytes(text);
        var partitioner = new PlainTextPartitioner();

        var elements = await partitioner.PartitionAsync(data, "test.md");

        Assert.Contains(elements, e => e.Type == ElementType.Table);
    }

    [Fact]
    public void PlainText_CanPartition_TextWildcard()
    {
        var partitioner = new PlainTextPartitioner();
        Assert.True(partitioner.CanPartition("text/anything"));
    }

    // ═══════════════════════════════════════
    // HtmlPartitioner
    // ═══════════════════════════════════════

    [Fact]
    public async Task Html_ClassifiesHeadingsAndParagraphs()
    {
        var html = "<html><body><h1>Title</h1><p>Body text</p><h2>Subtitle</h2></body></html>";
        var data = Encoding.UTF8.GetBytes(html);
        var partitioner = new HtmlPartitioner();

        Assert.True(partitioner.CanPartition("text/html"));
        var elements = await partitioner.PartitionAsync(data, "page.html");

        Assert.Equal(3, elements.Count);
        Assert.Equal(ElementType.Title, elements[0].Type);
        Assert.Equal("Title", elements[0].Text);
        Assert.Equal(ElementType.NarrativeText, elements[1].Type);
        Assert.Equal(ElementType.Title, elements[2].Type);
    }

    [Fact]
    public async Task Html_ClassifiesListItems()
    {
        var html = "<html><body><ul><li>First</li><li>Second</li></ul></body></html>";
        var data = Encoding.UTF8.GetBytes(html);
        var partitioner = new HtmlPartitioner();

        var elements = await partitioner.PartitionAsync(data, "page.html");

        Assert.Equal(2, elements.Count);
        Assert.All(elements, e => Assert.Equal(ElementType.ListItem, e.Type));
    }

    [Fact]
    public async Task Html_ExtractsTable()
    {
        var html = "<html><body><table><tr><th>Name</th></tr><tr><td>Alice</td></tr></table></body></html>";
        var data = Encoding.UTF8.GetBytes(html);
        var partitioner = new HtmlPartitioner();

        var elements = await partitioner.PartitionAsync(data, "page.html");

        Assert.Single(elements);
        Assert.Equal(ElementType.Table, elements[0].Type);
        Assert.Contains("Name", elements[0].Text);
    }

    [Fact]
    public async Task Html_SkipsScriptAndStyle()
    {
        var html = "<html><body><script>alert('x')</script><style>.a{}</style><p>Keep this</p></body></html>";
        var data = Encoding.UTF8.GetBytes(html);
        var partitioner = new HtmlPartitioner();

        var elements = await partitioner.PartitionAsync(data, "page.html");

        Assert.Single(elements);
        Assert.Equal("Keep this", elements[0].Text);
    }

    [Fact]
    public async Task Html_ExtractsCodeSnippet()
    {
        var html = "<html><body><pre>var x = 1;</pre></body></html>";
        var data = Encoding.UTF8.GetBytes(html);
        var partitioner = new HtmlPartitioner();

        var elements = await partitioner.PartitionAsync(data, "page.html");

        Assert.Single(elements);
        Assert.Equal(ElementType.CodeSnippet, elements[0].Type);
    }

    [Fact]
    public async Task Html_ExtractsMetadata()
    {
        var html = "<html><head><title>My Page</title><meta name='description' content='A test page'></head><body><p>Text</p></body></html>";
        var data = Encoding.UTF8.GetBytes(html);
        var partitioner = new HtmlPartitioner();

        var elements = await partitioner.PartitionAsync(data, "page.html");

        Assert.Single(elements);
        Assert.Equal("My Page", elements[0].Metadata["title"]);
        Assert.Equal("A test page", elements[0].Metadata["description"]);
    }

    // ═══════════════════════════════════════
    // ImagePartitioner
    // ═══════════════════════════════════════

    [Fact]
    public async Task Image_WithoutOcr_ReturnsPlaceholder()
    {
        var partitioner = new ImagePartitioner(ocrProvider: null);

        Assert.True(partitioner.CanPartition("image/png"));
        var elements = await partitioner.PartitionAsync([0xFF, 0xD8], "photo.png");

        Assert.Single(elements);
        Assert.Equal(ElementType.Image, elements[0].Type);
        Assert.Contains("photo.png", elements[0].Text);
    }

    [Fact]
    public async Task Image_WithOcr_ReturnsOcrText()
    {
        var mockOcr = new MockOcrProvider("Hello from OCR", 0.95f);
        var partitioner = new ImagePartitioner(mockOcr);

        var elements = await partitioner.PartitionAsync([0xFF, 0xD8], "scan.png");

        Assert.Single(elements);
        Assert.Equal(ElementType.UncategorizedText, elements[0].Type);
        Assert.Equal("Hello from OCR", elements[0].Text);
        Assert.Equal("0.95", elements[0].Metadata["ocr_confidence"]);
    }

    [Fact]
    public async Task Image_OcrDisabled_ReturnsPlaceholder()
    {
        var mockOcr = new MockOcrProvider("Should not appear", 0.9f);
        var partitioner = new ImagePartitioner(mockOcr);

        var options = new PartitionOptions { EnableOcr = false };
        var elements = await partitioner.PartitionAsync([0xFF, 0xD8], "scan.png", options);

        Assert.Single(elements);
        Assert.Equal(ElementType.Image, elements[0].Type);
    }

    [Fact]
    public async Task Image_OcrReturnsEmpty_ReturnsNoTextDetected()
    {
        var mockOcr = new MockOcrProvider("", 0f);
        var partitioner = new ImagePartitioner(mockOcr);

        var elements = await partitioner.PartitionAsync([0xFF, 0xD8], "blank.png");

        Assert.Single(elements);
        Assert.Equal(ElementType.Image, elements[0].Type);
        Assert.Contains("no text detected", elements[0].Text);
    }

    [Fact]
    public async Task Image_AiDescribeMode_ReturnsDescription()
    {
        var mockDescriber = new MockImageDescriber("A bar chart showing Q3 revenue growth", 0.9f);
        var partitioner = new ImagePartitioner(imageDescriber: mockDescriber);
        var options = new PartitionOptions { ImageMode = ImageProcessingMode.AiDescribe };

        var imageData = CreateTestPng(100, 80);
        var elements = await partitioner.PartitionAsync(imageData, "chart.png", options);

        Assert.Single(elements);
        Assert.Equal(ElementType.Image, elements[0].Type);
        Assert.Contains("Q3 revenue", elements[0].Text);
        Assert.Equal("ai-describe", elements[0].Metadata["image_processing_mode"]);
    }

    [Fact]
    public async Task Image_AiDescribeMode_NoDescriber_FallbackToPlaceholder()
    {
        var partitioner = new ImagePartitioner();
        var options = new PartitionOptions { ImageMode = ImageProcessingMode.AiDescribe };

        var imageData = CreateTestPng(100, 80);
        var elements = await partitioner.PartitionAsync(imageData, "photo.png", options);

        Assert.Single(elements);
        Assert.Equal(ElementType.Image, elements[0].Type);
    }

    [Fact]
    public async Task Image_HybridMode_OcrHighConfidence_UsesOcr()
    {
        var mockOcr = new MockOcrProvider("OCR text", 0.9f);
        var mockDescriber = new MockImageDescriber("Should not be called", 0.9f);
        var partitioner = new ImagePartitioner(mockOcr, mockDescriber);
        var options = new PartitionOptions
        {
            ImageMode = ImageProcessingMode.Hybrid,
            HybridOcrThreshold = 0.5f,
        };

        var imageData = CreateTestPng(100, 80);
        var elements = await partitioner.PartitionAsync(imageData, "scan.png", options);

        Assert.Single(elements);
        Assert.Equal("OCR text", elements[0].Text);
        Assert.Equal("hybrid-ocr", elements[0].Metadata["image_processing_mode"]);
    }

    [Fact]
    public async Task Image_SkipMode_ReturnsPlaceholder()
    {
        var mockDescriber = new MockImageDescriber("Should not appear", 0.9f);
        var partitioner = new ImagePartitioner(imageDescriber: mockDescriber);
        var options = new PartitionOptions { ImageMode = ImageProcessingMode.Skip, EnableOcr = false };

        var imageData = CreateTestPng(100, 80);
        var elements = await partitioner.PartitionAsync(imageData, "photo.png", options);

        Assert.Single(elements);
        Assert.Equal(ElementType.Image, elements[0].Type);
        Assert.Contains("photo.png", elements[0].Text);
    }

    // ═══════════════════════════════════════
    // PdfPartitioner
    // ═══════════════════════════════════════

    [Fact]
    public void Pdf_CanPartition()
    {
        var partitioner = new PdfPartitioner();
        Assert.True(partitioner.CanPartition("application/pdf"));
        Assert.False(partitioner.CanPartition("text/plain"));
    }

    // ═══════════════════════════════════════
    // XlsxPartitioner
    // ═══════════════════════════════════════

    [Fact]
    public void Xlsx_CanPartition()
    {
        var partitioner = new XlsxPartitioner();
        Assert.True(partitioner.CanPartition(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        Assert.False(partitioner.CanPartition("text/plain"));
    }

    // ═══════════════════════════════════════
    // PptxPartitioner
    // ═══════════════════════════════════════

    [Fact]
    public void Pptx_CanPartition()
    {
        var partitioner = new PptxPartitioner();
        Assert.True(partitioner.CanPartition(
            "application/vnd.openxmlformats-officedocument.presentationml.presentation"));
        Assert.False(partitioner.CanPartition("text/plain"));
    }

    // ═══════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════

    private sealed class MockOcrProvider(string text, float confidence) : IOcrProvider
    {
        public Task<OcrProviderResult> RecognizeAsync(byte[] imageData, string languages, CancellationToken ct) =>
            Task.FromResult(new OcrProviderResult { Text = text, Confidence = confidence });
    }

    private sealed class MockImageDescriber(string description, float confidence) : IImageDescriber
    {
        public Task<ImageDescriptionResult> DescribeAsync(
            byte[] imageData, string mimeType, ImageDescriptionContext? context, CancellationToken ct) =>
            Task.FromResult(new ImageDescriptionResult
            {
                Description = description,
                Confidence = confidence,
                InputTokens = 200,
                OutputTokens = 50,
            });
    }

    private static byte[] CreateTestPng(int width, int height)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return ms.ToArray();
    }

    private static byte[] CreateDocx(Action<WordprocessingDocument> configure)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            configure(doc);
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }
}
