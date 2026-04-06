namespace AgentCraftLab.Script;

/// <summary>
/// 腳本執行引擎介面 — 可替換實作（Jint / Roslyn / Python）。
/// </summary>
public interface IScriptEngine
{
    /// <summary>在沙箱中執行腳本。</summary>
    /// <param name="code">腳本程式碼</param>
    /// <param name="input">輸入文字（注入為 `input` 變數）</param>
    /// <param name="options">沙箱設定（null 使用預設）</param>
    /// <param name="cancellationToken">取消 token</param>
    Task<ScriptResult> ExecuteAsync(string code, string input,
        ScriptOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>沙箱執行設定。</summary>
public record ScriptOptions
{
    public int TimeoutSeconds { get; init; } = 5;
    public int MemoryLimitMB { get; init; } = 50;
    public int MaxRecursion { get; init; } = 100;
    public int MaxStatements { get; init; } = 100_000;
}

/// <summary>腳本執行結果。</summary>
public record ScriptResult
{
    /// <summary>執行輸出（result 變數的值或最後一個表達式）。</summary>
    public required string Output { get; init; }

    /// <summary>是否成功執行。</summary>
    public bool Success { get; init; }

    /// <summary>錯誤訊息（失敗時）。</summary>
    public string? Error { get; init; }

    /// <summary>console.log 收集的除錯輸出。</summary>
    public string? ConsoleOutput { get; init; }

    /// <summary>執行耗時。</summary>
    public TimeSpan Elapsed { get; init; }
}
