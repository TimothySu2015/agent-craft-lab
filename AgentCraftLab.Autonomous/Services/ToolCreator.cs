using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 工具建立器 — 負責驗證、測試、註冊 Agent 自製的 JS 工具。
/// 三層驗證：安全掃描 → 語法測試 → 功能測試。失敗時回傳錯誤訊息供 LLM 修正。
/// </summary>
public sealed class ToolCreator
{
    private readonly IToolCodeRunner _runner;
    private readonly ILogger _logger;

    /// <summary>每個 session 最多可建立的工具數。</summary>
    private const int MaxToolsPerSession = 10;

    /// <summary>已建立的工具數（thread-safe）。</summary>
    private int _createdCount;

    public ToolCreator(IToolCodeRunner runner, ILogger logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <summary>
    /// 建立並驗證一個新工具。成功時回傳包裝好的 AITool，失敗時回傳錯誤訊息。
    /// </summary>
    public async Task<ToolCreationResult> CreateAsync(
        string name,
        string description,
        string code,
        string? testInput,
        string? testExpected,
        DynamicToolSet? dynamicToolSet,
        CancellationToken ct)
    {
        // 0. 數量限制
        if (Volatile.Read(ref _createdCount) >= MaxToolsPerSession)
        {
            return ToolCreationResult.Failed(
                $"Tool creation limit reached ({MaxToolsPerSession} per session). Reuse existing tools.");
        }

        // 1. 基本驗證
        if (string.IsNullOrWhiteSpace(name))
        {
            return ToolCreationResult.Failed("Tool name is required.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return ToolCreationResult.Failed("Tool description is required.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return ToolCreationResult.Failed("Tool code is required.");
        }

        // 名稱格式化
        var safeName = SanitizeName(name);

        // 重複檢查
        if (dynamicToolSet?.IsAvailable(safeName) == true)
        {
            return ToolCreationResult.Failed(
                $"Tool '{safeName}' already exists and is ready to use. " +
                $"Do NOT create it again — just call it directly: {safeName}(input)");
        }

        // 2. 安全掃描
        var scanIssue = ToolCodeSanitizer.Scan(code);
        if (scanIssue is not null)
        {
            return ToolCreationResult.Failed($"Security check failed: {scanIssue}");
        }

        // 3. 語法測試（空輸入執行）
        var syntaxResult = await _runner.ExecuteAsync(code, "", ct: ct);
        if (!syntaxResult.Success)
        {
            return ToolCreationResult.Failed(
                $"Syntax/runtime error: {syntaxResult.Error}\nFix the code and try again.");
        }

        // 4. 功能測試（若提供了測試案例）
        if (!string.IsNullOrWhiteSpace(testInput) && !string.IsNullOrWhiteSpace(testExpected))
        {
            var testResult = await _runner.ExecuteAsync(code, testInput, ct: ct);
            if (!testResult.Success)
            {
                return ToolCreationResult.Failed(
                    $"Test execution failed: {testResult.Error}\nTest input: {testInput}");
            }

            if (!testResult.Output.Contains(testExpected, StringComparison.OrdinalIgnoreCase))
            {
                return ToolCreationResult.Failed(
                    $"Test output mismatch.\nExpected to contain: {testExpected}\nActual output: {testResult.Output}\nFix the code and try again.");
            }
        }

        // 5. 建立 AITool — 包裝 JS 執行為可呼叫工具
        var tool = AIFunctionFactory.Create(
            async (string input, CancellationToken cancellationToken) =>
            {
                var result = await _runner.ExecuteAsync(code, input, ct: cancellationToken);
                return result.Success ? result.Output : $"[Error] {result.Error}";
            },
            safeName,
            description);

        Interlocked.Increment(ref _createdCount);
        _logger.LogInformation("已建立自製工具: {Name} ({Description})", safeName, description);

        return ToolCreationResult.Ok(tool, safeName, description);
    }

    /// <summary>名稱正規化：轉 snake_case，移除特殊字元。</summary>
    private static string SanitizeName(string name)
    {
        // 已經是 snake_case → 保留
        if (name.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            return name.ToLowerInvariant();
        }

        // 移除非字母數字字元，轉小寫
        var chars = name
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_')
            .ToArray();
        return new string(chars).Trim('_');
    }
}

/// <summary>工具建立結果。</summary>
public sealed record ToolCreationResult
{
    public bool Success { get; init; }
    public AITool? Tool { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string? ErrorMessage { get; init; }

    public static ToolCreationResult Ok(AITool tool, string name, string description)
        => new() { Success = true, Tool = tool, Name = name, Description = description };

    public static ToolCreationResult Failed(string error)
        => new() { Success = false, ErrorMessage = error };
}
