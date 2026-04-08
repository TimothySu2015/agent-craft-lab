using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Diagnostics;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Strategies;

/// <summary>
/// 命令式執行期間的共享狀態，避免在方法間傳遞大量參數。
/// </summary>
public class ImperativeExecutionState
{
    public required Dictionary<string, List<(string ToId, string FromOutput)>> Adjacency { get; init; }
    public required Dictionary<string, WorkflowNode> NodeMap { get; init; }
    public required Dictionary<string, ChatClientAgent> Agents { get; init; }
    public required Dictionary<string, IChatClient> ChatClients { get; init; }
    public required Dictionary<string, List<ChatMessage>> ChatHistories { get; init; }
    public required Dictionary<string, int> LoopCounters { get; init; }
    public ChatClientHolder JudgeHolder { get; init; } = new();
    public HumanInputBridge? HumanBridge { get; init; }
    public string PreviousResult { get; set; } = "";
    public FileAttachment? Attachment { get; set; }
    public Dictionary<string, WorkflowNode>? A2ANodes { get; init; }
    public A2AClientService? A2AClient { get; init; }
    public required AgentExecutionContext AgentContext { get; init; }
    public required WorkflowExecutionRequest Request { get; init; }
    public Services.WorkflowHookRunner? HookRunner { get; init; }
    public WorkflowHooks? Hooks { get; init; }
    public string WorkflowName { get; init; } = "";
    public required Services.IHistoryStrategy HistoryStrategy { get; init; }

    /// <summary>Checkpoint 用的執行 ID（= AG-UI runId）。</summary>
    public string ExecutionId { get; init; } = "";

    /// <summary>使用者原始輸入（Context Passing 模式用）。</summary>
    public string OriginalUserMessage { get; init; } = "";

    /// <summary>各節點執行結果（accumulate 模式用）。</summary>
    public Dictionary<string, string> NodeResults { get; init; } = new();

    /// <summary>Context Passing 模式：previous-only / with-original / accumulate。</summary>
    public string ContextPassing { get; init; } = NodeExecutors.ContextPassingModes.PreviousOnly;

    /// <summary>
    /// Body chain 執行器 — Loop/Iteration/Parallel 的 body 子流程共用。
    /// 由 ImperativeWorkflowStrategy 在初始化時注入。
    /// </summary>
    public Func<string, string, string, ImperativeExecutionState, CancellationToken, Task<string>>? ExecuteBodyChain { get; init; }

    /// <summary>Trace session ID（= AG-UI runId），用於 OTel Activity 的 session.id tag。</summary>
    public string? SessionId { get; init; }

    /// <summary>Debug Mode 暫停橋接器（non-null = Debug 模式啟用）。</summary>
    public DebugBridge? DebugBridge { get; init; }

    /// <summary>引用壓縮器 — {{node:}} 引用超過門檻時自動壓縮（可選）。</summary>
    public IContextCompactor? ReferenceCompactor { get; init; }
}

/// <summary>
/// 命令式圖走訪執行策略：依照連線圖逐節點執行，支援 condition 分支、loop 迴圈和 chat history。
/// </summary>
public class ImperativeWorkflowStrategy : IWorkflowStrategy
{
    private readonly IHistoryStrategy _historyStrategy;
    private readonly NodeExecutors.NodeExecutorRegistry? _executorRegistry;
    private readonly ICheckpointStore? _checkpointStore;
    private readonly ILogger? _logger;

    public ImperativeWorkflowStrategy(IHistoryStrategy historyStrategy,
        NodeExecutors.NodeExecutorRegistry? executorRegistry = null,
        ICheckpointStore? checkpointStore = null,
        ILogger<ImperativeWorkflowStrategy>? logger = null)
    {
        _historyStrategy = historyStrategy;
        _executorRegistry = executorRegistry;
        _checkpointStore = checkpointStore;
        _logger = logger;
    }

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowStrategyContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sessionId = context.SessionId;

