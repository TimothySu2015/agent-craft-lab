using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Services.Compression;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// Flow 結構化執行器 — IGoalExecutor 的 Node-based 實作。
/// LLM 透過 NODE 語意規劃執行步驟，每步對應一個 Engine 節點類型，
/// 產生可追蹤的 ExecutionTrace，成功後可 Crystallize 為固定 Workflow。
/// </summary>
public sealed class FlowExecutor : IGoalExecutor
{
    private const string AgentName = "Flow Agent";
    private const int MaxPlanningRetries = 2;
    private const int MaxReplanAttempts = 1;

    /// <summary>Context Windowing：觸發壓縮的字元數門檻（預設 3000 chars ≈ 750 tokens）。</summary>
    private const int ContextWindowingThreshold = 3000;

    /// <summary>Context Windowing：壓縮目標 token 數。</summary>
    private const int ContextWindowingBudget = 500;

    private readonly FlowAgentFactory _agentFactory;
    private readonly FlowNodeRunner _nodeRunner;
    private readonly WorkflowCrystallizer _crystallizer;
    private readonly IExecutionMemoryStore? _memoryStore;
    private readonly ICheckpointStore? _checkpointStore;
    private readonly ILogger<FlowExecutor> _logger;

    public FlowExecutor(
        FlowAgentFactory agentFactory,
        FlowNodeRunner nodeRunner,
        WorkflowCrystallizer crystallizer,
        ILogger<FlowExecutor> logger,
        IExecutionMemoryStore? memoryStore = null,
        ICheckpointStore? checkpointStore = null)
    {
        _agentFactory = agentFactory;
        _nodeRunner = nodeRunner;
        _crystallizer = crystallizer;
        _memoryStore = memoryStore;
        _checkpointStore = checkpointStore;
        _logger = logger;
    }

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        GoalExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // F1 Resume：偵測 Options 中的 resumeExecutionId → 走 resume 路徑
        if (request.Options?.TryGetValue("resumeExecutionId", out var resumeIdObj) == true
            && resumeIdObj is string resumeExecId && !string.IsNullOrEmpty(resumeExecId))
        {
            await foreach (var evt in ResumeFromCheckpointAsync(request, resumeExecId, cancellationToken))
            {
                yield return evt;
            }

            yield break;
        }

        var overallSw = Stopwatch.StartNew();
        var trace = new ExecutionTrace
        {
            ExecutionId = request.ExecutionId,
            Goal = request.Goal
        };

        yield return ExecutionEvent.AgentStarted(AgentName, text: request.Goal);

        // 1. 建立規劃用 LLM client
        var (plannerClient, error) = _agentFactory.CreatePlanner(request);
        if (error is not null)
        {
            yield return ExecutionEvent.Error(error);
            yield break;
        }

        // 1.5 查詢歷史 plan 經驗（ReAct 軌跡轉換的 FlowPlan JSON）
        string? experienceHint = null;
        if (_memoryStore is not null)
        {
            try
            {
                var keywords = ExtractKeywords(request.Goal);
                if (keywords.Length > 0)
                {
                    var memories = await _memoryStore.SearchAsync(request.UserId, keywords, limit: 3);
                    experienceHint = memories
                        .Where(m => m.Succeeded && !string.IsNullOrWhiteSpace(m.PlanJson))
                        .Select(m => m.PlanJson!)
                        .FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "查詢歷史 plan 失敗，繼續無經驗規劃");
            }
        }

        if (experienceHint is not null)
        {
            _logger.LogInformation("注入歷史 plan 經驗（{Length} 字元）", experienceHint.Length);
            yield return ExecutionEvent.TextChunk(AgentName,
                $"[Experience] 找到歷史 plan 經驗（{experienceHint.Length} 字元），已注入規劃 prompt");
        }

