using System.Numerics.Tensors;

namespace AgentCraftLab.Search.Scoring;

/// <summary>
/// Cosine Similarity 計算 — 使用 .NET TensorPrimitives（SIMD / AVX2 加速）。
/// </summary>
public static class CosineSimilarity
{
    /// <summary>
    /// 計算兩個向量的 cosine similarity（-1 ~ 1，越接近 1 越相似）。
    /// </summary>
    public static float Compute(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
    {
        return TensorPrimitives.CosineSimilarity(vectorA, vectorB);
    }

    /// <summary>
    /// 對 query 向量與一批候選向量計算相似度，回傳 top-K 結果（已排序）。
    /// </summary>
    public static IReadOnlyList<(int Index, float Score)> SearchTopK(
        ReadOnlyMemory<float> queryVector,
        IReadOnlyList<ReadOnlyMemory<float>> candidateVectors,
        int topK)
    {
        if (candidateVectors.Count == 0)
        {
            return [];
        }

        var querySpan = queryVector.Span;
        var scored = new List<(int Index, float Score)>(candidateVectors.Count);

        for (int i = 0; i < candidateVectors.Count; i++)
        {
            float score = Compute(querySpan, candidateVectors[i].Span);
            scored.Add((i, score));
        }

        // 降序排列取 top-K
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        return scored.Take(topK).ToList();
    }
}
