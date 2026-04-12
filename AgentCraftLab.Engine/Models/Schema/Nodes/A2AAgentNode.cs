using System.ComponentModel;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// 遠端 A2A Agent 節點 — 透過 Agent-to-Agent 協定呼叫外部 agent。
/// </summary>
public sealed record A2AAgentNode : NodeConfig
{
    [Description("遠端 A2A agent 的 endpoint URL")]
    public string Url { get; init; } = "";

    [Description("A2A 協定格式 — Auto（自動偵測）/ Google / Microsoft")]
    public A2AFormat Format { get; init; } = A2AFormat.Auto;

    [Description("傳遞給遠端 agent 的指示（可含變數引用）")]
    public string Instructions { get; init; } = "";
}

public enum A2AFormat
{
    Auto,
    Google,
    Microsoft
}
