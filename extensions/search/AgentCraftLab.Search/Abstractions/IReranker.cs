namespace AgentCraftLab.Search.Abstractions;

/// <summary>
/// 重新排序介面 — 對初步檢索結果進行二次排序，提升相關性。
/// </summary>
public interface IReranker
{
    /// <summary>
    /// 根據查詢對搜尋結果重新排序。
    /// </summary>
    /// <param name="query">使用者查詢文字。</param>
    /// <param name="results">初步檢索結果。</param>
    /// <param name="topK">回傳結果數量上限。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>重新排序後的結果（已依相關性降序排列）。</returns>
    Task<IReadOnlyList<SearchResult>> RerankAsync(
        string query,
        IReadOnlyList<SearchResult> results,
        int topK,
        CancellationToken ct = default);
}
