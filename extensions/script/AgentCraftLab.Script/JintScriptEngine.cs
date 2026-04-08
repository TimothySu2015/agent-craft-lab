using System.Diagnostics;
using System.Text;
using Jint;
using Jint.Native;
using Jint.Runtime;
using JintEngine = Jint.Engine;

namespace AgentCraftLab.Script;

/// <summary>
/// Jint JavaScript 沙箱引擎實作。
/// 每次執行建立獨立的 Jint Engine，天然隔離 + 四道資源限制（timeout/memory/recursion/statements）。
/// </summary>
public sealed class JintScriptEngine : IScriptEngine
{
    private readonly ScriptOptions _defaultOptions;
    private readonly IReadOnlyList<ISandboxApi> _sandboxApis;

    public JintScriptEngine(ScriptOptions? defaultOptions = null, IEnumerable<ISandboxApi>? sandboxApis = null)
    {
        _defaultOptions = defaultOptions ?? new ScriptOptions();
        _sandboxApis = sandboxApis?.ToList() ?? [];
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

        // Jint 是同步的，用 Task.Run 避免阻塞呼叫端
        return Task.Run(() => Execute(code, input, options ?? _defaultOptions), cancellationToken);
    }

    private ScriptResult Execute(string code, string input, ScriptOptions options)
    {
        var sw = Stopwatch.StartNew();
        var consoleLog = new StringBuilder();

        try
        {
            var engine = new JintEngine(cfg =>
            {
                cfg.TimeoutInterval(TimeSpan.FromSeconds(options.TimeoutSeconds));
                cfg.LimitMemory(options.MemoryLimitMB * 1_048_576L);
                cfg.LimitRecursion(options.MaxRecursion);
                cfg.MaxStatements(options.MaxStatements);
                cfg.Strict = false;
            });

            // 注入 input 變數
            engine.SetValue("input", input);

            // 注入 console.log
            engine.SetValue("console", new
            {
                log = (Action<object[]>)(args =>
                {
                    consoleLog.AppendLine(string.Join(" ", args.Select(a => a?.ToString() ?? "undefined")));
                }),
            });

            // 注入白名單 API — 將每個 ISandboxApi 的方法註冊為 JS 全域物件
            foreach (var api in _sandboxApis)
            {
                var methods = api.GetMethods();
                var obj = new Dictionary<string, object>();
                foreach (var (name, method) in methods)
                {
                    obj[name] = method;
                }
                engine.SetValue(api.Name, obj);
            }

            // 執行腳本（Evaluate 回傳最後表達式的 completion value，不需要重跑）
            var completionValue = engine.Evaluate(code);

            // 取得 result：優先讀 `result` 變數，fallback 到 completion value
            var resultValue = engine.GetValue("result");
            string output;

            if (resultValue is not null && resultValue.Type != Jint.Runtime.Types.Undefined && resultValue.Type != Jint.Runtime.Types.Empty)
            {
                output = Stringify(engine, resultValue);
            }
            else
            {
                output = (completionValue is null || completionValue.Type == Jint.Runtime.Types.Undefined)
                    ? ""
                    : Stringify(engine, completionValue);
            }

            sw.Stop();
            return new ScriptResult
            {
                Output = output,
                Success = true,
                ConsoleOutput = consoleLog.Length > 0 ? consoleLog.ToString() : null,
                Elapsed = sw.Elapsed,
            };
        }
        catch (TimeoutException)
        {
            sw.Stop();
            return Fail($"Script execution timed out after {options.TimeoutSeconds} seconds.", consoleLog, sw.Elapsed);
        }
        catch (MemoryLimitExceededException)
        {
            sw.Stop();
            return Fail($"Script exceeded memory limit of {options.MemoryLimitMB}MB.", consoleLog, sw.Elapsed);
        }
        catch (RecursionDepthOverflowException)
        {
            sw.Stop();
            return Fail($"Script exceeded recursion depth limit of {options.MaxRecursion}.", consoleLog, sw.Elapsed);
        }
        catch (StatementsCountOverflowException)
        {
            sw.Stop();
            return Fail($"Script exceeded maximum statement count of {options.MaxStatements}.", consoleLog, sw.Elapsed);
        }
        catch (JavaScriptException ex)
        {
            sw.Stop();
            return Fail($"JavaScript error: {ex.Message}", consoleLog, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Fail($"Script execution failed: {ex.Message}", consoleLog, sw.Elapsed);
        }
    }

    private static ScriptResult Fail(string error, StringBuilder consoleLog, TimeSpan elapsed) => new()
    {
        Output = "",
        Success = false,
        Error = error,
        ConsoleOutput = consoleLog.Length > 0 ? consoleLog.ToString() : null,
        Elapsed = elapsed,
    };

    /// <summary>將 JS 值轉為字串（物件/陣列用 JSON.stringify）。</summary>
    private static string Stringify(JintEngine engine, JsValue value)
    {
        if (value.Type == Jint.Runtime.Types.String)
        {
            return value.ToString();
        }

        // 物件/陣列/數字/布林 — 用 JSON.stringify 確保格式一致
        try
        {
            engine.SetValue("__tmp", value);
            var json = engine.Evaluate("typeof __tmp === 'object' && __tmp !== null ? JSON.stringify(__tmp) : String(__tmp)");
            return json.ToString();
        }
        catch
        {
            return value.ToString();
        }
    }
}
