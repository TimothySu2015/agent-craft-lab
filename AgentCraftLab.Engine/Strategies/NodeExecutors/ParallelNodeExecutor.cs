using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Parallel 節點執行器 — N 個分支同時執行，合併結果。
/// 支援共用節點偵測（降級為序列）和 linked cancellation。
/// </summary>
public sealed class ParallelNodeExecutor : NodeExecutorBase<ParallelNode>
{
    protected override async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, ParallelNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = string.IsNullOrWhiteSpace(node.Name) ? $"Parallel_{node.Id}" : node.Name;
        yield return ExecutionEvent.AgentStarted(nodeName);

        var branchNames = node.Branches.Count > 0
            ? node.Branches.Select(b => b.Name).ToArray()
            : ["Branch1", "Branch2"];

        yield return ExecutionEvent.ToolCall(nodeName, "Parallel", $"{branchNames.Length} branches");

        if (state.ExecuteBodyChain is null)
        {
            yield return ExecutionEvent.Error($"[{nodeName}] ExecuteBodyChain not available");
            yield break;
        }

        var branchStartIds = new List<(string Name, string? StartId)>();
        var allBranchNodeIds = new HashSet<string>();
        var hasSharedNodes = false;

        for (var i = 0; i < branchNames.Length; i++)
        {
            var portName = $"output_{i + 1}";
            var startId = WorkflowGraphHelper.GetNextNodeId(state.Adjacency, nodeId, portName);
            branchStartIds.Add((branchNames[i], startId));

            var walkId = startId;
            while (walkId is not null && walkId != nodeId && !hasSharedNodes)
            {
                if (!allBranchNodeIds.Add(walkId)) hasSharedNodes = true;
                walkId = WorkflowGraphHelper.GetNextNodeId(state.Adjacency, walkId, OutputPorts.Output1);
            }
        }

        var results = new List<(string Name, string Result)>();
        var input = state.PreviousResult;

        if (hasSharedNodes)
        {
            foreach (var (name, startId) in branchStartIds)
            {
                var result = startId is not null
                    ? await state.ExecuteBodyChain(startId, nodeId, input, state, cancellationToken)
                    : input;
                results.Add((name, result));
                yield return ExecutionEvent.ToolResult(nodeName, name,
                    ImperativeWorkflowStrategy.Truncate(result));
            }
        }
        else
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var branchTasks = branchStartIds.Select(b =>
                (b.Name, Task: b.StartId is not null
                    ? state.ExecuteBodyChain(b.StartId, nodeId, input, state, linkedCts.Token)
                    : Task.FromResult(input))).ToList();

            try
            {
                await Task.WhenAll(branchTasks.Select(b => b.Task));
            }
            catch
            {
                await linkedCts.CancelAsync();
            }

            foreach (var (name, task) in branchTasks)
            {
                string result;
                if (task.IsCompletedSuccessfully)
                {
                    result = await task;
                }
                else
                {
                    var errorMsg = task.Exception?.InnerException?.Message ?? "Unknown error";
                    result = $"[Branch '{name}' failed: {errorMsg}]";
                }
                results.Add((name, result));
                yield return ExecutionEvent.ToolResult(nodeName, name,
                    ImperativeWorkflowStrategy.Truncate(result));
            }
        }

        var merged = ImperativeWorkflowStrategy.MergeParallelResults(results, FormatMerge(node.Merge));
        var branchTokens = results.Sum(r => ModelPricing.EstimateTokens(r.Result));
        yield return ExecutionEvent.TextChunk(nodeName, merged);
        // ParallelNode 沒有 Model 欄位（分支內各 agent 才有自己的 model），token metadata 不附 model
        yield return ExecutionEvent.AgentCompleted(nodeName, merged, branchTokens, branchTokens, null);
    }

    protected override Task<NodeExecutionResult> BuildResultAsync(
        string nodeId, ParallelNode node,
        ImperativeExecutionState state, List<ExecutionEvent> collectedEvents,
        CancellationToken cancellationToken = default)
    {
        // Done port = 分支數 + 1
        var branchCount = node.Branches.Count > 0 ? node.Branches.Count : 2;
        return Task.FromResult(new NodeExecutionResult
        {
            OutputPort = $"output_{branchCount + 1}"
        });
    }

    private static string FormatMerge(MergeStrategyKind kind) => kind switch
    {
        MergeStrategyKind.Join => "join",
        MergeStrategyKind.Json => "json",
        _ => "labeled"
    };
}
