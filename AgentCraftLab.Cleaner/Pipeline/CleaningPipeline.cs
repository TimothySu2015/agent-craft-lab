using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Pipeline;

/// <summary>
/// 清洗管線實作 — 組合 Partitioner + Filter + Rules 執行完整流程。
/// </summary>
public sealed class CleaningPipeline : IDocumentCleaner
{
    private readonly IEnumerable<IPartitioner> _partitioners;
    private readonly IEnumerable<ICleaningRule> _rules;
    private readonly IEnumerable<IElementFilter> _filters;

    public CleaningPipeline(
        IEnumerable<IPartitioner> partitioners,
        IEnumerable<ICleaningRule> rules,
        IEnumerable<IElementFilter> filters)
    {
        _partitioners = partitioners;
        _rules = rules;
        _filters = filters;
    }

    public async Task<CleanedDocument> CleanAsync(
        byte[] data,
        string fileName,
        string mimeType,
        CleaningOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new CleaningOptions();

        // Step 1: Partition — 找到對應的 Partitioner 拆解文件
        var partitioner = _partitioners.FirstOrDefault(p => p.CanPartition(mimeType))
            ?? throw new NotSupportedException($"No partitioner registered for MIME type: {mimeType}");

        var elements = await partitioner.PartitionAsync(data, fileName, options.Partition, ct);
        var mutableElements = elements.ToList();

        // Step 2: Filter — 依類型過濾元素
        if (options.ExcludeElementTypes is { Count: > 0 } excluded)
        {
            mutableElements.RemoveAll(e => excluded.Contains(e.Type));
        }

        foreach (var filter in _filters)
        {
            mutableElements.RemoveAll(e => !filter.ShouldKeep(e));
        }

        // Step 3: Clean — 依序套用清洗規則
        var orderedRules = _rules
            .Where(r => options.EnabledRules is null || options.EnabledRules.Contains(r.Name))
            .OrderBy(r => r.Order);

        foreach (var rule in orderedRules)
        {
            foreach (var element in mutableElements)
            {
                if (rule.ShouldApply(element))
                {
                    rule.Apply(element);
                }
            }
        }

        // Step 4: 移除清洗後為空的元素
        if (options.RemoveEmptyElements)
        {
            mutableElements.RemoveAll(e => string.IsNullOrWhiteSpace(e.Text));
        }

        return new CleanedDocument
        {
            FileName = fileName,
            Elements = mutableElements,
        };
    }

    public async Task<IReadOnlyList<CleanedDocument>> CleanBatchAsync(
        IEnumerable<RawDocument> documents,
        CleaningOptions? options = null,
        CancellationToken ct = default)
    {
        var tasks = documents.Select(doc =>
            CleanAsync(doc.Data, doc.FileName, doc.MimeType, options, ct));
        var results = await Task.WhenAll(tasks);
        return results;
    }
}
