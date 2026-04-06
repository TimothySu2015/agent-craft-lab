namespace AgentCraftLab.Cleaner.Abstractions;

/// <summary>
/// OCR 提供者介面 — 讓 ImagePartitioner 不直接依賴 AgentCraftLab.Ocr。
/// 外部可透過 adapter 將 IOcrEngine 橋接到此介面。
/// </summary>
public interface IOcrProvider
{
    /// <summary>對圖片執行 OCR，回傳辨識的文字</summary>
    Task<OcrProviderResult> RecognizeAsync(
        byte[] imageData,
        string languages,
        CancellationToken ct = default);
}

/// <summary>OCR 辨識結果</summary>
public sealed class OcrProviderResult
{
    public required string Text { get; init; }

    /// <summary>辨識信心度（0.0 ~ 1.0）</summary>
    public float Confidence { get; init; }
}
