using System.Text.Json;
using System.Text.Json.Serialization;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Autonomous.Flow.Models;

/// <summary>
/// LLM 產生的 Flow 規劃結果。
/// </summary>
public sealed class FlowPlan
{
    public List<PlannedNode> Nodes { get; init; } = [];
}

/// <summary>
/// 規劃中的單一節點 — LLM 輸出的 JSON 直接反序列化為此型別。
/// 內部組合 NodeConfig 作為節點配置（Single Source of Truth）。
/// </summary>
[JsonConverter(typeof(PlannedNodeConverter))]
public sealed class PlannedNode
{
    public string NodeType { get; init; } = NodeTypes.Agent;
    public string Name { get; init; } = "";

    // Agent
    public string? Instructions { get; init; }
    public List<string>? Tools { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }

    // Condition / Loop
    public string? ConditionType { get; init; }
    public string? ConditionValue { get; init; }
    public int? MaxIterations { get; init; }

    // Condition 分支索引（LLM 可指定，FlowPlanValidator 可驗證）
    // 若未指定，FlowExecutor 預設 trueBranch = index+1, falseBranch = index+2
    public int? TrueBranchIndex { get; init; }
    public int? FalseBranchIndex { get; init; }

    // Code
    public string? TransformType { get; init; }
    public string? TransformPattern { get; init; }
    public string? TransformReplacement { get; init; }

    // Parallel
    public List<ParallelBranchConfig>? Branches { get; init; }
    public string? MergeStrategy { get; init; }

    // Iteration
    public string? SplitMode { get; init; }
    public string? Delimiter { get; init; }
    public int? MaxItems { get; init; }
    public int? MaxConcurrency { get; init; }

    // HTTP Request — catalog 模式
    public string? HttpApiId { get; init; }
    public string? HttpArgsTemplate { get; init; }

    // HTTP Request — inline 模式
    public string? HttpUrl { get; init; }
    public string? HttpMethod { get; init; }
    public string? HttpHeaders { get; init; }
    public string? HttpBodyTemplate { get; init; }
    public string? HttpContentType { get; init; }
    public int? HttpTimeoutSeconds { get; init; }
    public string? HttpAuthMode { get; init; }
    public string? HttpAuthCredential { get; init; }
    public string? HttpAuthKeyName { get; init; }
    public int? HttpRetryCount { get; init; }
    public int? HttpRetryDelayMs { get; init; }
    public string? HttpResponseFormat { get; init; }
    public string? HttpResponseJsonPath { get; init; }
    public int? HttpResponseMaxLength { get; init; }

    // Router
    public string? Routes { get; init; }

    // 輸出格式（text / json / json_schema）
    public string? OutputFormat { get; init; }
    public string? OutputSchema { get; init; }

    /// <summary>
    /// 轉換為 NodeConfig（TraceStep 和 Crystallizer 共用）。
    /// </summary>
    public NodeConfig ToConfig() => new()
    {
        Instructions = Instructions,
        Tools = Tools,
        Provider = Provider,
        Model = Model,
        ConditionType = ConditionType,
        ConditionValue = ConditionValue,
        MaxIterations = MaxIterations,
        TransformType = TransformType,
        TransformPattern = TransformPattern,
        TransformReplacement = TransformReplacement,
        Branches = Branches,
        MergeStrategy = MergeStrategy,
        SplitMode = SplitMode,
        Delimiter = Delimiter,
        MaxItems = MaxItems,
        MaxConcurrency = MaxConcurrency,
        HttpApiId = HttpApiId,
        HttpArgsTemplate = HttpArgsTemplate,
        HttpUrl = HttpUrl,
        HttpMethod = HttpMethod,
        HttpHeaders = HttpHeaders,
        HttpBodyTemplate = HttpBodyTemplate,
        HttpContentType = HttpContentType,
        HttpTimeoutSeconds = HttpTimeoutSeconds,
        HttpAuthMode = HttpAuthMode,
        HttpAuthCredential = HttpAuthCredential,
        HttpAuthKeyName = HttpAuthKeyName,
        HttpRetryCount = HttpRetryCount,
        HttpRetryDelayMs = HttpRetryDelayMs,
        HttpResponseFormat = HttpResponseFormat,
        HttpResponseJsonPath = HttpResponseJsonPath,
        HttpResponseMaxLength = HttpResponseMaxLength,
        OutputFormat = OutputFormat,
        OutputSchema = OutputSchema,
        Routes = Routes
    };
}

/// <summary>
/// PlannedNode 自訂反序列化 — 同時接受 "nodeType" 和 "type" 作為節點類型欄位。
/// LLM 有時輸出 "type" 而非 "nodeType"，此 converter 統一處理。
/// </summary>
internal sealed class PlannedNodeConverter : JsonConverter<PlannedNode>
{
    private static readonly JsonSerializerOptions InnerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public override PlannedNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // 優先讀 "nodeType"，fallback 讀 "type"
        string nodeType = NodeTypes.Agent;
        if (root.TryGetProperty("nodeType", out var nt))
        {
            nodeType = nt.GetString() ?? NodeTypes.Agent;
        }
        else if (root.TryGetProperty("type", out var t))
        {
            nodeType = t.GetString() ?? NodeTypes.Agent;
        }

        // 用預設反序列化處理其他欄位（排除 converter 避免遞迴）
        var raw = root.Deserialize<PlannedNodeRaw>(InnerOptions);

