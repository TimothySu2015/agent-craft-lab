namespace AgentCraftLab.Cleaner.Abstractions;

/// <summary>
/// 圖片處理模式 — 控制 Partitioner 如何處理文件內嵌圖片。
/// </summary>
public enum ImageProcessingMode
{
    /// <summary>跳過圖片（預設，向下相容）</summary>
    Skip,

    /// <summary>使用 OCR 辨識圖片中的文字</summary>
    Ocr,

    /// <summary>使用多模態 LLM 產生圖片語意描述</summary>
    AiDescribe,

    /// <summary>OCR 先做，信心度不足時 fallback 到 AI 描述</summary>
    Hybrid,
}
