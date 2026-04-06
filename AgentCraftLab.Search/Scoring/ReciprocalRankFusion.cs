namespace AgentCraftLab.Search.Scoring;

/// <summary>
/// Reciprocal Rank Fusion (RRF) — 將多路搜尋的排名融合為統一排名。
/// 參考 Azure AI Search 實作，預設 k=60。
/// </summary>
/// <remarks>
/// 公式：RRF_score(d) = Σ weight_r / (k + rank_r(d))
/// 其中 rank 從 1 開始。
/// </remarks>
public static class ReciprocalRankFusion
{
    /// <summary>預設平滑常數（與 Azure AI Search 相同）。</summary>
    public const int DefaultK = 60;

    /// <summary>
    /// 融合多路排名結果。
    /// </summary>
    /// <param name="rankedLists">多路排名結果，每路是 (documentId, originalScore) 的有序清單。</param>
    /// <param name="weights">每路的權重（null 時全部權重 1.0）。</param>
    /// <param name="k">平滑常數。</param>
    /// <param name="topK">回傳結果數量上限。</param>
    /// <returns>融合後的排名：(documentId, rrfScore)，已降序排列。</returns>
    public static IReadOnlyList<(string Id, float Score)> Fuse(
        IReadOnlyList<IReadOnlyList<string>> rankedLists,
        IReadOnlyList<float>? weights = null,
        int k = DefaultK,
        int topK = int.MaxValue)
    {
        var scores = new Dictionary<string, float>();

        for (int listIndex = 0; listIndex < rankedLists.Count; listIndex++)
        {
            float weight = weights is not null && listIndex < weights.Count
                ? weights[listIndex]
                : 1.0f;

            var list = rankedLists[listIndex];
            for (int rank = 0; rank < list.Count; rank++)
            {
                string docId = list[rank];
                float rrfScore = weight / (k + rank + 1); // rank 從 1 開始

                if (scores.TryGetValue(docId, out float existing))
                {
                    scores[docId] = existing + rrfScore;
                }
                else
                {
                    scores[docId] = rrfScore;
                }
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }
}
