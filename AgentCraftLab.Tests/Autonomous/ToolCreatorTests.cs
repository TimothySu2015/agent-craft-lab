using AgentCraftLab.Autonomous.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCraftLab.Tests.Autonomous;

public class ToolCreatorTests
{
    // ─── 假的 IToolCodeRunner ───

    private sealed class FakeCodeRunner(Func<string, string, ToolCodeResult>? handler = null) : IToolCodeRunner
    {
        public Task<ToolCodeResult> ExecuteAsync(string code, string input, int timeoutSeconds = 3, CancellationToken ct = default)
        {
            if (handler is not null)
            {
                return Task.FromResult(handler(code, input));
            }

            // 預設：模擬 Jint 執行 — 直接回傳 input 的長度
            return Task.FromResult(new ToolCodeResult(true, input.Length.ToString(), null));
        }
    }

    private static ToolCreator MakeCreator(IToolCodeRunner? runner = null)
    {
        return new ToolCreator(runner ?? new FakeCodeRunner(), NullLogger.Instance);
    }

    private static DynamicToolSet MakeToolSet()
    {
        return new DynamicToolSet([]);
    }

    // ─── 成功建立 ───

    [Fact]
    public async Task Create_ValidTool_Succeeds()
    {
        // FakeCodeRunner 回傳 input.Length — "hello world" = 11
        var creator = MakeCreator();
        var result = await creator.CreateAsync(
            "word_count", "Count words in text",
            "result = input.split(' ').length.toString();",
            "hello world", "11",
            MakeToolSet(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("word_count", result.Name);
        Assert.NotNull(result.Tool);
    }

    [Fact]
    public async Task Create_RegistersToDynamicToolSet()
    {
        var creator = MakeCreator();
        var toolSet = MakeToolSet();

        var result = await creator.CreateAsync(
            "my_tool", "test tool",
            "result = 'ok';",
            null, null,
            toolSet, CancellationToken.None);

        Assert.True(result.Success);
        // 工具應由 MetaToolFactory 的 LoadCreatedTool 註冊，此處只驗證 Creator 不自行註冊
        Assert.Equal(0, toolSet.LoadedCount);
    }

    // ─── 驗證失敗 ───

    [Fact]
    public async Task Create_EmptyName_Fails()
    {
        var creator = MakeCreator();
        var result = await creator.CreateAsync(
            "", "desc", "result = 'x';", null, null,
            MakeToolSet(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("name", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_EmptyDescription_Fails()
    {
        var creator = MakeCreator();
        var result = await creator.CreateAsync(
            "tool", "", "result = 'x';", null, null,
            MakeToolSet(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("description", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_EmptyCode_Fails()
    {
        var creator = MakeCreator();
        var result = await creator.CreateAsync(
            "tool", "desc", "", null, null,
            MakeToolSet(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("code", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    // ─── 安全掃描 ───

    [Fact]
    public async Task Create_DangerousCode_Fails()
    {
        var creator = MakeCreator();
        var result = await creator.CreateAsync(
            "bad_tool", "evil tool",
            "eval('alert(1)');",
            null, null,
            MakeToolSet(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Security", result.ErrorMessage!);
    }

    // ─── 語法測試 ───

    [Fact]
    public async Task Create_SyntaxError_Fails()
    {
        var runner = new FakeCodeRunner((_, input) =>
            input == "" ? new ToolCodeResult(false, "", "SyntaxError: Unexpected token")
                        : new ToolCodeResult(true, "ok", null));

        var creator = MakeCreator(runner);
        var result = await creator.CreateAsync(
            "broken", "broken tool",
            "result = {{{invalid;",
            null, null,
            MakeToolSet(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Syntax", result.ErrorMessage!);
    }

    // ─── 功能測試 ───

    [Fact]
    public async Task Create_TestMismatch_Fails()
    {
        var runner = new FakeCodeRunner((_, _) => new ToolCodeResult(true, "wrong answer", null));

        var creator = MakeCreator(runner);
        var result = await creator.CreateAsync(
            "calc", "calculator",
            "result = '42';",
            "2+2", "4",
            MakeToolSet(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("mismatch", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_TestPasses_Succeeds()
    {
        var runner = new FakeCodeRunner((_, _) => new ToolCodeResult(true, "result is 4", null));

        var creator = MakeCreator(runner);
        var result = await creator.CreateAsync(
            "calc", "calculator",
            "result = '4';",
            "2+2", "4",
            MakeToolSet(), CancellationToken.None);

        Assert.True(result.Success);
    }

    // ─── 名稱正規化 ───

    [Fact]
    public async Task Create_NameWithSpaces_Sanitized()
    {
        var creator = MakeCreator();
        var result = await creator.CreateAsync(
            "My Cool Tool", "desc",
            "result = 'ok';",
            null, null,
            MakeToolSet(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("my_cool_tool", result.Name);
    }

    // ─── 重複名稱 ───

    [Fact]
    public async Task Create_DuplicateName_Fails()
    {
        var creator = MakeCreator();
        var toolSet = MakeToolSet();

        // 先載入一個同名工具
        toolSet.LoadCreatedTool("existing_tool",
            AIFunctionFactory.Create(() => "ok", "existing_tool"));

        var result = await creator.CreateAsync(
            "existing_tool", "desc",
            "result = 'ok';",
            null, null,
            toolSet, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage!);
    }

    // ─── 數量限制 ───

    [Fact]
    public async Task Create_ExceedsLimit_Fails()
    {
        var creator = MakeCreator();
        var toolSet = MakeToolSet();

        // 建立 10 個工具（上限）
        for (var i = 0; i < 10; i++)
        {
            var r = await creator.CreateAsync(
                $"tool_{i}", "desc",
                "result = 'ok';",
                null, null,
                toolSet, CancellationToken.None);
            Assert.True(r.Success);
        }

        // 第 11 個應失敗
        var result = await creator.CreateAsync(
            "tool_overflow", "desc",
            "result = 'ok';",
            null, null,
            toolSet, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("limit", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }
}