        using var workflowActivity = EngineActivitySource.Source.StartActivity(
            "workflow_execute", ActivityKind.Server);
        workflowActivity?.SetTag("workflow.name", context.Payload.WorkflowSettings.Type);
        workflowActivity?.SetTag("workflow.node_count", context.Payload.Nodes.Count);
        if (sessionId is not null)
            workflowActivity?.SetTag(EngineActivitySource.SessionIdTag, sessionId);

        var payload = context.Payload;

        var adj = new Dictionary<string, List<(string ToId, string FromOutput)>>();
        foreach (var conn in payload.Connections)
        {
            if (!adj.ContainsKey(conn.From))
                adj[conn.From] = [];
            adj[conn.From].Add((conn.To, conn.FromOutput));
        }

        var (startNode, startPath) = FindStartNode(payload);
        if (startNode is null)
        {
            yield return ExecutionEvent.Error("Cannot find start node for imperative execution.");
            yield break;
        }
        yield return ExecutionEvent.StartNodeResolved(startNode.Id, startPath);

        var state = new ImperativeExecutionState
        {
            Adjacency = adj,
            NodeMap = payload.Nodes.ToDictionary(n => n.Id),
            Agents = context.AgentContext.Agents,
            ChatClients = context.AgentContext.ChatClients,
            ChatHistories = InitializeChatHistories(payload.Nodes),
            LoopCounters = new Dictionary<string, int>(),
            JudgeHolder = new ChatClientHolder { Client = context.AgentContext.JudgeClient },
            HumanBridge = context.AgentContext.HumanBridge,
            PreviousResult = context.Request.UserMessage,
            Attachment = context.Request.Attachment,
            A2ANodes = context.AgentContext.A2ANodes,
            A2AClient = context.AgentContext.A2AClient,
            AgentContext = context.AgentContext,
            Request = context.Request,
            HookRunner = context.HookRunner,
            Hooks = context.Hooks,
            WorkflowName = context.Payload.WorkflowSettings.Type,
            HistoryStrategy = _historyStrategy,
            ExecutionId = sessionId ?? Guid.NewGuid().ToString("N"),
            OriginalUserMessage = context.Request.UserMessage,
            NodeResults = new Dictionary<string, string>(),
            ContextPassing = context.Payload.WorkflowSettings.ContextPassing,
            ExecuteBodyChain = ExecuteBodyChainAsync,
            SessionId = sessionId,
            DebugBridge = context.AgentContext.DebugBridge,
            ReferenceCompactor = context.AgentContext.ChatClients.Values.FirstOrDefault() is { } firstClient
                ? new LlmContextCompactor(firstClient) : null
        };

        var currentNodeId = startNode.Id;
        var completedNodeIds = new List<string>();
        var checkpointIteration = 0;

        while (currentNodeId is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!state.NodeMap.TryGetValue(currentNodeId, out var currentNode))
                break;

