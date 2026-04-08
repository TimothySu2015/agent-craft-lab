using TesseractOCR.Enums;
using TesseractOCR.Pix;
using TessEngine = TesseractOCR.Engine;

namespace AgentCraftLab.Ocr;

/// <summary>
/// Tesseract OCR 引擎實作。
/// tessdata 目錄須包含對應語言的 .traineddata 檔案。
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine, IDisposable
{
    private readonly string _tessDataPath;
    private readonly string _defaultLanguages;
    private bool _disposed;

    /// <param name="tessDataPath">tessdata 目錄路徑（含 .traineddata 檔案）</param>
    /// <param name="defaultLanguages">預設語言（"+" 分隔），預設 "chi_tra+chi_sim+eng+jpn+kor"</param>
    public TesseractOcrEngine(string tessDataPath, string defaultLanguages = "chi_tra+chi_sim+eng+jpn+kor")
    {
        if (!Directory.Exists(tessDataPath))
        {
            throw new DirectoryNotFoundException($"tessdata directory not found: {tessDataPath}");
        }

        _tessDataPath = tessDataPath;
        _defaultLanguages = defaultLanguages;
    }

    public Task<OcrResult> RecognizeAsync(byte[] imageData, IReadOnlyList<string>? languages = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(imageData);

        if (imageData.Length == 0)
        {
            return Task.FromResult(new OcrResult { Text = "", Confidence = 0 });
        }

        // Tesseract 不是 thread-safe，用 Task.Run 避免阻塞呼叫端
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lang = languages is { Count: > 0 }
                ? string.Join("+", languages)
                : _defaultLanguages;

            using var engine = new TessEngine(_tessDataPath, lang, EngineMode.Default);
            using var img = Image.LoadFromMemory(imageData);

            cancellationToken.ThrowIfCancellationRequested();

            using var page = engine.Process(img);

            return new OcrResult
            {
                Text = page.Text?.Trim() ?? "",
                Confidence = page.MeanConfidence,
            };
        }, cancellationToken);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
