using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

/// <summary>
/// ToolRegistryService 完整性驗證 — 確保所有工具正確註冊且可解析。
/// 新增工具時如果忘了加 ID、描述為空、factory 拋錯，這些測試會立刻失敗。
/// </summary>
public sealed class ToolRegistryTests
{
    private readonly ToolRegistryService _registry;

    /// <summary>所有已註冊的工具 ID — 新增工具時加到這裡，測試會自動涵蓋。</summary>
    public static readonly string[] AllToolIds =
    [
        // Search
        "azure_web_search", "tavily_search", "tavily_extract",
        "brave_search", "serper_search", "web_search", "wikipedia",
        // Utility
        "get_datetime", "calculator", "uuid_generator",
        // Web
        "url_fetch",
        // Data
        "json_parser", "csv_log_analyzer", "zip_extractor",
        "write_file", "write_csv", "send_email",
        // Code Explorer
        "list_directory", "read_file", "search_code",
        "file_diff", "text_diff",
    ];

    public ToolRegistryTests()
    {
        var httpFactory = new TestHttpClientFactory();
        _registry = new ToolRegistryService(httpFactory);
    }

    // ═══════════════════════════════════════════════
    // 1. 註冊完整性
    // ═══════════════════════════════════════════════

    [Fact]
    public void AllExpectedTools_AreRegistered()
    {
        var registered = _registry.GetAvailableTools().Select(t => t.Id).ToHashSet();

        foreach (var id in AllToolIds)
        {
            Assert.True(registered.Contains(id), $"Tool '{id}' is listed in AllToolIds but not registered in ToolRegistryService");
        }
    }

    [Fact]
    public void NoUnexpectedTools_AreRegistered()
    {
        var registered = _registry.GetAvailableTools().Select(t => t.Id).ToHashSet();
        var expected = AllToolIds.ToHashSet();

        foreach (var id in registered)
        {
            Assert.True(expected.Contains(id), $"Tool '{id}' is registered but not listed in AllToolIds — please add it to the test");
        }
    }

    [Fact]
    public void AllTools_HaveUniqueIds()
    {
        var tools = _registry.GetAvailableTools();
        var ids = tools.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void AllTools_HaveNonEmptyDescription()
    {
        var tools = _registry.GetAvailableTools();
        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description),
                $"Tool '{tool.Id}' has empty description");
        }
    }

    [Fact]
    public void AllTools_HaveNonEmptyDisplayName()
    {
        var tools = _registry.GetAvailableTools();
        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.DisplayName),
                $"Tool '{tool.Id}' has empty display name");
        }
    }

    [Fact]
    public void ToolCount_MatchesExpected()
    {
        var tools = _registry.GetAvailableTools();
        Assert.Equal(AllToolIds.Length, tools.Count);
    }

    // ═══════════════════════════════════════════════
    // 2. Factory Smoke Test — 每個工具都可以建立，不 crash
    // ═══════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(FreeToolIds))]
    public void FreeTool_FactoryDoesNotThrow(string toolId)
    {
        // 不需要 credential 的工具應能直接 Resolve
        var tools = _registry.Resolve([toolId]);
        Assert.Single(tools);
    }

    [Theory]
    [MemberData(nameof(CredentialToolIds))]
    public void CredentialTool_FactoryWithoutCredential_StillResolves(string toolId)
    {
        // 需要 credential 的工具，沒給 credential 時 fallback 到預設 factory（不 crash）
        var tools = _registry.Resolve([toolId]);
        Assert.Single(tools);
    }

    [Fact]
    public void Resolve_UnknownToolId_ReturnsEmpty()
    {
        var tools = _registry.Resolve(["nonexistent_tool"]);
        Assert.Empty(tools);
    }

    [Fact]
    public void Resolve_MultipleTools_ReturnsAll()
    {
        var tools = _registry.Resolve(["calculator", "get_datetime", "uuid_generator"]);
        Assert.Equal(3, tools.Count);
    }

    // ═══════════════════════════════════════════════
    // Test Data
    // ═══════════════════════════════════════════════

    /// <summary>不需要 credential 的工具</summary>
    public static IEnumerable<object[]> FreeToolIds()
    {
        string[] free = ["web_search", "wikipedia", "get_datetime", "calculator", "uuid_generator",
            "url_fetch", "json_parser", "csv_log_analyzer", "zip_extractor", "write_file", "write_csv",
            "list_directory", "read_file", "search_code"];
        return free.Select(id => new object[] { id });
    }

    /// <summary>需要 credential 的工具</summary>
    public static IEnumerable<object[]> CredentialToolIds()
    {
        string[] cred = ["azure_web_search", "tavily_search", "tavily_extract",
            "brave_search", "serper_search", "send_email"];
        return cred.Select(id => new object[] { id });
    }

    /// <summary>測試用 HttpClientFactory</summary>
    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
