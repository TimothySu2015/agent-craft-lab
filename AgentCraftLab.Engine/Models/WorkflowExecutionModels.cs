using System.Text.Json;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Models;

public class FileAttachment
{
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// 從 JSON 元素解析 fileBase64/fileName/fileMimeType 為 FileAttachment。
    /// </summary>
    public static FileAttachment? FromJson(JsonElement element)
    {
        if (!element.TryGetProperty("fileBase64", out var fb64))
        {
            return null;
        }

        var b64 = fb64.GetString();
        if (string.IsNullOrEmpty(b64))
        {
            return null;
        }

        return new FileAttachment
        {
            FileName = element.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "attachment" : "attachment",
            MimeType = element.TryGetProperty("fileMimeType", out var fm) ? fm.GetString() ?? "application/octet-stream" : "application/octet-stream",
            Data = Convert.FromBase64String(b64)
        };
    }
}

public class WorkflowExecutionRequest
{
    public string WorkflowJson { get; set; } = "";
    public string UserMessage { get; set; } = "";
    public Dictionary<string, ProviderCredential> Credentials { get; set; } = new();
    public Dictionary<string, HttpApiDefinition>? HttpApiDefs { get; set; }
    public List<ChatHistoryEntry> History { get; set; } = [];
    public FileAttachment? Attachment { get; set; }
    /// <summary>Trace session ID（= AG-UI runId），用於 OTel Activity 的 session.id tag。</summary>
    public string? SessionId { get; set; }
    /// <summary>Debug Mode 暫停橋接器（由 API 端點建構並注入）。</summary>
    public Services.DebugBridge? DebugBridge { get; set; }
    /// <summary>執行時變數覆蓋（覆蓋 Workflow 定義的預設值）。</summary>
    public Dictionary<string, string>? RuntimeVariables { get; set; }
}

public class ChatHistoryEntry
{
    public string Role { get; set; } = "user"; // "user" | "assistant"
    public string Text { get; set; } = "";
}


public class ExecutionEvent
{
    public string Type { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string Text { get; set; } = "";
    public string? InputType { get; set; }
    public string? Choices { get; set; }
    public List<string>? Skills { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public List<object>? Citations { get; set; }

    public static ExecutionEvent AgentStarted(string name, List<string>? skills = null, string? text = null)
        => new() { Type = EventTypes.AgentStarted, AgentName = name, Skills = skills, Text = text ?? "" };

    public static ExecutionEvent TextChunk(string name, string text)
        => new() { Type = EventTypes.TextChunk, AgentName = name, Text = text };

    public static ExecutionEvent AgentCompleted(string name, string text)
        => new() { Type = EventTypes.AgentCompleted, AgentName = name, Text = text };

    public static ExecutionEvent AgentCompleted(string name, string text, long inputTokens, long outputTokens, string? model = null)
        => new()
        {
            Type = EventTypes.AgentCompleted, AgentName = name, Text = text,
            Metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Tokens] = (inputTokens + outputTokens).ToString(),
                [MetadataKeys.InputTokens] = inputTokens.ToString(),
                [MetadataKeys.OutputTokens] = outputTokens.ToString(),
                [MetadataKeys.Model] = model ?? "",
            }
        };

    public static ExecutionEvent ToolCall(string agentName, string toolName, string args)
        => new() { Type = EventTypes.ToolCall, AgentName = agentName, Text = $"{toolName}({args})" };

    public static ExecutionEvent ToolResult(string agentName, string toolName, string result)
        => new() { Type = EventTypes.ToolResult, AgentName = agentName, Text = $"{toolName}: {result}" };

    public static ExecutionEvent WorkflowCompleted()
        => new() { Type = EventTypes.WorkflowCompleted };

    public static ExecutionEvent Error(string message)
        => new() { Type = EventTypes.Error, Text = message };

    public static ExecutionEvent RagProcessing(string text)
        => new() { Type = EventTypes.RagProcessing, Text = text };

    public static ExecutionEvent RagReady(string text)
        => new() { Type = EventTypes.RagReady, Text = text };

    public static ExecutionEvent RagCitations(List<RagChunk> chunks, List<string>? expandedQueries = null)
        => new()
        {
            Type = EventTypes.RagCitations,
            Text = $"{chunks.Count} sources found",
            Citations = chunks.Select(c => (object)new
            {
                c.FileName,
                c.ChunkIndex,
                c.Score,
                Content = c.Content.Length > Defaults.TruncateLength ? c.Content[..Defaults.TruncateLength] + "..." : c.Content
            }).ToList(),
            Metadata = expandedQueries is { Count: > 0 }
                ? new Dictionary<string, string> { ["expandedQueries"] = System.Text.Json.JsonSerializer.Serialize(expandedQueries) }
                : null
        };

