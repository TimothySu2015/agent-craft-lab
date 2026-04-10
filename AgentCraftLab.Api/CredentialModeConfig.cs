namespace AgentCraftLab.Api;

/// <summary>
/// Credential 儲存模式設定。
/// "database" = Key 存 DB（DataProtection 加密），適用自建平台。
/// "browser" = Key 只存瀏覽器 sessionStorage，適用公開 Demo。
/// </summary>
public record CredentialModeConfig(string Mode)
{
    public bool IsBrowserMode => Mode == "browser";
}
