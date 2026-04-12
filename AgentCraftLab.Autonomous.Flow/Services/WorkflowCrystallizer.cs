using System.Text.Json;
using System.Text.Json.Serialization;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Engine.Models;
using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// Workflow Crystallizer — 將 ExecutionTrace 轉換為 Studio 畫布可匯入的 Workflow JSON。
/// 輸出格式與 FlowBuilderService / buildFromAiSpec() 相同（flat CrystallizedNode 陣列）。
/// 輸入為 <see cref="Schema.NodeConfig"/>（Step 2 後 Flow 內部統一用 Schema 型別）。
/// </summary>
public sealed class WorkflowCrystallizer
{
    /// <summary>
    /// 將執行軌跡凍結為 Workflow JSON 字串。
    /// </summary>
    public string Crystallize(ExecutionTrace trace)
    {
        var nodes = new List<CrystallizedNode>();
        var connections = new List<CrystallizedConnection>();

        for (var i = 0; i < trace.Steps.Count; i++)
        {
            var step = trace.Steps[i];

            switch (step.Config)
            {
                case Schema.ParallelNode { Branches: { Count: > 0 } } parallelNode:
                    ExpandParallel(step, parallelNode, i, trace, nodes, connections);
                    break;

                case Schema.LoopNode loopNode:
                    ExpandLoop(step, loopNode, i, trace, nodes, connections);
                    break;

                case Schema.IterationNode iterationNode:
                    ExpandIteration(step, iterationNode, i, trace, nodes, connections);
                    break;

                default:
                    var nodeIndex = nodes.Count;
                    nodes.Add(StepToNode(step));

                    if (i + 1 < trace.Steps.Count)
                    {
                        connections.Add(new CrystallizedConnection
                        {
                            From = nodeIndex,
                            To = nodeIndex + 1,
                            FromOutput = step.OutputPort ?? OutputPorts.Output1
                        });
                    }
                    break;
            }
        }

        var workflow = new CrystallizedWorkflow
        {
            Nodes = nodes,
            Connections = connections
        };

        return JsonSerializer.Serialize(workflow, CrystallizeJsonOptions);
    }

    private static CrystallizedNode StepToNode(TraceStep step)
        => FromNodeConfig(step.NodeName, step.Config);

    /// <summary>
    /// <see cref="Schema.NodeConfig"/> → <see cref="CrystallizedNode"/>（flat, 前端 buildFromAiSpec 相容）。
    /// </summary>
    public static CrystallizedNode FromNodeConfig(string name, Schema.NodeConfig config)
    {
        var node = new CrystallizedNode
        {
            Type = NodeConfigHelpers.GetNodeTypeString(config),
            Name = name
        };

        switch (config)
        {
            case Schema.AgentNode agent:
                node.Instructions = agent.Instructions;
                node.Tools = agent.Tools.ToList();
                node.Provider = agent.Model.Provider;
                node.Model = agent.Model.Model;
                node.OutputFormat = FormatOutputKind(agent.Output.Kind);
                if (!string.IsNullOrWhiteSpace(agent.Output.SchemaJson))
                {
                    node.OutputSchema = agent.Output.SchemaJson;
                }
                break;

            case Schema.ConditionNode condition:
                node.ConditionType = FormatConditionKind(condition.Condition.Kind);
                node.ConditionExpression = condition.Condition.Value;
                break;

            case Schema.LoopNode loop:
                node.ConditionType = FormatConditionKind(loop.Condition.Kind);
                node.ConditionExpression = loop.Condition.Value;
                node.MaxIterations = loop.MaxIterations;
                break;

            case Schema.CodeNode code:
                node.TransformType = FormatTransformKind(code.Kind, code.Replacement);
                node.Pattern = code.Expression;
                node.Replacement = code.Replacement;
                node.Template = string.IsNullOrEmpty(code.Expression) ? "{{input}}" : code.Expression;
                break;

            case Schema.ParallelNode parallel:
                node.Branches = string.Join(",", parallel.Branches.Select(b => b.Name));
                node.MergeStrategy = FormatMergeStrategy(parallel.Merge);
                break;

            case Schema.IterationNode iteration:
                node.SplitMode = FormatSplitMode(iteration.Split);
                node.IterationDelimiter = iteration.Delimiter;
                node.MaxItems = iteration.MaxItems;
                if (iteration.MaxConcurrency > 1)
                {
                    node.MaxConcurrency = iteration.MaxConcurrency;
                }
                break;

            case Schema.HttpRequestNode http:
                PopulateHttpFields(node, http);
                break;

            case Schema.RouterNode router:
                node.Routes = string.Join(",", router.Routes.Select(r => r.Name));
                break;
        }

        return node;
    }

    // ════════════════════════════════════════
    // Parallel 展開：parallel + N 個 branch agent
    // ════════════════════════════════════════

