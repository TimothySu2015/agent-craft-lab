using System.Text.Json;
using System.Text.Json.Serialization;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// Workflow Crystallizer — 將 ExecutionTrace 轉換為 Studio 畫布可匯入的 Workflow JSON。
/// 輸出格式與 FlowBuilderService / buildFromAiSpec() 相同。
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

            if (step.NodeType == NodeTypes.Parallel && step.Config.Branches is { Count: > 0 })
            {
                ExpandParallel(step, i, trace, nodes, connections);
            }
            else if (step.NodeType == NodeTypes.Loop && step.Config.Instructions is not null)
            {
                ExpandLoop(step, i, trace, nodes, connections);
            }
            else if (step.NodeType == NodeTypes.Iteration && step.Config.Instructions is not null)
            {
                ExpandIteration(step, i, trace, nodes, connections);
            }
            else
            {
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
        => FromConfig(step.NodeType, step.NodeName, step.Config);

    /// <summary>
    /// NodeConfig → CrystallizedNode 映射（共用於 Crystallizer 和 FlowPlanConverter）。
    /// </summary>
    public static CrystallizedNode FromConfig(string nodeType, string name, NodeConfig config)
    {
        var node = new CrystallizedNode
        {
            Type = nodeType,
            Name = name
        };

        switch (nodeType)
        {
            case NodeTypes.Agent:
                node.Instructions = config.Instructions ?? "";
                node.Tools = config.Tools ?? [];
                if (config.Provider is not null) node.Provider = config.Provider;
                if (config.Model is not null) node.Model = config.Model;
                if (config.OutputFormat is not null) node.OutputFormat = config.OutputFormat;
                if (config.OutputSchema is not null) node.OutputSchema = config.OutputSchema;
                break;

            case NodeTypes.Condition:
                node.ConditionType = config.ConditionType ?? "contains";
                node.ConditionExpression = config.ConditionValue ?? "";
                break;

            case NodeTypes.Loop:
                node.ConditionType = config.ConditionType ?? "contains";
                node.ConditionExpression = config.ConditionValue ?? "";
                node.MaxIterations = config.MaxIterations ?? 5;
                break;

            case NodeTypes.Code:
                node.TransformType = config.TransformType ?? "template";
                node.Pattern = config.TransformPattern ?? "";
                node.Replacement = config.TransformReplacement;
                node.Template = config.TransformPattern ?? "{{input}}";
                break;

            case NodeTypes.Parallel:
                node.Branches = string.Join(",",
                    config.Branches?.Select(b => b.Name) ?? []);
                node.MergeStrategy = config.MergeStrategy ?? "labeled";
                break;

            case NodeTypes.Iteration:
                node.SplitMode = config.SplitMode ?? "json-array";
                node.IterationDelimiter = config.Delimiter ?? "\n";
                node.MaxItems = config.MaxItems ?? 50;
                if (config.MaxConcurrency is > 1) node.MaxConcurrency = config.MaxConcurrency;
                break;

            case NodeTypes.HttpRequest:
                node.HttpApiId = config.HttpApiId;
                node.HttpArgsTemplate = config.HttpArgsTemplate;
                node.HttpUrl = config.HttpUrl;
                node.HttpMethod = config.HttpMethod;
                node.HttpHeaders = config.HttpHeaders;
                node.HttpBodyTemplate = config.HttpBodyTemplate;
                node.HttpContentType = config.HttpContentType;
                node.HttpTimeoutSeconds = config.HttpTimeoutSeconds;
                node.HttpAuthMode = config.HttpAuthMode;
                node.HttpAuthCredential = config.HttpAuthCredential;
                node.HttpAuthKeyName = config.HttpAuthKeyName;
                node.HttpRetryCount = config.HttpRetryCount;
                node.HttpRetryDelayMs = config.HttpRetryDelayMs;
                node.HttpResponseFormat = config.HttpResponseFormat;
                node.HttpResponseJsonPath = config.HttpResponseJsonPath;
                node.HttpResponseMaxLength = config.HttpResponseMaxLength;
                break;

            case NodeTypes.Router:
                node.ConditionExpression = config.ConditionValue ?? "";
                node.Routes = config.Routes ?? "";
                break;
        }

        return node;
    }

    // ════════════════════════════════════════
    // Parallel 展開：parallel + N 個 branch agent
    // ════════════════════════════════════════

    private static void ExpandParallel(TraceStep step, int stepIndex, ExecutionTrace trace,
        List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var parallelIndex = nodes.Count;
        nodes.Add(StepToNode(step));

        foreach (var branch in step.Config.Branches!)
        {
            var branchIndex = nodes.Count;
            nodes.Add(new CrystallizedNode
            {
                Type = NodeTypes.Agent,
                Name = branch.Name,
                Instructions = branch.Goal,
                Tools = branch.Tools ?? []
            });

            var portIndex = branchIndex - parallelIndex;
            connections.Add(new CrystallizedConnection
            {
                From = parallelIndex, To = branchIndex,
                FromOutput = $"output_{portIndex}"
            });
        }

        // Done port → 下一個節點
        if (stepIndex + 1 < trace.Steps.Count)
        {
            connections.Add(new CrystallizedConnection
            {
                From = parallelIndex, To = nodes.Count,
                FromOutput = $"output_{step.Config.Branches.Count + 1}"
            });
        }
    }

    // ════════════════════════════════════════
    // Loop 展開：loop + body agent（output_1 迴圈體，output_2 退出）
    // ════════════════════════════════════════

    private static void ExpandLoop(TraceStep step, int stepIndex, ExecutionTrace trace,
        List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var loopIndex = nodes.Count;
        nodes.Add(StepToNode(step));

        // body agent（output_1）
        var bodyIndex = nodes.Count;
        nodes.Add(new CrystallizedNode
        {
            Type = NodeTypes.Agent,
            Name = $"{step.NodeName} Body",
            Instructions = step.Config.Instructions ?? "",
            Tools = step.Config.Tools ?? []
        });

        // loop → body（output_1）
        connections.Add(new CrystallizedConnection
        {
            From = loopIndex, To = bodyIndex, FromOutput = OutputPorts.Output1
        });

        // body → loop（迴圈回去）
        connections.Add(new CrystallizedConnection
        {
            From = bodyIndex, To = loopIndex, FromOutput = OutputPorts.Output1
        });

        // loop → next（output_2 退出）
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

    private static void ExpandIteration(TraceStep step, int stepIndex, ExecutionTrace trace,
        List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var iterIndex = nodes.Count;
        nodes.Add(StepToNode(step));

        // body agent（output_1）
        var bodyIndex = nodes.Count;
        nodes.Add(new CrystallizedNode
        {
            Type = NodeTypes.Agent,
            Name = $"{step.NodeName} Body",
            Instructions = step.Config.Instructions ?? "",
            Tools = step.Config.Tools ?? []
        });

        // iteration → body（output_1）
        connections.Add(new CrystallizedConnection
        {
            From = iterIndex, To = bodyIndex, FromOutput = OutputPorts.Output1
        });

        // iteration → next（output_2 Done）
        if (stepIndex + 1 < trace.Steps.Count)
        {
            connections.Add(new CrystallizedConnection
            {
                From = iterIndex, To = nodes.Count, FromOutput = OutputPorts.Output2
            });
        }
    }

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
/// 欄位名對齊 Engine WorkflowNode（PropertyNameCaseInsensitive 匹配）。
/// </summary>
public sealed class CrystallizedNode : IWorkflowNodeContract
{
    public string Type { get; init; } = "";
    public string Name { get; init; } = "";

    // Agent
    public string? Instructions { get; set; }
    public List<string>? Tools { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }

    // Condition / Loop — 對齊 WorkflowNode.ConditionExpression
    public string? ConditionType { get; set; }
    public string? ConditionExpression { get; set; }
    public int? MaxIterations { get; set; }

    // Code — 對齊 WorkflowNode.Pattern / Replacement / Template
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
/// 欄位名對齊 Engine WorkflowConnection.FromOutput。
/// </summary>
public sealed class CrystallizedConnection : IWorkflowConnectionContract
{
    public int From { get; init; }
    public int To { get; init; }
    public string? FromOutput { get; init; }
}
