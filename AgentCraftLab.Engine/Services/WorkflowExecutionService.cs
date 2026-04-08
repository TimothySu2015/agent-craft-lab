using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Strategies;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 工作流程執行服務：精簡編排器。
/// 職責：解析 JSON → Hook → 委派前處理 → 選擇策略 → 執行 → Hook → Dispose。
/// 重邏輯已抽到 WorkflowPreprocessor 和 WorkflowStrategyResolver。
/// </summary>
public class WorkflowExecutionService
{
    private readonly WorkflowPreprocessor _preprocessor;
    private readonly WorkflowStrategyResolver _strategyResolver;
    private readonly WorkflowHookRunner _hookRunner;
    private readonly IUserContext _userContext;
    private readonly ICheckpointStore _checkpointStore;
    private readonly ILogger<WorkflowExecutionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WorkflowExecutionService(
        WorkflowPreprocessor preprocessor,
        WorkflowStrategyResolver strategyResolver,
        WorkflowHookRunner hookRunner,
        IUserContext userContext,
        ICheckpointStore checkpointStore,
        ILogger<WorkflowExecutionService> logger)
    {
        _preprocessor = preprocessor;
        _strategyResolver = strategyResolver;
        _hookRunner = hookRunner;
        _userContext = userContext;
        _checkpointStore = checkpointStore;
        _logger = logger;
    }

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 1. 解析並驗證 workflow JSON
        var payload = ParseAndValidatePayload(request.WorkflowJson, out var parseError);
        if (payload is null)
        {
            yield return ExecutionEvent.Error(parseError!);
            yield break;
        }

        var hooks = payload.WorkflowSettings.Hooks;
        var userId = await _userContext.GetUserIdAsync();
        var workflowName = payload.WorkflowSettings.Type;

        // ── Hook ①: OnInput ──
        if (hooks.OnInput is not null)
        {
            var hookCtx = CreateHookContext(request, userId, workflowName);
            var result = await _hookRunner.ExecuteAsync(hooks.OnInput, hookCtx, cancellationToken);
            if (result.IsBlocked)
            {
                yield return ExecutionEvent.HookBlocked("OnInput", result.Message ?? "Blocked");
                yield break;
            }

            request.UserMessage = result.TransformedInput;
            if (result.Message is not null)
                yield return ExecutionEvent.HookExecuted("OnInput", result.Message);
        }

        // 2. 前處理（節點分類 + RAG + AgentContext 建構）
        WorkflowPreprocessor.PreprocessResult? prepResult = null;
        await foreach (var (evt, result) in _preprocessor.PrepareAsync(payload, request, cancellationToken))
        {
            if (result is not null)
            {
                prepResult = result;
            }
            else if (evt is not null)
            {
                yield return evt;
                if (evt.Type == EventTypes.Error) yield break;
            }
        }

        if (prepResult is null)
        {
            yield return ExecutionEvent.Error("Preprocessing failed without explicit error.");
            yield break;
        }

        var agentContext = prepResult.Context;

        // 3. 選擇執行策略
        var (strategy, strategyReason) = _strategyResolver.Resolve(
            payload, agentContext, prepResult.ResolvedConnections, request, prepResult.HasA2AOrAutonomousNodes);
        yield return ExecutionEvent.StrategySelected(strategy.GetType().Name, strategyReason);

        var strategyContext = new WorkflowStrategyContext(
            payload, prepResult.AllAgentNodes, prepResult.ResolvedConnections, agentContext, request,
            hooks.PreAgent is not null || hooks.PostAgent is not null ? _hookRunner : null,
            hooks, userId, request.SessionId);

        // ── Hook ②: PreExecute ──
        if (hooks.PreExecute is not null)
        {
            var hookCtx = CreateHookContext(request, userId, workflowName);
            var result = await _hookRunner.ExecuteAsync(hooks.PreExecute, hookCtx, cancellationToken);
            if (result.IsBlocked)
            {
                yield return ExecutionEvent.HookBlocked("PreExecute", result.Message ?? "Blocked");
                await agentContext.DisposeAsync();
                yield break;
            }

            if (result.Message is not null)
                yield return ExecutionEvent.HookExecuted("PreExecute", result.Message);
        }

        // 4. 委派執行 + try/finally 確保資源釋放
        string? lastOutput = null;
        string? capturedError = null;
        bool completedSuccessfully = false;

        try
        {
            await foreach (var evt in strategy.ExecuteAsync(strategyContext, cancellationToken))
            {
                if (evt.Type == EventTypes.AgentCompleted) lastOutput = evt.Text;
                else if (evt.Type == EventTypes.Error) capturedError = evt.Text;
                yield return evt;
            }

            completedSuccessfully = true;
        }
        finally
        {
            if (!completedSuccessfully && hooks.OnError is not null)
            {
                var hookCtx = CreateHookContext(request, userId, workflowName,
                    error: capturedError ?? "Workflow execution terminated unexpectedly");
                _ = _hookRunner.ExecuteAsync(hooks.OnError, hookCtx, CancellationToken.None)
                    .ContinueWith(t => _logger.LogWarning(t.Exception, "OnError hook failed"),
                        TaskContinuationOptions.OnlyOnFaulted);
            }

            await agentContext.DisposeAsync();
        }

