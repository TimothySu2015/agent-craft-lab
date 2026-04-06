namespace AgentCraftLab.Engine.Pii;

/// <summary>
/// PII 實體類型。涵蓋 GDPR/HIPAA/PCI-DSS 常見的個人識別資訊分類。
/// </summary>
public enum PiiEntityType
{
    /// <summary>電子郵件地址。</summary>
    Email,

    /// <summary>本地格式電話號碼。</summary>
    Phone,

    /// <summary>國際格式電話號碼（E.164）。</summary>
    InternationalPhone,

    /// <summary>美國社會安全號碼。</summary>
    Ssn,

    /// <summary>信用卡號碼（PCI-DSS）。</summary>
    CreditCard,

    /// <summary>國民身分證字號（台灣、韓國等）。</summary>
    IdCard,

    /// <summary>實體地址。</summary>
    Address,

    /// <summary>國際銀行帳號（IBAN）。</summary>
    Iban,

    /// <summary>IPv4 位址。</summary>
    IpAddress,

    /// <summary>網址（URL）。</summary>
    Url,

    /// <summary>護照號碼。</summary>
    Passport,

    /// <summary>駕照號碼。</summary>
    DriverLicense,

    /// <summary>加密貨幣錢包地址（BTC/ETH）。</summary>
    CryptoWallet,

    /// <summary>出生日期。</summary>
    DateOfBirth,

    /// <summary>醫療識別碼（健保卡、NHS 等）。</summary>
    MedicalId,

    /// <summary>稅務編號（統一編號、TIN 等）。</summary>
    TaxId,

    /// <summary>銀行帳號。</summary>
    BankAccount,

    /// <summary>郵遞區號。</summary>
    PostalCode,

    /// <summary>MAC 位址。</summary>
    MacAddress,

    /// <summary>人名（需 NER 模型，Regex 僅限 context pattern）。</summary>
    Name,

    /// <summary>使用者自訂規則。</summary>
    Custom,
}
