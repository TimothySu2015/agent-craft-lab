namespace AgentCraftLab.Engine.Middleware;

/// <summary>掃描方向。</summary>
public enum GuardRailsDirection
{
    /// <summary>使用者輸入。</summary>
    Input,

    /// <summary>LLM 回應。</summary>
    Output,
}

/// <summary>
/// GuardRails 規則匹配結果。
/// </summary>
/// <param name="Rule">命中的規則。</param>
/// <param name="MatchedText">匹配到的文字片段。</param>
/// <param name="Direction">掃描方向。</param>
public sealed record GuardRailsMatch(
    GuardRailsRule Rule,
    string MatchedText,
    GuardRailsDirection Direction);
