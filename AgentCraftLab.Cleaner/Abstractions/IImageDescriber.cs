namespace AgentCraftLab.Cleaner.Abstractions;

/// <summary>
/// 圖片描述提供者介面 — 讓 Partitioner 不直接依賴多模態 LLM SDK。
/// 外部可透過 adapter 將 IChatClient（多模態）橋接到此介面。
/// </summary>
public interface IImageDescriber
{
    /// <summary>對圖片產生語意描述</summary>
    Task<ImageDescriptionResult> DescribeAsync(
        byte[] imageData,
        string mimeType,
        ImageDescriptionContext? context = null,
        CancellationToken ct = default);
}

/// <summary>圖片描述結果</summary>
public sealed class ImageDescriptionResult
{
    /// <summary>AI 產生的圖片描述文字</summary>
    public required string Description { get; init; }

    /// <summary>描述信心度（0.0 ~ 1.0）</summary>
    public float Confidence { get; init; }

    /// <summary>輸入 token 用量</summary>
    public int InputTokens { get; init; }

    /// <summary>輸出 token 用量</summary>
    public int OutputTokens { get; init; }
}

/// <summary>圖片描述的上下文資訊 — 提供同頁文字讓 AI 產生更精確的描述</summary>
public sealed class ImageDescriptionContext
{
    /// <summary>同頁/同 slide 的文字內容</summary>
    public string? PageText { get; init; }

    /// <summary>頁面/slide 標題</summary>
    public string? PageTitle { get; init; }

    /// <summary>來源檔案名稱</summary>
    public string? FileName { get; init; }

    /// <summary>頁碼/slide 編號</summary>
    public int? PageNumber { get; init; }
}
