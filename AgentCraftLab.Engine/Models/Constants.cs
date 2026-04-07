using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentCraftLab.Engine.Models;

/// <summary>
/// 節點類型常數。
/// </summary>
public static class NodeTypes
{
    public const string Agent = "agent";
    public const string Condition = "condition";
    public const string Loop = "loop";
    public const string Router = "router";
    public const string Start = "start";
    public const string End = "end";

    public const string Rag = "rag";
    public const string A2AAgent = "a2a-agent";
    public const string Human = "human";
    public const string Code = "code";
    public const string Iteration = "iteration";
    public const string Parallel = "parallel";
    public const string HttpRequest = "http-request";
    public const string Autonomous = "autonomous";
}

/// <summary>
/// 節點類型 metadata — 集中定義各節點的特性，新增節點類型只需在此 registry 加一行。
/// 消除分散在 Preprocessor / StrategyResolver / GraphHelper / ImperativeStrategy 中的硬編碼 type 檢查。
/// </summary>
public record NodeTypeInfo(
    string Type,
    bool IsExecutable = false,
    bool RequiresImperative = false,
    bool IsAgentLike = false,
    bool IsMeta = false,
    bool IsDataNode = false);

/// <summary>
/// 節點類型 registry — 唯一真相來源。
/// 新增節點：(1) NodeTypes 加常數 (2) 這裡加一行 (3) ImperativeStrategy/NodeExecutorRegistry 加 handler。
/// </summary>
public static class NodeTypeRegistry
{
    private static readonly Dictionary<string, NodeTypeInfo> Registry = new(StringComparer.OrdinalIgnoreCase)
    {
        [NodeTypes.Agent]      = new(NodeTypes.Agent,      IsExecutable: true, IsAgentLike: true),
        [NodeTypes.A2AAgent]   = new(NodeTypes.A2AAgent,   IsExecutable: true, IsAgentLike: true, RequiresImperative: true),
        [NodeTypes.Autonomous] = new(NodeTypes.Autonomous, IsExecutable: true, IsAgentLike: true, RequiresImperative: true),
        [NodeTypes.Condition]  = new(NodeTypes.Condition,  IsExecutable: true, RequiresImperative: true),
        [NodeTypes.Loop]       = new(NodeTypes.Loop,       IsExecutable: true, RequiresImperative: true),
        [NodeTypes.Router]     = new(NodeTypes.Router,     IsExecutable: true, RequiresImperative: true),
        [NodeTypes.Human]      = new(NodeTypes.Human,      IsExecutable: true, RequiresImperative: true),
        [NodeTypes.Code]       = new(NodeTypes.Code,       IsExecutable: true, RequiresImperative: true),
        [NodeTypes.Iteration]  = new(NodeTypes.Iteration,  IsExecutable: true, RequiresImperative: true),
        [NodeTypes.Parallel]   = new(NodeTypes.Parallel,   IsExecutable: true, RequiresImperative: true),
        [NodeTypes.HttpRequest] = new(NodeTypes.HttpRequest, IsExecutable: true, RequiresImperative: true),
        [NodeTypes.Start]      = new(NodeTypes.Start,      IsMeta: true),
        [NodeTypes.End]        = new(NodeTypes.End,        IsMeta: true),
        [NodeTypes.Rag]        = new(NodeTypes.Rag,        IsDataNode: true),
    };

    public static NodeTypeInfo? Get(string type) => Registry.GetValueOrDefault(type);
    public static bool IsExecutable(string type) => Registry.TryGetValue(type, out var info) && info.IsExecutable;
    public static bool RequiresImperative(string type) => Registry.TryGetValue(type, out var info) && info.RequiresImperative;
    public static bool IsAgentLike(string type) => Registry.TryGetValue(type, out var info) && info.IsAgentLike;
    public static bool IsMeta(string type) => Registry.TryGetValue(type, out var info) && info.IsMeta;
    public static bool HasAnyRequiringImperative(IEnumerable<WorkflowNode> nodes) => nodes.Any(n => RequiresImperative(n.Type));
    public static bool HasAnyExecutable(IEnumerable<WorkflowNode> nodes) => nodes.Any(n => IsExecutable(n.Type));
}

