using System.Text;
using AgentCraftLab.Search.Abstractions;
using UglyToad.PdfPig;

namespace AgentCraftLab.Search.Extraction;

/// <summary>
/// PDF 文字擷取器（使用 PdfPig）。
/// </summary>
public class PdfExtractor : IDocumentExtractor
{
    private static readonly HashSet<string> SupportedTypes =
        ["application/pdf"];

    public bool CanExtract(string mimeType) =>
        SupportedTypes.Contains(mimeType);

    public Task<ExtractionResult> ExtractAsync(byte[] data, string fileName, CancellationToken ct = default)
    {
        using var document = PdfDocument.Open(data);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        var metadata = new Dictionary<string, string>
        {
            ["page_count"] = document.NumberOfPages.ToString(),
            ["format"] = "PDF"
        };
        var info = document.Information;
        if (!string.IsNullOrWhiteSpace(info.Title))
        {
            metadata["title"] = info.Title;
        }

        if (!string.IsNullOrWhiteSpace(info.Author))
        {
            metadata["author"] = info.Author;
        }

        return Task.FromResult(new ExtractionResult
        {
            Text = sb.ToString(),
            FileName = fileName,
            PageCount = document.NumberOfPages,
            MimeType = "application/pdf",
            Metadata = metadata
        });
    }
}
