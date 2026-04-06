using AgentCraftLab.Autonomous.Services;

namespace AgentCraftLab.Tests.Autonomous;

public class ToolCodeSanitizerTests
{
    // ─── 安全程式碼 ───

    [Fact]
    public void Scan_SafeCode_ReturnsNull()
    {
        var code = """
            var words = input.split(' ');
            result = words.length.toString();
            """;
        Assert.Null(ToolCodeSanitizer.Scan(code));
    }

    [Fact]
    public void Scan_MathCode_ReturnsNull()
    {
        Assert.Null(ToolCodeSanitizer.Scan("result = (2 + 2).toString();"));
    }

    // ─── 危險模式 ───

    [Theory]
    [InlineData("eval('alert(1)')", "eval")]
    [InlineData("EVAL('test')", "eval")]
    [InlineData("var f = Function('return 1')", "Function constructor")]
    [InlineData("obj.__proto__.toString", "__proto__")]
    [InlineData("x.constructor.constructor('return this')()", "constructor.constructor")]
    [InlineData("require('fs')", "import/require")]
    [InlineData("import('module')", "import/require")]
    [InlineData("globalThis.eval", "globalThis")]
    [InlineData("process.exit(0)", "process")]
    public void Scan_DangerousPattern_ReturnsBlocked(string code, string expectedKeyword)
    {
        var result = ToolCodeSanitizer.Scan(code);
        Assert.NotNull(result);
        Assert.Contains("Blocked", result);
        Assert.Contains(expectedKeyword, result, StringComparison.OrdinalIgnoreCase);
    }

    // ─── 邊界情況 ───

    [Fact]
    public void Scan_EmptyCode_ReturnsError()
    {
        Assert.NotNull(ToolCodeSanitizer.Scan(""));
        Assert.NotNull(ToolCodeSanitizer.Scan("   "));
    }

    [Fact]
    public void Scan_TooLongCode_ReturnsError()
    {
        var longCode = new string('x', 10_001);
        var result = ToolCodeSanitizer.Scan(longCode);
        Assert.NotNull(result);
        Assert.Contains("maximum length", result);
    }

    [Fact]
    public void Scan_ExactMaxLength_Passes()
    {
        var code = "result = '" + new string('a', 9_988) + "';";
        Assert.Null(ToolCodeSanitizer.Scan(code));
    }

    // ─── 合法包含關鍵字的情況（不應誤判）───

    [Fact]
    public void Scan_EvalInVariableName_NotBlocked()
    {
        // "evaluate" 不應觸發 eval 規則（\beval\s*\( 需要後接括號）
        Assert.Null(ToolCodeSanitizer.Scan("var evaluate = true; result = evaluate.toString();"));
    }

    [Fact]
    public void Scan_ProcessInString_Blocked()
    {
        // "process." 在任何位置都應被擋（即使在字串中 — defense in depth）
        var result = ToolCodeSanitizer.Scan("var x = 'process.env';");
        Assert.NotNull(result);
    }
}