/// <summary>
/// Workflow 執行模式常數。
/// </summary>
public static class WorkflowTypes
{
    public const string Auto = "auto";
    public const string Sequential = "sequential";
    public const string Concurrent = "concurrent";
    public const string Handoff = "handoff";
    public const string Imperative = "imperative";
}

/// <summary>
/// 節點輸出埠常數。
/// </summary>
public static class OutputPorts
{
    public const string Output1 = "output_1";
    public const string Output2 = "output_2";
}

/// <summary>
/// LLM Provider 常數。
/// </summary>
public static class Providers
{
    public const string OpenAI = "openai";
    public const string AzureOpenAI = "azure-openai";
    public const string Ollama = "ollama";
    public const string Foundry = "foundry";
    public const string GitHubCopilot = "github-copilot";
    public const string Anthropic = "anthropic";
    public const string AwsBedrock = "aws-bedrock";

    /// <summary>
    /// 所有 Provider 的顯示名稱與預設模型清單（唯一真相來源）。
    /// Agent Node 和 AI Build 共用此定義。
    /// </summary>
    public static readonly Dictionary<string, (string Label, string[] Models)> Catalog = new()
    {
        [OpenAI] = ("OpenAI", ["gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini", "gpt-4.1-nano", "o3-mini"]),
        [AzureOpenAI] = ("Azure OpenAI", ["gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini"]),
        [Ollama] = ("Ollama", ["gemma4:e4b", "llama3.3", "phi4", "mistral", "gemma2", "qwen2.5", "deepseek-r1"]),
        [Foundry] = ("Microsoft Foundry", ["gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini"]),
        [GitHubCopilot] = ("GitHub Copilot", ["gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini", "o3-mini"]),
        [Anthropic] = ("Anthropic Claude", ["claude-sonnet-4-20250514", "claude-opus-4-20250514", "claude-haiku-4-5-20251001"]),
        [AwsBedrock] = ("AWS Bedrock", ["anthropic.claude-sonnet-4-20250514-v1:0", "anthropic.claude-opus-4-20250514-v1:0", "amazon.nova-pro-v1:0"]),
    };

    /// <summary>取得 Provider 的預設模型。</summary>
    public static string GetDefaultModel(string provider) =>
        Catalog.TryGetValue(provider, out var entry) ? entry.Models[0] : Defaults.Model;

    /// <summary>key-optional Provider 的預設 API Key（OpenAI SDK 需要非空值）。</summary>
    public const string DefaultLocalApiKey = "local";

    /// <summary>不需要 API Key 的本地推理 Provider（OpenAI 相容端點）。</summary>
    private static readonly HashSet<string> KeyOptionalProviders =
        [Ollama, "lm-studio", "localai", "vllm", "llamacpp", "jan"];

    /// <summary>需要 /v1 路徑補正的 Provider（OpenAI SDK 2.x 不會自動加 /v1）。</summary>
    private static readonly HashSet<string> V1PrefixProviders =
        [Ollama, "lm-studio", "localai", "vllm", "llamacpp", "jan"];

    /// <summary>API Key 非必填的 Provider（如 Ollama、LM Studio 等本地推理伺服器）。</summary>
    public static bool IsKeyOptional(string provider) => KeyOptionalProviders.Contains(provider);

    /// <summary>需要 /v1 路徑補正的 Provider。</summary>
    public static bool RequiresV1Prefix(string provider) => V1PrefixProviders.Contains(provider);
}

