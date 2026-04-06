using AgentCraftLab.Autonomous.Models;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 收斂偵測器 — 偵測 ReAct 迴圈的收益遞減，支持提前終止。
/// 當 AI 連續多次呼叫相同工具且結果高度相似時，判定為收斂並建議提前結束。
/// </summary>
internal sealed class ConvergenceDetector
{
    private readonly List<(string ToolName, string ResultSnippet)> _toolHistory = [];

    /// <summary>記錄每步回應文字的長度，用於資訊增量偵測。</summary>
    private readonly List<int> _responseLengths = [];

    private readonly ReactExecutorConfig _config;

    public ConvergenceDetector(ReactExecutorConfig? config = null)
    {
        _config = config ?? new ReactExecutorConfig();
    }

    // ─── Checkpoint 快照 / 恢復 ───

    /// <summary>取得工具呼叫歷史快照（供 CheckpointManager 使用）。</summary>
    internal List<Models.ConvergenceEntry> GetToolHistorySnapshot()
    {
        return _toolHistory.Select(t => new Models.ConvergenceEntry(t.ToolName, t.ResultSnippet)).ToList();
    }

    /// <summary>取得回應長度歷史快照。</summary>
    internal List<int> GetResponseLengthsSnapshot()
    {
        return [.. _responseLengths];
    }

    /// <summary>從快照恢復狀態。</summary>
    internal void RestoreFromSnapshot(
        List<Models.ConvergenceEntry> toolHistory,
        List<int> responseLengths)
    {
        _toolHistory.Clear();
        foreach (var entry in toolHistory)
        {
            _toolHistory.Add((entry.ToolName, entry.ResultSnippet));
        }

        _responseLengths.Clear();
        _responseLengths.AddRange(responseLengths);
    }

    /// <summary>記錄步驟的回應文字長度，用於資訊增量偵測。</summary>
    public void RecordResponseLength(int length)
    {
        _responseLengths.Add(length);
    }

    /// <summary>記錄工具呼叫結果。</summary>
    public void RecordToolCall(string toolName, string result)
    {
        // 只保留結果的前 N 字做比較（節省記憶體）
        var snippet = result.Length > _config.ConvergenceSnippetMaxLength ? result[.._config.ConvergenceSnippetMaxLength] : result;
        _toolHistory.Add((toolName, snippet));
    }

    /// <summary>
    /// 判斷是否應提前終止。
    /// 條件：最近 3 次工具呼叫為相同工具，且兩兩結果的 Jaccard 相似度皆超過門檻。
    /// </summary>
    public bool ShouldTerminateEarly()
    {
        var minHistory = _config.ConvergenceMinHistory;
        var count = _toolHistory.Count;

        if (count < minHistory)
        {
            return false;
        }

        // 直接用索引訪問，不建立中間 List
        var idx0 = count - minHistory;
        var idx1 = count - minHistory + 1;
        var idx2 = count - 1;

        // 先檢查工具名是否相同（O(1) 字串比較），不同則跳過昂貴的 Jaccard 計算
        if (_toolHistory[idx0].ToolName != _toolHistory[idx1].ToolName ||
            _toolHistory[idx1].ToolName != _toolHistory[idx2].ToolName)
        {
            // 工具名不同，直接檢查資訊枯竭
            return IsInformationDepleted();
        }

        var sim1 = JaccardSimilarity(_toolHistory[idx0].ResultSnippet, _toolHistory[idx1].ResultSnippet);
        var sim2 = JaccardSimilarity(_toolHistory[idx1].ResultSnippet, _toolHistory[idx2].ResultSnippet);

        if (sim1 > _config.ConvergenceSimilarityThreshold && sim2 > _config.ConvergenceSimilarityThreshold)
        {
            return true;
        }

        // 條件 2：資訊增量枯竭 — 最近數步回應幾乎無內容
        return IsInformationDepleted();
    }

    /// <summary>
    /// 偵測資訊增量枯竭：最近 5 步中有 3 步以上回應長度極短（幾乎沒內容），
    /// 代表 LLM 已無法產生有意義的新資訊，應提前終止。
    /// </summary>
    private bool IsInformationDepleted()
    {
        const int windowSize = 5;
        const int shortThreshold = 50;
        const int minShortCount = 3;

        var count = _responseLengths.Count;
        if (count < windowSize)
        {
            return false;
        }

        // 直接用索引存取，不建立中間 List
        var shortResponses = 0;
        for (var i = count - windowSize; i < count; i++)
        {
            if (_responseLengths[i] < shortThreshold)
            {
                shortResponses++;
            }
        }

        return shortResponses >= minShortCount;
    }

    /// <summary>
    /// Jaccard 相似度 — 基於詞集合的交集/聯集比率。
    /// 比 Levenshtein 距離快很多，適合用於粗略判斷文字相似性。
    /// </summary>
    private static double JaccardSimilarity(string a, string b)
    {
        // 快速路徑：完全相同的字串
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }

        var wordsA = a.Split([' ', ',', '.', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wordsB = b.Split([' ', ',', '.', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 兩者皆為空集合 → 完全相同
        if (wordsA.Count == 0 && wordsB.Count == 0)
        {
            return 1.0;
        }

        // 其中一方為空 → 完全不同
        if (wordsA.Count == 0 || wordsB.Count == 0)
        {
            return 0.0;
        }

        // 直接計算交集數量（避免 LINQ 建立中間集合）
        var intersectionCount = 0;
        foreach (var word in wordsA)
        {
            if (wordsB.Contains(word))
            {
                intersectionCount++;
            }
        }

        // |A ∪ B| = |A| + |B| - |A ∩ B|
        var unionCount = wordsA.Count + wordsB.Count - intersectionCount;

        return (double)intersectionCount / unionCount;
    }
}
