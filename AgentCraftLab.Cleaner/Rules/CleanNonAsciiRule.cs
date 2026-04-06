using System.Text.RegularExpressions;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Rules;

/// <summary>
/// 移除非 ASCII 的控制字元（保留 CJK、標點等正常 Unicode）。
/// 對應 Unstructured 的 clean_non_ascii_chars，但 CJK 友善版。
/// </summary>
public sealed partial class CleanNonAsciiRule : ICleaningRule
{
    public string Name => "clean_non_ascii_control";
    public int Order => 50;

    public bool ShouldApply(DocumentElement element) => true;

    public void Apply(DocumentElement element)
    {
        // 只移除 Unicode 控制字元（C0/C1），保留所有可見字元（含 CJK）
        element.Text = ControlChars().Replace(element.Text, "");
    }

    // 匹配 Unicode 控制字元，排除 \n \r \t
    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F\x80-\x9F]")]
    private static partial Regex ControlChars();
}