        // 2. LLM 規劃 — 產生節點序列
        FlowPlan? plan = null;
        long planTokens = 0;
        for (var attempt = 0; attempt <= MaxPlanningRetries; attempt++)
        {
            var (parsedPlan, tokens, planError) = await PlanAsync(plannerClient!, request, experienceHint, cancellationToken);
            if (parsedPlan is not null)
            {
                plan = parsedPlan;
                planTokens = tokens;
                break;
            }

            if (attempt == MaxPlanningRetries)
            {
                yield return ExecutionEvent.Error($"Flow planning failed: {planError}");
                yield break;
            }

            _logger.LogWarning("Flow planning attempt {Attempt} failed: {Error}. Retrying...", attempt + 1, planError);
        }

        // 2.5 Plan 驗證 + 自動修正
        var (validatedPlan, validationWarnings) = FlowPlanValidator.ValidateAndFix(plan!, request);
        plan = validatedPlan;

        if (plan.Nodes.Count == 0)
        {
            yield return ExecutionEvent.Error("Plan validation failed: no executable nodes");
            yield break;
        }

        var planEvt = ExecutionEvent.PlanGenerated(AgentName, FormatPlanForDisplay(plan));
        planEvt.Metadata = new Dictionary<string, string> { [MetadataKeys.Tokens] = planTokens.ToString() };
        yield return planEvt;

        foreach (var warning in validationWarnings)
        {
            _logger.LogWarning("Plan validation: {Warning}", warning);
        }

        // 3. 依序執行節點（支援 Condition 分支跳轉）
        var previousResult = request.Goal;
        var stepSequence = 0;
        var skipIndices = new HashSet<int>(); // Condition 分支：要跳過的節點 index
        var contextCompactor = plannerClient is not null ? new LlmContextCompactor(plannerClient) : null;
        var compressionState = new CompressionState();
        var workingMemory = new FlowWorkingMemory();
        _nodeRunner.WorkingMemory = workingMemory;
        var nodeOutputs = new Dictionary<string, string>();
        _nodeRunner.NodeOutputs = nodeOutputs;
        _nodeRunner.ReferenceCompactor = contextCompactor;
        var replanBudget = MaxReplanAttempts;
        var lastSuccessfulResult = request.Goal; // 最後一個非空的 output（Replan 時回退到此）

