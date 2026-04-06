using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Abstractions;

/// <summary>
/// 文件清洗引擎 — 整合 Partition → Filter → Clean 的完整管線。
/// 這是外部呼叫端的主要入口（類似 CraftSearch 的 ISearchEngine）。
/// </summary>
public interface IDocumentCleaner
{
    /// <summary>
    /// 從原始檔案執行完整清洗管線：Partition → Filter → Clean → CleanedDocument
    /// </summary>
    Task<CleanedDocument> CleanAsync(
        byte[] data,
        string fileName,
        string mimeType,
        CleaningOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// 批次清洗多個檔案
    /// </summary>
    Task<IReadOnlyList<CleanedDocument>> CleanBatchAsync(
        IEnumerable<RawDocument> documents,
        CleaningOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>待清洗的原始文件</summary>
public sealed class RawDocument
{
    public required byte[] Data { get; init; }
    public required string FileName { get; init; }
    public required string MimeType { get; init; }
}

/// <summary>清洗選項</summary>
public sealed class CleaningOptions
{
    /// <summary>分割選項</summary>
    public PartitionOptions Partition { get; init; } = new();

    /// <summary>
    /// 要套用的規則名稱（null = 套用所有已註冊規則）。
    /// 可用來選擇性啟用/停用特定規則。
    /// </summary>
    public IReadOnlySet<string>? EnabledRules { get; init; }

    /// <summary>要排除的元素類型（清洗前先過濾掉）</summary>
    public IReadOnlySet<ElementType>? ExcludeElementTypes { get; init; }

    /// <summary>是否移除清洗後為空的元素（預設 true）</summary>
    public bool RemoveEmptyElements { get; init; } = true;
}
