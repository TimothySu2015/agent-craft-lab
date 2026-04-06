namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 工具程式碼執行介面 — 讓 Autonomous 層可執行沙箱腳本而不直接依賴 Script 專案。
/// 由 API/App 層註冊橋接器（IScriptEngine → IToolCodeRunner）。
/// </summary>
public interface IToolCodeRunner
{
    /// <summary>在沙箱中執行程式碼。</summary>
    /// <param name="code">JavaScript 程式碼</param>
    /// <param name="input">輸入文字（注入為 input 變數）</param>
    /// <param name="timeoutSeconds">執行超時秒數</param>
    /// <param name="ct">取消 token</param>
    Task<ToolCodeResult> ExecuteAsync(string code, string input, int timeoutSeconds = 3, CancellationToken ct = default);
}

/// <summary>沙箱執行結果。</summary>
public sealed record ToolCodeResult(bool Success, string Output, string? Error);
