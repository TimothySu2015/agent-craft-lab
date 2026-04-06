namespace AgentCraftLab.Ocr;

/// <summary>
/// OCR 引擎介面 — 從圖片擷取文字。
/// </summary>
public interface IOcrEngine
{
    /// <summary>辨識圖片中的文字。</summary>
    /// <param name="imageData">圖片 byte[]（支援 PNG/JPG/TIFF/BMP）</param>
    /// <param name="languages">語言清單（如 "eng", "chi_tra"），null 使用預設語言</param>
    /// <param name="cancellationToken">取消 token</param>
    /// <returns>辨識結果</returns>
    Task<OcrResult> RecognizeAsync(byte[] imageData, IReadOnlyList<string>? languages = null, CancellationToken cancellationToken = default);
}

/// <summary>OCR 辨識結果。</summary>
public record OcrResult
{
    /// <summary>辨識出的完整文字。</summary>
    public required string Text { get; init; }

    /// <summary>平均信心度（0.0 ~ 1.0）。</summary>
    public float Confidence { get; init; }

    /// <summary>辨識是否成功（有文字且信心度 > 0）。</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Text);
}