            // 優先查 NodeExecutorRegistry（已提取的節點走 registry）
            var executor = _executorRegistry?.Get(currentNode.Type);
            if (executor is not null)
            {
                var nodeName = currentNode.Name ?? currentNode.Id;

                // ── 投機執行：llm-judge Condition 同時搶跑兩條分支第一個節點 ──
                if (executor is NodeExecutors.ConditionNodeExecutor
                    && currentNode.ConditionType?.Equals("llm-judge", StringComparison.OrdinalIgnoreCase) == true
                    && context.Payload.WorkflowSettings.SpeculativeExecution)
                {
                    var specResult = await ExecuteSpeculativeConditionAsync(
                        currentNodeId, currentNode, executor, state, cancellationToken);
                    if (specResult is not null)
                    {
                        foreach (var evt in specResult.Events)
                            yield return evt;
                        state.PreviousResult = specResult.WinnerResult;
                        currentNodeId = specResult.NextNodeId;

                        // Checkpoint
                        if (_checkpointStore is not null)
                        {
                            completedNodeIds.Add(currentNode.Id);
                            completedNodeIds.Add(specResult.WinnerNodeId);
                            SaveCheckpointFireAndForget(state.ExecutionId, checkpointIteration++,
                                new ImperativeCheckpointSnapshot
                                {
                                    CompletedNodeIds = [.. completedNodeIds],
                                    PreviousResult = state.PreviousResult,
                                    NextNodeId = currentNodeId ?? "",
                                    NodeResults = new(state.NodeResults),
                                    LoopCounters = new(state.LoopCounters),
                                    OriginalUserMessage = state.OriginalUserMessage,
                                    ContextPassing = state.ContextPassing
                                });
                        }

                        continue;
                    }
                    // 投機不適用 → fall through 到正常執行
                }

                // State Sync：節點開始執行
                if (!NodeTypeRegistry.IsMeta(currentNode.Type))
                {
                    yield return ExecutionEvent.NodeExecuting(currentNode.Type, nodeName);
                }

                var collectedEvents = new List<ExecutionEvent>();
                await foreach (var evt in executor.ExecuteAsync(currentNodeId, currentNode, state, cancellationToken))
                {
                    collectedEvents.Add(evt);
                    if (evt.Type == EventTypes.AgentCompleted)
                    {
                        state.PreviousResult = evt.Text ?? "";
                        TrackNodeResult(state, currentNodeId, evt.Text);
                    }
                    yield return evt;
                }

                var nodeResult = await executor.BuildResultAsync(currentNodeId, currentNode, state, collectedEvents, cancellationToken);

                if (nodeResult.Output is not null && !collectedEvents.Any(e => e.Type == EventTypes.AgentCompleted))
                {
                    state.PreviousResult = nodeResult.Output!;
                    TrackNodeResult(state, currentNodeId, nodeResult.Output);
                }

                // State Sync：節點完成（在 Activity Stop 之後，確保 GetSpans 能拿到）
                if (!NodeTypeRegistry.IsMeta(currentNode.Type))
                {
                    yield return ExecutionEvent.NodeCompleted(currentNode.Type, nodeName, state.PreviousResult);
                }

                if (nodeResult.ManagesOwnNavigation)
                    currentNodeId = nodeResult.NextNodeId;
                else
                    currentNodeId = WorkflowGraphHelper.GetNextNodeId(
                        state.Adjacency, currentNodeId, nodeResult.OutputPort ?? OutputPorts.Output1);

                // ─── Checkpoint：節點完成後存快照 ───
                if (_checkpointStore is not null && !NodeTypeRegistry.IsMeta(currentNode.Type))
                {
                    completedNodeIds.Add(currentNode.Id);
                    SaveCheckpointFireAndForget(state.ExecutionId, checkpointIteration++,
                        new ImperativeCheckpointSnapshot
                        {
                            CompletedNodeIds = [.. completedNodeIds],
                            PreviousResult = state.PreviousResult,
                            NextNodeId = currentNodeId ?? "",
                            NodeResults = new(state.NodeResults),
                            LoopCounters = new(state.LoopCounters),
                            OriginalUserMessage = state.OriginalUserMessage,
                            ContextPassing = state.ContextPassing
                        });
                }

                // ─── Debug Mode：節點完成後暫停等待使用者操作 ───
                if (state.DebugBridge is not null && !NodeTypeRegistry.IsMeta(currentNode.Type))
                {
                    yield return ExecutionEvent.DebugPaused(currentNode.Type, nodeName, state.PreviousResult);
                    var action = await state.DebugBridge.WaitForActionAsync(cancellationToken);
                    yield return ExecutionEvent.DebugResumed(nodeName, action.ToString());

                    if (action == DebugAction.Rerun)
                    {
                        // 不更新 currentNodeId — continue 回到 while 開頭重跑此節點
                        currentNodeId = currentNode.Id;
                        continue;
                    }

                    if (action == DebugAction.Skip && currentNodeId is not null)
                    {
                        // 跳過下一個節點：再 navigate 一次
                        currentNodeId = WorkflowGraphHelper.GetNextNodeId(
                            state.Adjacency, currentNodeId, OutputPorts.Output1);
                    }
                }

                continue;
            }

