using System.Text;
using AgentCraftLab.Search.Abstractions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AgentCraftLab.Search.Extraction;

/// <summary>
/// Word (.docx) 文字擷取器（使用 OpenXml）。
/// </summary>
public class DocxExtractor : IDocumentExtractor
{
    private static readonly HashSet<string> SupportedTypes =
    [
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    ];

    public bool CanExtract(string mimeType) =>
        SupportedTypes.Contains(mimeType);

    public Task<ExtractionResult> ExtractAsync(byte[] data, string fileName, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(data);
        using var doc = WordprocessingDocument.Open(stream, false)!;

        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return Task.FromResult(new ExtractionResult
            {
                Text = "",
                FileName = fileName,
                MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            });
        }

        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var text = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
            }
        }

        var metadata = new Dictionary<string, string> { ["format"] = "DOCX" };
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
            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            Metadata = metadata
        });
    }
}
