using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Autonomous.Models;

/// <summary>
/// Autonomous Agent 執行請求 — 不需要 JSON workflow，只需目標和工具集。
/// </summary>
public record AutonomousRequest
{
    /// <summary>執行 ID — 用於檢查點追蹤與未來中斷恢復</summary>
    public string ExecutionId { get; init; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>使用者 ID（用於跨 Session 記憶隔離）</summary>
    public string UserId { get; init; } = "local";

    /// <summary>使用者的自然語言目標</summary>
    public required string Goal { get; init; }

    /// <summary>各 provider 的 API Key/Endpoint/Model 憑證</summary>
    public required Dictionary<string, ProviderCredential> Credentials { get; init; }

    /// <summary>Orchestrator Agent 使用的 provider（openai / azure-openai）</summary>
    public string Provider { get; init; } = "openai";

    /// <summary>Orchestrator Agent 使用的模型</summary>
    public string Model { get; init; } = "gpt-4o";

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

    /// <summary>Token 預算限制</summary>
    public TokenBudget Budget { get; init; } = new();

    /// <summary>工具呼叫次數限制</summary>
    public ToolCallLimits ToolLimits { get; init; } = new();

    /// <summary>最大 ReAct 迴圈次數</summary>
    public int MaxIterations { get; init; } = 25;

    /// <summary>附件（PDF/圖片）</summary>
    public FileAttachment? Attachment { get; init; }

    /// <summary>共享狀態初始值（key-value，供 sub-agent 協作使用）</summary>
    public Dictionary<string, string>? SharedStateInit { get; init; }

    /// <summary>風險管控設定（P0: Risk-based Human Override）</summary>
    public RiskConfig? Risk { get; init; }

    /// <summary>反思機制設定（P2: Self-Reflection / Auditor Agent）</summary>
    public ReflectionConfig? Reflection { get; init; }

    /// <summary>craft.md — 使用者自訂的 Agent 行為規範（已 sanitize，注入 system prompt）。</summary>
    public string? CraftMd { get; init; }
}

/// <summary>
/// Token 預算配置 — 防止成本失控。
/// </summary>
public record TokenBudget
{
    /// <summary>輸入 token 上限（0 = 無限制）</summary>
    public long MaxInputTokens { get; init; }

    /// <summary>輸出 token 上限（0 = 無限制）</summary>
    public long MaxOutputTokens { get; init; }

    /// <summary>總 token 上限（預設 200,000）</summary>
    public long MaxTotalTokens { get; init; } = 200_000;

    /// <summary>超過預算時的行為</summary>
    public BudgetExceededAction OnExceed { get; init; } = BudgetExceededAction.Stop;
}

public enum BudgetExceededAction
{
    /// <summary>立即停止執行</summary>
    Stop,

    /// <summary>發出警告事件但繼續</summary>
    Warn
}

/// <summary>
/// 工具呼叫次數限制 — 防止工具濫用。
/// </summary>
public record ToolCallLimits
{
    /// <summary>每個工具的呼叫上限（key: toolId, value: 最大次數）</summary>
    public Dictionary<string, int> PerToolLimits { get; init; } = [];

    /// <summary>所有工具的總呼叫次數上限（預設 50）</summary>
    public int MaxTotalCalls { get; init; } = 50;

    /// <summary>單一工具的預設上限（未在 PerToolLimits 指定時套用，預設 10）</summary>
    public int DefaultPerToolLimit { get; init; } = 10;
}

/// <summary>
/// ReAct 單步記錄 — 記錄 AI 的每一步思考和行動。
/// </summary>
public record ReactStep
{
    /// <summary>步驟序號（從 1 開始）</summary>
    public required int Sequence { get; init; }

    /// <summary>AI 的思考/推理</summary>
    public string Thought { get; init; } = "";

    /// <summary>選擇的行動（工具名稱，null 表示最終回答）</summary>
    public string? Action { get; init; }

    /// <summary>行動的輸入參數</summary>
    public string? ActionInput { get; init; }

    /// <summary>行動的觀察結果</summary>
    public string? Observation { get; init; }

    /// <summary>此步驟消耗的 token 數</summary>
    public TokenUsage Tokens { get; init; } = new();

    /// <summary>此步驟的耗時</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>時間戳</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Token 使用量記錄。
/// </summary>
public record TokenUsage
{
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// Autonomous 執行摘要 — 完整的執行審計記錄。
/// </summary>
public record AutonomousExecutionSummary
{
    /// <summary>執行 ID</summary>
    public string ExecutionId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>原始目標</summary>
    public required string Goal { get; init; }

    /// <summary>最終結果</summary>
    public string FinalAnswer { get; init; } = "";

    /// <summary>所有 ReAct 步驟</summary>
    public List<ReactStep> Steps { get; init; } = [];

    /// <summary>累計 token 使用量</summary>
    public TokenUsage TotalTokens { get; init; } = new();