        // ── Hook ⑤: OnComplete ──
        if (hooks.OnComplete is not null)
        {
            var hookCtx = CreateHookContext(request, userId, workflowName, output: lastOutput);
            var completeResult = await _hookRunner.ExecuteAsync(hooks.OnComplete, hookCtx, cancellationToken);
            if (completeResult.Message is not null)
                yield return ExecutionEvent.HookExecuted("OnComplete", completeResult.Message);
        }

        yield return ExecutionEvent.WorkflowCompleted();
    }

    /// <summary>
    /// 從指定節點重跑：載入 checkpoint → 用當前畫布定義重建 context → 從 rerunNodeId 繼續執行。
    /// </summary>
    public async IAsyncEnumerable<ExecutionEvent> ResumeFromNodeAsync(
        WorkflowExecutionRequest request,
        string executionId,
        string rerunNodeId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 1. 從 checkpoint store 找到目標節點的前一個快照
        var checkpoints = await _checkpointStore.ListAsync(executionId);
        if (checkpoints.Count == 0)
        {
            yield return ExecutionEvent.Error($"No checkpoints found for execution {executionId}");
            yield break;
        }

        // 找到 CompletedNodeIds 不包含 rerunNodeId 的最後一個 checkpoint
        // （= rerunNodeId 執行之前的狀態）
        ImperativeCheckpointSnapshot? targetSnapshot = null;
        foreach (var cp in checkpoints.AsEnumerable().Reverse())
        {
            var snapshot = ImperativeCheckpointSnapshot.Deserialize(cp.StateJson);
            if (snapshot is null) continue;

            // 如果 checkpoint 的 NextNodeId 就是 rerunNodeId，完美匹配
            if (snapshot.NextNodeId == rerunNodeId)
            {
                targetSnapshot = snapshot;
                break;
            }

            // 或者 CompletedNodeIds 不包含 rerunNodeId（還沒跑到）
            if (!snapshot.CompletedNodeIds.Contains(rerunNodeId))
            {
                targetSnapshot = snapshot;
                break;
            }
        }

        if (targetSnapshot is null)
        {
            yield return ExecutionEvent.Error($"Cannot find checkpoint before node {rerunNodeId}");
            yield break;
        }

        // 2. 解析當前畫布定義（可能已被使用者修改）
        var payload = ParseAndValidatePayload(request.WorkflowJson, out var parseError);
        if (payload is null)
        {
            yield return ExecutionEvent.Error(parseError!);
            yield break;
        }

        // 3. 前處理（用當前畫布定義重建 AgentContext）
        WorkflowPreprocessor.PreprocessResult? prepResult = null;
        await foreach (var (evt, result) in _preprocessor.PrepareAsync(payload, request, cancellationToken))
        {
            if (result is not null)
                prepResult = result;
            else if (evt is not null)
            {
                yield return evt;
                if (evt.Type == EventTypes.Error) yield break;
            }
        }

        if (prepResult is null)
        {
            yield return ExecutionEvent.Error("Preprocessing failed without explicit error.");
            yield break;
        }

        var agentContext = prepResult.Context;
        var userId = await _userContext.GetUserIdAsync();
        var hooks = payload.WorkflowSettings.Hooks;

        // 4. 建立 Imperative strategy 並呼叫 ResumeFromNodeAsync
        var (strategy, _) = _strategyResolver.Resolve(
            payload, agentContext, prepResult.ResolvedConnections, request, prepResult.HasA2AOrAutonomousNodes);

        if (strategy is not ImperativeWorkflowStrategy imperativeStrategy)
        {
            yield return ExecutionEvent.Error("Rerun is only supported for imperative workflow strategy.");
            await agentContext.DisposeAsync();
            yield break;
        }

        var strategyContext = new WorkflowStrategyContext(
            payload, prepResult.AllAgentNodes, prepResult.ResolvedConnections, agentContext, request,
            hooks.PreAgent is not null || hooks.PostAgent is not null ? _hookRunner : null,
            hooks, userId, request.SessionId);

        try
        {
            await foreach (var evt in imperativeStrategy.ResumeFromNodeAsync(
                strategyContext, targetSnapshot, rerunNodeId, cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            await agentContext.DisposeAsync();
        }

        yield return ExecutionEvent.WorkflowCompleted();
    }

    internal static WorkflowPayload? ParseAndValidatePayload(string workflowJson, out string? error)
    {
        WorkflowPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WorkflowPayload>(workflowJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            var preview = workflowJson.Length > 500 ? workflowJson[..500] + "..." : workflowJson;
            error = $"Invalid workflow JSON: {ex.Message}\nJSON preview: {preview}";
            return null;
        }

        if (payload is null)
        {
            var preview = workflowJson.Length > 500 ? workflowJson[..500] + "..." : workflowJson;
            error = $"Invalid workflow JSON: deserialization returned null.\nJSON preview: {preview}";
            return null;
        }

        if (payload.Nodes.Count == 0)
        {
            error = "Workflow has no nodes.";
            return null;
        }

        error = null;
        return payload;
    }

    internal static HookContext CreateHookContext(
        WorkflowExecutionRequest request, string userId, string workflowName,
        string? output = null, string? error = null, string? agentName = null)
        => new()
        {
            Input = request.UserMessage,
            WorkflowName = workflowName,
            UserId = userId,
            Output = output,
            Error = error,
            AgentName = agentName ?? ""
        };
}
