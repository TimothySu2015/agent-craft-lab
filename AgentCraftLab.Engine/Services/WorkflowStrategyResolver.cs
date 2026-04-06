using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Strategies;
using AgentCraftLab.Engine.Strategies.NodeExecutors;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 策略選擇器 — 從 WorkflowExecutionService 抽出的職責：
/// 根據 payload 特性選擇對應的 IWorkflowStrategy。
/// </summary>
public class WorkflowStrategyResolver
{
    private readonly IHistoryStrategy _historyStrategy;
    private readonly NodeExecutorRegistry? _executorRegistry;
    private readonly ICheckpointStore? _checkpointStore;
    private readonly ILogger<ImperativeWorkflowStrategy>? _imperativeLogger;

    public WorkflowStrategyResolver(
        IHistoryStrategy? historyStrategy = null,
        NodeExecutorRegistry? executorRegistry = null,
        ICheckpointStore? checkpointStore = null,
        ILogger<ImperativeWorkflowStrategy>? imperativeLogger = null)
    {
        _historyStrategy = historyStrategy ?? new SimpleTrimmingStrategy();
        _executorRegistry = executorRegistry;
        _checkpointStore = checkpointStore;
        _imperativeLogger = imperativeLogger;
    }

    /// <summary>
    /// 根據 workflow 特性選擇執行策略。
    /// 優先級：A2A/Autonomous → SingleAgent → Human → Attachment → SpecialNodes → ExplicitType → AutoDetect
    /// </summary>
    public (IWorkflowStrategy Strategy, string Reason) Resolve(
        WorkflowPayload payload,
        AgentExecutionContext agentContext,
        List<WorkflowConnection> resolvedConnections,
        WorkflowExecutionRequest request,
        bool hasA2AOrAutonomousNodes = false)
    {
        if (hasA2AOrAutonomousNodes)
            return (CreateImperative(), "hasA2ANodes");

        // Debug Mode 需要 Imperative strategy（逐節點暫停）
        if (request.DebugBridge is not null)
            return (CreateImperative(), "debugMode");

        if (agentContext.Agents.Count == 1)
            return (new SingleAgentStrategy(), "singleAgent");

        if (request.Attachment is { Data.Length: > 0 })
            return (CreateImperative(), "hasAttachment");

        // 查詢 NodeTypeRegistry：任何需要 Imperative 的節點（Human/Code/Iteration/Parallel 等）
        if (NodeTypeRegistry.HasAnyRequiringImperative(payload.Nodes))
            return (CreateImperative(), "hasImperativeNodes");

        var workflowType = payload.WorkflowSettings.Type;
        if (workflowType == WorkflowTypes.Auto)
            workflowType = WorkflowGraphHelper.DetectWorkflowType(payload.Nodes, resolvedConnections, agentContext.Agents);

        return (workflowType switch
        {
            WorkflowTypes.Sequential => new SequentialWorkflowStrategy(),
            WorkflowTypes.Concurrent => new ConcurrentWorkflowStrategy(),
            WorkflowTypes.Handoff => new HandoffWorkflowStrategy(),
            WorkflowTypes.Imperative => CreateImperative(),
            _ => throw new NotSupportedException(
                $"Workflow type '{workflowType}' is not supported. Supported: auto, sequential, concurrent, handoff, imperative.")
        }, $"detected:{workflowType}");
    }

    private ImperativeWorkflowStrategy CreateImperative()
        => new(_historyStrategy, _executorRegistry, _checkpointStore, _imperativeLogger);
}