    public static ExecutionEvent A2ATaskStatus(string agentName, string status)
        => new() { Type = EventTypes.A2ATaskStatus, AgentName = agentName, Text = status };

    public static ExecutionEvent WaitingForInput(string agentName, string prompt, string inputType, string choices)
        => new() { Type = EventTypes.WaitingForInput, AgentName = agentName, Text = prompt, InputType = inputType, Choices = choices };

    public static ExecutionEvent UserInputReceived(string agentName, string input)
        => new() { Type = EventTypes.UserInputReceived, AgentName = agentName, Text = input };

    public static ExecutionEvent HookExecuted(string hookName, string text)
        => new() { Type = EventTypes.HookExecuted, AgentName = hookName, Text = text };

    public static ExecutionEvent HookBlocked(string hookName, string reason)
        => new() { Type = EventTypes.HookBlocked, AgentName = hookName, Text = reason };

    // Planning
    public static ExecutionEvent PlanGenerated(string agentName, string plan)
        => new() { Type = EventTypes.PlanGenerated, AgentName = agentName, Text = plan };

    public static ExecutionEvent PlanRevised(string agentName, string plan)
        => new() { Type = EventTypes.PlanRevised, AgentName = agentName, Text = plan };

    // P0: Risk-based Human Override
    public static ExecutionEvent WaitingForRiskApproval(string agentName, string toolName, string args, string riskLevel)
        => new()
        {
            Type = EventTypes.WaitingForRiskApproval, AgentName = agentName,
            Text = $"Tool '{toolName}' requires approval",
            InputType = "approval",
            Metadata = new Dictionary<string, string>
            {
                [MetadataKeys.ToolName] = toolName, [MetadataKeys.Arguments] = args, [MetadataKeys.RiskLevel] = riskLevel
            }
        };

