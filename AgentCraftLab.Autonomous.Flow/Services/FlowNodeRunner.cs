using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Services.Variables;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// 節點執行器 — 依 NodeConfig 子型別委派給對應執行邏輯。
/// 複用 Engine 已有的工具（TransformHelper、ToolRegistryService 等），
/// 但不依賴 ImperativeWorkflowStrategy（避免耦合）。
/// Phase C2 Step 2：改用 <see cref="Schema.NodeConfig"/> pattern matching 取代字串 switch。
/// </summary>
public sealed class FlowNodeRunner
{
    private const int MaxParallelBranches = 3;

    private readonly FlowAgentFactory _agentFactory;
    private readonly HttpApiToolService _httpApiTool;
    private readonly IVariableResolver _variableResolver;
    private readonly ILogger<FlowNodeRunner> _logger;

    /// <summary>
    /// Flow 級 Working Memory — 節點間共享記憶（由 FlowExecutor 每次執行前設定）。
    /// 注意：FlowNodeRunner 是 DI singleton，此屬性為 per-execution 狀態。
    /// 目前單執行緒環境下安全，多執行緒時應改為參數傳遞。
    /// </summary>
    public FlowWorkingMemory? WorkingMemory { get; set; }

    /// <summary>
    /// 已完成節點的輸出對照表（name → output），供 {{node:step_name}} 跨節點引用。
    /// 由 FlowExecutor 每次執行前設定，每完成一個節點即更新。
    /// </summary>
    public Dictionary<string, string>? NodeOutputs { get; set; }

    /// <summary>
    /// 引用壓縮器 — 超過門檻的 {{node:}} 引用自動壓縮。
    /// 由 FlowExecutor 每次執行前設定（需 planner client）。為 null 時不壓縮。
    /// </summary>
    public IContextCompactor? ReferenceCompactor { get; set; }

    public FlowNodeRunner(
        FlowAgentFactory agentFactory,
        HttpApiToolService httpApiTool,
        IVariableResolver variableResolver,
        ILogger<FlowNodeRunner> logger)
    {
        _agentFactory = agentFactory;
        _httpApiTool = httpApiTool;
        _variableResolver = variableResolver;
        _logger = logger;
    }

    /// <summary>
    /// 執行單一節點，串流回傳 ExecutionEvent。
    /// </summary>
    public async IAsyncEnumerable<ExecutionEvent> ExecuteNodeAsync(
        Schema.NodeConfig node,
        string input,
        GoalExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        switch (node)
        {
            case Schema.AgentNode agentNode:
                await foreach (var evt in ExecuteAgentNodeAsync(agentNode, input, request, cancellationToken))
                    yield return evt;
                break;

            case Schema.CodeNode codeNode:
                yield return ExecuteCodeNode(codeNode, input);
                break;

            case Schema.ConditionNode conditionNode:
                yield return ExecuteConditionNode(conditionNode, input);
                break;

            case Schema.IterationNode iterationNode:
                await foreach (var evt in ExecuteIterationNodeAsync(iterationNode, input, request, cancellationToken))
                    yield return evt;
                break;

            case Schema.ParallelNode parallelNode:
                await foreach (var evt in ExecuteParallelNodeAsync(parallelNode, input, request, cancellationToken))
                    yield return evt;
                break;

            case Schema.LoopNode loopNode:
                await foreach (var evt in ExecuteLoopNodeAsync(loopNode, input, request, cancellationToken))
                    yield return evt;
                break;

            case Schema.HttpRequestNode httpNode:
                await foreach (var evt in ExecuteHttpRequestNodeAsync(httpNode, input, request))
                    yield return evt;
                break;

            default:
                var nodeTypeString = NodeConfigHelpers.GetNodeTypeString(node);
                _logger.LogWarning("Node type '{NodeType}' not yet implemented in FlowExecutor", nodeTypeString);
                yield return ExecutionEvent.NodeCompleted(nodeTypeString, node.Name, input);
                break;
        }
    }

    // ════════════════════════════════════════
    // Agent 節點
    // ════════════════════════════════════════

