namespace AgentCraftLab.Cleaner.Elements;

/// <summary>
/// 清洗後的完整文件 — Pipeline 的最終輸出。
/// </summary>
public sealed class CleanedDocument
{
    /// <summary>來源檔案名稱</summary>
    public required string FileName { get; init; }

    /// <summary>清洗後的元素清單</summary>
    public required IReadOnlyList<DocumentElement> Elements { get; init; }

    /// <summary>文件級 metadata（擷取階段產生）</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>取得合併後的純文字（所有元素的 Text 以雙換行串接）</summary>
    public string GetFullText() =>
        string.Join("\n\n", Elements.Where(e => !string.IsNullOrWhiteSpace(e.Text)).Select(e => e.Text));

    /// <summary>依類型過濾元素</summary>
    public IEnumerable<DocumentElement> GetElements(params ElementType[] types) =>
        Elements.Where(e => types.Contains(e.Type));
}