/// <summary>
/// 預設值常數。
/// </summary>
public static class Defaults
{
    public const string Model = "gpt-4o";
    public const string JudgeModel = "gpt-4o-mini";
    public const string Provider = "openai";
    public const int MaxMessages = 20;
    public const int MaxIterations = 5;
    public const int MaxTurns = 10;
    public const int TruncateLength = 200;
    public const int ChunkPreviewLength = 150;
    public const int ErrorTruncateLength = 300;
    public const int MaxConsoleLogs = 200;
    public const int DefaultChunkSize = 1000;
    public const int DefaultChunkOverlap = 100;
    public const int DefaultTopK = 5;
    public const string DefaultEmbeddingModel = "text-embedding-3-small";
    public const int EmbeddingDimensions = 1536;
    public const int EmbeddingBatchSize = 100;

    /// <summary>
    /// 根據 embedding 模型名稱回傳對應的向量維度。
    /// text-embedding-3-large 原生 3072 維，其他模型預設 1536 維。
    /// </summary>
    public static int GetEmbeddingDimensions(string? modelName) => modelName switch
    {
        "text-embedding-3-large" => 3072,
        _ => 1536
    };
}

/// <summary>
/// 超時常數（秒）。
/// </summary>
public static class Timeouts
{
    public const int DiscoverySeconds = 10;
    public const int ToolCallSeconds = 30;
    public const int A2AAgentSeconds = 300;
    public const int HttpApiSeconds = 15;
    public const int SearchSeconds = 12;
    public const int LlmNetworkTimeoutMinutes = 5;
}

/// <summary>
/// 暫存路徑常數。
/// </summary>
public static class TempPaths
{
    public const string ZipFolder = "AgentCraftLab_ZIP";
}

/// <summary>
/// A2A Task 狀態常數。
/// </summary>
public static class TaskStates
{
    public const string Submitted = "submitted";
    public const string Working = "working";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
}

/// <summary>
/// A2A 協定常數。
/// </summary>
public static class A2AProtocol
{
    public const string JsonRpcVersion = "2.0";
    public const string MethodSend = "message/send";
    public const string MethodSendStreaming = "message/sendStreaming";

    // JSON-RPC 錯誤碼
    public const int ParseError = -32700;
    public const int MethodNotFound = -32601;
    public const int InternalError = -32603;
}

/// <summary>
/// MCP 協定常數。
/// </summary>
public static class McpProtocol
{
    public const string Version = "2025-03-26";
}

/// <summary>
/// Workflow 發布類型常數。
/// </summary>
public static class PublishTypes
{
    public const string A2A = "a2a";
    public const string Mcp = "mcp";
    public const string Api = "api";
    public const string Teams = "teams";
    public const string General = "general";
}

/// <summary>
/// 共用 JSON 序列化選項。
/// </summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions A2AOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

/// <summary>
/// 執行事件類型常數。
/// </summary>
public static class EventTypes
{
    /// <summary>Agent 開始執行（含 agent 名稱）</summary>
    public const string AgentStarted = "AgentStarted";
    /// <summary>串流文字片段（即時輸出）</summary>
    public const string TextChunk = "TextChunk";
    /// <summary>Agent 執行完成（含完整回應文字）</summary>
    public const string AgentCompleted = "AgentCompleted";
    /// <summary>工具呼叫（含工具名稱和參數）</summary>
    public const string ToolCall = "ToolCall";
    /// <summary>工具回傳結果</summary>
    public const string ToolResult = "ToolResult";
    /// <summary>整個 Workflow 執行完成</summary>
    public const string WorkflowCompleted = "WorkflowCompleted";
    /// <summary>錯誤事件</summary>
    public const string Error = "Error";
    /// <summary>使用者訊息（多輪對話）</summary>
    public const string UserMessage = "UserMessage";
    /// <summary>RAG 管線處理中</summary>
    public const string RagProcessing = "RagProcessing";
    /// <summary>RAG 管線就緒</summary>
    public const string RagReady = "RagReady";
    /// <summary>RAG 搜尋引用來源</summary>
    public const string RagCitations = "RagCitations";
    /// <summary>A2A 遠端 Agent 任務狀態更新</summary>
    public const string A2ATaskStatus = "a2a-task-status";
    /// <summary>等待使用者輸入（Human 節點暫停）</summary>
    public const string WaitingForInput = "WaitingForInput";
    /// <summary>使用者輸入已接收</summary>
    public const string UserInputReceived = "UserInputReceived";
    /// <summary>Hook 已執行</summary>
    public const string HookExecuted = "HookExecuted";
    /// <summary>Hook 阻擋了請求</summary>
    public const string HookBlocked = "HookBlocked";

