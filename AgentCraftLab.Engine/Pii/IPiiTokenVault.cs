namespace AgentCraftLab.Engine.Pii;

/// <summary>
/// PII 可逆 Token 保管庫介面。將 PII 原始值替換為型別化 token（如 [EMAIL_1]），
/// 並可在 LLM 回應後還原。實作可替換為 Redis、加密 DB 等。
/// </summary>
public interface IPiiTokenVault
{
    /// <summary>
    /// 將 PII 值存入保管庫，回傳型別化 token（如 [EMAIL_1]）。
    /// 相同 session + 相同 value 會回傳相同 token。
    /// </summary>
    /// <param name="sessionKey">對話/請求的唯一識別碼，用於隔離不同對話的 token。</param>
    /// <param name="originalValue">PII 原始值。</param>
    /// <param name="type">PII 實體類型。</param>
    /// <returns>型別化 token 字串。</returns>
    string Tokenize(string sessionKey, string originalValue, PiiEntityType type);

    /// <summary>
    /// 將文字中的所有 token 還原為原始值。
    /// </summary>
    /// <param name="sessionKey">對話/請求的唯一識別碼。</param>
    /// <param name="text">包含 token 的文字。</param>
    /// <returns>還原後的文字。</returns>
    string Detokenize(string sessionKey, string text);

    /// <summary>
    /// 清除指定 session 的所有映射（如對話結束時呼叫）。
    /// </summary>
    /// <param name="sessionKey">對話/請求的唯一識別碼。</param>
    void ClearSession(string sessionKey);
}
