namespace AgentCraftLab.Engine.Pii;

/// <summary>
/// PII 偵測器介面。實作可替換為 Regex、ONNX NER、Presidio、Azure AI 等。
/// </summary>
public interface IPiiDetector
{
    /// <summary>
    /// 偵測文字中的 PII 實體。
    /// 結果依 <see cref="PiiEntity.Start"/> 降序排列，以便由右到左安全替換。
    /// </summary>
    /// <param name="text">待偵測的文字。</param>
    /// <param name="confidenceThreshold">信賴度門檻，低於此值的結果將被過濾（預設 0.5）。</param>
    /// <returns>偵測到的 PII 實體清單。</returns>
    IReadOnlyList<PiiEntity> Detect(string text, double confidenceThreshold = 0.5);
}
