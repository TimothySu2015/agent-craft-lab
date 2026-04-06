using AgentCraftLab.Autonomous.Services;

namespace AgentCraftLab.Tests.Autonomous;

public class IsSafeToolTests
{
    // ─── 白名單工具 ───

    [Theory]
    [InlineData("WebSearch", true)]
    [InlineData("AzureWebSearch", true)]
    [InlineData("Wikipedia", true)]
    [InlineData("Calculator", true)]
    [InlineData("GetDateTime", true)]
    [InlineData("UrlFetch", true)]
    [InlineData("JsonParser", true)]
    [InlineData("ListDirectory", true)]
    [InlineData("ReadFile", true)]
    [InlineData("SearchCode", true)]
    public void IsSafeTool_WhitelistedTools_ReturnsTrue(string toolName, bool expected)
    {
        Assert.Equal(expected, SafeWhitelistToolDelegation.IsSafeTool(toolName));
    }

    // ─── 非白名單工具 ───

    [Theory]
    [InlineData("SendEmail")]
    [InlineData("WriteFile")]
    [InlineData("DeleteFile")]
    [InlineData("CustomMcpTool")]
    [InlineData("create_sub_agent")]
    public void IsSafeTool_NonWhitelistedTools_ReturnsFalse(string toolName)
    {
        Assert.False(SafeWhitelistToolDelegation.IsSafeTool(toolName));
    }

    // ─── 大小寫不敏感 + 格式正規化 ───

    [Theory]
    [InlineData("websearch")]
    [InlineData("WEBSEARCH")]
    [InlineData("web_search")]
    [InlineData("azure_web_search")]
    [InlineData("azure-web-search")]
    public void IsSafeTool_CaseAndFormatInsensitive(string toolName)
    {
        Assert.True(SafeWhitelistToolDelegation.IsSafeTool(toolName));
    }
}
