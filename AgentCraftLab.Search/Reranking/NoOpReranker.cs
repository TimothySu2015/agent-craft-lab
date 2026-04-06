using AgentCraftLab.Search.Abstractions;

namespace AgentCraftLab.Search.Reranking;

/// <summary>
/// 不做任何重排序 — 直接回傳原始結果（預設實作，確保向下相容）。
/// </summary>
public class NoOpReranker : IReranker
{
    public Task<IReadOnlyList<SearchResult>> RerankAsync(
        string query,
        IReadOnlyList<SearchResult> results,
        int topK,
        CancellationToken ct = default)
    {
        IReadOnlyList<SearchResult> truncated = results.Count <= topK
            ? results
            : results.Take(topK).ToList();

        return Task.FromResult(truncated);
    }
}
