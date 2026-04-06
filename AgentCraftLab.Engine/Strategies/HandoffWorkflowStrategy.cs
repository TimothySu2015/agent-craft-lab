using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Models;
using Microsoft.Agents.AI.Workflows;

namespace AgentCraftLab.Engine.Strategies;

/// <summary>
/// Handoff workflow 策略：router agent 將任務委派給 target agents。
/// </summary>
public class HandoffWorkflowStrategy : IWorkflowStrategy
{
    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowStrategyContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var routerInfo = WorkflowGraphHelper.FindRouterAndTargets(
            context.AgentNodes, context.ResolvedConnections, context.AgentContext.Agents);

        if (routerInfo is null)
        {
            yield return ExecutionEvent.Error("Handoff workflow requires a router agent with connections to target agents.");
            yield break;
        }

        var (routerId, handoffMap) = routerInfo.Value;
        var routerAgent = context.AgentContext.Agents[routerId];

        var builder = AgentWorkflowBuilder.CreateHandoffBuilderWith(routerAgent);
        foreach (var (fromId, targetIds) in handoffMap)
        {
            if (!context.AgentContext.Agents.TryGetValue(fromId, out var fromAgent)) continue;
            var targets = targetIds
                .Where(context.AgentContext.Agents.ContainsKey)
                .Select(id => context.AgentContext.Agents[id])
                .ToArray();
            if (targets.Length == 1)
                builder = builder.WithHandoff(fromAgent, targets[0]);
            else if (targets.Length > 1)
                builder = builder.WithHandoffs(fromAgent, targets);
        }

        Workflow workflow = builder.Build();

        await foreach (var evt in WorkflowStreamHelper.RunWorkflowStreamAsync(
            workflow, context.Request.UserMessage, null, cancellationToken))
            yield return evt;
    }
}
