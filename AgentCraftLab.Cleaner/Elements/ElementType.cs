namespace AgentCraftLab.Cleaner.Elements;

/// <summary>
/// 文件元素類型（參考 Unstructured.io Element Types）。
/// Partition 階段將原始文件拆解為帶類型的元素，下游可依類型過濾或套用不同清洗規則。
/// </summary>
public enum ElementType
{
    /// <summary>標題 / 章節標題</summary>
    Title,

    /// <summary>段落文字（正文）</summary>
    NarrativeText,

    /// <summary>清單項目</summary>
    ListItem,

    /// <summary>表格</summary>
    Table,

    /// <summary>圖片（含 OCR 結果）</summary>
    Image,

    /// <summary>頁首</summary>
    Header,

    /// <summary>頁尾</summary>
    Footer,

    /// <summary>頁碼</summary>
    PageNumber,

    /// <summary>數學公式</summary>
    Formula,

    /// <summary>程式碼區塊</summary>
    CodeSnippet,

    /// <summary>地址</summary>
    Address,

    /// <summary>Email 地址</summary>
    EmailAddress,

    /// <summary>圖片標題說明</summary>
    FigureCaption,

    /// <summary>表單鍵值對</summary>
    FormKeyValue,

    /// <summary>無法分類的文字</summary>
    UncategorizedText,

    /// <summary>分頁符號</summary>
    PageBreak
}