        return new PlannedNode
        {
            NodeType = nodeType,
            Name = raw?.Name ?? "",
            Instructions = raw?.Instructions,
            Tools = raw?.Tools,
            Provider = raw?.Provider,
            Model = raw?.Model,
            ConditionType = raw?.ConditionType,
            ConditionValue = raw?.ConditionValue,
            MaxIterations = raw?.MaxIterations,
            TrueBranchIndex = raw?.TrueBranchIndex,
            FalseBranchIndex = raw?.FalseBranchIndex,
            TransformType = raw?.TransformType,
            TransformPattern = raw?.TransformPattern,
            TransformReplacement = raw?.TransformReplacement,
            Branches = raw?.Branches,
            MergeStrategy = raw?.MergeStrategy,
            SplitMode = raw?.SplitMode,
            Delimiter = raw?.Delimiter,
            MaxItems = raw?.MaxItems,
            HttpApiId = raw?.HttpApiId,
            HttpArgsTemplate = raw?.HttpArgsTemplate,
            HttpUrl = raw?.HttpUrl,
            HttpMethod = raw?.HttpMethod,
            HttpHeaders = raw?.HttpHeaders,
            HttpBodyTemplate = raw?.HttpBodyTemplate,
            HttpContentType = raw?.HttpContentType,
            HttpTimeoutSeconds = raw?.HttpTimeoutSeconds,
            HttpAuthMode = raw?.HttpAuthMode,
            HttpAuthCredential = raw?.HttpAuthCredential,
            HttpAuthKeyName = raw?.HttpAuthKeyName,
            HttpRetryCount = raw?.HttpRetryCount,
            HttpRetryDelayMs = raw?.HttpRetryDelayMs,
            HttpResponseFormat = raw?.HttpResponseFormat,
            HttpResponseJsonPath = raw?.HttpResponseJsonPath,
            HttpResponseMaxLength = raw?.HttpResponseMaxLength,
            OutputFormat = raw?.OutputFormat,
            OutputSchema = raw?.OutputSchema,
        };
    }

    public override void Write(Utf8JsonWriter writer, PlannedNode value, JsonSerializerOptions options)
    {
        // 序列化時使用 nodeType（標準格式）
        var raw = new PlannedNodeRaw
        {
            NodeType = value.NodeType,
            Name = value.Name,
            Instructions = value.Instructions,
            Tools = value.Tools,
            Provider = value.Provider,
            Model = value.Model,
            ConditionType = value.ConditionType,
            ConditionValue = value.ConditionValue,
            MaxIterations = value.MaxIterations,
            TrueBranchIndex = value.TrueBranchIndex,
            FalseBranchIndex = value.FalseBranchIndex,
            TransformType = value.TransformType,
            TransformPattern = value.TransformPattern,
            TransformReplacement = value.TransformReplacement,
            Branches = value.Branches,
            MergeStrategy = value.MergeStrategy,
            SplitMode = value.SplitMode,
            Delimiter = value.Delimiter,
            MaxItems = value.MaxItems,
            HttpApiId = value.HttpApiId,
            HttpArgsTemplate = value.HttpArgsTemplate,
            HttpUrl = value.HttpUrl,
            HttpMethod = value.HttpMethod,
            HttpHeaders = value.HttpHeaders,
            HttpBodyTemplate = value.HttpBodyTemplate,
            HttpContentType = value.HttpContentType,
            HttpTimeoutSeconds = value.HttpTimeoutSeconds,
            HttpAuthMode = value.HttpAuthMode,
            HttpAuthCredential = value.HttpAuthCredential,
            HttpAuthKeyName = value.HttpAuthKeyName,
            HttpRetryCount = value.HttpRetryCount,
            HttpRetryDelayMs = value.HttpRetryDelayMs,
            HttpResponseFormat = value.HttpResponseFormat,
            HttpResponseJsonPath = value.HttpResponseJsonPath,
            HttpResponseMaxLength = value.HttpResponseMaxLength,
            OutputFormat = value.OutputFormat,
            OutputSchema = value.OutputSchema,
        };
        JsonSerializer.Serialize(writer, raw, InnerOptions);
    }

    /// <summary>內部 DTO — 不帶 JsonConverter 避免遞迴。</summary>
    private sealed class PlannedNodeRaw
    {
        public string NodeType { get; init; } = NodeTypes.Agent;
        public string Name { get; init; } = "";
        public string? Instructions { get; init; }
        public List<string>? Tools { get; init; }
        public string? Provider { get; init; }
        public string? Model { get; init; }
        public string? ConditionType { get; init; }
        public string? ConditionValue { get; init; }
        public int? MaxIterations { get; init; }
        public int? TrueBranchIndex { get; init; }
        public int? FalseBranchIndex { get; init; }
        public string? TransformType { get; init; }
        public string? TransformPattern { get; init; }
        public string? TransformReplacement { get; init; }
        public List<ParallelBranchConfig>? Branches { get; init; }
        public string? MergeStrategy { get; init; }
        public string? SplitMode { get; init; }
        public string? Delimiter { get; init; }
        public int? MaxItems { get; init; }
        public string? HttpApiId { get; init; }
        public string? HttpArgsTemplate { get; init; }
        public string? HttpUrl { get; init; }
        public string? HttpMethod { get; init; }
        public string? HttpHeaders { get; init; }
        public string? HttpBodyTemplate { get; init; }
        public string? HttpContentType { get; init; }
        public int? HttpTimeoutSeconds { get; init; }
        public string? HttpAuthMode { get; init; }
        public string? HttpAuthCredential { get; init; }
        public string? HttpAuthKeyName { get; init; }
        public int? HttpRetryCount { get; init; }
        public int? HttpRetryDelayMs { get; init; }
        public string? HttpResponseFormat { get; init; }
        public string? HttpResponseJsonPath { get; init; }
        public int? HttpResponseMaxLength { get; init; }
        public string? OutputFormat { get; init; }
        public string? OutputSchema { get; init; }
    }
}
