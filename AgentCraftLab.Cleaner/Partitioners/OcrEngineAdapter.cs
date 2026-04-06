using AgentCraftLab.Cleaner.Abstractions;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// 將外部 OCR 引擎橋接到 CraftCleaner 的 IOcrProvider。
/// 使用方式：在 DI 中手動註冊，或用 AddOcrProvider 擴充方法。
///
/// 範例（在 Engine 或 Api 層）：
/// <code>
/// services.AddSingleton&lt;IOcrProvider&gt;(sp =>
///     new OcrEngineAdapter(sp.GetRequiredService&lt;IOcrEngine&gt;()));
/// </code>
/// </summary>
/// <remarks>
/// 此類別不直接引用 AgentCraftLab.Ocr，而是透過泛用的 delegate 解耦。
/// 這樣 Cleaner 層不需要 ProjectReference 到 Ocr 層。
/// </remarks>
public sealed class OcrEngineAdapter : IOcrProvider
{
    private readonly Func<byte[], IReadOnlyList<string>?, CancellationToken, Task<(string Text, float Confidence)>> _recognizeFunc;

    /// <summary>
    /// 以 delegate 建構（完全解耦，不依賴任何外部型別）。
    /// </summary>
    public OcrEngineAdapter(
        Func<byte[], IReadOnlyList<string>?, CancellationToken, Task<(string Text, float Confidence)>> recognizeFunc)
    {
        _recognizeFunc = recognizeFunc;
    }

    public async Task<OcrProviderResult> RecognizeAsync(byte[] imageData, string languages, CancellationToken ct = default)
    {
        // 將 "chi_tra+chi_sim+eng" 格式轉為 ["chi_tra", "chi_sim", "eng"]
        var langList = string.IsNullOrWhiteSpace(languages)
            ? null
            : (IReadOnlyList<string>)languages.Split('+', StringSplitOptions.RemoveEmptyEntries);

        var (text, confidence) = await _recognizeFunc(imageData, langList, ct);

        return new OcrProviderResult
        {
            Text = text,
            Confidence = confidence,
        };
    }
}
