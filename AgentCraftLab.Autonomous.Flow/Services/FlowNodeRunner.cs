using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// 節點執行器 — 依 NodeType 委派給對應的執行邏輯。
/// 複用 Engine 已有的工具（TransformHelper、ToolRegistryService 等），
/// 但不依賴 ImperativeWorkflowStrategy（避免耦合）。
/// </summary>
public sealed class FlowNodeRunner
{
    private const int MaxParallelBranches = 3;

    private readonly FlowAgentFactory _agentFactory;
    private readonly HttpApiToolService _httpApiTool;
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
        ILogger<FlowNodeRunner> logger)
    {
        _agentFactory = agentFactory;
        _httpApiTool = httpApiTool;
        _logger = logger;
    }

    /// <summary>
    /// 執行單一節點，串流回傳 ExecutionEvent。
    /// </summary>
    public async IAsyncEnumerable<ExecutionEvent> ExecuteNodeAsync(
        PlannedNode node,
        string input,
        GoalExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        switch (node.NodeType)
        {
            case NodeTypes.Agent:
                await foreach (var evt in ExecuteAgentNodeAsync(node, input, request, cancellationToken))
                    yield return evt;
                break;

            case NodeTypes.Code:
                yield return ExecuteCodeNode(node, input);
                break;

            case NodeTypes.Condition:
                yield return ExecuteConditionNode(node, input);
                break;

            case NodeTypes.Iteration:
                await foreach (var evt in ExecuteIterationNodeAsync(node, input, request, cancellationToken))
                    yield return evt;
                break;

            case NodeTypes.Parallel:
                await foreach (var evt in ExecuteParallelNodeAsync(node, input, request, cancellationToken))
                    yield return evt;
                break;

            case NodeTypes.Loop:
                await foreach (var evt in ExecuteLoopNodeAsync(node, input, request, cancellationToken))
                    yield return evt;
                break;

            case NodeTypes.HttpRequest:
                await foreach (var evt in ExecuteHttpRequestNodeAsync(node, input, request))
                    yield return evt;
                break;

            default:
                _logger.LogWarning("Node type '{NodeType}' not yet implemented in FlowExecutor", node.NodeType);
                yield return ExecutionEvent.NodeCompleted(node.NodeType, node.Name, input);
                break;
        }
    }

    // ════════════════════════════════════════
    // Agent 節點
    // ════════════════════════════════════════

    private async IAsyncEnumerable<ExecutionEvent> ExecuteAgentNodeAsync(
        PlannedNode node,
        string input,
        GoalExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var agentName = node.Name;
        var startEvt = ExecutionEvent.AgentStarted(agentName, text: node.Instructions ?? "");
        startEvt.Metadata = new Dictionary<string, string>
        {
            [MetadataKeys.Instructions] = node.Instructions ?? "",
            ["tools"] = string.Join(", ", node.Tools ?? [])
        };
        yield return startEvt;

        var effectiveTools = node.Tools is { Count: > 0 }
            ? node.Tools
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
            node.Instructions, node.Name ?? "", cancellationToken);
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
        var hasResponseFormat = node.OutputFormat is "json" or "json_schema";

        if (hasTools || hasResponseFormat)
        {
            // 有工具或強制格式 — 用 GetResponseAsync（需要 ChatOptions）
            var events = await RunAgentWithToolsAsync(client, messages, resolvedTools ?? [],
                agentName, node.OutputFormat, node.OutputSchema, cancellationToken);
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
        string agentName, string? outputFormat, string? outputSchema,
        CancellationToken cancellationToken)
    {
        var events = new List<ExecutionEvent>();
        var chatOptions = new ChatOptions { Tools = tools.Cast<AITool>().ToList() };

        // 強制輸出格式（複用 Engine 的 ResponseFormat 邏輯）
        if (outputFormat == "json")
        {
            chatOptions.ResponseFormat = ChatResponseFormat.Json;
        }
        else if (outputFormat == "json_schema" && !string.IsNullOrWhiteSpace(outputSchema))
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

            // Token 計數：從 response.Usage 提取，fallback 字元數 / 4 估算
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
        PlannedNode node,
        string input,
        GoalExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = node.Name;
        var items = SplitInput(input, node.SplitMode ?? "delimiter", node.Delimiter ?? "\n");
        var maxItems = node.MaxItems ?? 10;
        if (items.Count > maxItems) items = items[..maxItems];

        yield return ExecutionEvent.NodeExecuting(NodeTypes.Iteration, $"{nodeName} ({items.Count} items)");

        var maxConcurrency = node.MaxConcurrency is > 1 ? node.MaxConcurrency.Value : 1;

        if (maxConcurrency <= 1)
        {
            // 順序執行（預設）
            var results = new List<string>();
            for (var i = 0; i < items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[i].Trim();
                if (string.IsNullOrWhiteSpace(item)) continue;

                var itemAgent = new PlannedNode
                {
                    NodeType = NodeTypes.Agent,
                    Name = $"{nodeName}[{i + 1}]",
                    Instructions = node.Instructions ?? "Process the following item and provide a result.",
                    Tools = node.Tools
                };

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
                var itemAgent = new PlannedNode
                {
                    NodeType = NodeTypes.Agent,
                    Name = $"{nodeName}[{i + 1}]",
                    Instructions = node.Instructions ?? "Process the following item and provide a result.",
                    Tools = node.Tools
                };

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

        // Replay buffered events（按原始順序）
        foreach (var (events, _) in allResults)
        {
            foreach (var evt in events)
                yield return evt;
        }

        var parallelMerged = string.Join("\n\n", allResults.Select(r => r.Result).Where(r => !string.IsNullOrEmpty(r)));
        yield return ExecutionEvent.NodeCompleted(NodeTypes.Iteration, nodeName, parallelMerged);
    }

    private static List<string> SplitInput(string input, string splitMode, string delimiter)
    {
        if (splitMode == "json-array")
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
        PlannedNode node,
        string input,
        GoalExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = node.Name;
        var branches = node.Branches;

        if (branches is not { Count: > 0 })
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
            // {{node:}} 在 ExecuteAgentNodeAsync 中解析
            var branchAgent = new PlannedNode
            {
                NodeType = NodeTypes.Agent,
                Name = $"{nodeName}/{branch.Name}",
                Instructions = branch.Goal,
                Tools = branch.Tools
            };
            // 只傳 branch name 作為 input，不傳完整的使用者輸入
            // 避免 LLM 看到所有項目後搜尋全部（gpt-4o-mini 不遵守隔離指令）
            var branchInput = branch.Name;
            return RunBranchThrottledAsync(throttle, branchAgent, branchInput, request, cancellationToken);
        }).ToList();

        var branchResults = await Task.WhenAll(branchTasks);

        // 發出每個分支的事件
        foreach (var (events, _) in branchResults)
        {
            foreach (var evt in events)
            {
                yield return evt;
            }
        }

        // 合併結果
        var mergeStrategy = node.MergeStrategy ?? "labeled";
        var mergedOutput = MergeResults(branches, branchResults, mergeStrategy);

        yield return ExecutionEvent.NodeCompleted(NodeTypes.Parallel, nodeName, mergedOutput);
    }

    private async Task<(List<ExecutionEvent> Events, string Result)> RunBranchThrottledAsync(
        SemaphoreSlim throttle, PlannedNode branchAgent, string input,
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
        PlannedNode branchAgent, string input, GoalExecutionRequest request,
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
        List<ParallelBranchConfig> branches,
        (List<ExecutionEvent> Events, string Result)[] results,
        string mergeStrategy)
    {
        return mergeStrategy switch
        {
            "json" => JsonSerializer.Serialize(
                branches.Zip(results, (b, r) => new { b.Name, r.Result })
                    .ToDictionary(x => x.Name, x => x.Result)),

            "join" => string.Join("\n\n", results.Select(r => r.Result)),

            _ => string.Join("\n\n", // "labeled" (default)
                branches.Zip(results, (b, r) => $"[{b.Name}]\n{r.Result}"))
        };
    }

    // ════════════════════════════════════════
    // Loop 節點 — 重複執行直到條件滿足或達到上限
    // ════════════════════════════════════════

    private async IAsyncEnumerable<ExecutionEvent> ExecuteLoopNodeAsync(
        PlannedNode node,
        string input,
        GoalExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = node.Name;
        var maxIterations = node.MaxIterations ?? 5;
        var conditionType = node.ConditionType ?? "contains";
        var conditionValue = node.ConditionValue ?? "";
        var currentInput = input;

        for (var i = 0; i < maxIterations; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 檢查退出條件（用上一輪的結果判斷）
            if (EvaluateCondition(currentInput, conditionType, conditionValue))
            {
                yield return ExecutionEvent.NodeCompleted(NodeTypes.Loop,
                    $"{nodeName} (exit at iteration {i + 1})", currentInput);
                yield break;
            }

            yield return ExecutionEvent.NodeExecuting(NodeTypes.Loop, $"{nodeName} (iteration {i + 1}/{maxIterations})");

            // {{node:}} 在 ExecuteAgentNodeAsync 中解析
            var bodyAgent = new PlannedNode
            {
                NodeType = NodeTypes.Agent,
                Name = $"{nodeName}[{i + 1}]",
                Instructions = node.Instructions ?? "Improve and refine the following content based on the context.",
                Tools = node.Tools
            };

            var bodyResult = new StringBuilder();
            await foreach (var evt in ExecuteAgentNodeAsync(bodyAgent, currentInput, request, cancellationToken))
            {
                if (evt.Type == EventTypes.AgentCompleted)
                    bodyResult.Append(evt.Text);
                yield return evt;
            }

            var newResult = bodyResult.ToString();

            // 如果新結果觸發退出條件，保留上一輪的實質內容（新結果可能只是確認訊息）
            if (EvaluateCondition(newResult, conditionType, conditionValue))
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

    private static bool EvaluateCondition(string input, string conditionType, string conditionValue)
    {
        return conditionType switch
        {
            "contains" => input.Contains(conditionValue, StringComparison.OrdinalIgnoreCase),
            "regex" => TryRegexMatch(input, conditionValue),
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
        PlannedNode node,
        string input,
        GoalExecutionRequest request)
    {
        var nodeName = string.IsNullOrWhiteSpace(node.Name) ? "HTTP Request" : node.Name;
        yield return ExecutionEvent.AgentStarted(nodeName);
        yield return ExecutionEvent.ToolCall(nodeName, "HTTP", node.HttpApiId ?? node.HttpUrl ?? "");

        string result;

        var apiDef = ResolveHttpApiDefinition(node, request);
        if (apiDef is null)
        {
            result = "[HTTP Error] No HttpApiId or inline URL specified";
        }
        else
        {
            var escapedInput = JsonSerializer.Serialize(input).Trim('"');
            var argsJson = (node.HttpArgsTemplate ?? "{}").Replace("{input}", escapedInput);

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

    private static HttpApiDefinition? ResolveHttpApiDefinition(PlannedNode node, GoalExecutionRequest request)
    {
        // Catalog 模式
        if (!string.IsNullOrWhiteSpace(node.HttpApiId) &&
            request.HttpApis.TryGetValue(node.HttpApiId, out var catalogDef))
        {
            return catalogDef;
        }

        // Inline 模式
        if (!string.IsNullOrWhiteSpace(node.HttpUrl))
        {
            return new HttpApiDefinition
            {
                Id = node.Name ?? "inline",
                Name = node.Name ?? "inline-http",
                Url = node.HttpUrl,
                Method = string.IsNullOrWhiteSpace(node.HttpMethod) ? "GET" : node.HttpMethod,
                Headers = node.HttpHeaders ?? "",
                BodyTemplate = node.HttpBodyTemplate ?? "",
                ContentType = string.IsNullOrWhiteSpace(node.HttpContentType) ? "application/json" : node.HttpContentType,
                TimeoutSeconds = node.HttpTimeoutSeconds ?? 15,
                ResponseMaxLength = node.HttpResponseMaxLength ?? 2000,
                AuthMode = string.IsNullOrWhiteSpace(node.HttpAuthMode) ? "none" : node.HttpAuthMode,
                AuthCredential = node.HttpAuthCredential ?? "",
                AuthKeyName = node.HttpAuthKeyName ?? "",
                RetryCount = node.HttpRetryCount ?? 0,
                RetryDelayMs = node.HttpRetryDelayMs ?? 1000,
                ResponseFormat = string.IsNullOrWhiteSpace(node.HttpResponseFormat) ? "text" : node.HttpResponseFormat,
                ResponseJsonPath = node.HttpResponseJsonPath ?? "",
            };
        }

        return null;
    }

    // ════════════════════════════════════════
    // Code 節點
    // ════════════════════════════════════════

    private static ExecutionEvent ExecuteCodeNode(PlannedNode node, string input)
    {
        var output = TransformHelper.ApplyTransform(
            node.TransformType ?? "template",
            input,
            node.TransformPattern ?? "{{input}}",
            node.TransformReplacement);

        return ExecutionEvent.NodeCompleted(NodeTypes.Code, node.Name, output);
    }

    // ════════════════════════════════════════
    // Condition 節點
    // ════════════════════════════════════════

    private static ExecutionEvent ExecuteConditionNode(PlannedNode node, string input)
    {
        var conditionType = node.ConditionType ?? "contains";
        var conditionValue = node.ConditionValue ?? "";
        var met = EvaluateCondition(input, conditionType, conditionValue);

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
    /// 解析文字中的 {{node:step_name}} 引用，替換為對應節點的輸出。
    /// 委派給 Engine 共用的 NodeReferenceResolver。
    /// </summary>
    internal string ResolveNodeReferences(string? text) =>
        NodeReferenceResolver.Resolve(text, NodeOutputs);

    /// <summary>
    /// 非同步解析 {{node:}} 引用，超過門檻時用 compactor 壓縮後注入。
    /// compressContext 提供壓縮方向（當前節點的 instructions 描述）。
    /// </summary>
    internal async Task<string> ResolveNodeReferencesAsync(
        string? text, string compressContext, CancellationToken ct = default)
    {
        if (ReferenceCompactor is null)
            return ResolveNodeReferences(text);

        return await NodeReferenceResolver.ResolveAsync(
            text, NodeOutputs, ReferenceCompactor, compressContext, ct);
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