    public static ExecutionEvent RiskApprovalResult(string agentName, bool approved, string toolName)
        => new()
        {
            Type = EventTypes.RiskApprovalResult, AgentName = agentName,
            Text = approved ? $"Approved: {toolName}" : $"Rejected: {toolName}",
            Metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Approved] = approved.ToString(), [MetadataKeys.ToolName] = toolName
            }
        };

    // P1: Transparency Cockpit
    public static ExecutionEvent SubAgentCreated(string agentName, string subAgentName, string instructions)
        => new()
        {
            Type = EventTypes.SubAgentCreated, AgentName = agentName,
            Text = $"Created sub-agent: {subAgentName}",
            Metadata = new Dictionary<string, string>
            {
                [MetadataKeys.SubAgentName] = subAgentName, [MetadataKeys.Instructions] = instructions
            }
        };

    public static ExecutionEvent SubAgentAsked(string agentName, string subAgentName, string message)
        => new()
        {
            Type = EventTypes.SubAgentAsked, AgentName = agentName,
            Text = $"Asked {subAgentName}: {(message.Length > 100 ? message[..100] + "..." : message)}",
            Metadata = new Dictionary<string, string>
            {
                [MetadataKeys.SubAgentName] = subAgentName, [MetadataKeys.Message] = message
            }
        };

    public static ExecutionEvent SubAgentResponded(string agentName, string subAgentName, string response)
        => new()
        {
            Type = EventTypes.SubAgentResponded, AgentName = agentName,
            Text = $"{subAgentName} responded: {(response.Length > 200 ? response[..200] + "..." : response)}",
            Metadata = new Dictionary<string, string>
            {
                [MetadataKeys.SubAgentName] = subAgentName, [MetadataKeys.Response] = response
            }
        };

    public static ExecutionEvent ReasoningStep(string agentName, int step, int maxSteps, int tokens, double durationMs)
        => new()
        {
            Type = EventTypes.ReasoningStep, AgentName = agentName,
            Text = $"Step {step}/{maxSteps}",
            Metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Step] = step.ToString(), [MetadataKeys.MaxSteps] = maxSteps.ToString(),
                [MetadataKeys.Tokens] = tokens.ToString(), [MetadataKeys.DurationMs] = durationMs.ToString("F0")
            }
        };

    // P2: Self-Reflection
    public static ExecutionEvent AuditStarted(string agentName, int revision)
        => new()
        {
            Type = EventTypes.AuditStarted, AgentName = agentName,
            Text = $"Auditing response (revision {revision})...",
            Metadata = new Dictionary<string, string> { [MetadataKeys.Revision] = revision.ToString() }
        };

    public static ExecutionEvent AuditCompleted(string agentName, string verdict, string explanation, string issues)
        => new()
        {
            Type = EventTypes.AuditCompleted, AgentName = agentName,
            Text = $"Audit: {verdict}",
            Metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Verdict] = verdict, [MetadataKeys.Explanation] = explanation, [MetadataKeys.Issues] = issues
            }
        };

    // Flow 結構化模式
    public static ExecutionEvent NodeExecuting(string nodeType, string nodeName)
        => new()
        {
            Type = EventTypes.NodeExecuting,
            Text = $"[{nodeType}] {nodeName}",
            Metadata = new Dictionary<string, string>
            {
                ["nodeType"] = nodeType, ["nodeName"] = nodeName
            }
        };

    public static ExecutionEvent NodeCompleted(string nodeType, string nodeName, string output)
        => new()
        {
            Type = EventTypes.NodeCompleted,
            Text = $"[{nodeType}] {nodeName} → {(output.Length > 200 ? output[..200] + "..." : output)}",
            Metadata = new Dictionary<string, string>
            {
                ["nodeType"] = nodeType, ["nodeName"] = nodeName, ["output"] = output
            }
        };

    public static ExecutionEvent NodeCancelled(string nodeType, string nodeName, string reason)
        => new()
        {
            Type = EventTypes.NodeCancelled,
            Text = $"[{nodeType}] {nodeName} — {reason}",
            Metadata = new Dictionary<string, string>
            {
                ["nodeType"] = nodeType, ["nodeName"] = nodeName, ["reason"] = reason
            }
        };

    public static ExecutionEvent DebugPaused(string nodeType, string nodeName, string output)
        => new()
        {
            Type = EventTypes.DebugPaused,
            Text = $"[Debug] Paused at {nodeName}",
            Metadata = new Dictionary<string, string>
            {
                ["nodeType"] = nodeType, ["nodeName"] = nodeName, ["output"] = output
            }
        };

    public static ExecutionEvent DebugResumed(string nodeName, string action)
        => new()
        {
            Type = EventTypes.DebugResumed,
            Text = $"[Debug] {action} — {nodeName}",
            Metadata = new Dictionary<string, string>
            {
                ["nodeName"] = nodeName, ["action"] = action
            }
        };

    public static ExecutionEvent FlowCrystallized(string workflowJson)
        => new()
        {
            Type = EventTypes.FlowCrystallized,
            Text = "Flow crystallized — ready to import to Studio",
            Metadata = new Dictionary<string, string> { ["workflowJson"] = workflowJson }
        };

    public static ExecutionEvent StrategySelected(string strategy, string reason)
        => new()
        {
            Type = EventTypes.StrategySelected,
            Metadata = new Dictionary<string, string> { ["strategy"] = strategy, ["reason"] = reason }
        };

    public static ExecutionEvent StartNodeResolved(string nodeId, string path)
        => new()
        {
            Type = EventTypes.StartNodeResolved,
            Metadata = new Dictionary<string, string> { [MetadataKeys.NodeName] = nodeId, ["path"] = path }
        };
}

public class WorkflowPayload
{
    public List<WorkflowNode> Nodes { get; set; } = [];
    public List<WorkflowConnection> Connections { get; set; } = [];
    public WorkflowSettings WorkflowSettings { get; set; } = new();
    public List<McpServerDefinition> McpServers { get; set; } = [];
    public List<A2AAgentDefinition> A2AAgents { get; set; } = [];
    public Dictionary<string, HttpApiDefinition> HttpApis { get; set; } = new();
    public List<string> Skills { get; set; } = [];
    /// <summary>Workflow 變數定義（使用者在畫布上定義，執行時初始化）。</summary>
    public List<WorkflowVariable> Variables { get; set; } = [];
}

/// <summary>
/// Workflow 變數定義 — 使用者在畫布設定，執行時透過 {{var:name}} 引用。
/// </summary>
public class WorkflowVariable
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";    // string | number | boolean | json
    public string DefaultValue { get; set; } = "";
    public string Description { get; set; } = "";
}

public class McpServerDefinition
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

public class A2AAgentDefinition
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Description { get; set; } = "";
    public string Format { get; set; } = "auto";
}

