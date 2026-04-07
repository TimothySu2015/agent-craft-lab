using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AgentCraftLab.Script;

/// <summary>
/// Roslyn C# 沙箱引擎實作。
/// 使用低階 CSharpCompilation + Collectible AssemblyLoadContext，避免 CSharpScript 高階 API 的記憶體洩漏。
/// 每次執行：編譯 → 載入到 collectible ALC → 反射呼叫 → Unload ALC。
/// </summary>
public sealed class RoslynScriptEngine : IScriptEngine
{
    private readonly ScriptOptions _defaultOptions;
    private readonly IReadOnlyList<ISandboxApi> _sandboxApis;
    private static readonly object WarmupLock = new();
    private static bool _warmedUp;

    /// <summary>安全白名單 References — 只允許這些 assembly。</summary>
    private static readonly Lazy<MetadataReference[]> SafeReferences = new(BuildSafeReferences);

    public RoslynScriptEngine(ScriptOptions? defaultOptions = null, IEnumerable<ISandboxApi>? sandboxApis = null)
    {
        _defaultOptions = defaultOptions ?? new ScriptOptions();
        _sandboxApis = sandboxApis?.ToList() ?? [];
    }

    /// <summary>
    /// 預熱 Roslyn 編譯器（首次編譯需 3-5 秒），建議在 app 啟動時呼叫。
    /// </summary>
    public static void Warmup()
    {
        if (_warmedUp) return;
        lock (WarmupLock)
        {
            if (_warmedUp) return;
            // 編譯一段 dummy 程式碼以載入 Roslyn 編譯器
            var tree = CSharpSyntaxTree.ParseText("public class W { public static object Execute(string input) { return input; } }");
            CSharpCompilation.Create("warmup",
                [tree],
                SafeReferences.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            _warmedUp = true;
        }
    }

    public Task<ScriptResult> ExecuteAsync(string code, string input,
        ScriptOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult(new ScriptResult
            {
                Output = input,
                Success = true,
                Elapsed = TimeSpan.Zero,
            });
        }

        var opts = options ?? _defaultOptions;
        return Task.Run(() => Execute(code, input, opts, cancellationToken), cancellationToken);
    }

    private ScriptResult Execute(string code, string input, ScriptOptions options, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var consoleLog = new StringBuilder();

        try
        {
            var wrappedCode = WrapUserCode(code);

            var scanError = RoslynCodeSanitizer.Scan(wrappedCode);
            if (scanError is not null)
            {
                return Fail(scanError, consoleLog, sw.Elapsed);
            }

            var (assembly, compileError) = CompileToAssembly(wrappedCode, ct);
            if (compileError is not null)
            {
                return Fail(compileError, consoleLog, sw.Elapsed);
            }

            return InvokeAndUnload(assembly!, input, options, consoleLog, ct, sw);
        }
        catch (OperationCanceledException)
        {
            return Fail("Script execution was cancelled.", consoleLog, sw.Elapsed);
        }
        catch (AggregateException ex)
        {
            return Fail($"Runtime error: {UnwrapException(ex).Message}", consoleLog, sw.Elapsed);
        }
        catch (TargetInvocationException ex)
        {
            return Fail($"Runtime error: {ex.InnerException?.Message ?? ex.Message}", consoleLog, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return Fail($"Script execution failed: {ex.Message}", consoleLog, sw.Elapsed);
        }
    }

    /// <summary>編譯使用者程式碼為 assembly（collectible ALC）。</summary>
    private static (Assembly? Assembly, string? Error) CompileToAssembly(string wrappedCode, CancellationToken ct)
    {
        var tree = CSharpSyntaxTree.ParseText(wrappedCode);
        var compilation = CSharpCompilation.Create(
            $"Script_{Guid.NewGuid():N}",
            [tree],
            SafeReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms, cancellationToken: ct);

        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage())
                .ToList();
            return (null, $"Compilation error: {string.Join("; ", errors)}");
        }

