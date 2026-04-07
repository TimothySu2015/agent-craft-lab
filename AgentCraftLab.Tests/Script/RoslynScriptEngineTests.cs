using AgentCraftLab.Script;

namespace AgentCraftLab.Tests.Script;

public class RoslynScriptEngineTests
{
    private readonly RoslynScriptEngine _engine = new();

    [Fact]
    public async Task ExecuteAsync_SimpleReturn_ReturnsOutput()
    {
        var result = await _engine.ExecuteAsync("return input.ToUpper();", "hello");

        Assert.True(result.Success);
        Assert.Equal("HELLO", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_NumericReturn_ReturnsStringified()
    {
        var result = await _engine.ExecuteAsync("return input.Length;", "hello");

        Assert.True(result.Success);
        Assert.Equal("5", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_LinqSelect_Works()
    {
        var code = """
            var items = new[] { "apple", "banana", "cherry" };
            return string.Join(", ", items.Select(x => x.ToUpper()));
            """;

        var result = await _engine.ExecuteAsync(code, "");

        Assert.True(result.Success);
        Assert.Equal("APPLE, BANANA, CHERRY", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_LinqWhereOrderBy_Works()
    {
        var code = """
            var numbers = new[] { 5, 3, 8, 1, 9, 2 };
            var filtered = numbers.Where(n => n > 3).OrderBy(n => n).ToList();
            return string.Join(", ", filtered);
            """;

        var result = await _engine.ExecuteAsync(code, "");

        Assert.True(result.Success);
        Assert.Equal("5, 8, 9", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_JsonDeserialization_Works()
    {
        var code = """
            var items = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(input)!;
            var names = items.Select(d => d["name"].GetString()).ToList();
            return string.Join(", ", names!);
            """;
        var input = """[{"name":"Alice"},{"name":"Bob"}]""";

        var result = await _engine.ExecuteAsync(code, input);

        Assert.True(result.Success);
        Assert.Equal("Alice, Bob", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_JsonSerialization_ReturnsJson()
    {
        var code = """
            var data = new Dictionary<string, object>
            {
                ["count"] = input.Length,
                ["upper"] = input.ToUpper()
            };
            return data;
            """;

        var result = await _engine.ExecuteAsync(code, "hi");

        Assert.True(result.Success);
        Assert.Contains("\"count\":2", result.Output);
        Assert.Contains("\"upper\":\"HI\"", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_RegexMatch_Works()
    {
        var code = """
            var matches = Regex.Matches(input, @"[\w.-]+@[\w.-]+\.[a-z]{2,}", RegexOptions.IgnoreCase);
            return string.Join(", ", matches.Select(m => m.Value));
            """;

        var result = await _engine.ExecuteAsync(code, "Contact us at test@example.com or info@test.org");

        Assert.True(result.Success);
        Assert.Contains("test@example.com", result.Output);
        Assert.Contains("info@test.org", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCode_ReturnsInput()
    {
        var result = await _engine.ExecuteAsync("", "passthrough");

        Assert.True(result.Success);
        Assert.Equal("passthrough", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_NullReturn_ReturnsEmpty()
    {
        var result = await _engine.ExecuteAsync("return null;", "test");

        Assert.True(result.Success);
        Assert.Equal("", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_CompilationError_ReturnsFailure()
    {
        var result = await _engine.ExecuteAsync("var x = {{{;", "");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Compilation error", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_RuntimeError_ReturnsFailure()
    {
        var result = await _engine.ExecuteAsync("throw new InvalidOperationException(\"test error\");", "");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        // TargetInvocationException 包裝後可能出現在不同層級
        Assert.True(result.Error.Contains("test error") || result.Error.Contains("Runtime error") || result.Error.Contains("failed"),
            $"Expected runtime error, got: {result.Error}");
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_ReturnsFailure()
    {
        var options = new ScriptOptions { TimeoutSeconds = 1 };
        var code = "while (true) { }";

        var result = await _engine.ExecuteAsync(code, "", options);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("timed out", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_StringConcatenation_Works()
    {
        var code = """
            var parts = input.Split(',');
            return string.Join(" | ", parts.Select(p => p.Trim().ToUpper()));
            """;

        var result = await _engine.ExecuteAsync(code, "foo, bar, baz");

        Assert.True(result.Success);
        Assert.Equal("FOO | BAR | BAZ", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ConsoleLog_Captured()
    {
        var code = """
            UserScript.Log("hello", "world");
            UserScript.Log("count:", 42);
            return "done";
            """;

        var result = await _engine.ExecuteAsync(code, "");

        Assert.True(result.Success);
        Assert.Equal("done", result.Output);
        Assert.NotNull(result.ConsoleOutput);
        Assert.Contains("hello world", result.ConsoleOutput);
        Assert.Contains("count: 42", result.ConsoleOutput);
    }

    [Fact]
    public async Task ExecuteAsync_DictionaryOperations_Work()
    {
        var code = """
            var dict = new Dictionary<string, int>
            {
                ["a"] = 1,
                ["b"] = 2,
                ["c"] = 3
            };
            return dict.Where(kv => kv.Value > 1).Sum(kv => kv.Value).ToString();
            """;

        var result = await _engine.ExecuteAsync(code, "");

        Assert.True(result.Success);
        Assert.Equal("5", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_CsvConversion_Works()
    {
        var code = """
            var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(input)!;
            var headers = rows[0].Keys.ToList();
            var csv = new List<string> { string.Join(",", headers) };
            foreach (var row in rows)
            {
                csv.Add(string.Join(",", headers.Select(h => row[h].ToString())));
            }
            return string.Join("\n", csv);
            """;
        var input = """[{"Name":"Alice","Score":"95"},{"Name":"Bob","Score":"87"}]""";

        var result = await _engine.ExecuteAsync(code, input);

        Assert.True(result.Success);
        Assert.Contains("Name,Score", result.Output);
        Assert.Contains("Alice,95", result.Output);
        Assert.Contains("Bob,87", result.Output);
    }
}
