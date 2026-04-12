using System.ComponentModel;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// Human-in-the-loop 節點 — 暫停執行等待使用者輸入（text / choice / approval）。
/// </summary>
public sealed record HumanNode : NodeConfig
{
    [Description("顯示給使用者的提示訊息")]
    public string Prompt { get; init; } = "";

    [Description("輸入類型 — Text（自由文字）/ Choice（選單）/ Approval（是否）")]
    public HumanInputKind Kind { get; init; } = HumanInputKind.Text;

    [Description("當 Kind = Choice 時的選項清單")]
    public IReadOnlyList<string>? Choices { get; init; }

    [Description("等待逾時秒數（0 = 無限等待）")]
    public int TimeoutSeconds { get; init; }
}

public enum HumanInputKind
{
    Text,
    Choice,
    Approval
}
