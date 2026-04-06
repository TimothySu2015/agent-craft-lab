using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Abstractions;

/// <summary>
/// 元素過濾器 — 決定哪些元素應該保留或移除。
/// 例如：移除 Header/Footer、移除空白元素、只保留特定類型。
/// </summary>
public interface IElementFilter
{
    /// <summary>過濾器名稱</summary>
    string Name { get; }

    /// <summary>判斷該元素是否應保留（true=保留，false=移除）</summary>
    bool ShouldKeep(DocumentElement element);
}