            // 所有節點都在 registry — 未知節點跳過
            currentNodeId = WorkflowGraphHelper.GetNextNodeId(state.Adjacency, currentNodeId, OutputPorts.Output1);
        }
    }

    /// <summary>
    /// 非同步存 checkpoint（fire-and-forget，不阻塞主流程）。
    /// </summary>
    private void SaveCheckpointFireAndForget(string executionId, int iteration, ImperativeCheckpointSnapshot snapshot)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var json = snapshot.Serialize();
                var doc = new CheckpointDocument
                {
                    Id = $"imp-{executionId}-{iteration}",
                    ExecutionId = executionId,
                    Iteration = iteration,
                    StateJson = json,
                    StateSizeBytes = Encoding.UTF8.GetByteCount(json),
                    CreatedAt = DateTime.UtcNow
                };
                await _checkpointStore!.SaveAsync(doc);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to save imperative checkpoint {ExecutionId}/{Iteration}", executionId, iteration);
            }
        });
    }

    /// <summary>
    /// 從 checkpoint 恢復執行 — 載入快照中的執行狀態，用當前畫布定義的 NodeMap/Agents，
    /// 從指定節點開始重跑。節點設定來自當前畫布（使用者可能已修改），input 來自 checkpoint。
    /// </summary>
    public async IAsyncEnumerable<ExecutionEvent> ResumeFromNodeAsync(
        WorkflowStrategyContext context,
        ImperativeCheckpointSnapshot checkpoint,
        string rerunNodeId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sessionId = context.SessionId;
        var payload = context.Payload;

        var adj = new Dictionary<string, List<(string ToId, string FromOutput)>>();
        foreach (var conn in payload.Connections)
        {
            if (!adj.ContainsKey(conn.From))
                adj[conn.From] = [];
            adj[conn.From].Add((conn.To, conn.FromOutput));
        }

        // 從 checkpoint 恢復狀態，但 NodeMap/Agents 用當前畫布定義
        var state = new ImperativeExecutionState
        {
            Adjacency = adj,
            NodeMap = payload.Nodes.ToDictionary(n => n.Id),
            Agents = context.AgentContext.Agents,
            ChatClients = context.AgentContext.ChatClients,
            ChatHistories = InitializeChatHistories(payload.Nodes),
            LoopCounters = new Dictionary<string, int>(checkpoint.LoopCounters),
            JudgeHolder = new ChatClientHolder { Client = context.AgentContext.JudgeClient },
            HumanBridge = context.AgentContext.HumanBridge,
            PreviousResult = checkpoint.PreviousResult,
            Attachment = context.Request.Attachment,
            A2ANodes = context.AgentContext.A2ANodes,
            A2AClient = context.AgentContext.A2AClient,
            AgentContext = context.AgentContext,
            Request = context.Request,
            HookRunner = context.HookRunner,
            Hooks = context.Hooks,
            WorkflowName = context.Payload.WorkflowSettings.Type,
            HistoryStrategy = _historyStrategy,
            ExecutionId = sessionId ?? Guid.NewGuid().ToString("N"),
            OriginalUserMessage = checkpoint.OriginalUserMessage,
            NodeResults = new Dictionary<string, string>(checkpoint.NodeResults),
            ContextPassing = checkpoint.ContextPassing,
            ExecuteBodyChain = ExecuteBodyChainAsync,
            SessionId = sessionId,
            ReferenceCompactor = context.AgentContext.ChatClients.Values.FirstOrDefault() is { } resumeClient
                ? new LlmContextCompactor(resumeClient) : null
        };

        var currentNodeId = rerunNodeId;
        var completedNodeIds = new List<string>(checkpoint.CompletedNodeIds);
        var checkpointIteration = completedNodeIds.Count;

        // 主迴圈 — 與 ExecuteAsync 共用相同的節點走訪邏輯
        // TODO: Phase 3 完成後評估是否抽取 ExecuteNodeLoopAsync 共用方法（Debug 暫停邏輯會讓兩者分化）
        while (currentNodeId is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!state.NodeMap.TryGetValue(currentNodeId, out var currentNode))
                break;

            var executor = _executorRegistry?.Get(currentNode.Type);
            if (executor is not null)
            {
                var nodeName = currentNode.Name ?? currentNode.Id;

                if (!NodeTypeRegistry.IsMeta(currentNode.Type))
                {
                    yield return ExecutionEvent.NodeExecuting(currentNode.Type, nodeName);
                }

                var collectedEvents = new List<ExecutionEvent>();
                await foreach (var evt in executor.ExecuteAsync(currentNodeId, currentNode, state, cancellationToken))
                {
                    collectedEvents.Add(evt);
                    if (evt.Type == EventTypes.AgentCompleted)
                    {
                        state.PreviousResult = evt.Text ?? "";
                        TrackNodeResult(state, currentNodeId, evt.Text);
                    }
                    yield return evt;
                }

                var nodeResult = await executor.BuildResultAsync(currentNodeId, currentNode, state, collectedEvents, cancellationToken);

                if (nodeResult.Output is not null && !collectedEvents.Any(e => e.Type == EventTypes.AgentCompleted))
                {
                    state.PreviousResult = nodeResult.Output!;
                    TrackNodeResult(state, currentNodeId, nodeResult.Output);
                }

                if (!NodeTypeRegistry.IsMeta(currentNode.Type))
                {
                    yield return ExecutionEvent.NodeCompleted(currentNode.Type, nodeName, state.PreviousResult);
                }

                if (nodeResult.ManagesOwnNavigation)
                    currentNodeId = nodeResult.NextNodeId;
                else
                    currentNodeId = WorkflowGraphHelper.GetNextNodeId(
                        state.Adjacency, currentNodeId, nodeResult.OutputPort ?? OutputPorts.Output1);

                if (_checkpointStore is not null && !NodeTypeRegistry.IsMeta(currentNode.Type))
                {
                    completedNodeIds.Add(currentNode.Id);
                    SaveCheckpointFireAndForget(state.ExecutionId, checkpointIteration++,
                        new ImperativeCheckpointSnapshot
                        {
                            CompletedNodeIds = [.. completedNodeIds],
                            PreviousResult = state.PreviousResult,
                            NextNodeId = currentNodeId ?? "",
                            NodeResults = new(state.NodeResults),
                            LoopCounters = new(state.LoopCounters),
                            OriginalUserMessage = state.OriginalUserMessage,
                            ContextPassing = state.ContextPassing
                        });
                }

                continue;
            }

            currentNodeId = WorkflowGraphHelper.GetNextNodeId(state.Adjacency, currentNodeId, OutputPorts.Output1);
        }
    }

    /// <summary>
    /// 走訪 body 子流程（loop body / iteration body 共用）。
    /// 從 startNodeId 開始，依序執行 Agent/A2A/Human/Code 節點，直到遇到 stopNodeId 或結尾。
    /// </summary>
    private async Task<string> ExecuteBodyChainAsync(
        string startNodeId, string stopNodeId, string input,
        ImperativeExecutionState state, CancellationToken cancellationToken)
    {
        var result = input;
        var nodeId = startNodeId;
        while (nodeId is not null && nodeId != stopNodeId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!state.NodeMap.TryGetValue(nodeId, out var node))
                break;

            // 優先走 NodeExecutorRegistry
            var executor = _executorRegistry?.Get(node.Type);
            if (executor is not null)
            {
                state.PreviousResult = result;
                var collectedEvents = new List<ExecutionEvent>();
                await foreach (var evt in executor.ExecuteAsync(nodeId, node, state, cancellationToken))
                {
                    collectedEvents.Add(evt);
                    if (evt.Type == EventTypes.AgentCompleted)
                    {
                        result = evt.Text ?? "";
                        TrackNodeResult(state, nodeId, evt.Text);
                    }
                }

                var execResult = await executor.BuildResultAsync(nodeId, node, state, collectedEvents, cancellationToken);
                if (execResult.Output is not null && !collectedEvents.Any(e => e.Type == EventTypes.AgentCompleted))
                {
                    result = execResult.Output;
                    TrackNodeResult(state, nodeId, execResult.Output);
                }
            }

            nodeId = WorkflowGraphHelper.GetNextNodeId(state.Adjacency, nodeId, OutputPorts.Output1);
        }

        return result ?? "";
    }

    private static void TrackNodeResult(ImperativeExecutionState state, string nodeId, string? output)
    {
        // 永遠儲存（{{node:}} 跨節點引用需要，不限於 Accumulate 模式）
        if (output is not null)
            state.NodeResults[nodeId] = output;
    }

    /// <summary>
    /// 根據節點的 OutputFormat/OutputSchema 建構 ChatOptions。
    /// </summary>
    internal static ChatOptions? BuildResponseFormatOptions(WorkflowNode node)
    {
        ChatResponseFormat? responseFormat = null;
        if (node.OutputFormat == "json")
        {
            responseFormat = ChatResponseFormat.Json;
        }
        else if (node.OutputFormat == "json_schema" && !string.IsNullOrWhiteSpace(node.OutputSchema))
        {
            try
            {
                var schemaElement = JsonDocument.Parse(node.OutputSchema).RootElement;
                responseFormat = ChatResponseFormat.ForJsonSchema(
                    schemaElement,
                    schemaName: "OutputSchema",
                    schemaDescription: "Agent output schema defined by user");
            }
            catch { /* invalid schema, fall back to text */ }
        }

        return responseFormat is not null ? new ChatOptions { ResponseFormat = responseFormat } : null;
    }

    internal static (WorkflowNode? Node, string Path) FindStartNode(WorkflowPayload payload)
    {
        var nodeIds = new HashSet<string>(payload.Nodes.Select(n => n.Id));

        // Start 節點連出的目標就是起點
        var startNodeConn = payload.Connections
            .FirstOrDefault(c => payload.Nodes.Any(n => n.Id == c.From && n.Type == NodeTypes.Start));
        if (startNodeConn is not null)
        {
            var target = payload.Nodes.FirstOrDefault(n => n.Id == startNodeConn.To);
            if (target is not null) return (target, "start-node");
        }

        // 前端過濾掉 start/end 節點，但 connections 仍引用。From 不在 nodes 裡 → phantom start。
        var phantomStartConn = payload.Connections
            .FirstOrDefault(c => !nodeIds.Contains(c.From));
        if (phantomStartConn is not null)
        {
            var target = payload.Nodes.FirstOrDefault(n => n.Id == phantomStartConn.To);
            if (target is not null) return (target, "phantom-start");
        }

        // Fallback：找沒有 incoming connection 的第一個 executable 節點
        var hasIncoming = new HashSet<string>(
            payload.Connections.Where(c => nodeIds.Contains(c.From)).Select(c => c.To));
        var firstExecutable = payload.Nodes
            .Where(n => NodeTypeRegistry.IsExecutable(n.Type))
            .FirstOrDefault(n => !hasIncoming.Contains(n.Id));

        if (firstExecutable is not null) return (firstExecutable, "no-incoming");
        var fallback = payload.Nodes.FirstOrDefault(n => NodeTypeRegistry.IsAgentLike(n.Type) || n.Type == NodeTypes.Code);
        return (fallback, "fallback-first-agent");
    }

    internal static Dictionary<string, List<ChatMessage>> InitializeChatHistories(List<WorkflowNode> nodes)
    {
        var histories = new Dictionary<string, List<ChatMessage>>();
        foreach (var node in nodes.Where(n => n.HistoryProvider == "inmemory"))
        {
            var systemPrompt = string.IsNullOrWhiteSpace(node.Instructions)
                ? "You are a helpful assistant."
                : node.Instructions;
            histories[node.Id] = [new(ChatRole.System, systemPrompt)];
        }
        return histories;
    }

    /// <summary>
    /// 將輸入文字拆分為迭代陣列。
    /// </summary>
    internal static List<string> SplitIterationInput(WorkflowNode node, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        var mode = node.SplitMode?.ToLowerInvariant() ?? "json-array";

        if (mode == "json-array")
        {
            // 嘗試直接 parse；失敗時從文字中抽取 [...] 再試（LLM 常在 JSON 前後加說明文字）
            var jsonInput = input;
            if (!input.TrimStart().StartsWith('['))
            {
                var bracketStart = input.IndexOf('[');
                var bracketEnd = input.LastIndexOf(']');
                if (bracketStart >= 0 && bracketEnd > bracketStart)
                {
                    jsonInput = input[bracketStart..(bracketEnd + 1)];
                }
            }

            try
            {
                using var doc = JsonDocument.Parse(jsonInput);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return doc.RootElement.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText())
                        .ToList();
                }
            }
            catch { /* not valid JSON array, fall through to delimiter */ }
        }

        var delimiter = string.IsNullOrEmpty(node.IterationDelimiter) ? "\n" : node.IterationDelimiter;
        return input.Split(delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    internal static string Truncate(string text, int maxLen = 80)
    {
        return text.Length <= maxLen ? text : text[..maxLen] + "...";
    }

    internal static string MergeParallelResults(List<(string Name, string Result)> results, string? strategy)
    {
        return (strategy?.ToLowerInvariant() ?? "labeled") switch
        {
            "join" => string.Join("\n", results.Select(r => r.Result)),
            "json" => JsonSerializer.Serialize(results.ToDictionary(r => r.Name, r => r.Result)),
            _ => string.Join("\n\n", results.Select(r => $"[{r.Name}]\n{r.Result}"))  // labeled
        };
    }

    // ════════════════════════════════════════
    // 投機執行 — llm-judge Condition 同時搶跑兩條分支
    // ════════════════════════════════════════

    private sealed record SpeculativeResult(
        List<ExecutionEvent> Events,
        string WinnerResult,
        string WinnerNodeId,
        string? NextNodeId);

    /// <summary>
    /// 嘗試投機執行 llm-judge Condition 節點。
    /// 同時啟動 condition 評估 + TRUE/FALSE 兩條分支的第一個 agent 節點。
    /// Condition 結果出來後取消輸家分支，採用贏家的 buffered events。
    /// 回傳 null 表示不適合投機（分支不存在、不是 agent 等），呼叫端應 fall through 到正常執行。
    /// </summary>
    private async Task<SpeculativeResult?> ExecuteSpeculativeConditionAsync(
        string conditionNodeId, WorkflowNode conditionNode,
        NodeExecutors.INodeExecutor conditionExecutor,
        ImperativeExecutionState state, CancellationToken cancellationToken)
    {
        var conditionName = conditionNode.Name ?? conditionNodeId;

        // 查兩條分支的起始節點
        var trueBranchId = WorkflowGraphHelper.GetNextNodeId(state.Adjacency, conditionNodeId, OutputPorts.Output1);
        var falseBranchId = WorkflowGraphHelper.GetNextNodeId(state.Adjacency, conditionNodeId, OutputPorts.Output2);

        // 兩條分支都必須存在且是 agent-like 節點
        if (trueBranchId is null || falseBranchId is null) return null;
        if (!state.NodeMap.TryGetValue(trueBranchId, out var trueNode) || !NodeTypeRegistry.IsAgentLike(trueNode.Type)) return null;
        if (!state.NodeMap.TryGetValue(falseBranchId, out var falseNode) || !NodeTypeRegistry.IsAgentLike(falseNode.Type)) return null;

        var trueExecutor = _executorRegistry?.Get(trueNode.Type);
        var falseExecutor = _executorRegistry?.Get(falseNode.Type);
        if (trueExecutor is null || falseExecutor is null) return null;

        // 搶跑 input = condition 的 input（此時已確定）
        var input = state.PreviousResult;

        // 兩個獨立 CTS — condition 結果出來後只取消輸家，不影響贏家
        using var trueCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var falseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 統一設定 PreviousResult（兩個分支都讀同一個值，分支內不修改）
        state.PreviousResult = input;

        // 同時啟動：condition 評估 + 兩條分支搶跑
        var conditionTask = conditionExecutor.BuildResultAsync(
            conditionNodeId, conditionNode, state, [], cancellationToken);
        var trueBranchTask = ExecuteSpeculativeBranchAsync(
            trueBranchId, trueNode, trueExecutor, state, input, trueCts.Token);
        var falseBranchTask = ExecuteSpeculativeBranchAsync(
            falseBranchId, falseNode, falseExecutor, state, input, falseCts.Token);

        // 等 condition 結果先出來
        var conditionResult = await conditionTask;
        var isTrue = conditionResult.OutputPort == OutputPorts.Output1;

        // 只取消輸家分支
        var (winnerTask, loserTask) = isTrue ? (trueBranchTask, falseBranchTask) : (falseBranchTask, trueBranchTask);
        var (winnerId, loserId) = isTrue ? (trueBranchId, falseBranchId) : (falseBranchId, trueBranchId);
        var (winnerNode, loserNode) = isTrue ? (trueNode, falseNode) : (falseNode, trueNode);

        if (isTrue)
            await falseCts.CancelAsync();
        else
            await trueCts.CancelAsync();

        // 等贏家完成（贏家用自己的 CTS，不會被取消）
        var (winnerEvents, winnerResult) = await winnerTask;

        // 組裝 events
        var events = new List<ExecutionEvent>();

        // 1. Condition 節點 events
        events.Add(ExecutionEvent.NodeExecuting(conditionNode.Type, conditionName));
        events.Add(ExecutionEvent.NodeCompleted(conditionNode.Type, conditionName,
            $"[condition] {conditionName} → {(isTrue ? "TRUE" : "FALSE")} (speculative)"));

        // 2. 贏家分支 events
        var winnerName = winnerNode.Name ?? winnerId;
        events.Add(ExecutionEvent.NodeExecuting(winnerNode.Type, winnerName));
        events.AddRange(winnerEvents);
        events.Add(ExecutionEvent.NodeCompleted(winnerNode.Type, winnerName, winnerResult));

        // 3. 輸家取消 event
        var loserName = loserNode.Name ?? loserId;
        events.Add(ExecutionEvent.NodeCancelled(loserNode.Type, loserName, "speculative execution — wrong branch"));

        // 更新 state
        TrackNodeResult(state, winnerId, winnerResult);

        var nextNodeId = WorkflowGraphHelper.GetNextNodeId(state.Adjacency, winnerId, OutputPorts.Output1);
        return new SpeculativeResult(events, winnerResult, winnerId, nextNodeId);
    }

    /// <summary>
    /// 搶跑單一分支的第一個節點，收集 events 到 buffer（不 yield）。
    /// PreviousResult 由呼叫端在啟動前統一設定，分支內不修改共享 state。
    /// </summary>
    private static async Task<(List<ExecutionEvent> Events, string Result)> ExecuteSpeculativeBranchAsync(
        string nodeId, WorkflowNode node, NodeExecutors.INodeExecutor executor,
        ImperativeExecutionState state, string input,
        CancellationToken cancellationToken)
    {
        var events = new List<ExecutionEvent>();
        var result = input;

        await foreach (var evt in executor.ExecuteAsync(nodeId, node, state, cancellationToken))
        {
            events.Add(evt);
            if (evt.Type == EventTypes.AgentCompleted)
            {
                result = evt.Text ?? "";
            }
        }

        var execResult = await executor.BuildResultAsync(nodeId, node, state, events, cancellationToken);
        if (execResult.Output is not null && !events.Any(e => e.Type == EventTypes.AgentCompleted))
        {
            result = execResult.Output;
        }

        return (events, result);
    }

}