public class WorkflowNode : IWorkflowNodeContract
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string Instructions { get; set; } = "";
    public string Model { get; set; } = "gpt-4o";
    public string Provider { get; set; } = "openai";
    public string Middleware { get; set; } = "";
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public int? MaxOutputTokens { get; set; }
    public string HistoryProvider { get; set; } = "none";
    public int MaxMessages { get; set; } = 20;
    public List<string> Tools { get; set; } = [];
    public List<string> McpServers { get; set; } = [];
    public List<string> A2AAgents { get; set; } = [];
    public List<string> HttpApis { get; set; } = [];
    public string OutputFormat { get; set; } = "text"; // text | json | json_schema
    public string OutputSchema { get; set; } = "";
    public Dictionary<string, Dictionary<string, string>> MiddlewareConfig { get; set; } = new();
    public List<string> Skills { get; set; } = [];
    public string ConditionType { get; set; } = "";
    public string ConditionExpression { get; set; } = "";
    public int MaxIterations { get; set; } = 5;
    public RagSettings? RagConfig { get; set; }
    public string A2AUrl { get; set; } = "";
    public string A2AFormat { get; set; } = "auto";

    // Human node
    public string Prompt { get; set; } = "";
    public string InputType { get; set; } = "text";    // text | choice | approval
    public string Choices { get; set; } = "";            // 逗號分隔選項
    public int TimeoutSeconds { get; set; }              // 0=無限等待

    // HTTP Request node — catalog 模式（向下相容）
    public string HttpApiId { get; set; } = "";
    public string HttpArgsTemplate { get; set; } = "{}";  // JSON args，{input} 會被替換為前一節點輸出

    // HTTP Request node — inline 模式（httpApiId 為空時使用）
    public string HttpUrl { get; set; } = "";
    public string HttpMethod { get; set; } = "GET";
    public string HttpHeaders { get; set; } = "";          // 每行一組 Key: Value
    public string HttpBodyTemplate { get; set; } = "";
    public string HttpContentType { get; set; } = "application/json";
    public int HttpResponseMaxLength { get; set; } = 2000;   // 0=不截斷
    public int HttpTimeoutSeconds { get; set; } = 15;         // 0=使用全域預設
    public string HttpAuthMode { get; set; } = "none";        // none | bearer | basic | apikey-header | apikey-query
    public string HttpAuthCredential { get; set; } = "";
    public string HttpAuthKeyName { get; set; } = "";
    public int HttpRetryCount { get; set; } = 0;
    public int HttpRetryDelayMs { get; set; } = 1000;
    public string HttpResponseFormat { get; set; } = "text";   // text | json | jsonpath
    public string HttpResponseJsonPath { get; set; } = "";

    // Parallel node
    public string Branches { get; set; } = "Branch1,Branch2";
    public string MergeStrategy { get; set; } = "labeled";   // labeled | join | json

    // Router node
    public string Routes { get; set; } = "";

    // Knowledge Base
    public List<string> KnowledgeBaseIds { get; set; } = [];

    // Iteration node
    public string SplitMode { get; set; } = "json-array";   // json-array | delimiter
    public string IterationDelimiter { get; set; } = "\n";
    public int MaxItems { get; set; } = 50;
    public int MaxConcurrency { get; set; } = 1;   // 1=順序, >1=並行（SemaphoreSlim 節流）

    // Code node transform properties
    public string TransformType { get; set; } = "template";
    public string Pattern { get; set; } = "";
    public string Replacement { get; set; } = "";
    public string Template { get; set; } = "{{input}}";
    public int MaxLength { get; set; } = 0;
    public string Delimiter { get; set; } = "\n";
    public int SplitIndex { get; set; } = 0;

    /// <summary>腳本語言（script 模式用）：javascript（預設）或 csharp。</summary>
    public string ScriptLanguage { get; set; } = "javascript";
}

public class WorkflowConnection : IWorkflowConnectionContract
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string FromOutput { get; set; } = "";
}

public class WorkflowSettings
{
    public string Type { get; set; } = "auto";
    public int MaxTurns { get; set; } = 10;
    public WorkflowHooks Hooks { get; set; } = new();
    public string ContextPassing { get; set; } = Strategies.NodeExecutors.ContextPassingModes.PreviousOnly;

    /// <summary>
    /// 啟用投機執行 — llm-judge Condition 節點評估時同時搶跑兩條分支的第一個節點。
    /// 預設關閉（opt-in），啟用後輸家分支會消耗額外 token。
    /// </summary>
    public bool SpeculativeExecution { get; set; }
}

/// <summary>
/// 工作流程 Hook 設定：6 個插入點，每個可配 code 或 webhook 類型。
/// </summary>
public class WorkflowHooks
{
    public WorkflowHook? OnInput { get; set; }
    public WorkflowHook? PreExecute { get; set; }
    public WorkflowHook? PreAgent { get; set; }
    public WorkflowHook? PostAgent { get; set; }
    public WorkflowHook? OnComplete { get; set; }
    public WorkflowHook? OnError { get; set; }
}

