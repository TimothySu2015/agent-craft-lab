using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Code 節點執行器 — 確定性資料轉換，零 LLM 成本。
/// </summary>
public sealed class CodeNodeExecutor : INodeExecutor
{
    public string NodeType => NodeTypes.Code;

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, WorkflowNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = string.IsNullOrWhiteSpace(node.Name) ? $"Code_{node.Id}" : node.Name;
        yield return ExecutionEvent.AgentStarted(nodeName);

        var result = TransformHelper.ApplyTransform(node, state.PreviousResult);

        yield return ExecutionEvent.TextChunk(nodeName, result);
        yield return ExecutionEvent.AgentCompleted(nodeName, result);
        await Task.CompletedTask;
    }
}
