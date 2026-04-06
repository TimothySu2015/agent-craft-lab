using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Rules;

/// <summary>
/// 移除 Header、Footer、PageNumber 元素。
/// 這些通常是重複性的版面裝飾，對內容理解無幫助。
/// </summary>
public sealed class RemoveHeaderFooterFilter : IElementFilter
{
    public string Name => "remove_header_footer";

    private static readonly HashSet<ElementType> ExcludedTypes =
    [
        ElementType.Header,
        ElementType.Footer,
        ElementType.PageNumber,
        ElementType.PageBreak,
    ];

    public bool ShouldKeep(DocumentElement element) => !ExcludedTypes.Contains(element.Type);
}
