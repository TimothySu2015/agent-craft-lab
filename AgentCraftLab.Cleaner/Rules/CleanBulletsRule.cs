using System.Text.RegularExpressions;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Rules;

/// <summary>
/// 移除行首的 bullet 符號（•、○、■、►、–、* 等）。
/// 對應 Unstructured 的 clean_bullets。
/// </summary>
public sealed partial class CleanBulletsRule : ICleaningRule
{
    public string Name => "clean_bullets";
    public int Order => 200;

    public bool ShouldApply(DocumentElement element) =>
        element.Type is ElementType.ListItem or ElementType.NarrativeText;

    public void Apply(DocumentElement element)
    {
        element.Text = BulletPattern().Replace(element.Text, "").TrimStart();
    }

    [GeneratedRegex(@"^[\s]*[•○●■□►▸▹–—\-\*]\s*", RegexOptions.Multiline)]
    private static partial Regex BulletPattern();
}
