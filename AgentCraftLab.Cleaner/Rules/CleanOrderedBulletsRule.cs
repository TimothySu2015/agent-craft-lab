using System.Text.RegularExpressions;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Rules;

/// <summary>
/// 移除行首的有序編號（1. / 1) / (1) / a. / i. 等，最多三層子編號）。
/// 對應 Unstructured 的 clean_ordered_bullets。
/// </summary>
public sealed partial class CleanOrderedBulletsRule : ICleaningRule
{
    public string Name => "clean_ordered_bullets";
    public int Order => 210;

    public bool ShouldApply(DocumentElement element) =>
        element.Type is ElementType.ListItem or ElementType.NarrativeText;

    public void Apply(DocumentElement element)
    {
        element.Text = OrderedBulletPattern().Replace(element.Text, "").TrimStart();
    }

    // 匹配：1. / 1) / (1) / a. / a) / (a) / i. / i) / (i) / 1.2.3. 等
    [GeneratedRegex(@"^[\s]*(?:\(?[0-9a-zA-Z]+[\.\)]\s*){1,3}", RegexOptions.Multiline)]
    private static partial Regex OrderedBulletPattern();
}
