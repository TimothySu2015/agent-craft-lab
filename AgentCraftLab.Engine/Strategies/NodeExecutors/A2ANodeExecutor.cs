using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// A2A Agent 節點執行器 — 呼叫遠端 A2A Agent。
/// </summary>
public sealed class A2ANodeExecutor : INodeExecutor
{
    public string NodeType => NodeTypes.A2AAgent;

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, WorkflowNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var agentName = string.IsNullOrWhiteSpace(node.Name) ? $"A2A_{node.Id}" : node.Name;
        yield return ExecutionEvent.AgentStarted(agentName);

        if (state.A2AClient is null || string.IsNullOrWhiteSpace(node.A2AUrl))
        {
            yield return ExecutionEvent.Error($"A2A Agent '{agentName}' has no URL or A2AClientService is not available.");
            yield break;
        }

        yield return ExecutionEvent.A2ATaskStatus(agentName, TaskStates.Submitted);

        var contextPrefix = ContextPassingHelper.BuildContextPrefix(state, nodeId);
        var message = string.IsNullOrEmpty(contextPrefix) ? state.PreviousResult : contextPrefix + "\n\n" + state.PreviousResult;

        var format = node.A2AFormat ?? "auto";
        var result = await state.A2AClient.SendMessageAsync(
            node.A2AUrl, message, format: format, timeoutSeconds: Timeouts.A2AAgentSeconds);

        cancellationToken.ThrowIfCancellationRequested();

        if (result.StartsWith("A2A call failed", StringComparison.Ordinal) ||
            result.StartsWith("A2A call timed out", StringComparison.Ordinal) ||
            result.StartsWith("A2A error", StringComparison.Ordinal))
        {
            yield return ExecutionEvent.A2ATaskStatus(agentName, TaskStates.Failed);
            yield return ExecutionEvent.Error($"[{agentName}] {result}");
            yield break;
        }

        yield return ExecutionEvent.A2ATaskStatus(agentName, TaskStates.Completed);
        yield return ExecutionEvent.TextChunk(agentName, result);
        yield return ExecutionEvent.AgentCompleted(agentName, result);
    }
}
