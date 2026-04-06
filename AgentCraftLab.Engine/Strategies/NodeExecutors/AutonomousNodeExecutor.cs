using System.Runtime.CompilerServices;
using System.Text;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Autonomous 節點執行器 — 委派給 IAutonomousNodeExecutor 執行 ReAct/Flow 迴圈。
/// </summary>
public sealed class AutonomousNodeExecutor : INodeExecutor
{
    public string NodeType => NodeTypes.Autonomous;

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, WorkflowNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = string.IsNullOrWhiteSpace(node.Name) ? $"Autonomous_{node.Id}" : node.Name;

        if (state.AgentContext.AutonomousExecutor is null)
        {
            yield return ExecutionEvent.Error($"Autonomous Node '{nodeName}' requires AutonomousNodeExecutor. Please ensure AgentCraftLab.Autonomous is registered.");
            yield break;
        }

        var contextPrefix = ContextPassingHelper.BuildContextPrefix(state, nodeId);
        var baseGoal = !string.IsNullOrWhiteSpace(node.Instructions)
            ? $"{node.Instructions}\n\nContext:\n{state.PreviousResult}"
            : state.PreviousResult;
        var goal = string.IsNullOrEmpty(contextPrefix) ? baseGoal : contextPrefix + "\n\n" + baseGoal;

        var request = new AutonomousNodeRequest
        {
            Goal = goal,
            Credentials = state.Request.Credentials,
            Provider = node.Provider,
            Model = node.Model,
            AvailableTools = node.Tools,
            AvailableSkills = node.Skills,
            McpServers = node.McpServers,
            A2AAgents = node.A2AAgents,
            HttpApis = state.Request.HttpApiDefs ?? [],
            MaxIterations = node.MaxIterations > 0 ? node.MaxIterations : 25,
            MaxTotalTokens = node.MaxOutputTokens is > 0 ? (long)node.MaxOutputTokens.Value : 200_000L,
            Attachment = state.Attachment
        };

        if (state.Attachment is not null)
            state.Attachment = null;

        yield return ExecutionEvent.AgentStarted(nodeName);

        var contentBuilder = new StringBuilder();
        await foreach (var evt in state.AgentContext.AutonomousExecutor.ExecuteAsync(request, cancellationToken))
        {
            if (evt.Type is EventTypes.WorkflowCompleted or EventTypes.AgentCompleted or EventTypes.AgentStarted)
                continue;

            if (evt.Type == EventTypes.TextChunk)
                contentBuilder.Append(evt.Text);

            yield return evt;
        }

        yield return ExecutionEvent.AgentCompleted(nodeName, contentBuilder.ToString());
    }
}
