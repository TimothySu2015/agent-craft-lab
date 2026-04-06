namespace AgentCraftLab.Engine.Pii;

/// <summary>
/// PII 規則的地區分組。使用者可選擇啟用特定地區以減少誤判。
/// </summary>
public enum PiiLocale
{
    /// <summary>通用規則（Email、IP、URL、信用卡、IBAN、加密貨幣、MAC Address）。</summary>
    Global,

    /// <summary>台灣（身分證、電話、統一編號、健保卡、郵遞區號、地址）。</summary>
    TW,

    /// <summary>日本（My Number、電話、護照、駕照、郵便番號、法人番號）。</summary>
    JP,

    /// <summary>韓國（住民登錄番號、電話、護照、駕照、事業者登錄番號）。</summary>
    KR,

    /// <summary>美國（SSN、電話、護照、駕照、TIN/EIN）。</summary>
    US,

    /// <summary>英國（NHS、NINO、護照、Postcode）。</summary>
    UK,
}
