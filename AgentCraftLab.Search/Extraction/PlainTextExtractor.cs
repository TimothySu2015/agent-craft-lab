using AgentCraftLab.Search.Abstractions;

namespace AgentCraftLab.Search.Extraction;

/// <summary>
/// 純文字擷取器 — 支援 txt, md, csv, json, 程式碼等。
/// </summary>
public class PlainTextExtractor : IDocumentExtractor
{
    private static readonly HashSet<string> SupportedTypes =
    [
        "text/plain",
        "text/markdown",
        "text/csv",
        "text/tab-separated-values",
        "application/json",
        "application/xml",
        "text/xml",
        // 程式碼
        "text/x-csharp",
        "text/x-python",
        "text/javascript",
        "text/x-java",
        "text/x-go",
        "text/x-rust",
        "text/x-typescript",
        "text/x-yaml",
        "application/x-yaml"
    ];

    public bool CanExtract(string mimeType) =>
        SupportedTypes.Contains(mimeType) || mimeType.StartsWith("text/", StringComparison.Ordinal);

    public Task<ExtractionResult> ExtractAsync(byte[] data, string fileName, CancellationToken ct = default)
    {
        var text = System.Text.Encoding.UTF8.GetString(data);
        var lineCount = text.AsSpan().Count('\n') + 1;

        return Task.FromResult(new ExtractionResult
        {
            Text = text,
            FileName = fileName,
            MimeType = "text/plain",
            Metadata = new Dictionary<string, string>
            {
                ["format"] = "Text",
                ["line_count"] = lineCount.ToString(),
                ["char_count"] = text.Length.ToString()
            }
        });
    }
}
