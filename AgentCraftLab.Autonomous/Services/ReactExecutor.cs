using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.Engine.Middleware;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Services.Compression;
using AgentCraftLab.Engine.Strategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// ReAct 執行器 — Autonomous Agent 的核心迴圈。
/// Reason → Act → Observe → Reason → ... → Final Answer
/// </summary>
public sealed class ReactExecutor : IGoalExecutor
{
    private const string AgentName = "Autonomous Agent";

    private readonly AgentFactory _agentFactory;
    private readonly SystemPromptBuilder _promptBuilder;
    private readonly IReflectionEngine _reflectionEngine;
    private readonly IHumanInteractionHandler _humanHandler;
    private readonly IBudgetPolicy _budgetPolicy;
    private readonly IHistoryManager _historyManager;
    private readonly IExecutionMemoryService? _memoryService;
    private readonly IExecutionCheckpoint? _checkpoint;
    private readonly IMetricsCollector? _metrics;
    private readonly IGuardRailsPolicy? _guardRailsPolicy;
    private readonly CheckpointManager? _checkpointManager;
    private readonly IToolCodeRunner? _toolCodeRunner;
    private readonly IStepEvaluator? _stepEvaluator;
    private readonly HumanInputBridge? _humanBridge;
    private readonly ReactExecutorConfig _config;
    private readonly ILogger<ReactExecutor> _logger;

    public ReactExecutor(
        AgentFactory agentFactory,
        SystemPromptBuilder promptBuilder,
        IReflectionEngine reflectionEngine,
        IHumanInteractionHandler humanHandler,
        IBudgetPolicy budgetPolicy,
        IHistoryManager historyManager,
        ILogger<ReactExecutor> logger,
        ReactExecutorConfig? config = null,
        HumanInputBridge? humanBridge = null,
        IExecutionMemoryService? memoryService = null,
        IExecutionCheckpoint? checkpoint = null,
        IMetricsCollector? metrics = null,
        IGuardRailsPolicy? guardRailsPolicy = null,
        CheckpointManager? checkpointManager = null,
        IToolCodeRunner? toolCodeRunner = null,
        IStepEvaluator? stepEvaluator = null)
    {
        _agentFactory = agentFactory;
        _promptBuilder = promptBuilder;
        _reflectionEngine = reflectionEngine;
        _humanHandler = humanHandler;
        _budgetPolicy = budgetPolicy;
        _historyManager = historyManager;
        _config = config ?? new ReactExecutorConfig();
        _humanBridge = humanBridge;
        _memoryService = memoryService;
        _checkpoint = checkpoint;
        _metrics = metrics;
        _guardRailsPolicy = guardRailsPolicy;
        _checkpointManager = checkpointManager;
        _toolCodeRunner = toolCodeRunner;
        _stepEvaluator = stepEvaluator;
        _logger = logger;
    }

