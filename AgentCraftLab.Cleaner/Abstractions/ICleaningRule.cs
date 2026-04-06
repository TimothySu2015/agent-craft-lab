using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Abstractions;

/// <summary>
/// 清洗規則 — 對單一 DocumentElement 做文字清洗。
/// 參考 Unstructured 的 cleaning functions（str → str），
/// 但提升為 Element-aware（可依元素類型決定是否套用）。
/// </summary>
public interface ICleaningRule
{
    /// <summary>規則名稱（用於 logging 和設定）</summary>
    string Name { get; }

    /// <summary>執行順序（數字越小越先執行）</summary>
    int Order { get; }

    /// <summary>判斷此規則是否應套用到該元素</summary>
    bool ShouldApply(DocumentElement element);

    /// <summary>清洗元素的文字內容（就地修改 element.Text）</summary>
    void Apply(DocumentElement element);
}