        for (var nodeIndex = 0; nodeIndex < plan!.Nodes.Count; nodeIndex++)
        {
            if (skipIndices.Contains(nodeIndex))
                continue;

            var plannedNode = plan.Nodes[nodeIndex];
            var nodeTypeString = NodeConfigHelpers.GetNodeTypeString(plannedNode);
            cancellationToken.ThrowIfCancellationRequested();
            stepSequence++;

            var stepSw = Stopwatch.StartNew();
            yield return ExecutionEvent.NodeExecuting(nodeTypeString, plannedNode.Name);

            var step = new TraceStep
            {
                Sequence = stepSequence,
                NodeType = nodeTypeString,
                NodeName = plannedNode.Name,
                Config = plannedNode,
                Input = previousResult
            };

            // F3 Context Windowing：agent 節點的 input 超過門檻時壓縮（code/condition/http 需要精確輸入，不壓縮）
            if (plannedNode is Schema.AgentNode agentForWindow && previousResult.Length > ContextWindowingThreshold && contextCompactor is not null)
            {
                var nextInstructions = string.IsNullOrEmpty(agentForWindow.Instructions)
                    ? agentForWindow.Name
                    : agentForWindow.Instructions;
                var compressed = await contextCompactor.CompressAsync(
                    previousResult, nextInstructions, ContextWindowingBudget, cancellationToken);
                if (compressed is not null)
                {
                    var tokensSaved = (ModelPricing.EstimateTokens(previousResult) - ModelPricing.EstimateTokens(compressed));
                    compressionState.RecordCompression(tokensSaved);
                    _logger.LogInformation(
                        "[Flow] Context windowing: compressed {Original} → {Compressed} chars (~{Tokens} tokens saved) for node '{Node}'",
                        previousResult.Length, compressed.Length, tokensSaved, plannedNode.Name);
                    yield return ExecutionEvent.TextChunk(AgentName,
                        $"\n[Context windowing: {previousResult.Length} → {compressed.Length} chars]\n");
                    previousResult = compressed;
                }
            }

            // 委派給 FlowNodeRunner 執行各類型節點
            long stepTokens = 0;
            string? conditionOutputPort = null;
            await foreach (var evt in _nodeRunner.ExecuteNodeAsync(
                step.Config, previousResult, request, cancellationToken))
            {
                // 攔截 AgentCompleted 取得輸出 + token 計數
                if (evt.Type == EventTypes.AgentCompleted)
                {
                    previousResult = evt.Text;
                    if (!string.IsNullOrEmpty(evt.Text))
                    {
                        lastSuccessfulResult = evt.Text;
                    }

                    step.Output = evt.Text;

                    if (evt.Metadata?.TryGetValue(MetadataKeys.Tokens, out var tokenStr) == true
                        && long.TryParse(tokenStr, out var t))
                    {
                        stepTokens += t;
                    }
                }
                else if (evt.Type == EventTypes.NodeCompleted)
                {
                    previousResult = evt.Metadata?.GetValueOrDefault("output") ?? previousResult;
                    step.Output = previousResult;

                    // Condition 節點：記錄走哪個分支
                    if (evt.Metadata?.TryGetValue("conditionMet", out var metStr) == true)
                    {
                        conditionOutputPort = evt.Metadata.GetValueOrDefault("outputPort");
                    }
                }

                yield return evt;
            }

            // F4 Adaptive Replanning：agent 節點失敗（空 output）且有剩餘步驟時，嘗試重規劃
            if (string.IsNullOrEmpty(previousResult) && plannedNode is Schema.AgentNode
                && replanBudget > 0 && nodeIndex < plan!.Nodes.Count - 1 && plannerClient is not null)
            {
                replanBudget--;
                _logger.LogWarning("[Flow] Node '{Node}' failed with empty output, attempting replan ({Remaining} attempts left)",
                    plannedNode.Name, replanBudget);

                var replanGoal = $"The step '{plannedNode.Name}' failed. " +
                    $"Completed results so far: {lastSuccessfulResult[..Math.Min(lastSuccessfulResult.Length, 500)]}. " +
                    $"Replan to achieve the original goal: {request.Goal}";
                var replanRequest = request with { Goal = replanGoal };

                var (newPlan, replanTokens, replanError) = await PlanAsync(plannerClient, replanRequest, null, cancellationToken);
                if (newPlan is not null)
                {
                    var (validatedNewPlan, _) = FlowPlanValidator.ValidateAndFix(newPlan, request);
                    if (validatedNewPlan.Nodes.Count > 0)
                    {
                        plan = validatedNewPlan;
                        planTokens += replanTokens;
                        nodeIndex = -1; // for 迴圈會 +1 變 0
                        skipIndices.Clear();
                        nodeOutputs.Clear(); // 清空舊計劃的節點輸出
                        previousResult = lastSuccessfulResult; // 恢復到上一個成功的 output

                        yield return ExecutionEvent.PlanGenerated(AgentName, FormatPlanForDisplay(plan));
                        yield return ExecutionEvent.TextChunk(AgentName,
                            $"\n[Replanned: '{plannedNode.Name}' failed → {plan.Nodes.Count} new steps]\n");
                        continue;
                    }
                }

                _logger.LogWarning("[Flow] Replan failed: {Error}", replanError ?? "empty plan");
            }

            // Condition 分支邏輯：
            // 優先用 Meta 中 stash 的 TrueBranchIndex/FalseBranchIndex（明確指定）
            // 未指定時 fallback：TRUE = index+1, FALSE = index+2
            if (plannedNode is Schema.ConditionNode && conditionOutputPort is not null)
            {
                var trueIdx = NodeConfigHelpers.GetBranchIndex(plannedNode, NodeConfigHelpers.MetaTrueBranchIndex) ?? nodeIndex + 1;
                var falseIdx = NodeConfigHelpers.GetBranchIndex(plannedNode, NodeConfigHelpers.MetaFalseBranchIndex) ?? nodeIndex + 2;

                if (conditionOutputPort == OutputPorts.Output1)
                {
                    // TRUE — 跳過 FALSE 分支
                    if (falseIdx < plan.Nodes.Count)
                        skipIndices.Add(falseIdx);
                }
                else
                {
                    // FALSE — 跳過 TRUE 分支
                    if (trueIdx < plan.Nodes.Count)
                        skipIndices.Add(trueIdx);
                }
            }

            // F5: 記錄節點輸出，供後續 {{node:step_name}} 跨節點引用
            if (!string.IsNullOrEmpty(plannedNode.Name))
            {
                nodeOutputs[plannedNode.Name] = previousResult;
            }

            step.Duration = stepSw.Elapsed;
            step.TokensUsed = stepTokens;
            trace.Steps.Add(step);

            yield return ExecutionEvent.ReasoningStep(
                AgentName, stepSequence, plan.Nodes.Count, (int)stepTokens, stepSw.Elapsed.TotalMilliseconds);

            // F1 Checkpoint：每個節點完成後存快照（供 Phase B Resume 使用）
            if (_checkpointStore is not null && nodeIndex > 0)
            {
                try
                {
                    var snapshot = new FlowCheckpointSnapshot
                    {
                        PlanJson = JsonSerializer.Serialize(plan, Schema.SchemaJsonOptions.Default),
                        CompletedNodeIndex = nodeIndex,
                        PreviousResult = previousResult,
                        SkipIndices = skipIndices,
                        NodeOutputs = new Dictionary<string, string>(nodeOutputs),
                        AccumulatedTokens = trace.Steps.Sum(s => s.TokensUsed),
                    };
                    var doc = new CheckpointDocument
                    {
                        Id = $"fcp-{request.ExecutionId}-{nodeIndex}",
                        ExecutionId = request.ExecutionId,
                        Iteration = nodeIndex,
                        StateJson = JsonSerializer.Serialize(snapshot),
                        CreatedAt = DateTime.UtcNow,
                    };
                    await _checkpointStore.SaveAsync(doc);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Flow] Checkpoint save failed at node {Index}", nodeIndex);
                }
            }
        }

        trace.CompletedAt = DateTime.UtcNow;
        trace.Succeeded = true;

        if (compressionState.CompressionsApplied > 0)
        {
            _logger.LogInformation(
                "[Flow] Compression stats: {Applied} compressions, ~{Saved} tokens saved",
                compressionState.CompressionsApplied, compressionState.TotalTokensSaved);
        }

        // 4. 輸出最終結果
        yield return ExecutionEvent.AgentCompleted(AgentName, previousResult);

        // 5. Crystallize — 自動凍結為 Workflow JSON 並存檔
        var workflowJson = _crystallizer.Crystallize(trace);
        yield return ExecutionEvent.FlowCrystallized(workflowJson);

        // 存檔到 Data/ — Studio 可直接匯入
        try
        {
            var dir = Path.Combine("Data", "flow-outputs");
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, $"flow-{trace.ExecutionId}-{DateTime.Now:HHmmss}.json");
            File.WriteAllText(filePath, workflowJson);
            _logger.LogInformation("Crystallized workflow saved to {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save crystallized workflow");
        }

        // 6. 成本估算
        var totalTokens = planTokens + trace.Steps.Sum(s => s.TokensUsed);
        var actualPlannerModel = FlowAgentFactory.GetPlannerModel(request);
        var planCost = ModelPricing.EstimateCost(actualPlannerModel, planTokens);
        var executeCost = ModelPricing.EstimateCost(request.Model, totalTokens - planTokens);
        var totalCost = planCost + executeCost;

        var completedEvt = ExecutionEvent.WorkflowCompleted();
        completedEvt.Metadata = new Dictionary<string, string>
        {
            ["totalTokens"] = totalTokens.ToString(),
            ["planTokens"] = planTokens.ToString(),
            ["planModel"] = actualPlannerModel,
            ["executeModel"] = request.Model,
            ["estimatedCost"] = ModelPricing.FormatCost(totalCost)
        };
        yield return completedEvt;

        _logger.LogInformation("Flow completed in {Elapsed}ms, {Steps} steps, {Tokens} tokens, cost {Cost}",
            overallSw.ElapsedMilliseconds, trace.Steps.Count, totalTokens, ModelPricing.FormatCost(totalCost));
    }

    /// <summary>
    /// F1 Resume：從 checkpoint 恢復 Flow 執行。
    /// 載入最新 checkpoint → 還原計劃和狀態 → 從斷點的下一個節點繼續。
    /// NOTE: 此方法的節點迴圈是 ExecuteAsync 主迴圈的簡化版本（缺少 Context Windowing、Replan、TraceStep）。
    /// 未來應抽取 ExecuteNodesAsync 共用方法消除重複。
    /// </summary>
    private async IAsyncEnumerable<ExecutionEvent> ResumeFromCheckpointAsync(
        GoalExecutionRequest request,
        string resumeExecutionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_checkpointStore is null)
        {
            yield return ExecutionEvent.Error("Checkpoint store not configured, cannot resume");
            yield break;
        }

        var doc = await _checkpointStore.GetLatestAsync(resumeExecutionId);
        if (doc is null)
        {
            yield return ExecutionEvent.Error($"No checkpoint found for execution {resumeExecutionId}");
            yield break;
        }

        FlowCheckpointSnapshot? snapshot = null;
        FlowPlan? plan = null;
        string? deserializeError = null;
        try
        {
            snapshot = JsonSerializer.Deserialize<FlowCheckpointSnapshot>(doc.StateJson, Schema.SchemaJsonOptions.Default);
            plan = snapshot is not null
                ? JsonSerializer.Deserialize<FlowPlan>(snapshot.PlanJson, Schema.SchemaJsonOptions.Default)
                : null;
        }
        catch (Exception ex)
        {
            deserializeError = ex.Message;
        }

        if (snapshot is null || plan is null || deserializeError is not null)
        {
            yield return ExecutionEvent.Error($"Failed to deserialize checkpoint: {deserializeError ?? "null snapshot/plan"}");
            yield break;
        }

        if (plan?.Nodes is null || plan.Nodes.Count == 0)
        {
            yield return ExecutionEvent.Error("Checkpoint contains empty plan");
            yield break;
        }

        var resumeIndex = snapshot.CompletedNodeIndex + 1;
        if (resumeIndex >= plan.Nodes.Count)
        {
            yield return ExecutionEvent.TextChunk(AgentName, "\n[All nodes already completed, nothing to resume]\n");
            yield return ExecutionEvent.AgentCompleted(AgentName, snapshot.PreviousResult);
            yield break;
        }

        yield return ExecutionEvent.AgentStarted(AgentName, text: $"[Resuming] {request.Goal}");
        yield return ExecutionEvent.TextChunk(AgentName,
            $"\n[Resuming from checkpoint: node {resumeIndex + 1}/{plan.Nodes.Count}, " +
            $"accumulated {snapshot.AccumulatedTokens} tokens]\n");

        // 重建 planner client
        var (plannerClient, error) = _agentFactory.CreatePlanner(request);
        if (error is not null)
        {
            yield return ExecutionEvent.Error(error);
            yield break;
        }

        // 從 checkpoint 恢復狀態，執行剩餘節點
        var previousResult = snapshot.PreviousResult;
        var skipIndices = snapshot.SkipIndices;
        var workingMemory = new FlowWorkingMemory();
        _nodeRunner.WorkingMemory = workingMemory;
        var nodeOutputs = new Dictionary<string, string>(snapshot.NodeOutputs);
        _nodeRunner.NodeOutputs = nodeOutputs;
        var resumeCompactor = plannerClient is not null ? new LlmContextCompactor(plannerClient) : null;
        _nodeRunner.ReferenceCompactor = resumeCompactor;

        for (var nodeIndex = resumeIndex; nodeIndex < plan.Nodes.Count; nodeIndex++)
        {
            if (skipIndices.Contains(nodeIndex))
            {
                continue;
            }

            var plannedNode = plan.Nodes[nodeIndex];
            cancellationToken.ThrowIfCancellationRequested();
            yield return ExecutionEvent.NodeExecuting(NodeConfigHelpers.GetNodeTypeString(plannedNode), plannedNode.Name);

            await foreach (var evt in _nodeRunner.ExecuteNodeAsync(
                plannedNode, previousResult, request, cancellationToken))
            {
                if (evt.Type == EventTypes.AgentCompleted)
                {
                    previousResult = evt.Text;
                }
                else if (evt.Type == EventTypes.NodeCompleted)
                {
                    previousResult = evt.Metadata?.GetValueOrDefault("output") ?? previousResult;
                }

                yield return evt;
            }

            // F5: 記錄節點輸出供 {{node:}} 引用
            if (!string.IsNullOrEmpty(plannedNode.Name))
            {
                nodeOutputs[plannedNode.Name] = previousResult;
            }

            yield return ExecutionEvent.ReasoningStep(
                AgentName, nodeIndex + 1, plan.Nodes.Count, 0, 0);

            // 存 checkpoint
            if (_checkpointStore is not null)
            {
                try
                {
                    var cp = new FlowCheckpointSnapshot
                    {
                        PlanJson = snapshot.PlanJson,
                        CompletedNodeIndex = nodeIndex,
                        PreviousResult = previousResult,
                        SkipIndices = skipIndices,
                        NodeOutputs = new Dictionary<string, string>(nodeOutputs),
                        AccumulatedTokens = snapshot.AccumulatedTokens,
                    };
                    await _checkpointStore.SaveAsync(new CheckpointDocument
                    {
                        Id = $"fcp-{request.ExecutionId}-{nodeIndex}",
                        ExecutionId = request.ExecutionId,
                        Iteration = nodeIndex,
                        StateJson = JsonSerializer.Serialize(cp),
                        CreatedAt = DateTime.UtcNow,
                    });
                }
                catch { /* checkpoint save failure doesn't stop execution */ }
            }
        }

        yield return ExecutionEvent.AgentCompleted(AgentName, previousResult);
    }

    private async Task<(FlowPlan? Plan, long Tokens, string? Error)> PlanAsync(
        IChatClient client, GoalExecutionRequest request, string? experienceHint, CancellationToken cancellationToken)
    {
        var toolDescriptions = _agentFactory.GetToolDescriptions();
        var systemPrompt = FlowPlannerPrompt.Build(request, toolDescriptions, experienceHint);

        // CacheableSystemPrompt：planner prompt 跨執行可緩存（Anthropic 加 cache_control）
        var cacheablePrompt = new CacheableSystemPrompt(systemPrompt);
        var messages = new List<ChatMessage>();
        messages.AddRange(cacheablePrompt.ToChatMessages(request.Provider));
        messages.Add(new ChatMessage(ChatRole.User, request.Goal));

        try
        {
            var planOptions = new ChatOptions { Temperature = 0f };
            var response = await client.GetResponseAsync(messages, planOptions, cancellationToken);
            var text = response.Text ?? "";

            var tokens = response.Usage is not null
                ? (response.Usage.InputTokenCount ?? 0) + (response.Usage.OutputTokenCount ?? 0)
                : text.Length / 4;

            var jsonBlock = LlmJsonExtractor.Extract(text);
            if (jsonBlock is null)
            {
                return (null, 0, "No JSON block found in planning response");
            }

            var plan = JsonSerializer.Deserialize<FlowPlan>(jsonBlock, Schema.SchemaJsonOptions.Default);
            if (plan?.Nodes is null || plan.Nodes.Count == 0)
            {
                return (null, 0, "Empty plan");
            }

            return (plan, tokens, null);
        }
        catch (Exception ex)
        {
            return (null, 0, ex.Message);
        }
    }

    private static string FormatPlanForDisplay(FlowPlan plan)
    {
        var lines = plan.Nodes.Select((n, i) =>
            $"  {i + 1}. [{NodeConfigHelpers.GetNodeTypeString(n)}] {n.Name}" +
            (n is Schema.AgentNode agent ? $" — {Truncate(agent.Instructions ?? "", 80)}" : ""));
        return $"Flow Plan ({plan.Nodes.Count} nodes):\n{string.Join("\n", lines)}";
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "...";

    /// <summary>從目標文字提取關鍵字（簡化版，供記憶查詢用）。</summary>
    private static string ExtractKeywords(string goal, int max = 10)
    {
        var words = goal
            .Split([' ', ',', '.', '，', '。', '、', '：', '？', '！', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max);
        return string.Join(" ", words);
    }

}