    /// <summary>
    /// IGoalExecutor 實作 — 將 GoalExecutionRequest 轉換為 AutonomousRequest 後委派。
    /// </summary>
    async IAsyncEnumerable<ExecutionEvent> IGoalExecutor.ExecuteAsync(
        GoalExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var autonomousRequest = GoalRequestConverter.ToAutonomousRequest(request);
        await foreach (var evt in ExecuteAsync(autonomousRequest, cancellationToken))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// 從檢查點恢復執行 — 載入指定 iteration 的快照，從下一步繼續 ReAct 迴圈。
    /// </summary>
    /// <param name="request">原始執行請求（需與 checkpoint 時一致）。</param>
    /// <param name="fromIteration">從哪個 iteration 的 checkpoint 恢復（null = 最新）。</param>
    /// <param name="cancellationToken">取消 token。</param>
    public async IAsyncEnumerable<ExecutionEvent> ResumeAsync(
        AutonomousRequest request,
        int? fromIteration = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_checkpointManager is null)
        {
            yield return ExecutionEvent.Error("CheckpointManager not available. Enable CheckpointEnabled to use ResumeAsync.");
            yield break;
        }

        var snapshot = fromIteration.HasValue
            ? await _checkpointManager.LoadAsync(request.ExecutionId, fromIteration.Value, cancellationToken)
            : await _checkpointManager.LoadLatestAsync(request.ExecutionId, cancellationToken);

        if (snapshot is null)
        {
            yield return ExecutionEvent.Error($"No checkpoint found for execution {request.ExecutionId}" +
                (fromIteration.HasValue ? $" at iteration {fromIteration.Value}" : ""));
            yield break;
        }

        yield return ExecutionEvent.TextChunk(AgentName,
            $"\n[Resuming from checkpoint: iteration {snapshot.Iteration}]\n");

        await foreach (var evt in ExecuteAsync(request, cancellationToken, snapshot))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// 執行 Autonomous Agent — 串流回傳 ExecutionEvent。
    /// 內部核心方法，接受完整的 AutonomousRequest。
    /// </summary>
    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        AutonomousRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in ExecuteAsync(request, cancellationToken, resumeSnapshot: null))
        {
            yield return evt;
        }
    }

    /// <summary>核心執行方法（支援從 checkpoint 恢復）。</summary>
    private async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        AutonomousRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        CheckpointSnapshot? resumeSnapshot)
    {
        var overallSw = Stopwatch.StartNew();

        yield return ExecutionEvent.AgentStarted(AgentName, text: request.Goal);

        // 1. 建立 Orchestrator — 解析外部工具
        var (client, externalTools, _, error) = await _agentFactory.CreateOrchestratorAsync(
            request, cancellationToken, middleware: _config.OrchestratorMiddleware);
        if (error is not null)
        {
            yield return ExecutionEvent.Error(error);
            yield break;
        }

        // 2. 根據模型動態設定壓縮門檻
        _historyManager.SetModel(request.Model);

        // 3. 初始化追蹤器
        var tokenTracker = new TokenTracker(request.Budget);
        var toolCallTracker = new ToolCallTracker(request.ToolLimits);
        var steps = new List<ReactStep>();
        var toolRequests = new List<ToolRequest>();
        var toolCallEvents = new List<ExecutionEvent>();

        // 2.5 建立協作層
        var sharedState = new SharedStateStore();
        sharedState.Initialize(request.SharedStateInit);

        var llmThrottle = new SemaphoreSlim(3);
        var agentPool = new AgentPool(
            _agentFactory, request, externalTools, tokenTracker, toolCallTracker, llmThrottle, _logger, _config);
        await using var _ = agentPool;

        var askUserCtx = _humanBridge is not null ? new AskUserContext() : null;
        var riskCtx = request.Risk is { Enabled: true } && _humanBridge is not null
            ? new RiskApprovalContext() : null;

        // 工具編排（ToolOrchestrator 集中管理分類 + DynamicToolSet + ToolSearchIndex）
        var orchestrator = ToolOrchestrator.Build(
            externalTools, agentPool, sharedState, askUserCtx,
            _config, _toolCodeRunner, _logger);
        var allTools = (List<AITool>)[.. orchestrator.GetActiveTools()];
        var metaToolRegistry = orchestrator.MetaToolRegistry;
        var dynamicToolSet = orchestrator.DynamicToolSet;
        var toolSearchIndex = orchestrator.SearchIndex;

        // P0: 包裝高風險工具
        if (riskCtx is not null && request.Risk is { Rules.Count: > 0 })
        {
            _humanHandler.WrapRiskTools(allTools, riskCtx, request.Risk.Rules);
        }

        // 3. 建構 system prompt
        var searchableCount = orchestrator.SearchableCount;
        var systemPrompt = _promptBuilder.Build(request, allTools, _humanBridge is not null, searchableCount);

        // 3.5 執行前規劃 + 記憶注入（resume 時跳過；簡單任務跳過 Plan 生成省 token）
        string? plan = null;
        var planner = new TaskPlanner(_config);
        var isComplexGoal = SystemPromptBuilder.IsComplexGoal(request.Goal);
        if (resumeSnapshot is null && isComplexGoal)
        {
            IChatClient? ownedPlannerClient = null;
            IChatClient plannerClient = client;
            if (_config.PlannerModel is not null && _config.PlannerModel != request.Model)
            {
                var provider = AgentContextBuilder.NormalizeProvider(request.Provider);
                if (request.Credentials.TryGetValue(provider, out var plannerCred))
                {
                    ownedPlannerClient = AgentContextBuilder.CreateChatClient(
                        provider, plannerCred.ApiKey, plannerCred.Endpoint, _config.PlannerModel);
                    plannerClient = ownedPlannerClient;
                }
            }

            plan = await planner.GeneratePlanAsync(plannerClient, request.Goal, allTools, cancellationToken);
            if (ownedPlannerClient is IDisposable disposablePlanner) disposablePlanner.Dispose();
            if (plan is not null)
            {
                yield return ExecutionEvent.PlanGenerated(AgentName, plan);
                systemPrompt += "\n\n## Execution Plan (auto-generated)\nFollow this plan as a guideline, but adapt if needed:\n" + plan;
            }
        }

        // 3.7 跨 Session 記憶注入（所有非 resume 任務都注入）
        if (resumeSnapshot is null && _memoryService is not null)
        {
            try
            {
                var experiencePrompt = await _memoryService.GetRelevantExperienceAsync(
                    request.UserId, request.Goal, cancellationToken);
                if (experiencePrompt is not null)
                {
                    systemPrompt += "\n\n" + experiencePrompt;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "查詢跨 Session 記憶失敗，繼續執行");
            }
        }

        // 4. 建立對話歷史（CacheableSystemPrompt 讓 Anthropic provider 可明確標記 cache_control）
        var normalizedProvider = AgentContextBuilder.NormalizeProvider(request.Provider);
        var cacheablePrompt = new CacheableSystemPrompt(systemPrompt);
        var messages = new List<ChatMessage>();
        messages.AddRange(cacheablePrompt.ToChatMessages(normalizedProvider));
        messages.Add(AgentContextBuilder.BuildUserMessage(request.Goal, request.Attachment));

        // 4.5 壓縮狀態追蹤（跨迭代記錄壓縮操作，為 CacheAware 壓縮決策奠基）
        var compressionState = new CompressionState();

        // 5. 用 FunctionInvokingChatClient 包裝工具呼叫（啟用並行，讓多個 ask_sub_agent 同時執行）
        //    Orchestrator 為單執行緒，不需要節流；llmThrottle 僅供 AgentPool 給 sub-agent 使用
        using var toolClient = new FunctionInvokingChatClient(client) { AllowConcurrentInvocation = true };

        // 6. ReAct 迴圈
        var finalAnswer = "";
        var succeeded = false;
        var loopState = new ReactLoopState();
        const int maxAskUserCalls = 2;
        var convergenceDetector = new ConvergenceDetector(_config); // 收斂偵測：偵測重複工具呼叫
        long cachedMessageChars = messages.Sum(m => (long)(m.Text?.Length ?? 0)); // Token 估算快取

        // Step Evaluator 追蹤狀態
        string? prevToolName = null;
        string? prevToolArgs = null;
        var consecutiveNoTextSteps = 0;
        var consecutiveToolFailures = 0;
        var lastFailedTool = "";

        // 從 checkpoint 恢復狀態（如果有的話）
        var startIteration = 1;
        if (resumeSnapshot is not null && _checkpointManager is not null)
        {
            _checkpointManager.RestoreState(
                resumeSnapshot, messages, steps, tokenTracker, toolCallTracker,
                convergenceDetector, sharedState, loopState, toolCallEvents);

            finalAnswer = resumeSnapshot.FinalAnswer ?? "";
            succeeded = resumeSnapshot.Succeeded;
            cachedMessageChars = resumeSnapshot.CachedMessageChars;
            plan = resumeSnapshot.Plan ?? plan;
            startIteration = resumeSnapshot.Iteration + 1;

            // 恢復動態載入的工具
            if (resumeSnapshot.LoadedDynamicToolNames is { Count: > 0 } && dynamicToolSet is not null && toolSearchIndex is not null)
            {
                dynamicToolSet.LoadTools(resumeSnapshot.LoadedDynamicToolNames, toolSearchIndex);
            }
        }

        for (var iteration = startIteration; iteration <= request.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 預算檢查（Token + 工具呼叫次數）
            var budgetEvent = _budgetPolicy.CheckBudget(tokenTracker, toolCallTracker);
            if (budgetEvent is not null)
            {
                yield return budgetEvent;
                break;
            }

            // 檢查點儲存
            if (_checkpointManager is not null && _checkpointManager.ShouldSave(iteration, toolCallTracker.TotalCalls))
            {
                var snapshot = _checkpointManager.CaptureSnapshot(
                    iteration, messages, steps, tokenTracker, toolCallTracker,
                    convergenceDetector, sharedState, agentPool, loopState,
                    finalAnswer, succeeded, cachedMessageChars, plan, dynamicToolSet, toolCallEvents);
                await _checkpointManager.SaveAsync(request.ExecutionId, snapshot, cancellationToken);
            }
            else if (_checkpoint is not null && iteration % 5 == 0)
            {
                // 向後相容：未啟用 CheckpointManager 時使用舊版 metadata-only checkpoint
                await _checkpoint.SaveAsync(
                    request.ExecutionId, iteration, messages.Count,
                    tokenTracker.TotalTokensUsed, cancellationToken);
            }

            var stepSw = Stopwatch.StartNew();

            // 注入剩餘預算資訊（每 5 步或最後 3 步），就地更新同一條 message
            _budgetPolicy.InjectBudgetReminder(
                messages, loopState, iteration, request.MaxIterations,
                tokenTracker, toolCallTracker);

            // 事中自我檢查（每 8 步提醒 AI 評估方向，輕量注入不需額外 LLM 呼叫）
            _budgetPolicy.InjectMidExecutionCheck(messages, iteration, request.MaxIterations);

            // 動態重規劃（在 50% 進度後每 8 步檢查一次，需有初始計劃才觸發）
            if (iteration > 1 && iteration % 8 == 0 && plan is not null)
            {
                var progressSummary = string.Join("\n", messages
                    .TakeLast(4)
                    .Select(m => m.Text?[..Math.Min(m.Text?.Length ?? 0, 100)]));
                var revisedPlan = await planner.ReplanAsync(
                    client, request.Goal, progressSummary,
                    iteration, request.MaxIterations, cancellationToken);
                if (revisedPlan is not null)
                {
                    yield return ExecutionEvent.PlanRevised(AgentName, revisedPlan);
                    var planMsg = $"[Plan Update] Your execution plan has been revised:\n{revisedPlan}\nAdjust your approach accordingly.";
                    messages.Add(new ChatMessage(ChatRole.User, planMsg));
                    cachedMessageChars += planMsg.Length;
                }
            }

            // 呼叫 LLM（Tool Search 模式下使用動態工具清單）
            ChatResponse response;
            string? llmError = null;
            bool guardRailsBlocked = false;
            try
            {
                var activeTools = dynamicToolSet?.GetActiveTools() ?? (IList<AITool>)allTools;
                var chatOpts = new ChatOptions { Tools = activeTools };

                // 平行 Guardrails 模式：input scan 與 LLM 平行執行
                if (_config.ParallelGuardRails && _guardRailsPolicy is not null)
                {
                    var evaluator = new ParallelGuardRailsEvaluator(_guardRailsPolicy, _logger);
                    var scanResult = await evaluator.ExecuteWithGuardRailsAsync(
                        toolClient, messages, chatOpts, iteration, cancellationToken);

                    if (scanResult.BlockedMatch is not null)
                    {
                        guardRailsBlocked = true;
                        response = null!;
                    }
                    else
                    {
                        response = scanResult.Response!;
                    }
                }
                else
                {
                    response = await toolClient.GetResponseAsync(
                        messages, chatOpts, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM call failed at iteration {Iteration}", iteration);
                llmError = $"LLM error at step {iteration}: {ex.Message}";
                response = null!;
            }

            if (guardRailsBlocked)
            {
                yield return ExecutionEvent.TextChunk(AgentName,
                    "\n[Content policy violation detected — execution stopped]\n");
                break;
            }

            if (llmError is not null)
            {
                yield return ExecutionEvent.Error(llmError);
                break;
            }

            // S6: Token 預算 null 處理 — API 未回報 token 時改用估算值，避免預算限制失效
            long inputTokens;
            long outputTokens;
            if (response.Usage is not null)
            {
                inputTokens = response.Usage.InputTokenCount ?? 0;
                outputTokens = response.Usage.OutputTokenCount ?? 0;
            }
            else
            {
                // 粗略估算：使用快取字元數（O(1)），避免每步重新遍歷 messages（O(n²)）
                inputTokens = cachedMessageChars / _config.CharsPerTokenEstimate;
                outputTokens = (response.Text?.Length ?? 0) / _config.CharsPerTokenEstimate;
                _logger.LogWarning("API did not return token usage, using estimated values (input={InputTokens}, output={OutputTokens})",
                    inputTokens, outputTokens);
            }

            tokenTracker.Record(inputTokens, outputTokens);

            // 提取回應文字
            var responseText = response.Text ?? "";

            // 收斂偵測：記錄回應長度，用於資訊增量枯竭偵測
            convergenceDetector.RecordResponseLength(responseText.Length);

            // 記錄工具呼叫
            var toolCalls = ExtractToolCalls(response);
            foreach (var tc in toolCalls)
            {
                if (!toolCallTracker.Record(tc.Name))
                {
                    yield return ExecutionEvent.TextChunk(AgentName,
                        $"\n[Tool {tc.Name} limit reached, skipping]");
                }

                var toolCallEvt = ExecutionEvent.ToolCall(AgentName, tc.Name, tc.Args);
                toolCallEvents.Add(toolCallEvt);
                yield return toolCallEvt;
                yield return ExecutionEvent.ToolResult(AgentName, tc.Name,
                    Truncate(tc.Result, 500));

                // 指標收集：記錄工具呼叫
                _metrics?.RecordToolCall(request.ExecutionId, tc.Name, true, 0);

                // 收斂偵測：記錄工具呼叫結果
                convergenceDetector.RecordToolCall(tc.Name, tc.Result);

                // P1: 偵測 meta-tool 呼叫，yield 視覺化事件
                if (metaToolRegistry.IsMetaTool(tc.Name))
                {
                    if (tc.Name == MetaToolFactory.CreateSubAgent)
                    {
                        var (subName, subInstructions) = ParseJsonArgs(tc.Args, "name", "instructions");
                        yield return ExecutionEvent.SubAgentCreated(AgentName, subName, subInstructions);
                    }
                    else if (tc.Name == MetaToolFactory.AskSubAgent)
                    {
                        var (subName, subMessage) = ParseJsonArgs(tc.Args, "agent_name", "message");
                        yield return ExecutionEvent.SubAgentAsked(AgentName, subName, subMessage);
                        yield return ExecutionEvent.SubAgentResponded(AgentName, subName, Truncate(tc.Result, 500));
                    }
                    else if (tc.Name == MetaToolFactory.SpawnSubAgent)
                    {
                        var (spawnLabel, spawnTask) = ParseJsonArgs(tc.Args, "label", "task");
                        var displayName = !string.IsNullOrEmpty(spawnLabel) ? spawnLabel : "worker";
                        yield return ExecutionEvent.SubAgentCreated(AgentName, displayName,
                            $"[Spawned] {spawnTask}");
                    }
                    else if (tc.Name == MetaToolFactory.StopSpawn)
                    {
                        var (stopId, _) = ParseJsonArgs(tc.Args, "runId", "");
                        yield return ExecutionEvent.SubAgentAsked(AgentName, stopId, "[Stop] Cancellation signal sent");
                    }
                    else if (tc.Name == MetaToolFactory.SendToSpawn)
                    {
                        var (targetId, sentMsg) = ParseJsonArgs(tc.Args, "runId", "message");
                        yield return ExecutionEvent.SubAgentAsked(AgentName, targetId, $"[Message] {Truncate(sentMsg, 80)}");
                    }
                    else if (tc.Name == MetaToolFactory.CollectResults)
                    {
                        var collectEvents = ParseCollectResults(tc.Result);
                        foreach (var evt in collectEvents)
                        {
                            yield return evt;
                        }
                    }
                    else if (tc.Name == MetaToolFactory.RequestPeerReview)
                    {
                        // 對等審查：reviewer 審查 source 的結論
                        var (sourceName, reviewerName) = ParseJsonArgs(tc.Args, "sourceName", "reviewerName");
                        yield return ExecutionEvent.SubAgentAsked(AgentName, reviewerName,
                            $"[Peer Review] Reviewing findings from {sourceName}");
                        yield return ExecutionEvent.SubAgentResponded(AgentName, reviewerName, Truncate(tc.Result, 500));
                    }
                    else if (tc.Name == MetaToolFactory.ChallengeAssertion)
                    {
                        // 質詢：challenger 質詢 target 的斷言
                        var (challengerName, targetName) = ParseJsonArgs(tc.Args, "challengerName", "targetName");
                        yield return ExecutionEvent.SubAgentAsked(AgentName, targetName,
                            $"[Challenge] Challenged by {challengerName}");
                        yield return ExecutionEvent.SubAgentResponded(AgentName, targetName, Truncate(tc.Result, 500));
                    }
                    else if (tc.Name == MetaToolFactory.CreateTool)
                    {
                        var (toolName, _) = ParseJsonArgs(tc.Args, "name", "description");
                        var status = tc.Result.StartsWith("[Failed]") ? "FAILED" : "OK";
                        yield return ExecutionEvent.TextChunk(AgentName,
                            $"\n[Tool Created: {toolName} — {status}]\n");
                    }
                }
            }

            // 記錄步驟
            var step = new ReactStep
            {
                Sequence = iteration,
                Thought = responseText,
                Action = toolCalls.Count > 0 ? toolCalls[0].Name : null,
                ActionInput = toolCalls.Count > 0 ? toolCalls[0].Args : null,
                Observation = toolCalls.Count > 0 ? toolCalls[0].Result : null,
                Tokens = new TokenUsage { InputTokens = inputTokens, OutputTokens = outputTokens },
                Duration = stepSw.Elapsed
            };
            steps.Add(step);

            // P1: yield ReasoningStep 事件
            yield return ExecutionEvent.ReasoningStep(
                AgentName, iteration, request.MaxIterations,
                (int)(inputTokens + outputTokens), stepSw.Elapsed.TotalMilliseconds);

            // Step-level PRM：每步品質評估
            if (_stepEvaluator is not null && toolCalls.Count > 0)
            {
                var tc0 = toolCalls[0];
                var isError = string.IsNullOrWhiteSpace(tc0.Result) ||
                              tc0.Result.StartsWith("[Error]", StringComparison.OrdinalIgnoreCase);

                // 更新連續失敗追蹤
                if (isError && tc0.Name == lastFailedTool)
                {
                    consecutiveToolFailures++;
                }
                else if (isError)
                {
                    consecutiveToolFailures = 1;
                    lastFailedTool = tc0.Name;
                }
                else
                {
                    consecutiveToolFailures = 0;
                    lastFailedTool = "";
                }

                // 更新連續無文字追蹤
                consecutiveNoTextSteps = string.IsNullOrWhiteSpace(responseText)
                    ? consecutiveNoTextSteps + 1 : 0;

                var stepCtx = new StepContext
                {
                    Iteration = iteration,
                    MaxIterations = request.MaxIterations,
                    ToolName = tc0.Name,
                    ToolArgs = tc0.Args,
                    ToolResult = tc0.Result,
                    HasTextResponse = !string.IsNullOrWhiteSpace(responseText),
                    PreviousToolName = prevToolName,
                    PreviousToolArgs = prevToolArgs,
                    ConsecutiveNoTextSteps = consecutiveNoTextSteps,
                    ConsecutiveToolFailures = consecutiveToolFailures
                };

                var evaluation = _stepEvaluator.Evaluate(stepCtx);
                if (evaluation is not null)
                {
                    // 注入提示到對話歷史
                    messages.Add(new ChatMessage(ChatRole.User, evaluation.Hint));
                    cachedMessageChars += evaluation.Hint.Length;
                    yield return ExecutionEvent.TextChunk(AgentName,
                        $"\n[{evaluation.Level}: {evaluation.RuleName}]\n");
                }

                prevToolName = tc0.Name;
                prevToolArgs = tc0.Args;
            }

            // 指標收集：記錄步驟完成
            _metrics?.RecordStep(request.ExecutionId, iteration,
                inputTokens + outputTokens, stepSw.ElapsedMilliseconds);

            // 將回應加入歷史（同步更新 Token 估算快取）
            foreach (var msg in response.Messages)
            {
                messages.Add(msg);
                cachedMessageChars += msg.Text?.Length ?? 0;
            }

            // P0: risk approval 佇列偵測：逐一暫停等待人類審批，不計入迴圈次數
            if (riskCtx is { IsWaiting: true } && _humanBridge is not null)
            {
                var riskResult = await _humanHandler.HandlePendingRiskApprovalsAsync(riskCtx, messages, cancellationToken);
                foreach (var evt in riskResult.Events)
                {
                    yield return evt;
                }

                iteration += riskResult.IterationAdjustment; // 不計入迴圈次數
                continue;
            }

            // ask_user 旗標偵測：暫停等待使用者回應，不計入迴圈次數
            if (askUserCtx is { IsWaiting: true } && _humanBridge is not null)
            {
                var askResult = await _humanHandler.HandlePendingUserInputAsync(
                    askUserCtx, messages, loopState.AskUserCount, maxAskUserCalls, cancellationToken);
                foreach (var evt in askResult.Events)
                {
                    yield return evt;
                }

                loopState.AskUserCount += askResult.AskUserCountIncrement;
                iteration += askResult.IterationAdjustment; // 不計入迴圈次數
                continue;
            }

            // 串流輸出文字（FunctionInvokingChatClient 有 tool call 時，
            // response.Text 已包含工具呼叫後的最終回答，直接輸出即完成）
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                yield return ExecutionEvent.TextChunk(AgentName, responseText);
            }

            // 檢查是否有工具需求回報
            var requests = ExtractToolRequests(responseText);
            toolRequests.AddRange(requests);

            // 三層 Context Compaction（token 或訊息數任一超過門檻即觸發）
            if (_historyManager.ShouldCompress(messages, cachedMessageChars))
            {
                var compressionResult = await _historyManager.CompressIfNeededAsync(
                    messages, client, tokenTracker, cancellationToken, compressionState);
                foreach (var evt in compressionResult.Events)
                {
                    yield return evt;
                }

                if (compressionResult.ShouldResetBudgetReminderIndex)
                {
                    loopState.BudgetReminderIndex = -1;
                }

                // 壓縮後重算字元數快取
                cachedMessageChars = messages.Sum(m => (long)(m.Text?.Length ?? 0));
            }

            // 收斂偵測：連續相同工具且結果相似 → 提前終止，避免無謂重複
            if (convergenceDetector.ShouldTerminateEarly())
            {
                _logger.LogInformation("Convergence detected at iteration {Iteration}, terminating early", iteration);
                yield return ExecutionEvent.TextChunk(AgentName,
                    "\n[Convergence detected — stopping early to avoid redundant work]\n");
                finalAnswer = responseText;
                succeeded = true;
                break;
            }

            // FunctionInvokingChatClient 已自動完成工具呼叫迴圈，
            // 若有 tool call 且有文字回應 = 工具用完後 AI 已給出最終答案
            // 若無 tool call = AI 直接回答，不需要工具
            // 兩種情況都代表本輪完成
            if (toolCalls.Count == 0 || !string.IsNullOrWhiteSpace(responseText))
            {
                finalAnswer = responseText;
                succeeded = !string.IsNullOrWhiteSpace(responseText);
                break;
            }
        }

        // P2: 反思機制 — 先稽核一次，不通過才進入修正迴圈（最多 MaxRevisions 次修正）
        AuditResult? lastAuditResult = null;
        if (request.Reflection is { Enabled: true } && succeeded && !string.IsNullOrWhiteSpace(finalAnswer))
        {
            yield return ExecutionEvent.AuditStarted(AgentName, 0);
            var auditResult = await _reflectionEngine.AuditAsync(request, finalAnswer, request.Reflection, cancellationToken);
            lastAuditResult = auditResult;
            tokenTracker.Record(auditResult.InputTokens, auditResult.OutputTokens);
            var auditEvent = ExecutionEvent.AuditCompleted(
                AgentName, auditResult.Verdict.ToString(), auditResult.Explanation,
                auditResult.Issues.Count > 0 ? string.Join("; ", auditResult.Issues) : "No issues found");
            // Panel 模式：附加各 Evaluator 個別判定
            if (auditResult.EvaluatorVerdicts is { Count: > 0 })
            {
                auditEvent.Metadata!["evaluators"] = string.Join(" | ",
                    auditResult.EvaluatorVerdicts.Select(v => $"{v.PersonaName}={v.Verdict}"));
            }

            yield return auditEvent;

            for (var revision = 1;
                 revision <= request.Reflection.MaxRevisions && auditResult.Verdict != AuditVerdict.Pass;
                 revision++)
            {
                // 注入審查回饋，讓 Orchestrator 修正
                var feedback = $"[Auditor feedback (verdict: {auditResult.Verdict})]\n" +
                               $"Issues: {string.Join(", ", auditResult.Issues)}\n" +
                               $"Explanation: {auditResult.Explanation}\n" +
                               "Please revise your answer to address these issues.";
                messages.Add(new ChatMessage(ChatRole.User, feedback));
                    cachedMessageChars += feedback.Length;

                // 額外一輪 LLM 呼叫修正（yield 不能在 try-catch 中，先收集結果再 yield）
                string? revisionText = null;
                var revisionFailed = false;
                try
                {
                    var revisionTools = dynamicToolSet?.GetActiveTools() ?? (IList<AITool>)allTools;
                    var revisionResponse = await toolClient.GetResponseAsync(
                        messages, new ChatOptions { Tools = revisionTools }, cancellationToken);

                    var revInputTokens = revisionResponse.Usage?.InputTokenCount ?? 0;
                    var revOutputTokens = revisionResponse.Usage?.OutputTokenCount ?? 0;
                    tokenTracker.Record(revInputTokens, revOutputTokens);

                    foreach (var msg in revisionResponse.Messages)
                    {
                        messages.Add(msg);
                        cachedMessageChars += msg.Text?.Length ?? 0;
                    }

                    if (!string.IsNullOrWhiteSpace(revisionResponse.Text))
                    {
                        finalAnswer = revisionResponse.Text;
                        revisionText = revisionResponse.Text;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Revision LLM call failed, keeping original answer");
                    revisionFailed = true;
                }

                if (revisionText is not null)
                {
                    yield return ExecutionEvent.TextChunk(AgentName,
                        $"\n[Revised answer after audit]\n{revisionText}");
                }

                if (revisionFailed)
                {
                    break;
                }

                // 修正後再次稽核
                yield return ExecutionEvent.AuditStarted(AgentName, revision);
                auditResult = await _reflectionEngine.AuditAsync(request, finalAnswer, request.Reflection, cancellationToken);
                lastAuditResult = auditResult;
                tokenTracker.Record(auditResult.InputTokens, auditResult.OutputTokens);
                yield return ExecutionEvent.AuditCompleted(
                    AgentName, auditResult.Verdict.ToString(), auditResult.Explanation,
                    auditResult.Issues.Count > 0 ? string.Join("; ", auditResult.Issues) : "");
            }
        }

        overallSw.Stop();

        // 7. 輸出工具需求
        if (toolRequests.Count > 0)
        {
            var requestsText = "\n\n--- Tool Requests ---\n" +
                string.Join('\n', toolRequests.Select(r =>
                    $"- [{r.SuggestedCategory}] {r.Description}: {r.Reason}"));
            yield return ExecutionEvent.TextChunk(AgentName, requestsText);
        }

        // 8. 完成事件
        var completionSummary = $"Completed in {steps.Count} steps, " +
                      $"{tokenTracker.TotalTokensUsed} tokens, " +
                      $"{toolCallTracker.TotalCalls} tool calls, " +
                      $"{overallSw.Elapsed.TotalSeconds:F1}s";

        yield return ExecutionEvent.AgentCompleted(AgentName,
            succeeded ? completionSummary : $"[Incomplete] {completionSummary}");

        // 壓縮統計
        if (compressionState.CompressionsApplied > 0)
        {
            _logger.LogInformation(
                "[ReAct] Compression stats: {Applied} compressions, ~{Saved} tokens saved, {Truncated} tool results truncated",
                compressionState.CompressionsApplied, compressionState.TotalTokensSaved, compressionState.TruncatedToolCallIds.Count);
        }

        // 指標收集：記錄執行完成
        _metrics?.RecordExecutionComplete(
            request.ExecutionId, succeeded, steps.Count,
            tokenTracker.TotalTokensUsed, overallSw.ElapsedMilliseconds);

        // 清理檢查點（執行完成，不再需要中間狀態）
        if (_checkpointManager is not null)
        {
            await _checkpointManager.CleanupAsync(request.ExecutionId, CancellationToken.None);
        }
        else if (_checkpoint is not null)
        {
            await _checkpoint.CleanupAsync(request.ExecutionId, CancellationToken.None);
        }

        // 記錄執行經驗到跨 Session 記憶（fire-and-forget，不阻塞 event stream）
        if (_memoryService is not null)
        {
            var capturedStepCount = steps.Count;
            var capturedTokensUsed = tokenTracker.TotalTokensUsed;
            var capturedElapsedMs = (long)overallSw.Elapsed.TotalMilliseconds;
            var capturedToolNames = steps
                .Where(s => s.Action is not null)
                .Select(s => s.Action!)
                .ToList();
            var capturedSucceeded = succeeded;
            var capturedGoal = request.Goal;
            var capturedUserId = request.UserId;
            var capturedClient = client;
            var capturedFinalAnswer = finalAnswer;

            // 擷取 Auditor 審查反饋，儲存到記憶中供未來參考
            var capturedAuditIssuesJson = lastAuditResult?.Issues is { Count: > 0 }
                ? JsonSerializer.Serialize(lastAuditResult.Issues)
                : null;

            // 軌跡轉換：將 spawn/collect 事件映射為 FlowPlan JSON（純規則映射，零 LLM）
            var capturedPlanJson = capturedSucceeded
                ? ReactTraceConverter.ConvertToFlowPlanJson(
                    [.. toolCallEvents, ExecutionEvent.WorkflowCompleted()],
                    capturedGoal)
                : null;

#pragma warning disable CS4014 // fire-and-forget：記錄記憶不應阻塞 event stream
            Task.Run(async () =>
            {
                // 30 秒超時保護，避免記憶寫入無限掛起
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.MemoryWriteTimeoutSeconds));
                try
                {
                    await _memoryService.RecordExecutionAsync(
                        capturedUserId,
                        capturedGoal,
                        capturedSucceeded,
                        capturedToolNames,
                        capturedStepCount,
                        capturedTokensUsed,
                        capturedElapsedMs,
                        capturedClient,
                        timeoutCts.Token,
                        capturedAuditIssuesJson,
                        capturedFinalAnswer,
                        capturedPlanJson);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "記錄執行記憶失敗");
                }
            }, CancellationToken.None);
#pragma warning restore CS4014
        }

        yield return ExecutionEvent.WorkflowCompleted();
    }

    private record ToolCallRecord(string Name, string Args, string Result);

    private static List<ToolCallRecord> ExtractToolCalls(ChatResponse response)
    {
        var calls = new List<ToolCallRecord>();
        var pendingCalls = new Dictionary<string, (string Name, string Args)>();

        foreach (var msg in response.Messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent call)
                {
                    var args = call.Arguments is not null
                        ? JsonSerializer.Serialize(call.Arguments, JsonDefaults.A2AOptions)
                        : "";
                    pendingCalls[call.CallId ?? call.Name] = (call.Name, args);
                }
                else if (content is FunctionResultContent result)
                {
                    var resultText = result.Result?.ToString() ?? "";
                    var callId = result.CallId ?? "unknown";
                    if (pendingCalls.TryGetValue(callId, out var pending))
                    {
                        calls.Add(new ToolCallRecord(pending.Name, pending.Args, resultText));
                        pendingCalls.Remove(callId);
                    }
                    else
                    {
                        calls.Add(new ToolCallRecord(callId, "", resultText));
                    }
                }
            }
        }

        // 加入尚未有結果的 pending calls
        foreach (var (_, pending) in pendingCalls)
        {
            calls.Add(new ToolCallRecord(pending.Name, pending.Args, ""));
        }

        return calls;
    }

    private static List<ToolRequest> ExtractToolRequests(string text)
    {
        var requests = new List<ToolRequest>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return requests;
        }

        foreach (var line in text.Split('\n'))
        {
            if (!line.Contains("[TOOL_REQUEST]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = line[(line.IndexOf(']') + 1)..].Trim();
            var parts = content.Split('|', 3);
            if (parts.Length >= 2)
            {
                requests.Add(new ToolRequest
                {
                    SuggestedCategory = parts[0].Trim(),
                    Description = parts[1].Trim(),
                    Reason = parts.Length >= 3 ? parts[2].Trim() : ""
                });
            }
        }

        return requests;
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "... [truncated]";
    }

    // ═══════════════════════════════════════════
    // P1: Sub-agent 事件解析
    // ═══════════════════════════════════════════

    /// <summary>解析 collect_results 的 JSON 結果為事件清單（避免 yield 在 try-catch 中的限制）。</summary>
    private List<ExecutionEvent> ParseCollectResults(string resultJson)
    {
        var events = new List<ExecutionEvent>();
        try
        {
            using var doc = JsonDocument.Parse(resultJson);

            // 新格式：JSON array of { id, label, status, runtimeSeconds, result }
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var label = item.TryGetProperty("label", out var l) ? l.GetString() ?? "worker" : "worker";
                    var status = item.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                    var result = item.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "";
                    var runtime = item.TryGetProperty("runtimeSeconds", out var rt) ? rt.GetDouble() : 0;

                    var statusTag = status is "completed" ? "" : $" [{status}]";
                    var runtimeTag = runtime > 0 ? $" ({runtime:F1}s)" : "";
                    events.Add(ExecutionEvent.SubAgentResponded(
                        AgentName, label, Truncate($"{statusTag}{result}".TrimStart() + runtimeTag, 500)));
                }
            }
            // 向下相容：舊格式 JSON object { runId: result }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    events.Add(ExecutionEvent.SubAgentResponded(
                        AgentName, prop.Name, Truncate(prop.Value.GetString() ?? "", 500)));
                }
            }
        }
        catch
        {
            events.Add(ExecutionEvent.TextChunk(AgentName, "[Collected results from spawned agents]"));
        }

        return events;
    }

    private static (string Name, string Content) ParseJsonArgs(string argsJson, string nameKey, string contentKey)
    {
        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            var name = doc.RootElement.TryGetProperty(nameKey, out var n) ? n.GetString() ?? "" : "";
            var content = doc.RootElement.TryGetProperty(contentKey, out var c) ? c.GetString() ?? "" : "";
            return (name, content);
        }
        catch
        {
            // 正常 fallback 路徑：工具參數不一定是合法 JSON，靜默忽略即可
            return ("unknown", "");
        }
    }

}
