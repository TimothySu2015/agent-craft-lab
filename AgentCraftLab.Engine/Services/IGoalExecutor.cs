using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 目標執行器核心抽象 — 接收目標，串流回執行事件。
/// 實作者決定執行策略（ReAct 自由模式 / Flow 結構化模式）。
/// 透過 DI 切換實作，消費端（Playground、Engine Autonomous 節點）不需知道具體實作。
/// </summary>
public interface IGoalExecutor
{
    IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        GoalExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 目標執行共用請求模型 — IGoalExecutor 的輸入。
/// 涵蓋 ReAct / Flow 兩種模式的共用欄位，實作特有設定透過 Options 字典傳遞。
/// </summary>
public record GoalExecutionRequest
{
    /// <summary>執行 ID — 用於追蹤與記錄</summary>
    public string ExecutionId { get; init; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>使用者 ID（用於記憶隔離）</summary>
    public string UserId { get; init; } = "local";

    /// <summary>使用者的自然語言目標</summary>
    public required string Goal { get; init; }

    /// <summary>各 provider 的 API Key/Endpoint/Model 憑證</summary>
    public required Dictionary<string, ProviderCredential> Credentials { get; init; }

    /// <summary>LLM Provider（openai / azure-openai / ollama ...）</summary>
    public string Provider { get; init; } = Defaults.Provider;

    /// <summary>LLM 模型名稱</summary>
    public string Model { get; init; } = Defaults.Model;

    /// <summary>可用的內建工具 ID 清單</summary>
    public List<string> AvailableTools { get; init; } = [];

    /// <summary>可用的 Skill ID 清單</summary>
    public List<string> AvailableSkills { get; init; } = [];

    /// <summary>可用的 MCP Server URL 清單</summary>
    public List<string> McpServers { get; init; } = [];

    /// <summary>可用的 A2A Agent URL 清單</summary>
    public List<string> A2AAgents { get; init; } = [];

    /// <summary>可用的 HTTP API 定義</summary>
    public Dictionary<string, HttpApiDefinition> HttpApis { get; init; } = [];

    /// <summary>最大迴圈/步驟次數</summary>
    public int MaxIterations { get; init; } = 25;

    /// <summary>總 Token 上限</summary>
    public long MaxTotalTokens { get; init; } = 200_000;

    /// <summary>工具呼叫總次數上限</summary>
    public int MaxToolCalls { get; init; } = 50;

    /// <summary>附件（PDF/圖片）</summary>
    public FileAttachment? Attachment { get; init; }

    /// <summary>
    /// 實作特有的擴展配置 — 各 IGoalExecutor 實作自行解讀。
    /// ReactExecutor：Risk, Reflection, SharedStateInit 等。
    /// FlowExecutor：Crystallize 設定、Node 偏好等。
    /// </summary>
    public Dictionary<string, object>? Options { get; init; }

    /// <summary>
    /// 從 AutonomousNodeRequest 建立 GoalExecutionRequest（消除 Adapter 重複映射）。
    /// </summary>
    public static GoalExecutionRequest FromNodeRequest(AutonomousNodeRequest request) => new()
    {
        Goal = request.Goal,
        Credentials = request.Credentials,
        Provider = request.Provider,
        Model = request.Model,
        AvailableTools = request.AvailableTools,
        AvailableSkills = request.AvailableSkills,
        McpServers = request.McpServers,
        A2AAgents = request.A2AAgents,
        HttpApis = request.HttpApis,
        MaxIterations = request.MaxIterations,
        MaxTotalTokens = request.MaxTotalTokens,
        MaxToolCalls = request.MaxToolCalls,
        Attachment = request.Attachment
    };
}
