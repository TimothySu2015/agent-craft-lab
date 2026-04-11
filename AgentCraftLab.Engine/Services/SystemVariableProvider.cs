namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 系統變數生成器 — 提供 {{sys:name}} 可引用的唯讀變數。
/// </summary>
public static class SystemVariableProvider
{
    /// <summary>允許透過 {{env:}} 存取的環境變數前綴（安全 allowlist）。</summary>
    private const string EnvPrefix = "AGENTCRAFTLAB_";

    /// <summary>
    /// 建立系統變數字典。timestamp 反映當下時間，其餘為執行期間不變的值。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Build(
        string userId, string executionId, string workflowName, string userMessage)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["user_id"] = userId,
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            ["execution_id"] = executionId,
            ["workflow_name"] = workflowName,
            ["user_message"] = userMessage,
        };
    }

    /// <summary>
    /// 建立環境變數字典 — 只載入 AGENTCRAFTLAB_ 前綴的環境變數。
    /// 引用時去除前綴：AGENTCRAFTLAB_API_URL → {{env:API_URL}}。
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildEnvironmentVariables()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Environment.GetEnvironmentVariables())
        {
            if (entry is System.Collections.DictionaryEntry de &&
                de.Key is string key && key.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase) &&
                de.Value is string value)
            {
                result[key[EnvPrefix.Length..]] = value;
            }
        }
        return result;
    }
}
