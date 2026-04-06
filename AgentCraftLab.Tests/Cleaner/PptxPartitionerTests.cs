using AgentCraftLab.Cleaner.Elements;
using AgentCraftLab.Cleaner.Partitioners;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace AgentCraftLab.Tests.Cleaner;

public class PptxPartitionerTests
{
    [Fact]
    public async Task Pptx_ClassifiesTitleAndBody()
    {
        var data = CreatePptx(pres =>
        {
            AddSlide(pres, [
                (PlaceholderValues.Title, "Slide Title"),
                (PlaceholderValues.Body, "Body text content"),
            ]);
        });

        var partitioner = new PptxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "test.pptx");

        Assert.Equal(2, elements.Count);
        Assert.Equal(ElementType.Title, elements[0].Type);
        Assert.Equal("Slide Title", elements[0].Text);
        Assert.Equal(ElementType.NarrativeText, elements[1].Type);
        Assert.Equal(1, elements[0].PageNumber);
    }

    [Fact]
    public async Task Pptx_MultipleSlides_CorrectPageNumbers()
    {
        var data = CreatePptx(pres =>
        {
            AddSlide(pres, [(PlaceholderValues.Title, "Slide 1")]);
            AddSlide(pres, [(PlaceholderValues.Title, "Slide 2")]);
            AddSlide(pres, [(PlaceholderValues.Title, "Slide 3")]);
        });

        var partitioner = new PptxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "test.pptx");

        Assert.Equal(3, elements.Count);
        Assert.Equal(1, elements[0].PageNumber);
        Assert.Equal(2, elements[1].PageNumber);
        Assert.Equal(3, elements[2].PageNumber);
    }

    [Fact]
    public async Task Pptx_FooterShape_ClassifiedAsFooter()
    {
        var data = CreatePptx(pres =>
        {
            AddSlide(pres, [
                (PlaceholderValues.Title, "Title"),
                (PlaceholderValues.Footer, "Footer text"),
            ]);
        });

        var partitioner = new PptxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "test.pptx");

        Assert.Contains(elements, e => e.Type == ElementType.Footer);
    }

    [Fact]
    public async Task Pptx_EmptyPresentation_ReturnsEmpty()
    {
        var data = CreatePptx(_ => { });
        var partitioner = new PptxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "empty.pptx");
        Assert.Empty(elements);
    }

    [Fact]
    public async Task Pptx_MetadataContainsFormat()
    {
        var data = CreatePptx(pres =>
        {
            AddSlide(pres, [(PlaceholderValues.Title, "Test")]);
        });

        var partitioner = new PptxPartitioner();
        var elements = await partitioner.PartitionAsync(data, "test.pptx");

        Assert.Equal("PPTX", elements[0].Metadata[MetadataKeys.Format]);
    }

    // ── Helpers ──

    private static byte[] CreatePptx(Action<PresentationDocument> configure)
    {
        using var stream = new MemoryStream();
        using (var doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presPart = doc.AddPresentationPart();
            presPart.Presentation = new Presentation(new SlideIdList());
            configure(doc);
            presPart.Presentation.Save();
        }
        return stream.ToArray();
    }

    private static void AddSlide(PresentationDocument doc, (PlaceholderValues Type, string Text)[] shapes)
    {
        var presPart = doc.PresentationPart!;
        var slidePart = presPart.AddNewPart<SlidePart>();
        var slide = new Slide(new CommonSlideData(new ShapeTree()));
        slidePart.Slide = slide;

        var shapeTree = slide.CommonSlideData!.ShapeTree!;
        uint shapeId = 1;

        foreach (var (phType, text) in shapes)
        {
            var shape = new Shape(
                new NonVisualShapeProperties(
                    new NonVisualDrawingProperties { Id = shapeId++, Name = $"Shape{shapeId}" },
                    new NonVisualShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties(
                        new PlaceholderShape { Type = phType })),
                new ShapeProperties(),
                new TextBody(
                    new A.BodyProperties(),
                    new A.Paragraph(new A.Run(new A.Text(text)))));

            shapeTree.Append(shape);
        }

        slidePart.Slide.Save();

        var slideIdList = presPart.Presentation!.SlideIdList!;
        var maxId = slideIdList.Elements<SlideId>().Select(s => s.Id?.Value ?? 255U).DefaultIfEmpty(255U).Max();
        slideIdList.Append(new SlideId
        {
            Id = maxId + 1,
            RelationshipId = presPart.GetIdOfPart(slidePart),
        });
    }
}
