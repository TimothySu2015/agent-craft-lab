using System.Text.RegularExpressions;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// craft.md 安全過濾器 — 過濾使用者自訂規範中的危險 pattern，防止 prompt injection。
/// 三道防線之一：過濾防線（另外兩道是位置防線 + 宣言防線，由 SystemPromptBuilder 處理）。
/// </summary>
public static partial class CraftMdSanitizer
{
    /// <summary>craft.md 最大允許長度（字元）。</summary>
    public const int MaxLength = 2000;

    /// <summary>
    /// 過濾 craft.md 內容。回傳 sanitized 內容 + 是否被過濾的標記。
    /// </summary>
    public static (string Content, bool WasFiltered) Sanitize(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ("", false);
        }

        var filtered = false;

        // 長度限制
        if (content.Length > MaxLength)
        {
            content = content[..MaxLength];
            filtered = true;
        }

        // 危險 pattern 過濾（移除匹配行而非整體封鎖）
        var lines = content.Split('\n');
        var safeLines = new List<string>();
        foreach (var line in lines)
        {
            var isDangerous = false;
            foreach (var pattern in DangerousPatterns)
            {
                if (pattern.IsMatch(line))
                {
                    isDangerous = true;
                    filtered = true;
                    break;
                }
            }

            if (!isDangerous)
            {
                safeLines.Add(line);
            }
        }

        return (string.Join('\n', safeLines), filtered);
    }

    private static readonly Regex[] DangerousPatterns =
    [
        IgnoreRulesPattern(),
        OverrideSystemPattern(),
        NoLimitPattern(),
        BypassSafetyPattern(),
        UnlimitedPattern(),
        YouAreNowPattern(),
        DanPattern(),
        JailbreakPattern(),
        IgnorePreviousPattern(),
        DisableToolPattern(),
        SkipVerificationPattern(),
    ];

    [GeneratedRegex(@"ignore\s*(all\s*)?rules", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoreRulesPattern();

    [GeneratedRegex(@"override\s*(the\s*)?system", RegexOptions.IgnoreCase)]
    private static partial Regex OverrideSystemPattern();

    [GeneratedRegex(@"no\s*(token\s*)?limit", RegexOptions.IgnoreCase)]
    private static partial Regex NoLimitPattern();

    [GeneratedRegex(@"bypass\s*(the\s*)?safety", RegexOptions.IgnoreCase)]
    private static partial Regex BypassSafetyPattern();

    [GeneratedRegex(@"\bunlimited\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnlimitedPattern();

    [GeneratedRegex(@"you\s+are\s+now", RegexOptions.IgnoreCase)]
    private static partial Regex YouAreNowPattern();

    [GeneratedRegex(@"\bDAN\b")]
    private static partial Regex DanPattern();

    [GeneratedRegex(@"\bjailbreak\b", RegexOptions.IgnoreCase)]
    private static partial Regex JailbreakPattern();

    [GeneratedRegex(@"ignore\s*(all\s*)?previous", RegexOptions.IgnoreCase)]
    private static partial Regex IgnorePreviousPattern();

    [GeneratedRegex(@"disable\s*(all\s*)?tool", RegexOptions.IgnoreCase)]
    private static partial Regex DisableToolPattern();

    [GeneratedRegex(@"skip\s*(all\s*)?(verification|validation|check)", RegexOptions.IgnoreCase)]
    private static partial Regex SkipVerificationPattern();
}