    /// <summary>總耗時</summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>是否成功完成</summary>
    public bool Succeeded { get; init; }

    /// <summary>AI 提出的工具需求（現有工具不足時）</summary>
    public List<ToolRequest> ToolRequests { get; init; } = [];
}

/// <summary>
/// AI 提出的工具需求 — 當現有工具無法完成任務時，AI 回報需要什麼能力。
/// </summary>
public record ToolRequest
{
    /// <summary>需要的能力描述</summary>
    public required string Description { get; init; }

    /// <summary>為什麼需要</summary>
    public string Reason { get; init; } = "";

    /// <summary>建議的工具類型（search / data / code / communication / ...）</summary>
    public string SuggestedCategory { get; init; } = "";
}

// ═══════════════════════════════════════════
// P0: Risk-based Human Override
// ═══════════════════════════════════════════

/// <summary>
/// 風險管控設定 — 定義哪些工具需要人類審批。
/// </summary>
public record RiskConfig
{
    /// <summary>是否啟用風險管控</summary>
    public bool Enabled { get; init; }

    /// <summary>風險規則清單</summary>
    public List<RiskRule> Rules { get; init; } = [];
}

/// <summary>
/// 單一風險規則 — 用 regex 匹配工具名稱，觸發指定的風險等級。
/// </summary>
public record RiskRule
{
    /// <summary>工具名稱的 regex pattern（例如 ".*email.*|.*send.*"）</summary>
    public string ToolPattern { get; init; } = "";

    /// <summary>風險等級標籤</summary>
    public string RiskLevel { get; init; } = "high";

    /// <summary>觸發時的行為</summary>
    public RiskAction Action { get; init; } = RiskAction.RequireApproval;
}

public enum RiskAction
{
    /// <summary>需要人類審批才能執行</summary>
    RequireApproval,

    /// <summary>直接阻擋（不可核准）</summary>
    Block
}

// ═══════════════════════════════════════════
// P2: Self-Reflection / Auditor Agent
// ═══════════════════════════════════════════

/// <summary>
/// 反思機制設定 — 用獨立 Auditor LLM 審查最終結果。
/// </summary>
public record ReflectionConfig
{
    /// <summary>是否啟用反思</summary>
    public bool Enabled { get; init; }

    /// <summary>Auditor 使用的 provider</summary>
    public string Provider { get; init; } = "openai";

    /// <summary>Auditor 使用的模型（預設 gpt-4o-mini，成本低）</summary>
    public string Model { get; init; } = "gpt-4o-mini";

    /// <summary>最大修正次數</summary>
    public int MaxRevisions { get; init; } = 2;

    /// <summary>反思模式：Single（單一 Auditor）/ Panel（多角色評估面板）/ Auto（自動判斷）</summary>
    public ReflectionMode Mode { get; init; } = ReflectionMode.Single;

    /// <summary>自訂評估者 Persona 清單（null = 使用預設三角色面板）</summary>
    public List<EvaluatorPersona>? Personas { get; init; }

    /// <summary>是否使用 Judge 合成統一反饋（Panel 模式下有效）</summary>
    public bool UseJudge { get; init; }
}

/// <summary>反思模式。</summary>
public enum ReflectionMode
{
    /// <summary>單一 Auditor（現有行為）。</summary>
    Single,
    /// <summary>多角色評估面板（3+ Evaluator 平行 + 投票聚合）。</summary>
    Panel,
    /// <summary>自動判斷（簡單任務 Single，複雜任務 Panel）。</summary>
    Auto
}

/// <summary>
/// 評估者角色定義 — 每個 Persona 從不同角度審查答案。
/// </summary>
public record EvaluatorPersona
{
    /// <summary>角色名稱（如 "Factual Auditor"）</summary>
    public string Name { get; init; } = "";
    /// <summary>角色系統提示（定義審查角度）</summary>
    public string SystemPrompt { get; init; } = "";
    /// <summary>投票權重（預設 1.0）</summary>
    public float Weight { get; init; } = 1.0f;
}

/// <summary>
/// 稽核結果。
/// </summary>
public record AuditResult
{
    public AuditVerdict Verdict { get; init; }
    public string Explanation { get; init; } = "";
    public List<string> Issues { get; init; } = [];
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }

    /// <summary>各評估者的個別判定（Panel 模式下填充）。</summary>
    public List<EvaluatorVerdict>? EvaluatorVerdicts { get; init; }
}

/// <summary>個別評估者的判定結果。</summary>
public record EvaluatorVerdict
{
    public string PersonaName { get; init; } = "";
    public AuditVerdict Verdict { get; init; }
    public string Explanation { get; init; } = "";
    public List<string> Issues { get; init; } = [];
}

public enum AuditVerdict
{
    Pass,
    Contradiction,
    NeedsRevision
}