        ms.Seek(0, SeekOrigin.Begin);
        var alc = new CollectibleLoadContext();
        try
        {
            var assembly = alc.LoadFromStream(ms);
            return (assembly, null);
        }
        catch (Exception ex)
        {
            alc.Unload();
            return (null, $"Assembly load error: {ex.Message}");
        }
    }

    /// <summary>反射呼叫 Execute 方法（帶 timeout），完成後 Unload ALC。</summary>
    private static ScriptResult InvokeAndUnload(
        Assembly assembly, string input, ScriptOptions options,
        StringBuilder consoleLog, CancellationToken ct, Stopwatch sw)
    {
        var alc = AssemblyLoadContext.GetLoadContext(assembly) as CollectibleLoadContext;
        try
        {
            var scriptType = assembly.GetType("UserScript")!;
            var executeMethod = scriptType.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static)!;

            object? result = null;
            var executeTask = Task.Run(() =>
            {
                result = executeMethod.Invoke(null, [input]);
            }, ct);

            if (!executeTask.Wait(TimeSpan.FromSeconds(options.TimeoutSeconds), ct))
            {
                return Fail($"Script execution timed out after {options.TimeoutSeconds} seconds.", consoleLog, sw.Elapsed);
            }

            if (executeTask.IsFaulted)
            {
                var ex = UnwrapException(executeTask.Exception!);
                return Fail($"Runtime error: {ex.Message}", consoleLog, sw.Elapsed);
            }

            var output = result switch
            {
                null => "",
                string s => s,
                _ => System.Text.Json.JsonSerializer.Serialize(result),
            };

            // 取回 console output（UserScript.Log() 寫入的內容）
            var getConsoleMethod = scriptType.GetMethod("GetConsoleOutput", BindingFlags.Public | BindingFlags.Static);
            if (getConsoleMethod is not null)
            {
                var console = getConsoleMethod.Invoke(null, null) as string;
                if (!string.IsNullOrEmpty(console))
                {
                    consoleLog.Append(console);
                }
            }

            return new ScriptResult
            {
                Output = output,
                Success = true,
                ConsoleOutput = consoleLog.Length > 0 ? consoleLog.ToString() : null,
                Elapsed = sw.Elapsed,
            };
        }
        finally
        {
            alc?.Unload();
        }
    }

    /// <summary>解包 AggregateException / TargetInvocationException 到實際例外。</summary>
    private static Exception UnwrapException(Exception ex)
    {
        var inner = ex is AggregateException agg ? agg.InnerException ?? ex : ex;
        if (inner is TargetInvocationException tie && tie.InnerException is not null)
        {
            inner = tie.InnerException;
        }
        return inner;
    }

    /// <summary>
    /// 將使用者程式碼包裝成可編譯的 class。
    /// 使用者只需寫方法體，不需要 using / class / method 宣告。
    /// </summary>
    private static string WrapUserCode(string userCode)
    {
        return $$"""
            using System;
            using System.Linq;
            using System.Collections.Generic;
            using System.Text;
            using System.Text.Json;
            using System.Text.RegularExpressions;

            public static class UserScript
            {
                private static readonly System.Text.StringBuilder __console = new();

                public static object Execute(string input)
                {
                    {{userCode}}
                }

                public static void Log(params object[] args)
                {
                    __console.AppendLine(string.Join(" ", args.Select(a => a?.ToString() ?? "null")));
                }

                public static string GetConsoleOutput() => __console.ToString();
            }
            """;
    }

    private static ScriptResult Fail(string error, StringBuilder consoleLog, TimeSpan elapsed) => new()
    {
        Output = "",
        Success = false,
        Error = error,
        ConsoleOutput = consoleLog.Length > 0 ? consoleLog.ToString() : null,
        Elapsed = elapsed,
    };

    /// <summary>
    /// 建構安全白名單 References。
    /// 只包含安全的 assembly，不包含 System.IO.FileSystem、System.Net.Http 等。
    /// </summary>
    private static MetadataReference[] BuildSafeReferences()
    {
        var refs = new List<MetadataReference>();

        // 從 trusted assemblies 中篩選安全的
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator) ?? [];

        var allowedAssemblyPrefixes = new[]
        {
            "System.Runtime",
            "System.Collections",
            "System.Linq",
            "System.Text.Json",
            "System.Text.RegularExpressions",
            "System.Text.Encoding",
            "System.Memory",
            "System.Buffers",
            "System.Numerics",
            "System.Console",        // Console.WriteLine 用於除錯
            "System.ObjectModel",
            "System.ComponentModel",
            "System.Private.CoreLib",
            "netstandard",
        };

        foreach (var path in trustedAssemblies)
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (allowedAssemblyPrefixes.Any(prefix => fileName.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)))
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return refs.ToArray();
    }

    /// <summary>
    /// Collectible AssemblyLoadContext — 允許 Unload 以釋放動態編譯的 assembly。
    /// </summary>
    private sealed class CollectibleLoadContext() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }
}
