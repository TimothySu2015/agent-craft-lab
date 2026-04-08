using AgentCraftLab.Search.Abstractions;

namespace AgentCraftLab.Search.Extraction;

/// <summary>
/// 文字擷取器工廠 — 根據 MIME type 分派到對應擷取器。
/// </summary>
public class DocumentExtractorFactory
{
    private readonly IReadOnlyList<IDocumentExtractor> _extractors;

    public DocumentExtractorFactory(IEnumerable<IDocumentExtractor> extractors)
    {
        _extractors = extractors.ToList();
    }

    /// <summary>取得支援該 MIME type 的擷取器；無對應時回傳 null。</summary>
    public IDocumentExtractor? GetExtractor(string mimeType)
    {
        return _extractors.FirstOrDefault(e => e.CanExtract(mimeType));
    }

    /// <summary>擷取文字；格式不支援時回傳 null。</summary>
    public async Task<ExtractionResult?> ExtractAsync(byte[] data, string fileName, string mimeType, CancellationToken ct = default)
    {
        var extractor = GetExtractor(mimeType);
        if (extractor is null)
        {
            return null;
        }

        return await extractor.ExtractAsync(data, fileName, ct);
    }

    /// <summary>列出所有支援的 MIME types（用於 UI 顯示）。</summary>
    public IReadOnlyList<string> SupportedMimeTypes =>
        _extractors.SelectMany(e => GetSupportedTypes(e)).Distinct().ToList();

    private static IEnumerable<string> GetSupportedTypes(IDocumentExtractor extractor)
    {
        // 測試常見 MIME types
        string[] commonTypes =
        [
            "application/pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "text/html", "text/plain", "text/markdown", "text/csv",
            "application/json", "text/javascript", "text/x-csharp", "text/x-python"
        ];

        return commonTypes.Where(extractor.CanExtract);
    }
}
