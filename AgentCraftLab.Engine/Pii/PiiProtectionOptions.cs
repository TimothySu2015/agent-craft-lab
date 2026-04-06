namespace AgentCraftLab.Engine.Pii;

/// <summary>
/// PII 保護的 DI 註冊選項。
/// </summary>
public sealed class PiiProtectionOptions
{
    /// <summary>啟用的地區規則（預設 Global + TW）。</summary>
    public List<PiiLocale> EnabledLocales { get; set; } = [PiiLocale.Global, PiiLocale.TW];

    /// <summary>自訂規則（key=Label, value=regex pattern）。</summary>
    public Dictionary<string, string>? CustomRules { get; set; }

    /// <summary>Token 保管庫的 TTL（預設 1 小時）。</summary>
    public TimeSpan TokenTtl { get; set; } = TimeSpan.FromHours(1);
}