/// <summary>
/// 單一 Hook 定義。Type 決定執行方式：code（本地轉換）或 webhook（HTTP 通知）。
/// </summary>
public class WorkflowHook
{
    public string Type { get; set; } = "code"; // code | webhook

    // code 類型（複用 Code 節點的 8 種 TransformType）
    public string TransformType { get; set; } = "template";
    public string Template { get; set; } = "{{input}}";
    public string Pattern { get; set; } = "";
    public string Replacement { get; set; } = "";
    public int MaxLength { get; set; }
    public string Delimiter { get; set; } = "\n";
    public int SplitIndex { get; set; }

    // webhook 類型
    public string Url { get; set; } = "";
    public string Method { get; set; } = "POST";
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? BodyTemplate { get; set; }

    // 共用：是否阻擋（OnInput 可用來攔截訊息）
    public string? BlockPattern { get; set; }
    public string? BlockMessage { get; set; }
}

/// <summary>
/// Hook 執行時的上下文變數，可用 {{variableName}} 在 template 中引用。
/// </summary>
public class HookContext
{
    public string Input { get; set; } = "";
    public string? Output { get; set; }
    public string AgentName { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string WorkflowName { get; set; } = "";
    public string UserId { get; set; } = "";
    public string? Error { get; set; }
}

/// <summary>
/// 用於在 async 方法間共享可延遲初始化的 IChatClient（替代 ref 參數）。
/// </summary>
public class ChatClientHolder
{
    public IChatClient? Client { get; set; }
}

public class HttpApiDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string Headers { get; set; } = "";
    public string BodyTemplate { get; set; } = "";
    public string ContentType { get; set; } = "application/json";
    public int ResponseMaxLength { get; set; } = 2000;   // 0=不截斷
    public int TimeoutSeconds { get; set; } = 15;         // 0=使用全域預設

    // Auth 預設
    public string AuthMode { get; set; } = "none";        // none | bearer | basic | apikey-header | apikey-query
    public string AuthCredential { get; set; } = "";       // token / user:pass / key value
    public string AuthKeyName { get; set; } = "";           // apikey 模式的 header/query 名稱

    // 重試
    public int RetryCount { get; set; } = 0;               // 0=不重試
    public int RetryDelayMs { get; set; } = 1000;          // 指數退避基底

    // 回應解析
    public string ResponseFormat { get; set; } = "text";   // text | json | jsonpath
    public string ResponseJsonPath { get; set; } = "";      // jsonpath 模式的路徑（如 data.items[0].name）
}

public class A2AAgentCard
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "";
    public string BaseUrl { get; set; } = "";
}

public class RagContext
{
    public required List<WorkflowNode> RagNodes { get; init; }
    public required List<WorkflowConnection> WorkflowConnections { get; init; }
    public required IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator { get; init; }
    public AgentCraftLab.Search.Abstractions.ISearchEngine? SearchEngine { get; init; }
    public required string IndexName { get; init; }                     // 臨時上傳索引（可為空字串）
    public List<string> KnowledgeBaseIndexNames { get; init; } = [];    // 知識庫索引列表
    /// <summary>索引名稱 → DataSourceId 映射（用於搜尋時路由到對應引擎，null = 預設引擎）。</summary>
    public Dictionary<string, string?> IndexDataSourceMap { get; init; } = new();
}

public class RagSettings
{
    public string DataSource { get; set; } = "upload";
    public int ChunkSize { get; set; } = Defaults.DefaultChunkSize;
    public int ChunkOverlap { get; set; } = Defaults.DefaultChunkOverlap;
    public int TopK { get; set; } = Defaults.DefaultTopK;
    public string EmbeddingModel { get; set; } = Defaults.DefaultEmbeddingModel;
    public string SearchMode { get; set; } = "hybrid";
    public float MinScore { get; set; } = Search.Abstractions.SearchEngineOptions.DefaultRagMinScore;
    public bool QueryExpansion { get; set; } = true;
    public string? FileNameFilter { get; set; }
    public bool ContextCompression { get; set; }
    public int TokenBudget { get; set; } = 1500;
}

public enum ToolCategory { Search, Utility, Web, Data }

/// <summary>
/// 工具所需的 credential 類型（供憑證管理頁面動態顯示）。
/// </summary>
public record ToolCredentialType(string Provider, string UsedByTools, string Icon);

/// <summary>
/// 工具定義的元資料。
/// </summary>
public record ToolDefinition(
    string Id,
    string DisplayName,
    string Description,
    Func<AITool> Factory,
    ToolCategory Category,
    string Icon = "&#x1F527;",
    string? RequiredCredential = null,
    Func<Dictionary<string, ProviderCredential>, AITool>? CredentialFactory = null);
