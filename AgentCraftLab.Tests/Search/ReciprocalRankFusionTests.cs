using AgentCraftLab.Search.Scoring;

namespace AgentCraftLab.Tests.Search;

public class ReciprocalRankFusionTests
{
    [Fact]
    public void Fuse_SingleList_CorrectScores()
    {
        IReadOnlyList<IReadOnlyList<string>> lists = [["A", "B", "C"]];
        var results = ReciprocalRankFusion.Fuse(lists, topK: 3);
        Assert.Equal(3, results.Count);
        Assert.Equal("A", results[0].Id); // rank 0 → highest score
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public void Fuse_TwoLists_OverlappingDocs_Summed()
    {
        IReadOnlyList<IReadOnlyList<string>> lists = [["A", "B"], ["B", "A"]];
        var results = ReciprocalRankFusion.Fuse(lists, topK: 2);
        // A: rank0 in list1 + rank1 in list2
        // B: rank1 in list1 + rank0 in list2
        // Both should have same score (symmetric)
        Assert.Equal(2, results.Count);
        Assert.InRange(Math.Abs(results[0].Score - results[1].Score), 0f, 0.001f);
    }

    [Fact]
    public void Fuse_TopK_Limits()
    {
        IReadOnlyList<IReadOnlyList<string>> lists = [["A", "B", "C", "D", "E"]];
        var results = ReciprocalRankFusion.Fuse(lists, topK: 2);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Fuse_EmptyLists_ReturnsEmpty()
    {
        IReadOnlyList<IReadOnlyList<string>> lists = [[]];
        var results = ReciprocalRankFusion.Fuse(lists);
        Assert.Empty(results);
    }

    [Fact]
    public void Fuse_OrderDescending()
    {
        IReadOnlyList<IReadOnlyList<string>> lists = [["A", "B", "C"]];
        var results = ReciprocalRankFusion.Fuse(lists);
        for (var i = 1; i < results.Count; i++)
        {
            Assert.True(results[i - 1].Score >= results[i].Score);
        }
    }

    [Fact]
    public void Fuse_WithWeights_Applied()
    {
        IReadOnlyList<IReadOnlyList<string>> lists = [["A"], ["B"]];
        IReadOnlyList<float> weights = [2.0f, 1.0f];
        var results = ReciprocalRankFusion.Fuse(lists, weights);
        var scoreA = results.First(r => r.Id == "A").Score;
        var scoreB = results.First(r => r.Id == "B").Score;
        Assert.True(scoreA > scoreB); // A has weight 2.0
    }
}
