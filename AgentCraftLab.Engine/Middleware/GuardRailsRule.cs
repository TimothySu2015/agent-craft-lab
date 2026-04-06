namespace AgentCraftLab.Engine.Middleware;

/// <summary>規則觸發時的動作。</summary>
public enum GuardRailsAction
{
    /// <summary>封鎖請求，回傳拒絕訊息。</summary>
    Block,

    /// <summary>記錄警告但允許通過。</summary>
    Warn,

    /// <summary>靜默記錄（僅 Information 等級）。</summary>
    Log,
}

/// <summary>
/// 單條 GuardRails 規則定義。
/// </summary>
/// <param name="Pattern">關鍵字或正則表達式。</param>
/// <param name="IsRegex">true 時以 Regex 匹配，false 時以 Contains 匹配。</param>
/// <param name="Action">匹配時的動作。</param>
/// <param name="Label">人類可讀標籤（用於審計日誌，預設為 Pattern 本身）。</param>
public sealed record GuardRailsRule(
    string Pattern,
    bool IsRegex,
    GuardRailsAction Action,
    string? Label = null);
