using System.Runtime.CompilerServices;
using System.Diagnostics;
using AgentCraftLab.Engine.Diagnostics;
using AgentCraftLab.Engine.Models;
using Microsoft.Agents.AI.Workflows;

namespace AgentCraftLab.Engine.Strategies;

/// <summary>
/// Sequential workflow 策略：按連線順序依次執行 agents。
/// </summary>
public class SequentialWorkflowStrategy : IWorkflowStrategy
{
    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowStrategyContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sessionId = context.SessionId;

        using var workflowActivity = EngineActivitySource.Source.StartActivity(
            "workflow_execute", ActivityKind.Server);
        workflowActivity?.SetTag("workflow.strategy", "sequential");
        workflowActivity?.SetTag("workflow.agent_count", context.AgentNodes.Count);
        if (sessionId is not null)
            workflowActivity?.SetTag(EngineActivitySource.SessionIdTag, sessionId);

        var orderedAgents = WorkflowGraphHelper.OrderAgentsByConnections(
            context.AgentNodes, context.ResolvedConnections, context.AgentContext.Agents);

        Workflow workflow = AgentWorkflowBuilder.BuildSequential(orderedAgents);

        await foreach (var evt in WorkflowStreamHelper.RunWorkflowStreamAsync(
            workflow, context.Request.UserMessage, context.AgentContext.ToolCallLogs, cancellationToken,
            sessionId))
            yield return evt;
    }
}
