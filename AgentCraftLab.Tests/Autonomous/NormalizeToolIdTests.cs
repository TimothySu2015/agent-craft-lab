using AgentCraftLab.Autonomous.Services;

namespace AgentCraftLab.Tests.Autonomous;

public class NormalizeToolIdTests
{
    [Fact]
    public void NormalizeToolId_NoPrefix_Unchanged()
    {
        var result = SafeWhitelistToolDelegation.NormalizeToolId("AzureWebSearch");
        Assert.Equal("AzureWebSearch", result);
    }

    [Fact]
    public void NormalizeToolId_FunctionsPrefix_Stripped()
    {
        var result = SafeWhitelistToolDelegation.NormalizeToolId("functions.AzureWebSearch");
        Assert.Equal("AzureWebSearch", result);
    }

    [Fact]
    public void NormalizeToolId_FunctionsPrefix_CaseInsensitive()
    {
        var result = SafeWhitelistToolDelegation.NormalizeToolId("Functions.WebSearch");
        Assert.Equal("WebSearch", result);
    }

    [Fact]
    public void NormalizeToolId_FunctionsPrefix_UpperCase()
    {
        var result = SafeWhitelistToolDelegation.NormalizeToolId("FUNCTIONS.Calculator");
        Assert.Equal("Calculator", result);
    }

    [Fact]
    public void NormalizeToolId_FunctionsInMiddle_NotStripped()
    {
        var result = SafeWhitelistToolDelegation.NormalizeToolId("my.functions.tool");
        Assert.Equal("my.functions.tool", result);
    }

    [Fact]
    public void NormalizeToolId_EmptyString_Unchanged()
    {
        var result = SafeWhitelistToolDelegation.NormalizeToolId("");
        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizeToolId_OnlyPrefix_ReturnsEmpty()
    {
        var result = SafeWhitelistToolDelegation.NormalizeToolId("functions.");
        Assert.Equal("", result);
    }
}
