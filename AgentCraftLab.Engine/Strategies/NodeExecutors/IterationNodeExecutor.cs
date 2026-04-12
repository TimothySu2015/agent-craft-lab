using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Iteration 節點執行器 — 拆分 input 為陣列，對每個元素走訪 body 子流程。
/// </summary>
public sealed class IterationNodeExecutor : NodeExecutorBase<IterationNode>
{
    protected override async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, IterationNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = string.IsNullOrWhiteSpace(node.Name) ? $"Iteration_{node.Id}" : node.Name;
        yield return ExecutionEvent.AgentStarted(nodeName);

        var items = ImperativeWorkflowStrategy.SplitIterationInput(
            FormatSplitMode(node.Split), node.Delimiter, state.PreviousResult);
        var maxItems = node.MaxItems > 0 ? node.MaxItems : 50;
        if (items.Count > maxItems)
            items = items.Take(maxItems).ToList();

        yield return ExecutionEvent.ToolCall(nodeName, "Iteration", $"{items.Count} items");

        var bodyStartId = WorkflowGraphHelper.GetNextNodeId(state.Adjacency, nodeId, OutputPorts.Output1);
        var maxConcurrency = node.MaxConcurrency > 0 ? node.MaxConcurrency : 1;
        string[] results;

        if (maxConcurrency <= 1 || bodyStartId is null || state.ExecuteBodyChain is null)
        {
            var sequential = new List<string>();
            for (var i = 0; i < items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[i];

                yield return ExecutionEvent.ToolResult(nodeName, "Iteration",
                    $"[{i + 1}/{items.Count}] {ImperativeWorkflowStrategy.Truncate(item)}");

                if (bodyStartId is null || state.ExecuteBodyChain is null)
                {
                    sequential.Add(item);
                    continue;
                }

                var bodyResult = await state.ExecuteBodyChain(bodyStartId, nodeId, item, state, cancellationToken);
                sequential.Add(bodyResult);
            }

            results = sequential.ToArray();
        }
        else
        {
            yield return ExecutionEvent.ToolResult(nodeName, "Iteration",
                $"Parallel: {items.Count} items × {maxConcurrency} concurrent");

            using var throttle = new SemaphoreSlim(maxConcurrency);
            var tasks = items.Select(async (item, i) =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    return await state.ExecuteBodyChain(bodyStartId, nodeId, item, state, cancellationToken);
                }
                finally
                {
                    throttle.Release();
                }
            }).ToList();

            results = await Task.WhenAll(tasks);
        }

        var aggregated = string.Join("\n", results.Where(r => !string.IsNullOrEmpty(r)));
        yield return ExecutionEvent.TextChunk(nodeName, aggregated);
        yield return ExecutionEvent.AgentCompleted(nodeName, aggregated);
    }

    protected override Task<NodeExecutionResult> BuildResultAsync(
        string nodeId, IterationNode node,
        ImperativeExecutionState state, List<ExecutionEvent> collectedEvents,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new NodeExecutionResult
        {
            OutputPort = OutputPorts.Output2 // Done port
        });

    private static string FormatSplitMode(SplitModeKind kind) => kind switch
    {
        SplitModeKind.Delimiter => "delimiter",
        _ => "json-array"
    };
}
