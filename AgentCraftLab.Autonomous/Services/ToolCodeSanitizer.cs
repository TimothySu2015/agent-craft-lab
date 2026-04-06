using System.Text.RegularExpressions;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 工具程式碼安全掃描器 — 在沙箱執行前檢查 JS 程式碼的危險模式。
/// Jint 沙箱已阻擋大部分攻擊向量，此掃描為額外防線（defense in depth）。
/// </summary>
public static partial class ToolCodeSanitizer
{
    /// <summary>最大允許程式碼長度（字元）。</summary>
    private const int MaxCodeLength = 10_000;

    /// <summary>
    /// 掃描程式碼是否包含危險模式。回傳 null 表示通過，否則回傳問題描述。
    /// </summary>
    public static string? Scan(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "Code is empty.";
        }

        if (code.Length > MaxCodeLength)
        {
            return $"Code exceeds maximum length ({MaxCodeLength} chars).";
        }

        // 危險模式掃描
        foreach (var (pattern, description) in DangerousPatterns)
        {
            if (pattern.IsMatch(code))
            {
                return $"Blocked: {description}";
            }
        }

        return null;
    }

    /// <summary>危險模式清單。</summary>
    private static readonly (Regex Pattern, string Description)[] DangerousPatterns =
    [
        (EvalPattern(), "eval() is not allowed"),
        (FunctionConstructorPattern(), "Function constructor is not allowed"),
        (ProtoPattern(), "__proto__ access is not allowed"),
        (ConstructorChainPattern(), "constructor.constructor chain is not allowed"),
        (ImportPattern(), "import/require is not allowed"),
        (GlobalThisPattern(), "globalThis access is not allowed"),
        (ProcessPattern(), "process object access is not allowed"),
    ];

    // ─── 編譯時正規表達式 ───

    [GeneratedRegex(@"\beval\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex EvalPattern();

    [GeneratedRegex(@"\bFunction\s*\(", RegexOptions.None)]
    private static partial Regex FunctionConstructorPattern();

    [GeneratedRegex(@"__proto__", RegexOptions.None)]
    private static partial Regex ProtoPattern();

    [GeneratedRegex(@"\.constructor\.constructor", RegexOptions.None)]
    private static partial Regex ConstructorChainPattern();

    [GeneratedRegex(@"\b(require|import)\s*[\(']", RegexOptions.None)]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"\bglobalThis\b", RegexOptions.None)]
    private static partial Regex GlobalThisPattern();

    [GeneratedRegex(@"\bprocess\s*\.", RegexOptions.None)]
    private static partial Regex ProcessPattern();
}
