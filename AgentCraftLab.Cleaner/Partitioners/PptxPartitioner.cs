using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// PowerPoint (.pptx) 分割器 — 利用 Slide 結構和 Shape 類型分類元素。
/// 支援文字、表格、內嵌圖片（透過 IOcrProvider / IImageDescriber）。
/// </summary>
public sealed class PptxPartitioner : IPartitioner
{
    private const string SupportedMimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private readonly IOcrProvider? _ocrProvider;
    private readonly IImageDescriber? _imageDescriber;

    public PptxPartitioner(IOcrProvider? ocrProvider = null, IImageDescriber? imageDescriber = null)
    {
        _ocrProvider = ocrProvider;
        _imageDescriber = imageDescriber;
    }

    public bool CanPartition(string mimeType) =>
        mimeType.Equals(SupportedMimeType, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<DocumentElement>> PartitionAsync(
        byte[] data,
        string fileName,
        PartitionOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new PartitionOptions();

        using var stream = new MemoryStream(data);
        using var doc = PresentationDocument.Open(stream, false)!;

        var presentationPart = doc.PresentationPart;
        if (presentationPart?.Presentation?.SlideIdList is null)
        {
            return [];
        }

        var elements = new List<DocumentElement>();
        var index = 0;
        var slideNumber = 0;
        var docMetadata = ExtractDocMetadata(doc);
        var imageCache = new ImageDescriptionCache();

        foreach (var slideId in presentationPart.Presentation.SlideIdList.Elements<SlideId>())
        {
            ct.ThrowIfCancellationRequested();
            slideNumber++;

            var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId!);
            var slideMetadata = new Dictionary<string, string>(docMetadata)
            {
                [MetadataKeys.SlideNumber] = slideNumber.ToString(),
            };

            // 先處理文字和表格（收集上下文用）
            var slideTextElements = new List<DocumentElement>();
            ProcessShapes(slidePart, fileName, slideNumber, slideMetadata, slideTextElements, ref index);
            ProcessTables(slidePart, fileName, slideNumber, slideMetadata, slideTextElements, ref index);
            elements.AddRange(slideTextElements);

            // 再處理圖片（帶同 slide 文字上下文）
            if (options.ImageMode != ImageProcessingMode.Skip)
            {
                var imageByteSequence = ExtractAllImageBytes(slidePart);
                var (imageElements, nextIndex) = await ImageProcessingHelper.ProcessImageBatchAsync(
                    imageByteSequence, fileName, slideNumber, slideMetadata, slideTextElements,
                    options, _ocrProvider, _imageDescriber, imageCache, index, ct);
                index = nextIndex;
                elements.AddRange(imageElements);
            }
        }

        return elements;
    }

    private static void ProcessShapes(
        SlidePart slidePart, string fileName, int slideNumber,
        Dictionary<string, string> metadata, List<DocumentElement> elements, ref int index)
    {
        foreach (var shape in slidePart.Slide?.Descendants<Shape>() ?? [])
        {
            var text = shape.TextBody?.InnerText;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            elements.Add(PartitionerHelper.CreateElement(
                ClassifyShape(shape), text, fileName, ref index, metadata, slideNumber));
        }
    }

    private static void ProcessTables(
        SlidePart slidePart, string fileName, int slideNumber,
        Dictionary<string, string> metadata, List<DocumentElement> elements, ref int index)
    {
        foreach (var table in slidePart.Slide?.Descendants<A.Table>() ?? [])
        {
            var rows = table.Elements<A.TableRow>().ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            var tableText = PartitionerHelper.ToMarkdownTable(
                rows.Select(row => row.Elements<A.TableCell>().Select(c => c.InnerText)));

            if (!string.IsNullOrWhiteSpace(tableText))
            {
                elements.Add(PartitionerHelper.CreateElement(
                    ElementType.Table, tableText, fileName, ref index, metadata, slideNumber));
            }
        }
    }

    /// <summary>從 slide 中提取所有圖片的 bytes 序列</summary>
    private static IEnumerable<byte[]?> ExtractAllImageBytes(SlidePart slidePart)
    {
        foreach (var picture in slidePart.Slide?.Descendants<Picture>() ?? [])
        {
            yield return ExtractImageBytes(slidePart, picture);
        }
    }

    /// <summary>從 Picture 元素提取圖片 bytes</summary>
    private static byte[]? ExtractImageBytes(SlidePart slidePart, Picture picture)
    {
        var blipFill = picture.BlipFill;
        var blip = blipFill?.Blip;
        if (blip?.Embed?.Value is not { } embedId)
        {
            return null;
        }

        try
        {
            var imagePart = slidePart.GetPartById(embedId);
            using var stream = imagePart.GetStream(FileMode.Open, FileAccess.Read);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static ElementType ClassifyShape(Shape shape)
    {
        var placeholder = shape.NonVisualShapeProperties?
            .ApplicationNonVisualDrawingProperties?
            .GetFirstChild<PlaceholderShape>();

        if (placeholder?.Type?.Value is { } phType)
        {
            if (phType == PlaceholderValues.Title || phType == PlaceholderValues.CenteredTitle ||
                phType == PlaceholderValues.SubTitle)
            {
                return ElementType.Title;
            }

            if (phType == PlaceholderValues.SlideNumber)
            {
                return ElementType.PageNumber;
            }

            if (phType == PlaceholderValues.Header)
            {
                return ElementType.Header;
            }

            if (phType == PlaceholderValues.Footer || phType == PlaceholderValues.DateAndTime)
            {
                return ElementType.Footer;
            }

            return ClassifyByContent(shape);
        }

        return ClassifyByContent(shape);
    }

    private static ElementType ClassifyByContent(Shape shape)
    {
        var paragraphs = shape.TextBody?.Elements<A.Paragraph>().ToList();
        if (paragraphs is null || paragraphs.Count == 0)
        {
            return ElementType.NarrativeText;
        }

        var bulletCount = paragraphs.Count(p =>
            p.ParagraphProperties?.GetFirstChild<A.BulletFont>() is not null ||
            p.ParagraphProperties?.GetFirstChild<A.CharacterBullet>() is not null ||
            p.ParagraphProperties?.GetFirstChild<A.AutoNumberedBullet>() is not null);

        return bulletCount > paragraphs.Count / 2 ? ElementType.ListItem : ElementType.NarrativeText;
    }

    private static Dictionary<string, string> ExtractDocMetadata(PresentationDocument doc)
    {
        var metadata = new Dictionary<string, string> { [MetadataKeys.Format] = "PPTX" };
        var props = doc.PackageProperties;

        if (!string.IsNullOrWhiteSpace(props.Title))
        {
            metadata[MetadataKeys.Title] = props.Title;
        }

        if (!string.IsNullOrWhiteSpace(props.Creator))
        {
            metadata[MetadataKeys.Author] = props.Creator;
        }

        var slideCount = doc.PresentationPart?.Presentation?.SlideIdList?.Count();
        if (slideCount.HasValue)
        {
            metadata[MetadataKeys.SlideCount] = slideCount.Value.ToString();
        }

        return metadata;
    }
}
