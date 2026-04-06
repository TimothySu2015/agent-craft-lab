using System.Text.RegularExpressions;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Rules;

/// <summary>
/// 正規化空白字元 — 多個空白合併為一個，修剪前後空白。
/// 對應 Unstructured 的 clean_extra_whitespace。
/// </summary>
public sealed partial class CleanWhitespaceRule : ICleaningRule
{
    public string Name => "clean_whitespace";
    public int Order => 100;

    public bool ShouldApply(DocumentElement element) => true;

    public void Apply(DocumentElement element)
    {
        var text = element.Text;
        text = MultipleSpaces().Replace(text, " ");
        text = MultipleNewlines().Replace(text, "\n\n");
        element.Text = text.Trim();
    }

    [GeneratedRegex(@"[^\S\n]{2,}")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleNewlines();
}
