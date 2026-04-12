using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// 強型別 Executor 基底 — 由 <see cref="NodeExecutorRegistry"/> 依 <see cref="NodeConfig"/>
/// 實際型別分派。子類別只需實作 <see cref="ExecuteAsync(string, TNode, ImperativeExecutionState, CancellationToken)"/>
/// 並獲得強型別 <typeparamref name="TNode"/>，不必自行 cast。
/// </summary>
/// <typeparam name="TNode">具體的 <see cref="NodeConfig"/> 子型別（例如 <c>AgentNode</c>、<c>HumanNode</c>）。</typeparam>
public abstract class NodeExecutorBase<TNode> : INodeExecutor where TNode : NodeConfig
{
    public Type NodeConfigType => typeof(TNode);

    public IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId,
        NodeConfig node,
        ImperativeExecutionState state,
        CancellationToken cancellationToken)
    {
        if (node is not TNode typed)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} expected {typeof(TNode).Name}, got {node.GetType().Name}");
        }

        return ExecuteAsync(nodeId, typed, state, cancellationToken);
    }

    public Task<NodeExecutionResult> BuildResultAsync(
        string nodeId,
        NodeConfig node,
        ImperativeExecutionState state,
        List<ExecutionEvent> collectedEvents,
        CancellationToken cancellationToken = default)
    {
        if (node is not TNode typed)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} expected {typeof(TNode).Name}, got {node.GetType().Name}");
        }

        return BuildResultAsync(nodeId, typed, state, collectedEvents, cancellationToken);
    }

    /// <summary>
    /// 子類別實作此方法，獲得強型別的 <typeparamref name="TNode"/>。
    /// </summary>
    protected abstract IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId,
        TNode node,
        ImperativeExecutionState state,
        CancellationToken cancellationToken);

    /// <summary>
    /// 子類別可覆寫此方法提供自訂 result 建構邏輯。預設從 AgentCompleted 事件抓最後一個 output，
    /// 透過 <see cref="OutputPorts.Output1"/> 導航。
    /// </summary>
    protected virtual Task<NodeExecutionResult> BuildResultAsync(
        string nodeId,
        TNode node,
        ImperativeExecutionState state,
        List<ExecutionEvent> collectedEvents,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new NodeExecutionResult
        {
            Output = collectedEvents.LastOrDefault(e => e.Type == EventTypes.AgentCompleted)?.Text,
            OutputPort = OutputPorts.Output1
        });
}
