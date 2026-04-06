using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Parallel 節點執行器 — N 個分支同時執行，合併結果。
/// 支援共用節點偵測（降級為序列）和 linked cancellation。
/// </summary>
public sealed class ParallelNodeExecutor : INodeExecutor
{
    public string NodeType => NodeTypes.Parallel;

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, WorkflowNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = string.IsNullOrWhiteSpace(node.Name) ? $"Parallel_{node.Id}" : node.Name;
        yield return ExecutionEvent.AgentStarted(nodeName);

        var branchNames = (node.Branches ?? "Branch1,Branch2")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        yield return ExecutionEvent.ToolCall(nodeName, "Parallel", $"{branchNames.Length} branches");

        if (state.ExecuteBodyChain is null)
        {
            yield return ExecutionEvent.Error($"[{nodeName}] ExecuteBodyChain not available");
            yield break;
        }

        // 收集分支起點 + 共用節點偵測
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

        var merged = ImperativeWorkflowStrategy.MergeParallelResults(results, node.MergeStrategy);
        // 合計所有分支的 token 估算（各分支 input + output）
        var branchTokens = results.Sum(r => ModelPricing.EstimateTokens(r.Result));
        yield return ExecutionEvent.TextChunk(nodeName, merged);
        yield return ExecutionEvent.AgentCompleted(nodeName, merged, branchTokens, branchTokens, node.Model);
    }

    public Task<NodeExecutionResult> BuildResultAsync(
        string nodeId, WorkflowNode node,
        ImperativeExecutionState state, List<ExecutionEvent> collectedEvents,
        CancellationToken cancellationToken = default)
    {
        // Done port = 分支數 + 1
        var branchCount = (node.Branches ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
        return Task.FromResult(new NodeExecutionResult
        {
            OutputPort = $"output_{branchCount + 1}"
        });
    }
}
