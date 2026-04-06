namespace AgentCraftLab.Engine.Pii;

/// <summary>
/// 偵測到的 PII 實體，包含位置、類型與信賴度。
/// </summary>
public sealed record PiiEntity
{
    /// <summary>PII 實體類型。</summary>
    public required PiiEntityType Type { get; init; }

    /// <summary>來源地區（Global/TW/JP/KR/US/UK）。</summary>
    public required PiiLocale Locale { get; init; }

    /// <summary>人類可讀標籤（如 "Email"、"台灣身分證"）。</summary>
    public required string Label { get; init; }

    /// <summary>在原始文字中的起始位置（字元索引）。</summary>
    public required int Start { get; init; }

    /// <summary>匹配文字的長度。</summary>
    public required int Length { get; init; }

    /// <summary>匹配到的原始文字。</summary>
    public required string Text { get; init; }

    /// <summary>信賴度分數（0.0 ~ 1.0）。</summary>
    public required double Confidence { get; init; }
}
