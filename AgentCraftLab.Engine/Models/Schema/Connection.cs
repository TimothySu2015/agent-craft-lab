namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// 節點連線 — 從來源節點 ID 指向目標節點 ID，附帶輸出 port 名稱。
/// Port 為字串以支援 Router / Condition 自訂命名（例如 "true" / "false" / "branch-name"）。
/// </summary>
public sealed record Connection
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string Port { get; init; } = "output_1";
}
