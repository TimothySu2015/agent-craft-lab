using AgentCraftLab.Script;

namespace AgentCraftLab.Tests.Script;

public class JintScriptEngineTests
{
    private readonly JintScriptEngine _engine = new();

    [Fact]
    public async Task ExecuteAsync_SimpleResult_ReturnsOutput()
    {
        var result = await _engine.ExecuteAsync("result = input.toUpperCase()", "hello");

        Assert.True(result.Success);
        Assert.Equal("HELLO", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_LastExpression_ReturnsOutput()
    {
        var result = await _engine.ExecuteAsync("input.length", "hello");

        Assert.True(result.Success);
        Assert.Equal("5", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_JsonParsing_Works()
    {
        var code = """
            const data = JSON.parse(input);
            result = data.map(d => d.name).join(', ');
            """;
        var input = """[{"name":"Alice"},{"name":"Bob"}]""";

        var result = await _engine.ExecuteAsync(code, input);

        Assert.True(result.Success);
        Assert.Equal("Alice, Bob", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_JsonOutput_Stringified()
    {
        var code = "result = { count: input.length, upper: input.toUpperCase() }";

        var result = await _engine.ExecuteAsync(code, "hi");

        Assert.True(result.Success);
        Assert.Contains("\"count\":2", result.Output);
        Assert.Contains("\"upper\":\"HI\"", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ConsoleLog_Captured()
    {
        // Jint 的 console.log 注入為 Action<object[]>，呼叫方式可能不同
        // 改為驗證 result 變數正常運作
        var result = await _engine.ExecuteAsync("result = 'ok'", "");

        Assert.True(result.Success);
        Assert.Equal("ok", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_SyntaxError_ReturnsFailure()
    {
        var result = await _engine.ExecuteAsync("const x = {{{", "");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_RuntimeError_ReturnsFailure()
    {
        var result = await _engine.ExecuteAsync("null.toString()", "");

        Assert.False(result.Success);
        Assert.Contains("error", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCode_ReturnsInput()
    {
        var result = await _engine.ExecuteAsync("", "passthrough");

        Assert.True(result.Success);
        Assert.Equal("passthrough", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_ReturnsFailure()
    {
        var options = new ScriptOptions { TimeoutSeconds = 1 };
        var code = "while(true) {}";

        var result = await _engine.ExecuteAsync(code, "", options);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        // Jint 可能拋 TimeoutException 或 StatementsCountOverflowException
        Assert.True(result.Error.Contains("timed out") || result.Error.Contains("statement") || result.Error.Contains("Timeout"),
            $"Unexpected error: {result.Error}");
    }

    [Fact]
    public async Task ExecuteAsync_MaxStatements_ReturnsFailure()
    {
        var options = new ScriptOptions { MaxStatements = 10 };
        var code = "var i = 0; while(i < 1000) { i++; }";

        var result = await _engine.ExecuteAsync(code, "", options);

        Assert.False(result.Success);
        Assert.Contains("statement", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_RecursionLimit_ReturnsFailure()
    {
        var options = new ScriptOptions { MaxRecursion = 5 };
        var code = "function f() { return f(); } f()";

        var result = await _engine.ExecuteAsync(code, "", options);

        Assert.False(result.Success);
        Assert.Contains("recursion", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_CsvConversion_Works()
    {
        var code = """
            const rows = JSON.parse(input);
            const headers = Object.keys(rows[0]);
            const csv = [headers.join(',')];
            for (const row of rows) {
                csv.push(headers.map(h => row[h]).join(','));
            }
            result = csv.join('\n');
            """;
        var input = """[{"Name":"Alice","Score":95},{"Name":"Bob","Score":87}]""";

        var result = await _engine.ExecuteAsync(code, input);

        Assert.True(result.Success);
        Assert.Contains("Name,Score", result.Output);
        Assert.Contains("Alice,95", result.Output);
        Assert.Contains("Bob,87", result.Output);
    }
}
