using System.Text;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Rules;

/// <summary>
/// Unicode 正規化 — 統一全形/半形標點、替換特殊引號等。
/// 對應 Unstructured 的 replace_unicode_quotes 等。
/// </summary>
public sealed class UnicodeNormalizeRule : ICleaningRule
{
    public string Name => "unicode_normalize";
    public int Order => 30;

    private static readonly (string From, string To)[] Replacements =
    [
        // 特殊引號 → 標準引號
        ("\u201c", "\""), // "
        ("\u201d", "\""), // "
        ("\u2018", "'"),  // '
        ("\u2019", "'"),  // '
        ("\u00ab", "\""), // «
        ("\u00bb", "\""), // »
        // 特殊空格 → 標準空格
        ("\u00a0", " "),  // NBSP
        ("\u2003", " "),  // EM SPACE
        ("\u2002", " "),  // EN SPACE
        ("\u200b", ""),   // ZERO WIDTH SPACE
        ("\ufeff", ""),   // BOM
    ];

    public bool ShouldApply(DocumentElement element) => true;

    public void Apply(DocumentElement element)
    {
        var text = element.Text;

        // NFC 正規化（組合字元統一）
        if (!text.IsNormalized(NormalizationForm.FormC))
        {
            text = text.Normalize(NormalizationForm.FormC);
        }

        foreach (var (from, to) in Replacements)
        {
            text = text.Replace(from, to);
        }

        element.Text = text;
    }
}