    /// <summary>風險工具等待人工審批</summary>
    public const string WaitingForRiskApproval = "WaitingForRiskApproval";
    /// <summary>風險審批結果（核准/拒絕）</summary>
    public const string RiskApprovalResult = "RiskApprovalResult";

    /// <summary>Sub-agent 已建立</summary>
    public const string SubAgentCreated = "SubAgentCreated";
    /// <summary>向 Sub-agent 發問</summary>
    public const string SubAgentAsked = "SubAgentAsked";
    /// <summary>Sub-agent 回應</summary>
    public const string SubAgentResponded = "SubAgentResponded";
    /// <summary>ReAct/Flow 推理步驟（含 token 和耗時）</summary>
    public const string ReasoningStep = "ReasoningStep";

    /// <summary>Plan 規劃完成（含節點序列）</summary>
    public const string PlanGenerated = "PlanGenerated";
    /// <summary>Plan 已修訂</summary>
    public const string PlanRevised = "PlanRevised";

    /// <summary>Auditor 開始審查</summary>
    public const string AuditStarted = "AuditStarted";
    /// <summary>Auditor 審查完成（含結論）</summary>
    public const string AuditCompleted = "AuditCompleted";

    /// <summary>Flow 節點開始執行</summary>
    public const string NodeExecuting = "NodeExecuting";
    /// <summary>Flow 節點執行完成（含輸出）</summary>
    public const string NodeCompleted = "NodeCompleted";
    /// <summary>節點被取消（投機執行的輸家分支）</summary>
    public const string NodeCancelled = "NodeCancelled";
    /// <summary>Flow 執行軌跡已凍結為 Workflow JSON</summary>
    public const string FlowCrystallized = "FlowCrystallized";
    /// <summary>Debug Mode：節點完成後暫停等待使用者操作</summary>
    public const string DebugPaused = "DebugPaused";
    /// <summary>Debug Mode：使用者提交操作後恢復執行</summary>
    public const string DebugResumed = "DebugResumed";

    /// <summary>Trace：策略已選定（含選擇原因）</summary>
    public const string StrategySelected = "StrategySelected";
    /// <summary>Trace：起始節點已解析（含路徑）</summary>
    public const string StartNodeResolved = "StartNodeResolved";
}

/// <summary>
/// ExecutionEvent.Metadata 的 key 常數，確保 producer/consumer 型別安全。
/// </summary>
public static class MetadataKeys
{
    public const string ToolName = "toolName";
    public const string Arguments = "arguments";
    public const string RiskLevel = "riskLevel";
    public const string Approved = "approved";
    public const string SubAgentName = "subAgentName";
    public const string Instructions = "instructions";
    public const string Message = "message";
    public const string Response = "response";
    public const string Step = "step";
    public const string MaxSteps = "maxSteps";
    public const string Tokens = "tokens";
    public const string DurationMs = "durationMs";
    public const string Revision = "revision";
    public const string Verdict = "verdict";
    public const string Explanation = "explanation";
    public const string Issues = "issues";
    public const string NodeName = "nodeName";
    public const string NodeType = "nodeType";
    public const string Model = "model";
    public const string InputTokens = "inputTokens";
    public const string OutputTokens = "outputTokens";
}

public static class HookTypes
{
    public const string Code = "code";
    public const string Webhook = "webhook";
}
