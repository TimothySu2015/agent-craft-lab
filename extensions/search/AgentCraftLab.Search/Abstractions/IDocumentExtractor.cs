namespace AgentCraftLab.Search.Abstractions;

/// <summary>
/// 文字擷取器介面 — 從各種檔案格式擷取純文字。
/// </summary>
public interface IDocumentExtractor
{
    /// <summary>是否支援該 MIME type。</summary>
    bool CanExtract(string mimeType);

    /// <summary>從檔案二進位資料擷取文字。</summary>
    Task<ExtractionResult> ExtractAsync(byte[] data, string fileName, CancellationToken ct = default);
}

/// <summary>文字擷取結果。</summary>
public class ExtractionResult
{
    /// <summary>擷取的純文字。</summary>
    public required string Text { get; init; }

    /// <summary>來源檔名。</summary>
    public required string FileName { get; init; }

    /// <summary>頁數（PDF 等分頁文件）。</summary>
    public int? PageCount { get; init; }

    /// <summary>原始 MIME type。</summary>
    public string MimeType { get; init; } = "";

    /// <summary>文件層級元資料（標題、作者等，由各 Extractor 自動填充）。</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>是否成功擷取到有意義的文字。</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Text);
}
