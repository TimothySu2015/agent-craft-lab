using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Abstractions;

/// <summary>
/// 格式分割器 — 將原始檔案拆解為帶類型的 DocumentElement 序列。
/// 每種檔案格式實作一個 Partitioner（對應 Unstructured 的 partition_xxx）。
/// </summary>
public interface IPartitioner
{
    /// <summary>判斷此 Partitioner 是否支援該 MIME type</summary>
    bool CanPartition(string mimeType);

    /// <summary>將原始檔案內容分割為結構化元素</summary>
    Task<IReadOnlyList<DocumentElement>> PartitionAsync(
        byte[] data,
        string fileName,
        PartitionOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>分割選項</summary>
public sealed class PartitionOptions
{
    /// <summary>是否保留 Header/Footer 元素（預設 true，清洗階段可移除）</summary>
    public bool IncludeHeaderFooter { get; init; } = true;

    /// <summary>是否對圖片執行 OCR（需要 IOcrPartitioner 支援）</summary>
    public bool EnableOcr { get; init; } = true;

    /// <summary>OCR 語言（預設繁中+簡中+英文）</summary>
    public string OcrLanguages { get; init; } = "chi_tra+chi_sim+eng";

    /// <summary>圖片處理模式（預設 Skip，向下相容）</summary>
    public ImageProcessingMode ImageMode { get; init; } = ImageProcessingMode.Skip;

    /// <summary>圖片最小寬度（像素），低於此值跳過（預設 50）</summary>
    public int MinImageWidth { get; init; } = 50;

    /// <summary>圖片最小高度（像素），低於此值跳過（預設 50）</summary>
    public int MinImageHeight { get; init; } = 50;

    /// <summary>圖片最大邊長（像素），超過則等比縮放（預設 2048）</summary>
    public int MaxImageDimension { get; init; } = 2048;

    /// <summary>Hybrid 模式中 OCR 信心度門檻，低於此值 fallback 到 AI 描述（預設 0.5）</summary>
    public float HybridOcrThreshold { get; init; } = 0.5f;
}