    private static void ExpandParallel(TraceStep step, Schema.ParallelNode parallelNode, int stepIndex,
        ExecutionTrace trace, List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var parallelIndex = nodes.Count;
        nodes.Add(StepToNode(step));

        foreach (var branch in parallelNode.Branches)
        {
            var branchIndex = nodes.Count;
            nodes.Add(new CrystallizedNode
            {
                Type = NodeTypes.Agent,
                Name = branch.Name,
                Instructions = branch.Goal,
                Tools = branch.Tools?.ToList() ?? []
            });

            var portIndex = branchIndex - parallelIndex;
            connections.Add(new CrystallizedConnection
            {
                From = parallelIndex, To = branchIndex,
                FromOutput = $"output_{portIndex}"
            });
        }

        if (stepIndex + 1 < trace.Steps.Count)
        {
            connections.Add(new CrystallizedConnection
            {
                From = parallelIndex, To = nodes.Count,
                FromOutput = $"output_{parallelNode.Branches.Count + 1}"
            });
        }
    }

    // ════════════════════════════════════════
    // Loop 展開：loop + body agent（output_1 迴圈體，output_2 退出）
    // ════════════════════════════════════════

    private static void ExpandLoop(TraceStep step, Schema.LoopNode loopNode, int stepIndex,
        ExecutionTrace trace, List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var loopIndex = nodes.Count;
        nodes.Add(StepToNode(step));

        var bodyIndex = nodes.Count;
        nodes.Add(new CrystallizedNode
        {
            Type = NodeTypes.Agent,
            Name = $"{step.NodeName} Body",
            Instructions = loopNode.BodyAgent.Instructions,
            Tools = loopNode.BodyAgent.Tools.ToList()
        });

        connections.Add(new CrystallizedConnection
        {
            From = loopIndex, To = bodyIndex, FromOutput = OutputPorts.Output1
        });

        connections.Add(new CrystallizedConnection
        {
            From = bodyIndex, To = loopIndex, FromOutput = OutputPorts.Output1
        });

        if (stepIndex + 1 < trace.Steps.Count)
        {
            connections.Add(new CrystallizedConnection
            {
                From = loopIndex, To = nodes.Count, FromOutput = OutputPorts.Output2
            });
        }
    }

    // ════════════════════════════════════════
    // Iteration 展開：iteration + body agent（output_1 每 item，output_2 Done）
    // ════════════════════════════════════════

    private static void ExpandIteration(TraceStep step, Schema.IterationNode iterationNode, int stepIndex,
        ExecutionTrace trace, List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var iterIndex = nodes.Count;
        nodes.Add(StepToNode(step));

        var bodyIndex = nodes.Count;
        nodes.Add(new CrystallizedNode
        {
            Type = NodeTypes.Agent,
            Name = $"{step.NodeName} Body",
            Instructions = iterationNode.BodyAgent.Instructions,
            Tools = iterationNode.BodyAgent.Tools.ToList()
        });

        connections.Add(new CrystallizedConnection
        {
            From = iterIndex, To = bodyIndex, FromOutput = OutputPorts.Output1
        });

        if (stepIndex + 1 < trace.Steps.Count)
        {
            connections.Add(new CrystallizedConnection
            {
                From = iterIndex, To = nodes.Count, FromOutput = OutputPorts.Output2
            });
        }
    }

    // ════════════════════════════════════════
    // HTTP 欄位填充 — Schema.HttpRequestSpec 解包為 flat CrystallizedNode 欄位
    // ════════════════════════════════════════

    private static void PopulateHttpFields(CrystallizedNode node, Schema.HttpRequestNode http)
    {
        if (http.Spec is Schema.CatalogHttpRef catalogRef)
        {
            node.HttpApiId = catalogRef.ApiId;
            node.HttpArgsTemplate = catalogRef.Args?.ToJsonString();
            return;
        }

        if (http.Spec is Schema.InlineHttpRequest inline)
        {
            node.HttpUrl = inline.Url;
            node.HttpMethod = inline.Method.ToString().ToUpperInvariant();
            node.HttpHeaders = string.Join("\n", inline.Headers.Select(h => $"{h.Name}: {h.Value}"));
            node.HttpBodyTemplate = inline.Body?.Content?.ToJsonString();
            node.HttpContentType = inline.ContentType;
            node.HttpTimeoutSeconds = inline.TimeoutSeconds;
            node.HttpRetryCount = inline.Retry.Count;
            node.HttpRetryDelayMs = inline.Retry.DelayMs;
            node.HttpResponseMaxLength = inline.ResponseMaxLength;

            switch (inline.Auth)
            {
                case Schema.BearerAuth bearer:
                    node.HttpAuthMode = "bearer";
                    node.HttpAuthCredential = bearer.Token;
                    break;
                case Schema.BasicAuth basic:
                    node.HttpAuthMode = "basic";
                    node.HttpAuthCredential = basic.UserPass;
                    break;
                case Schema.ApiKeyHeaderAuth apiHeader:
                    node.HttpAuthMode = "apikey-header";
                    node.HttpAuthCredential = apiHeader.Value;
                    node.HttpAuthKeyName = apiHeader.KeyName;
                    break;
                case Schema.ApiKeyQueryAuth apiQuery:
                    node.HttpAuthMode = "apikey-query";
                    node.HttpAuthCredential = apiQuery.Value;
                    node.HttpAuthKeyName = apiQuery.KeyName;
                    break;
                default:
                    node.HttpAuthMode = "none";
                    break;
            }

            switch (inline.Response)
            {
                case Schema.JsonParser:
                    node.HttpResponseFormat = "json";
                    break;
                case Schema.JsonPathParser jsonPath:
                    node.HttpResponseFormat = "jsonpath";
                    node.HttpResponseJsonPath = jsonPath.Path;
                    break;
                default:
                    node.HttpResponseFormat = "text";
                    break;
            }
        }
    }

