using System.ComponentModel;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// 本地 LLM Agent 節點 — 使用 ChatClientAgent + 工具集執行一輪對話。
/// </summary>
public sealed record AgentNode : NodeConfig
{
    [Description("Agent 系統提示詞，可含 {{node:}} / {{var:}} / {{env:}} / {{sys:}} 引用")]
    public string Instructions { get; init; } = "";

    [Description("LLM 模型設定 — provider / model / temperature / topP / maxOutputTokens")]
    public ModelConfig Model { get; init; } = new();

    [Description("內建工具 ID 清單（參考 ToolRegistryService）")]
    public IReadOnlyList<string> Tools { get; init; } = [];

    [Description("要掛載的 MCP server 名稱清單（對應 WorkflowResources.McpServers）")]
    public IReadOnlyList<string> McpServers { get; init; } = [];

    [Description("要呼叫的 A2A agent 名稱清單")]
    public IReadOnlyList<string> A2AAgents { get; init; } = [];

    [Description("要使用的 HTTP API ID 清單（對應 WorkflowResources.HttpApis）")]
    public IReadOnlyList<string> HttpApis { get; init; } = [];

    [Description("要載入的 Skill 名稱清單")]
    public IReadOnlyList<string> Skills { get; init; } = [];

    [Description("輸出格式設定 — Text / Json / JsonSchema")]
    public OutputConfig Output { get; init; } = new();

    [Description("歷史訊息設定 — provider / maxMessages")]
    public HistoryConfig History { get; init; } = new();

    [Description("Agent 層級的 middleware 綁定（覆蓋 workflow 層級）")]
    public IReadOnlyList<MiddlewareBinding> Middleware { get; init; } = [];
}
