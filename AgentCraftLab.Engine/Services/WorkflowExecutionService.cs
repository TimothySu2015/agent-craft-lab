using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Strategies;
using Microsoft.Extensions.Logging;
using Schema = AgentCraftLab.Engine.Models.Schema;

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
        // 1. 解析並驗證 workflow JSON（Schema v2 only — flat 格式已於 F2b 刪除）
        var (schemaPayload, schemaHooks, workflowName, parseError) = ParseAndValidatePayload(request.WorkflowJson);
        if (schemaPayload is null)
        {
            yield return ExecutionEvent.Error(parseError!);
            yield break;
        }

        var hooks = schemaHooks;
        var userId = await _userContext.GetUserIdAsync();

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
        await foreach (var (evt, result) in _preprocessor.PrepareAsync(schemaPayload, request, cancellationToken))
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
            schemaPayload, agentContext, prepResult.ResolvedConnections, request, prepResult.HasA2AOrAutonomousNodes);
        yield return ExecutionEvent.StrategySelected(strategy.GetType().Name, strategyReason);

        var strategyContext = new WorkflowStrategyContext(
            schemaPayload, prepResult.AllAgentNodes, prepResult.ResolvedConnections, agentContext, request,
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
        var (schemaPayload, schemaHooks, _, parseError) = ParseAndValidatePayload(request.WorkflowJson);
        if (schemaPayload is null)
        {
            yield return ExecutionEvent.Error(parseError!);
            yield break;
        }

        // 3. 前處理（用當前畫布定義重建 AgentContext）
        WorkflowPreprocessor.PreprocessResult? prepResult = null;
        await foreach (var (evt, result) in _preprocessor.PrepareAsync(schemaPayload, request, cancellationToken))
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
        var hooks = schemaHooks;

        // 4. 建立 Imperative strategy
        var (strategy, _) = _strategyResolver.Resolve(
            schemaPayload, agentContext, prepResult.ResolvedConnections, request, prepResult.HasA2AOrAutonomousNodes);

        if (strategy is not ImperativeWorkflowStrategy imperativeStrategy)
        {
            yield return ExecutionEvent.Error("Rerun is only supported for imperative workflow strategy.");
            await agentContext.DisposeAsync();
            yield break;
        }

        var strategyContext = new WorkflowStrategyContext(
            schemaPayload, prepResult.AllAgentNodes, prepResult.ResolvedConnections, agentContext, request,
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

    /// <summary>
    /// 解析 workflow JSON — 只接受新 Schema v2.0 nested discriminator union 格式。
    /// Flat 舊格式已於 F2b 棄用；前端於 F3 切換至 Schema v2。
    /// </summary>
    internal static (Schema.WorkflowPayload? Payload, Schema.WorkflowHooks Hooks, string WorkflowName, string? Error)
        ParseAndValidatePayload(string workflowJson)
    {
        if (string.IsNullOrWhiteSpace(workflowJson))
        {
            return (null, new Schema.WorkflowHooks(), "", "Invalid workflow JSON: empty payload");
        }

        Schema.WorkflowPayload? schemaPayload;
        try
        {
            schemaPayload = JsonSerializer.Deserialize<Schema.WorkflowPayload>(
                workflowJson, Schema.SchemaJsonOptions.Default);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var preview = workflowJson.Length > 500 ? workflowJson[..500] + "..." : workflowJson;
            return (null, new Schema.WorkflowHooks(), "",
                $"Invalid workflow JSON: {ex.Message}\nJSON preview: {preview}");
        }

        if (schemaPayload is null)
        {
            var preview = workflowJson.Length > 500 ? workflowJson[..500] + "..." : workflowJson;
            return (null, new Schema.WorkflowHooks(), "",
                $"Invalid workflow JSON: deserialization returned null.\nJSON preview: {preview}");
        }

        if (schemaPayload.Nodes.Count == 0)
        {
            return (null, new Schema.WorkflowHooks(), "", "Workflow has no nodes.");
        }

        return (schemaPayload, schemaPayload.Hooks, schemaPayload.Settings.Strategy, null);
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
