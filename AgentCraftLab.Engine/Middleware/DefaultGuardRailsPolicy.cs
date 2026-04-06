using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// 基於 Regex + 關鍵字的 GuardRails 規則引擎。
/// 支援 Block/Warn/Log 三種動作、Prompt Injection 偵測、Topic 限制。
/// </summary>
public sealed class DefaultGuardRailsPolicy : IGuardRailsPolicy
{
    /// <summary>Topic 預覽截斷長度（用於審計日誌）。</summary>
    private const int TopicPreviewMaxLength = 50;

    private readonly IReadOnlyList<CompiledRule> _rules;
    private readonly string[]? _allowedTopics;

    /// <summary>內部編譯後的規則。</summary>
    private sealed record CompiledRule(
        GuardRailsRule Source,
        Regex? CompiledRegex);

    private static readonly string[] DefaultBlockedTerms =
        ["密碼", "信用卡", "銀行帳號", "駭客", "攻擊", "password", "credit card", "hack", "attack"];

    /// <summary>Prompt Injection 偵測的內建 Regex 規則。</summary>
    private static readonly (string Pattern, string Label)[] InjectionPatterns =
    [
        (@"ignore\s+(all\s+)?previous\s+instructions", "injection:ignore-instructions"),
        (@"you\s+are\s+now\s+(?:a|an)\s+", "injection:role-override"),
        (@"(?:^|\n)\s*system\s*:", "injection:system-prompt"),
        (@"\bDAN\b", "injection:DAN"),
        (@"忽略.*指令", "injection:ignore-instructions-zh"),
        (@"無視.*指令", "injection:ignore-instructions-zh2"),
        (@"do\s+not\s+follow.*rules", "injection:ignore-rules"),
        (@"disregard\s+(all\s+)?prior", "injection:disregard-prior"),
        (@"pretend\s+you\s+(are|have)\s+no\s+restrictions", "injection:no-restrictions"),
    ];

    /// <summary>
    /// 建立規則引擎。
    /// </summary>
    /// <param name="rules">使用者定義的規則。</param>
    /// <param name="allowedTopics">允許的主題關鍵字（null = 不限制）。</param>
    /// <param name="enableInjectionDetection">是否啟用 Prompt Injection 偵測。</param>
    /// <param name="logger">可選的日誌記錄器。</param>
    public DefaultGuardRailsPolicy(
        IEnumerable<GuardRailsRule>? rules = null,
        string[]? allowedTopics = null,
        bool enableInjectionDetection = false,
        ILogger? logger = null)
    {
        var compiled = new List<CompiledRule>();

        if (rules is not null)
        {
            foreach (var rule in rules)
            {
                if (rule.IsRegex)
                {
                    try
                    {
                        var regex = new Regex(rule.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                        compiled.Add(new CompiledRule(rule, regex));
                    }
                    catch (ArgumentException ex)
                    {
                        logger?.LogWarning("[GUARD] Invalid regex rule '{Label}': {Message}, skipped",
                            rule.Label ?? rule.Pattern, ex.Message);
                    }
                }
                else
                {
                    compiled.Add(new CompiledRule(rule, null));
                }
            }
        }

        // Prompt Injection 偵測（opt-in）
        if (enableInjectionDetection)
        {
            foreach (var (pattern, label) in InjectionPatterns)
            {
                var rule = new GuardRailsRule(pattern, IsRegex: true, GuardRailsAction.Block, label);
                compiled.Add(new CompiledRule(rule, new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase)));
            }
        }

        _rules = compiled;
        _allowedTopics = allowedTopics;
    }

    /// <inheritdoc/>
    public IReadOnlyList<GuardRailsMatch> Evaluate(string text, GuardRailsDirection direction)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var matches = new List<GuardRailsMatch>();