    private async IAsyncEnumerable<ExecutionEvent> ExecuteAgentNodeAsync(
        Schema.AgentNode node,
        string input,
        GoalExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var agentName = node.Name;
        var startEvt = ExecutionEvent.AgentStarted(agentName, text: node.Instructions);
        startEvt.Metadata = new Dictionary<string, string>
        {
            [MetadataKeys.Instructions] = node.Instructions,
            ["tools"] = string.Join(", ", node.Tools)
        };
        yield return startEvt;

        var effectiveTools = node.Tools.Count > 0
            ? node.Tools.ToList()
            : request.AvailableTools;

        var (client, resolvedTools, error) = _agentFactory.CreateAgentClient(request, effectiveTools);
        if (client is null)
        {
            yield return ExecutionEvent.Error($"[{agentName}] {error}");
            yield return ExecutionEvent.AgentCompleted(agentName, "");
            yield break;
        }

        // F5: 解析 instructions 中的 {{node:step_name}} 跨節點引用（超過門檻時壓縮）
        var resolvedInstructions = await ResolveNodeReferencesAsync(
            node.Instructions, node.Name, cancellationToken);
        var messages = BuildAgentMessages(resolvedInstructions, effectiveTools, input, request.Provider, WorkingMemory);

        // F6: 注入 flow_memory_write meta-tool（讓 Agent 可主動存入發現供下游使用）
        if (WorkingMemory is not null)
        {
            var memory = WorkingMemory;
            var memoryTool = AIFunctionFactory.Create(
                (string key, string value) => { memory.Write(key, value); return $"Stored '{key}' in working memory"; },
                "flow_memory_write",
                "Store a key-value pair in shared working memory. Other nodes can access this data.");
            resolvedTools = [.. (resolvedTools ?? []), memoryTool];
        }

        var hasTools = resolvedTools is { Count: > 0 };
        var outputKind = node.Output.Kind;
        var hasResponseFormat = outputKind is Schema.OutputFormat.Json or Schema.OutputFormat.JsonSchema;

        if (hasTools || hasResponseFormat)
        {
            // 有工具或強制格式 — 用 GetResponseAsync（需要 ChatOptions）
            var events = await RunAgentWithToolsAsync(client, messages, resolvedTools ?? [],
                agentName, outputKind, node.Output.SchemaJson, cancellationToken);
            foreach (var evt in events)
            {
                yield return evt;
            }
        }
        else
        {
            // 無工具 + 無格式限制 — 真正串流
            var responseText = new StringBuilder();
            await foreach (var update in client.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    responseText.Append(update.Text);
                    yield return ExecutionEvent.TextChunk(agentName, update.Text);
                }
            }

            var finalText = responseText.ToString();
            var streamCompleted = ExecutionEvent.AgentCompleted(agentName, finalText);
            streamCompleted.Metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Tokens] = (finalText.Length / 4).ToString()
            };
            yield return streamCompleted;
        }
    }

    private async Task<List<ExecutionEvent>> RunAgentWithToolsAsync(
        IChatClient client, List<ChatMessage> messages, IList<AITool> tools,
        string agentName, Schema.OutputFormat outputKind, string? outputSchema,
        CancellationToken cancellationToken)
    {
        var events = new List<ExecutionEvent>();
        var chatOptions = new ChatOptions { Tools = tools.Cast<AITool>().ToList() };

        if (outputKind == Schema.OutputFormat.Json)
        {
            chatOptions.ResponseFormat = ChatResponseFormat.Json;
        }
        else if (outputKind == Schema.OutputFormat.JsonSchema && !string.IsNullOrWhiteSpace(outputSchema))
        {
            try
            {
                var schemaElement = System.Text.Json.JsonDocument.Parse(outputSchema).RootElement;
                chatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    schemaElement, schemaName: "OutputSchema",
                    schemaDescription: "Agent output schema");
            }
            catch { /* schema 解析失敗，fallback 不設 ResponseFormat */ }
        }

        try
        {
            var response = await client.GetResponseAsync(messages, chatOptions, cancellationToken);

            foreach (var msg in response.Messages)
            {
                foreach (var content in msg.Contents)
                {
                    if (content is FunctionCallContent call)
                    {
                        var argsStr = call.Arguments != null
                            ? string.Join(", ", call.Arguments.Select(kv => $"{kv.Key}=\"{kv.Value}\""))
                            : "";
                        events.Add(ExecutionEvent.ToolCall(agentName, call.Name ?? "", argsStr));
                    }
                    else if (content is FunctionResultContent result)
                    {
                        var resultStr = result.Result?.ToString() ?? "";
                        if (resultStr.Length > Defaults.TruncateLength)
                            resultStr = resultStr[..Defaults.TruncateLength] + "...";
                        events.Add(ExecutionEvent.ToolResult(agentName, result.CallId ?? "", resultStr));
                    }
                }
            }

            var completed = ExecutionEvent.AgentCompleted(agentName, response.Text ?? "");

            long totalTokens;
            if (response.Usage is not null)
            {
                totalTokens = (response.Usage.InputTokenCount ?? 0) + (response.Usage.OutputTokenCount ?? 0);
            }
            else
            {
                totalTokens = (response.Text?.Length ?? 0) / 4;
            }

            completed.Metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Tokens] = totalTokens.ToString()
            };
            events.Add(completed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent node '{Name}' failed", agentName);
            events.Add(ExecutionEvent.Error($"[{agentName}] {ex.Message}"));
            events.Add(ExecutionEvent.AgentCompleted(agentName, ""));
        }

        return events;
    }

    // ════════════════════════════════════════
    // Iteration 節點 — 拆分 input 為陣列，對每個元素執行 Agent
    // ════════════════════════════════════════

    private async IAsyncEnumerable<ExecutionEvent> ExecuteIterationNodeAsync(
        Schema.IterationNode node,
        string input,
        GoalExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = node.Name;
        var items = SplitInput(input, node.Split, node.Delimiter);
        var maxItems = node.MaxItems > 0 ? node.MaxItems : 10;
        if (items.Count > maxItems) items = items[..maxItems];

        yield return ExecutionEvent.NodeExecuting(NodeTypes.Iteration, $"{nodeName} ({items.Count} items)");

        var maxConcurrency = node.MaxConcurrency > 1 ? node.MaxConcurrency : 1;
        var bodyInstructions = string.IsNullOrWhiteSpace(node.BodyAgent.Instructions)
            ? "Process the following item and provide a result."
            : node.BodyAgent.Instructions;
        var bodyTools = node.BodyAgent.Tools;

        if (maxConcurrency <= 1)
        {
            // 順序執行（預設）
            var results = new List<string>();
            for (var i = 0; i < items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[i].Trim();
                if (string.IsNullOrWhiteSpace(item)) continue;

                var itemAgent = BuildInnerAgent($"{nodeName}[{i + 1}]", bodyInstructions, bodyTools);

                var itemResult = new StringBuilder();
                await foreach (var evt in ExecuteAgentNodeAsync(itemAgent, item, request, cancellationToken))
                {
                    if (evt.Type == EventTypes.AgentCompleted)
                        itemResult.Append(evt.Text);
                    yield return evt;
                }

                results.Add(itemResult.ToString());
            }

            var mergedOutput = string.Join("\n\n", results);
            yield return ExecutionEvent.NodeCompleted(NodeTypes.Iteration, nodeName, mergedOutput);
            yield break;
        }

        // 並行執行 — SemaphoreSlim 節流，events buffer 後 replay
        using var throttle = new SemaphoreSlim(maxConcurrency);
        var parallelTasks = items.Select(async (rawItem, i) =>
        {
            var item = rawItem.Trim();
            if (string.IsNullOrWhiteSpace(item)) return (Events: new List<ExecutionEvent>(), Result: "");

            await throttle.WaitAsync(cancellationToken);
            try
            {
                var itemAgent = BuildInnerAgent($"{nodeName}[{i + 1}]", bodyInstructions, bodyTools);

                var events = new List<ExecutionEvent>();
                var itemResult = new StringBuilder();
                await foreach (var evt in ExecuteAgentNodeAsync(itemAgent, item, request, cancellationToken))
                {
                    events.Add(evt);
                    if (evt.Type == EventTypes.AgentCompleted)
                        itemResult.Append(evt.Text);
                }

                return (Events: events, Result: itemResult.ToString());
            }
            finally
            {
                throttle.Release();
            }
        }).ToList();

        var allResults = await Task.WhenAll(parallelTasks);

        foreach (var (events, _) in allResults)
        {
            foreach (var evt in events)
                yield return evt;
        }

        var parallelMerged = string.Join("\n\n", allResults.Select(r => r.Result).Where(r => !string.IsNullOrEmpty(r)));
        yield return ExecutionEvent.NodeCompleted(NodeTypes.Iteration, nodeName, parallelMerged);
    }

    private static List<string> SplitInput(string input, Schema.SplitModeKind splitMode, string delimiter)
    {
        if (splitMode == Schema.SplitModeKind.JsonArray)
        {
            try
            {
                var arr = JsonSerializer.Deserialize<List<string>>(input);
                if (arr is { Count: > 0 }) return arr;
            }
            catch
            {
                // fallback to delimiter
            }
        }

        return input.Split(delimiter, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    // ════════════════════════════════════════
    // Parallel 節點 — N 個分支同時執行，合併結果
    // ════════════════════════════════════════

    private async IAsyncEnumerable<ExecutionEvent> ExecuteParallelNodeAsync(
        Schema.ParallelNode node,
        string input,
        GoalExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = node.Name;
        var branches = node.Branches;

        if (branches.Count == 0)
        {
            _logger.LogWarning("Parallel node '{Name}' has no branches, passing through", nodeName);
            yield return ExecutionEvent.NodeCompleted(NodeTypes.Parallel, nodeName, input);
            yield break;
        }

        yield return ExecutionEvent.NodeExecuting(NodeTypes.Parallel, $"{nodeName} ({branches.Count} branches)");

        // 每個分支建立 Agent 節點，SemaphoreSlim 限制並發（避免 API 429 rate limit）
        using var throttle = new SemaphoreSlim(MaxParallelBranches);
        var branchTasks = branches.Select(branch =>
        {
            var branchAgent = BuildInnerAgent(
                $"{nodeName}/{branch.Name}",
                branch.Goal,
                branch.Tools ?? []);
            // 只傳 branch name 作為 input，不傳完整的使用者輸入
            // 避免 LLM 看到所有項目後搜尋全部（gpt-4o-mini 不遵守隔離指令）
            var branchInput = branch.Name;
            return RunBranchThrottledAsync(throttle, branchAgent, branchInput, request, cancellationToken);
        }).ToList();

        var branchResults = await Task.WhenAll(branchTasks);

        foreach (var (events, _) in branchResults)
        {
            foreach (var evt in events)
            {
                yield return evt;
            }
        }

        var mergedOutput = MergeResults(branches, branchResults, node.Merge);

        yield return ExecutionEvent.NodeCompleted(NodeTypes.Parallel, nodeName, mergedOutput);
    }

    private async Task<(List<ExecutionEvent> Events, string Result)> RunBranchThrottledAsync(
        SemaphoreSlim throttle, Schema.AgentNode branchAgent, string input,
        GoalExecutionRequest request, CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            return await RunBranchAsync(branchAgent, input, request, cancellationToken);
        }
        finally
        {
            throttle.Release();
        }
    }

    private async Task<(List<ExecutionEvent> Events, string Result)> RunBranchAsync(
        Schema.AgentNode branchAgent, string input, GoalExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var events = new List<ExecutionEvent>();
        var result = "";

        await foreach (var evt in ExecuteAgentNodeAsync(branchAgent, input, request, cancellationToken))
        {
            events.Add(evt);
            if (evt.Type == EventTypes.AgentCompleted)
                result = evt.Text;
        }

        return (events, result);
    }

    private static string MergeResults(
        IReadOnlyList<Schema.BranchConfig> branches,
        (List<ExecutionEvent> Events, string Result)[] results,
        Schema.MergeStrategyKind mergeStrategy)
    {
        return mergeStrategy switch
        {
            Schema.MergeStrategyKind.Json => JsonSerializer.Serialize(
                branches.Zip(results, (b, r) => new { b.Name, r.Result })
                    .ToDictionary(x => x.Name, x => x.Result)),

            Schema.MergeStrategyKind.Join => string.Join("\n\n", results.Select(r => r.Result)),

            _ => string.Join("\n\n", // Labeled (default)
                branches.Zip(results, (b, r) => $"[{b.Name}]\n{r.Result}"))
        };
    }

    // ════════════════════════════════════════
    // Loop 節點 — 重複執行直到條件滿足或達到上限
    // ════════════════════════════════════════

    private async IAsyncEnumerable<ExecutionEvent> ExecuteLoopNodeAsync(
        Schema.LoopNode node,
        string input,
        GoalExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = node.Name;
        var maxIterations = node.MaxIterations > 0 ? node.MaxIterations : 5;
        var conditionKind = node.Condition.Kind;
        var conditionValue = node.Condition.Value;
        var currentInput = input;

        var bodyInstructions = string.IsNullOrWhiteSpace(node.BodyAgent.Instructions)
            ? "Improve and refine the following content based on the context."
            : node.BodyAgent.Instructions;
        var bodyTools = node.BodyAgent.Tools;

        for (var i = 0; i < maxIterations; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 檢查退出條件（用上一輪的結果判斷）
            if (EvaluateCondition(currentInput, conditionKind, conditionValue))
            {
                yield return ExecutionEvent.NodeCompleted(NodeTypes.Loop,
                    $"{nodeName} (exit at iteration {i + 1})", currentInput);
                yield break;
            }

            yield return ExecutionEvent.NodeExecuting(NodeTypes.Loop, $"{nodeName} (iteration {i + 1}/{maxIterations})");

            var bodyAgent = BuildInnerAgent($"{nodeName}[{i + 1}]", bodyInstructions, bodyTools);

            var bodyResult = new StringBuilder();
            await foreach (var evt in ExecuteAgentNodeAsync(bodyAgent, currentInput, request, cancellationToken))
            {
                if (evt.Type == EventTypes.AgentCompleted)
                    bodyResult.Append(evt.Text);
                yield return evt;
            }

            var newResult = bodyResult.ToString();

            // 如果新結果觸發退出條件，保留上一輪的實質內容（新結果可能只是確認訊息）
            if (EvaluateCondition(newResult, conditionKind, conditionValue))
            {
                yield return ExecutionEvent.NodeCompleted(NodeTypes.Loop,
                    $"{nodeName} (exit at iteration {i + 2})", currentInput);
                yield break;
            }

            currentInput = newResult;
        }

        // 達到上限退出
        yield return ExecutionEvent.NodeCompleted(NodeTypes.Loop,
            $"{nodeName} (max {maxIterations} reached)", currentInput);
    }

    private static bool EvaluateCondition(string input, Schema.ConditionKind kind, string value)
    {
        return kind switch
        {
            Schema.ConditionKind.Contains => input.Contains(value, StringComparison.OrdinalIgnoreCase),
            Schema.ConditionKind.Regex => TryRegexMatch(input, value),
            _ => false
        };
    }

    private static bool TryRegexMatch(string input, string pattern)
    {
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                input, pattern,
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(2));
        }
        catch
        {
            return false;
        }
    }

    // ════════════════════════════════════════
    // HTTP Request 節點 — 確定性 HTTP 呼叫，零 LLM 成本
    // ════════════════════════════════════════

    private async IAsyncEnumerable<ExecutionEvent> ExecuteHttpRequestNodeAsync(
        Schema.HttpRequestNode node,
        string input,
        GoalExecutionRequest request)
    {
        var nodeName = string.IsNullOrWhiteSpace(node.Name) ? "HTTP Request" : node.Name;
        var spec = node.Spec;
        var refLabel = spec switch
        {
            Schema.CatalogHttpRef catalog => catalog.ApiId,
            Schema.InlineHttpRequest inline => inline.Url,
            _ => ""
        };

        yield return ExecutionEvent.AgentStarted(nodeName);
        yield return ExecutionEvent.ToolCall(nodeName, "HTTP", refLabel);

        string result;

        var apiDef = ResolveHttpApiDefinition(node, request);
        if (apiDef is null)
        {
            result = "[HTTP Error] No HttpApiId or inline URL specified";
        }
        else
        {
            var escapedInput = JsonSerializer.Serialize(input).Trim('"');
            // Catalog 模式用 args template 把 {input} 替換；inline 模式沒有 args，走 body template
            var argsTemplate = spec is Schema.CatalogHttpRef catalogRef
                ? (catalogRef.Args?.ToJsonString() ?? "{}")
                : "{}";
            var argsJson = argsTemplate.Replace("{input}", escapedInput);

            try
            {
                result = await _httpApiTool.CallApiAsync(apiDef, argsJson);
            }
            catch (Exception ex)
            {
                result = $"[HTTP Error] {ex.Message}";
            }
        }

        yield return ExecutionEvent.ToolResult(nodeName, "HTTP", result.Length > Defaults.TruncateLength
            ? result[..Defaults.TruncateLength] + "..." : result);
        yield return ExecutionEvent.AgentCompleted(nodeName, result);
    }

    private static HttpApiDefinition? ResolveHttpApiDefinition(Schema.HttpRequestNode node, GoalExecutionRequest request)
    {
        switch (node.Spec)
        {
            case Schema.CatalogHttpRef catalogRef:
                if (!string.IsNullOrWhiteSpace(catalogRef.ApiId)
                    && request.HttpApis.TryGetValue(catalogRef.ApiId, out var catalogDef))
                {
                    return catalogDef;
                }
                return null;

            case Schema.InlineHttpRequest inline when !string.IsNullOrWhiteSpace(inline.Url):
                return new HttpApiDefinition
                {
                    Id = node.Name,
                    Name = string.IsNullOrWhiteSpace(node.Name) ? "inline-http" : node.Name,
                    Url = inline.Url,
                    Method = inline.Method.ToString().ToUpperInvariant(),
                    Headers = string.Join("\n", inline.Headers.Select(h => $"{h.Name}: {h.Value}")),
                    BodyTemplate = inline.Body?.Content?.ToJsonString() ?? "",
                    ContentType = inline.ContentType,
                    TimeoutSeconds = inline.TimeoutSeconds,
                    ResponseMaxLength = inline.ResponseMaxLength,
                    AuthMode = inline.Auth switch
                    {
                        Schema.BearerAuth => "bearer",
                        Schema.BasicAuth => "basic",
                        Schema.ApiKeyHeaderAuth => "apikey-header",
                        Schema.ApiKeyQueryAuth => "apikey-query",
                        _ => "none"
                    },
                    AuthCredential = inline.Auth switch
                    {
                        Schema.BearerAuth bearer => bearer.Token,
                        Schema.BasicAuth basic => basic.UserPass,
                        Schema.ApiKeyHeaderAuth apiH => apiH.Value,
                        Schema.ApiKeyQueryAuth apiQ => apiQ.Value,
                        _ => ""
                    },
                    AuthKeyName = inline.Auth switch
                    {
                        Schema.ApiKeyHeaderAuth apiH => apiH.KeyName,
                        Schema.ApiKeyQueryAuth apiQ => apiQ.KeyName,
                        _ => ""
                    },
                    RetryCount = inline.Retry.Count,
                    RetryDelayMs = inline.Retry.DelayMs,
                    ResponseFormat = inline.Response switch
                    {
                        Schema.JsonParser => "json",
                        Schema.JsonPathParser => "jsonpath",
                        _ => "text"
                    },
                    ResponseJsonPath = inline.Response is Schema.JsonPathParser jp ? jp.Path : "",
                };

            default:
                return null;
        }
    }

    // ════════════════════════════════════════
    // Code 節點
    // ════════════════════════════════════════

    private static ExecutionEvent ExecuteCodeNode(Schema.CodeNode node, string input)
    {
        // Schema.TransformKind enum → TransformHelper 期望的舊字串常數
        var transformType = FormatTransformType(node.Kind, node.Replacement);
        var output = TransformHelper.ApplyTransform(
            transformType,
            input,
            node.Expression,
            node.Replacement);

        return ExecutionEvent.NodeCompleted(NodeTypes.Code, node.Name, output);
    }

    private static string FormatTransformType(Schema.TransformKind kind, string? replacement) => kind switch
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

    // ════════════════════════════════════════
    // Condition 節點
    // ════════════════════════════════════════

    private static ExecutionEvent ExecuteConditionNode(Schema.ConditionNode node, string input)
    {
        var met = EvaluateCondition(input, node.Condition.Kind, node.Condition.Value);

        var outputPort = met ? OutputPorts.Output1 : OutputPorts.Output2;
        return new ExecutionEvent
        {
            Type = EventTypes.NodeCompleted,
            Text = $"[condition] {node.Name} → {(met ? "TRUE" : "FALSE")}",
            Metadata = new Dictionary<string, string>
            {
                ["nodeType"] = NodeTypes.Condition,
                ["nodeName"] = node.Name,
                ["output"] = input,
                ["outputPort"] = outputPort,
                ["conditionMet"] = met.ToString()
            }
        };
    }

    // ════════════════════════════════════════
    // 共用工具
    // ════════════════════════════════════════

    /// <summary>
    /// 建構內部 Agent 節點（iteration / parallel / loop body 共用）。
    /// </summary>
    private static Schema.AgentNode BuildInnerAgent(string name, string instructions, IReadOnlyList<string> tools) => new()
    {
        Id = name,
        Name = name,
        Instructions = instructions,
        Tools = tools
    };

    /// <summary>
    /// 解析文字中的 {{node:step_name}} 引用，替換為對應節點的輸出。
    /// 透過 <see cref="AgentCraftLab.Engine.Services.Variables.IVariableResolver"/> 統一入口，
    /// Flow 和畫布 Workflow 共用同一個 resolver。
    /// </summary>
    internal string ResolveNodeReferences(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var context = BuildVariableContext();
        return _variableResolver.Resolve(text, context);
    }

    /// <summary>
    /// 非同步解析 {{node:}} 引用，超過門檻時用 compactor 壓縮後注入。
    /// compressContext 提供壓縮方向（當前節點的 instructions 描述）。
    /// </summary>
    internal async Task<string> ResolveNodeReferencesAsync(
        string? text, string compressContext, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        if (ReferenceCompactor is null)
            return ResolveNodeReferences(text);

        var context = BuildVariableContext();
        return await _variableResolver.ResolveAsync(
            text, context, ReferenceCompactor, compressContext, ct);
    }

    private AgentCraftLab.Engine.Services.Variables.VariableContext BuildVariableContext()
    {
        return new AgentCraftLab.Engine.Services.Variables.VariableContext
        {
            NodeOutputs = NodeOutputs
                ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// 建構 agent 節點的訊息清單。使用 CacheableSystemPrompt 分割靜態/動態部分，
    /// 讓 Anthropic provider 可明確標記 cache_control 以啟用 prefix caching。
    /// </summary>
    internal static List<ChatMessage> BuildAgentMessages(string? instructions, List<string>? tools, string input, string? provider = null, FlowWorkingMemory? memory = null)
    {
        var messages = new List<ChatMessage>();

        var staticPart = !string.IsNullOrWhiteSpace(instructions) ? instructions : "";

        // 注入 Working Memory 快照（下游節點自動看到前驅節點存入的資料）
        if (memory is not null && !memory.IsEmpty)
        {
            staticPart += memory.ToPromptSection();
        }

        var toolsHint = tools is { Count: > 0 }
            ? $"You have access to the following tools: {string.Join(", ", tools)}. Use them to accomplish the task. Do NOT say you cannot access real-time information — use your tools instead. IMPORTANT: Only search for what your instructions specify. Do NOT search for unrelated items."
            : "";

        if (!string.IsNullOrEmpty(staticPart) || !string.IsNullOrEmpty(toolsHint))
        {
            var prompt = new CacheableSystemPrompt(
                string.IsNullOrEmpty(toolsHint) ? staticPart : staticPart + "\n\n",
                toolsHint);
            messages.AddRange(prompt.ToChatMessages(provider));
        }

        messages.Add(new ChatMessage(ChatRole.User, input));
        return messages;
    }
}
