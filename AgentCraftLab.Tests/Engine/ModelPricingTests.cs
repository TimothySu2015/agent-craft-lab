using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Tests.Engine;

public class ModelPricingTests
{
    [Fact]
    public void EstimateCost_KnownModel_CorrectRate()
    {
        var cost = ModelPricing.EstimateCost("gpt-4o", 1_000_000);
        Assert.Equal(7.00m, cost);
    }

    [Fact]
    public void EstimateCost_MiniModel_CheaperRate()
    {
        var cost = ModelPricing.EstimateCost("gpt-4o-mini", 1_000_000);
        Assert.Equal(0.42m, cost);
    }

    [Fact]
    public void EstimateCost_UnknownModel_FallbackRate()
    {
        var cost = ModelPricing.EstimateCost("unknown-model-xyz", 1_000_000);
        Assert.Equal(2.00m, cost);
    }

    [Fact]
    public void EstimateCost_CaseInsensitive()
    {
        var cost = ModelPricing.EstimateCost("GPT-4O", 1_000_000);
        Assert.Equal(7.00m, cost);
    }

    [Fact]
    public void EstimateCost_ZeroTokens_ZeroCost()
    {
        var cost = ModelPricing.EstimateCost("gpt-4o", 0);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void EstimateCost_OllamaModel_ZeroCost()
    {
        var cost = ModelPricing.EstimateCost("llama3.3", 500_000);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void FormatCost_LargeAmount_TwoDecimals()
    {
        var result = ModelPricing.FormatCost(1.50m);
        Assert.Equal("$1.50", result);
    }

    [Fact]
    public void FormatCost_SmallAmount_FourDecimals()
    {
        var result = ModelPricing.FormatCost(0.0042m);
        Assert.Equal("$0.0042", result);
    }
}
