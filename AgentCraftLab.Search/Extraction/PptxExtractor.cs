using System.Text;
using AgentCraftLab.Search.Abstractions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;

namespace AgentCraftLab.Search.Extraction;

/// <summary>
/// PowerPoint (.pptx) 文字擷取器（使用 OpenXml）。
/// </summary>
public class PptxExtractor : IDocumentExtractor
{
    private static readonly HashSet<string> SupportedTypes =
    [
        "application/vnd.openxmlformats-officedocument.presentationml.presentation"
    ];

    public bool CanExtract(string mimeType) =>
        SupportedTypes.Contains(mimeType);

    public Task<ExtractionResult> ExtractAsync(byte[] data, string fileName, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(data);
        using var doc = PresentationDocument.Open(stream, false)!;

        var presentationPart = doc.PresentationPart;
        if (presentationPart?.Presentation?.SlideIdList is null)
        {
            return Task.FromResult(new ExtractionResult
            {
                Text = "",
                FileName = fileName,
                MimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation"
            });
        }

        var sb = new StringBuilder();
        int slideNumber = 0;

        foreach (var slideId in presentationPart.Presentation.SlideIdList.Elements<SlideId>())
        {
            slideNumber++;
            var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId!);

            sb.AppendLine($"--- Slide {slideNumber} ---");

            foreach (var shape in slidePart.Slide?.Descendants<Shape>() ?? [])
            {
                var text = shape.TextBody?.InnerText;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                }
            }

            sb.AppendLine();
        }

        var metadata = new Dictionary<string, string>
        {
            ["slide_count"] = slideNumber.ToString(),
            ["format"] = "PPTX"
        };
        var props = doc.PackageProperties;
        if (!string.IsNullOrWhiteSpace(props.Title))
        {
            metadata["title"] = props.Title;
        }

        if (!string.IsNullOrWhiteSpace(props.Creator))
        {
            metadata["author"] = props.Creator;
        }

        return Task.FromResult(new ExtractionResult
        {
            Text = sb.ToString(),
            FileName = fileName,
            PageCount = slideNumber,
            MimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            Metadata = metadata
        });
    }
}
