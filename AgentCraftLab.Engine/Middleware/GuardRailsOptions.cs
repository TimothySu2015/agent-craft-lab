namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// GuardRails Middleware 的執行期選項。
/// </summary>
public sealed record GuardRailsOptions
{
    /// <summary>是否掃描所有 User 訊息（預設 true）。false 時只掃描最後一則（向下相容）。</summary>
    public bool ScanAllMessages { get; init; } = true;

    /// <summary>是否掃描 LLM 回應（預設 false）。</summary>
    public bool ScanOutput { get; init; }

    /// <summary>封鎖時的回應訊息。</summary>
    public string BlockedResponse { get; init; } = "Sorry, this request cannot be processed due to content policy.";

    /// <summary>
    /// 從前端 config dictionary 建立選項。
    /// </summary>
    public static GuardRailsOptions FromConfig(Dictionary<string, string>? config)
    {
        if (config is null || config.Count == 0)
        {
            return new GuardRailsOptions();
        }

        var options = new GuardRailsOptions();

        if (config.TryGetValue("scanAllMessages", out var scanAll))
        {
            options = options with { ScanAllMessages = !string.Equals(scanAll, "false", StringComparison.OrdinalIgnoreCase) };
        }

        if (config.TryGetValue("scanOutput", out var scanOut))
        {
            options = options with { ScanOutput = string.Equals(scanOut, "true", StringComparison.OrdinalIgnoreCase) };
        }

        if (config.TryGetValue("blockedResponse", out var response) && !string.IsNullOrWhiteSpace(response))
        {
            options = options with { BlockedResponse = response };
        }

        return options;
    }
}
