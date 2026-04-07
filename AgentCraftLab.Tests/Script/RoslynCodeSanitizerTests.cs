using AgentCraftLab.Script;

namespace AgentCraftLab.Tests.Script;

public class RoslynCodeSanitizerTests
{
    private static string Wrap(string userCode) => $$"""
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using System.Text;
        using System.Text.Json;
        using System.Text.RegularExpressions;

        public static class UserScript
        {
            public static object Execute(string input)
            {
                {{userCode}}
            }
        }
        """;

    [Fact]
    public void Scan_SafeCode_ReturnsNull()
    {
        var code = Wrap("return input.ToUpper();");
        Assert.Null(RoslynCodeSanitizer.Scan(code));
    }

    [Fact]
    public void Scan_LinqCode_ReturnsNull()
    {
        var code = Wrap("return new[] { 1, 2, 3 }.Where(x => x > 1).Select(x => x * 2).ToList();");
        Assert.Null(RoslynCodeSanitizer.Scan(code));
    }

    [Fact]
    public void Scan_FileAccess_ReturnsError()
    {
        var code = Wrap("return File.ReadAllText(\"secret.txt\");");
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        Assert.Contains("File", error);
    }

    [Fact]
    public void Scan_DirectoryAccess_ReturnsError()
    {
        var code = Wrap("var files = Directory.GetFiles(\"C:\\\\\"); return files[0];");
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        Assert.Contains("Directory", error);
    }

    [Fact]
    public void Scan_ProcessStart_ReturnsError()
    {
        var code = Wrap("Process.Start(\"cmd.exe\"); return \"done\";");
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        Assert.Contains("Process", error);
    }

    [Fact]
    public void Scan_HttpClient_ReturnsError()
    {
        var code = Wrap("var client = new HttpClient(); return \"done\";");
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        Assert.Contains("HttpClient", error);
    }

    [Fact]
    public void Scan_ReflectionAssembly_ReturnsError()
    {
        var code = Wrap("var asm = Assembly.GetExecutingAssembly(); return asm.FullName;");
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        Assert.Contains("Assembly", error);
    }

    [Fact]
    public void Scan_EnvironmentAccess_ReturnsError()
    {
        var code = Wrap("return Environment.GetEnvironmentVariable(\"PATH\");");
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        Assert.Contains("Environment", error);
    }

    [Fact]
    public void Scan_SystemIoUsing_ReturnsError()
    {
        var code = """
            using System.IO;
            public static class UserScript
            {
                public static object Execute(string input) { return ""; }
            }
            """;
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        Assert.Contains("System.IO", error);
    }

    [Fact]
    public void Scan_SystemNetUsing_ReturnsError()
    {
        var code = """
            using System.Net;
            public static class UserScript
            {
                public static object Execute(string input) { return ""; }
            }
            """;
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        Assert.Contains("System.Net", error);
    }

    [Fact]
    public void Scan_FullyQualifiedFileAccess_ReturnsError()
    {
        var code = Wrap("return System.IO.File.ReadAllText(\"secret.txt\");");
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        // 可能先被 identifier "File" 攔截，或被 "System.IO" member access 攔截
        Assert.True(error.Contains("System.IO") || error.Contains("File"),
            $"Expected error about System.IO or File, got: {error}");
    }

    [Fact]
    public void Scan_Activator_ReturnsError()
    {
        var code = Wrap("var obj = Activator.CreateInstance(typeof(object)); return obj;");
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        Assert.Contains("Activator", error);
    }

    [Fact]
    public void Scan_GetType_OnMemberAccess_ReturnsError()
    {
        var code = Wrap("var t = input.GetType(); return t.FullName;");
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        Assert.Contains("GetType", error);
    }

    [Fact]
    public void Scan_GC_ReturnsError()
    {
        var code = Wrap("GC.Collect(); return \"done\";");
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        Assert.Contains("GC", error);
    }

    [Fact]
    public void Scan_Thread_ReturnsError()
    {
        var code = Wrap("Thread.Sleep(1000); return \"done\";");
        var error = RoslynCodeSanitizer.Scan(code);
        Assert.NotNull(error);
        Assert.Contains("Thread", error);
    }

    [Fact]
    public void Scan_JsonSerializer_Allowed()
    {
        var code = Wrap("return JsonSerializer.Serialize(new { x = 1 });");
        Assert.Null(RoslynCodeSanitizer.Scan(code));
    }

    [Fact]
    public void Scan_RegexMatch_Allowed()
    {
        var code = Wrap("return Regex.IsMatch(input, @\"\\d+\").ToString();");
        Assert.Null(RoslynCodeSanitizer.Scan(code));
    }

    [Fact]
    public void Scan_StringBuilder_Allowed()
    {
        var code = Wrap("""
            var sb = new StringBuilder();
            sb.Append("hello ");
            sb.Append(input);
            return sb.ToString();
            """);
        Assert.Null(RoslynCodeSanitizer.Scan(code));
    }
}