    // ════════════════════════════════════════
    // Enum → legacy string formatters（保留舊 CrystallizedNode wire format）
    // ════════════════════════════════════════

    private static string FormatTransformKind(Schema.TransformKind kind, string? replacement) => kind switch
    {
        Schema.TransformKind.Template => "template",
        Schema.TransformKind.Regex => string.IsNullOrEmpty(replacement) ? "regex-extract" : "regex-replace",
        Schema.TransformKind.JsonPath => "json-path",
        Schema.TransformKind.Trim => "trim",
        Schema.TransformKind.Truncate => "trim",
        Schema.TransformKind.Split => "split-take",
        Schema.TransformKind.Upper => "upper",
        Schema.TransformKind.Lower => "lower",
        Schema.TransformKind.Script => "script",
        _ => "template"
    };

    private static string FormatConditionKind(Schema.ConditionKind kind) => kind switch
    {
        Schema.ConditionKind.Regex => "regex",
        Schema.ConditionKind.LlmJudge => "llm-judge",
        Schema.ConditionKind.Expression => "expression",
        _ => "contains"
    };

    private static string FormatMergeStrategy(Schema.MergeStrategyKind kind) => kind switch
    {
        Schema.MergeStrategyKind.Join => "join",
        Schema.MergeStrategyKind.Json => "json",
        _ => "labeled"
    };

    private static string FormatSplitMode(Schema.SplitModeKind kind) => kind switch
    {
        Schema.SplitModeKind.Delimiter => "delimiter",
        _ => "json-array"
    };

    private static string FormatOutputKind(Schema.OutputFormat format) => format switch
    {
        Schema.OutputFormat.Json => "json",
        Schema.OutputFormat.JsonSchema => "json_schema",
        _ => "text"
    };

    private static readonly JsonSerializerOptions CrystallizeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

/// <summary>
/// Crystallize 輸出的 Workflow 結構 — 與 buildFromAiSpec() 格式一致。
/// </summary>
public sealed class CrystallizedWorkflow
{
    public List<CrystallizedNode> Nodes { get; init; } = [];
    public List<CrystallizedConnection> Connections { get; init; } = [];
}

/// <summary>
/// 前端 buildFromAiSpec 相容的 flat 節點 DTO。
/// Phase F 會和前端 NodeData 型別一起重新定義。
/// </summary>
public sealed class CrystallizedNode
{
    public string Type { get; init; } = "";
    public string Name { get; init; } = "";

    // Agent
    public string? Instructions { get; set; }
    public List<string>? Tools { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }

    // Condition / Loop
    public string? ConditionType { get; set; }
    public string? ConditionExpression { get; set; }
    public int? MaxIterations { get; set; }

    // Code
    public string? TransformType { get; set; }
    public string? Pattern { get; set; }
    public string? Replacement { get; set; }
    public string? Template { get; set; }

    // Parallel
    public string? Branches { get; set; }
    public string? MergeStrategy { get; set; }

    // Iteration
    public string? SplitMode { get; set; }
    public string? IterationDelimiter { get; set; }
    public int? MaxItems { get; set; }
    public int? MaxConcurrency { get; set; }

    // HTTP Request
    public string? HttpApiId { get; set; }
    public string? HttpArgsTemplate { get; set; }
    public string? HttpUrl { get; set; }
    public string? HttpMethod { get; set; }
    public string? HttpHeaders { get; set; }
    public string? HttpBodyTemplate { get; set; }
    public string? HttpContentType { get; set; }
    public int? HttpTimeoutSeconds { get; set; }
    public string? HttpAuthMode { get; set; }
    public string? HttpAuthCredential { get; set; }
    public string? HttpAuthKeyName { get; set; }
    public int? HttpRetryCount { get; set; }
    public int? HttpRetryDelayMs { get; set; }
    public string? HttpResponseFormat { get; set; }
    public string? HttpResponseJsonPath { get; set; }
    public int? HttpResponseMaxLength { get; set; }

    // Router
    public string? Routes { get; set; }

    // 輸出格式
    public string? OutputFormat { get; set; }
    public string? OutputSchema { get; set; }
}

/// <summary>
/// 節點連線 — 對應前端 buildFromAiSpec 的 connection DTO。
/// </summary>
public sealed class CrystallizedConnection
{
    public int From { get; init; }
    public int To { get; init; }
    public string? FromOutput { get; init; }
}
