using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

/// <summary>
/// Lost in the Middle 重排序測試 — 透過反射呼叫 private static method。
/// </summary>
public class ReorderForAttentionTests
{
    private static List<RagChunk> Reorder(List<RagChunk> chunks)
    {
        // ReorderForAttention 是 RagChatClient 的 private static method
        var method = typeof(AgentCraftLab.Engine.Middleware.RagChatClient)
            .GetMethod("ReorderForAttention", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (List<RagChunk>)method!.Invoke(null, [chunks])!;
    }

    private static RagChunk Chunk(int score) => new()
    {
        Content = $"chunk-{score}",
        FileName = "test.txt",
        ChunkIndex = score,
        Score = score
    };

    [Fact]
    public void ThreeChunks_HighestAtStartAndEnd()
    {
        var input = new List<RagChunk> { Chunk(3), Chunk(2), Chunk(1) };
        var result = Reorder(input);

        // [3, 1, 2] — 頭=最高(3), 尾=次高(2), 中=最低(1)
        Assert.Equal(3, result[0].Score);
        Assert.Equal(2, result[^1].Score);
        Assert.Equal(1, result[1].Score); // 中間最低
    }

    [Fact]
    public void FiveChunks_HighScoresAtEdges()
    {
        var input = new List<RagChunk> { Chunk(5), Chunk(4), Chunk(3), Chunk(2), Chunk(1) };
        var result = Reorder(input);

        // [5, 3, 1, 2, 4] — 頭=5, 尾=4, 中間=1(最低)
        Assert.Equal(5, result[0].Score);
        Assert.Equal(4, result[^1].Score);
        Assert.True(result[0].Score > result[result.Count / 2].Score, "Start > middle");
        Assert.True(result[^1].Score > result[result.Count / 2].Score, "End > middle");
    }

    [Fact]
    public void SingleChunk_Unchanged()
    {
        var input = new List<RagChunk> { Chunk(1) };
        // Guard: count <= 2 不做 reorder，但直接測 method
        var result = Reorder(input);
        Assert.Single(result);
        Assert.Equal(1, result[0].Score);
    }

    [Fact]
    public void TwoChunks_PreservesOrder()
    {
        var input = new List<RagChunk> { Chunk(2), Chunk(1) };
        var result = Reorder(input);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void AllChunksPreserved()
    {
        var input = Enumerable.Range(1, 7).Select(Chunk).Reverse().ToList();
        var result = Reorder(input);
        Assert.Equal(7, result.Count);
        // 所有 chunk 都在
        Assert.Equal(input.Sum(c => c.Score), result.Sum(c => c.Score));
    }
}