        // Topic 限制（僅 Input）
        if (direction == GuardRailsDirection.Input && _allowedTopics is { Length: > 0 }
            && !HasAllowedTopic(text, _allowedTopics))
        {
            var preview = text.Length > TopicPreviewMaxLength
                ? text[..TopicPreviewMaxLength] + "..."
                : text;
            matches.Add(new GuardRailsMatch(
                new GuardRailsRule("off-topic", IsRegex: false, GuardRailsAction.Block, "topic-restriction"),
                preview, direction));
        }

        // 規則匹配
        foreach (var rule in _rules)
        {
            if (rule.CompiledRegex is not null)
            {
                // Regex 匹配
                var match = rule.CompiledRegex.Match(text);
                if (match.Success)
                {
                    matches.Add(new GuardRailsMatch(rule.Source, match.Value, direction));
                }
            }
            else
            {
                // 關鍵字 Contains 匹配（CJK 安全）
                if (text.Contains(rule.Source.Pattern, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(new GuardRailsMatch(rule.Source, rule.Source.Pattern, direction));
                }
            }
        }

        return matches;
    }

    /// <summary>檢查文字中是否包含至少一個允許的主題關鍵字。</summary>
    private static bool HasAllowedTopic(string text, string[] allowedTopics)
    {
        foreach (var topic in allowedTopics)
        {
            if (text.Contains(topic, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 從前端 config dictionary 建立規則引擎（向下相容）。
    /// </summary>
    public static DefaultGuardRailsPolicy FromConfig(Dictionary<string, string>? config, ILogger? logger = null)
    {
        var rules = new List<GuardRailsRule>();
        string[]? allowedTopics = null;
        var enableInjection = false;

        if (config is null || config.Count == 0)
        {
            // 預設規則
            foreach (var term in DefaultBlockedTerms)
            {
                rules.Add(new GuardRailsRule(term, IsRegex: false, GuardRailsAction.Block));
            }

            return new DefaultGuardRailsPolicy(rules, logger: logger);
        }

        // 向下相容：blockedTerms + severity
        var severity = config.GetValueOrDefault("severity") ?? "medium";
        var defaultAction = severity.Equals("low", StringComparison.OrdinalIgnoreCase)
            ? GuardRailsAction.Warn
            : GuardRailsAction.Block;

        if (config.TryGetValue("blockedTerms", out var blocked) && !string.IsNullOrWhiteSpace(blocked))
        {
            foreach (var term in blocked.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                rules.Add(new GuardRailsRule(term, IsRegex: false, defaultAction));
            }
        }
        else
        {
            // 無自訂 blockedTerms → 用預設
            foreach (var term in DefaultBlockedTerms)
            {
                rules.Add(new GuardRailsRule(term, IsRegex: false, defaultAction));
            }
        }

        // 新增：warnTerms
        if (config.TryGetValue("warnTerms", out var warn) && !string.IsNullOrWhiteSpace(warn))
        {
            foreach (var term in warn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                rules.Add(new GuardRailsRule(term, IsRegex: false, GuardRailsAction.Warn));
            }
        }

        // 新增：logTerms
        if (config.TryGetValue("logTerms", out var log) && !string.IsNullOrWhiteSpace(log))
        {
            foreach (var term in log.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                rules.Add(new GuardRailsRule(term, IsRegex: false, GuardRailsAction.Log));
            }
        }

        // 新增：regexRules（每行一條 regex）
        if (config.TryGetValue("regexRules", out var regex) && !string.IsNullOrWhiteSpace(regex))
        {
            foreach (var pattern in regex.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                rules.Add(new GuardRailsRule(pattern, IsRegex: true, GuardRailsAction.Block));
            }
        }

        // 新增：allowedTopics
        if (config.TryGetValue("allowedTopics", out var topics) && !string.IsNullOrWhiteSpace(topics))
        {
            allowedTopics = topics.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        // 新增：enableInjectionDetection
        if (config.TryGetValue("enableInjectionDetection", out var injection))
        {
            enableInjection = string.Equals(injection, "true", StringComparison.OrdinalIgnoreCase);
        }

        return new DefaultGuardRailsPolicy(rules, allowedTopics, enableInjection, logger);
    }
}
