using System.Text.RegularExpressions;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Rules;

/// <summary>
/// 移除裝飾性破折號（連續的 -、—、─ 等）。
/// 對應 Unstructured 的 clean_dashes。
/// </summary>
public sealed partial class CleanDashesRule : ICleaningRule
{
    public string Name => "clean_dashes";
    public int Order => 300;

    public bool ShouldApply(DocumentElement element) => true;

    public void Apply(DocumentElement element)
    {
        element.Text = DashPattern().Replace(element.Text, "");
    }

    // 連續 3 個以上的各種破折號字元
    [GeneratedRegex(@"[\-\u2013\u2014\u2015\u2500]{3,}")]
    private static partial Regex DashPattern();
}
