namespace AgentCraftLab.Cleaner.Elements;

/// <summary>
/// 文件元素 — Partition 的最小輸出單位。
/// 每個元素帶有類型、文字內容、以及來源 metadata。
/// </summary>
public sealed class DocumentElement
{
    public required ElementType Type { get; init; }
    public required string Text { get; set; }

    /// <summary>來源檔案名稱</summary>
    public string? FileName { get; init; }

    /// <summary>所在頁碼（PDF/PPTX 適用）</summary>
    public int? PageNumber { get; init; }

    /// <summary>元素在文件中的序號（從 0 開始）</summary>
    public int Index { get; init; }

    /// <summary>額外 metadata（擴充用）</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}
