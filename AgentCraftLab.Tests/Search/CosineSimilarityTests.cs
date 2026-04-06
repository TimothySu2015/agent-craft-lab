using AgentCraftLab.Search.Scoring;

namespace AgentCraftLab.Tests.Search;

public class CosineSimilarityTests
{
    [Fact]
    public void Compute_IdenticalVectors_Returns1()
    {
        float[] a = [1f, 2f, 3f];
        float[] b = [1f, 2f, 3f];
        var score = CosineSimilarity.Compute(a, b);
        Assert.InRange(score, 0.999f, 1.001f);
    }

    [Fact]
    public void Compute_OrthogonalVectors_Returns0()
    {
        float[] a = [1f, 0f];
        float[] b = [0f, 1f];
        var score = CosineSimilarity.Compute(a, b);
        Assert.InRange(score, -0.001f, 0.001f);
    }

    [Fact]
    public void Compute_OppositeVectors_ReturnsMinus1()
    {
        float[] a = [1f, 0f];
        float[] b = [-1f, 0f];
        var score = CosineSimilarity.Compute(a, b);
        Assert.InRange(score, -1.001f, -0.999f);
    }

    [Fact]
    public void Compute_ZeroVector_Returns0OrNaN()
    {
        float[] a = [1f, 2f, 3f];
        float[] b = [0f, 0f, 0f];
        var score = CosineSimilarity.Compute(a, b);
        Assert.True(score == 0f || float.IsNaN(score));
    }

    [Fact]
    public void Compute_DifferentMagnitude_StillNormalized()
    {
        float[] a = [1f, 0f];
        float[] b = [100f, 0f];
        var score = CosineSimilarity.Compute(a, b);
        Assert.InRange(score, 0.999f, 1.001f);
    }
}
