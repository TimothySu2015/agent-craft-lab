using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// 節點執行結果 — 包含輸出和下一個節點的決定。
/// </summary>
public sealed class NodeExecutionResult
{
    /// <summary>節點的輸出文字（更新 PreviousResult）</summary>
    public string? Output { get; init; }

    /// <summary>下一個節點的 output port（null = 由呼叫端用預設 output_1）</summary>
    public string? OutputPort { get; init; }

    /// <summary>直接指定下一個節點 ID（Loop 等需要自行決定跳轉的節點使用）</summary>
    public string? NextNodeId { get; init; }

    /// <summary>是否由 executor 自行管理 nextNodeId（true 時忽略 OutputPort）</summary>
    public bool ManagesOwnNavigation { get; init; }
}

/// <summary>
/// 節點執行器介面 — 每種節點類型一個實作。
/// 取代 ImperativeWorkflowStrategy 的 monolithic if-else chain。
/// </summary>
public interface INodeExecutor
{
    /// <summary>支援的節點類型</summary>
    string NodeType { get; }

    /// <summary>
    /// 執行節點，串流回傳事件。
    /// </summary>
    IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId,
        WorkflowNode node,
        ImperativeExecutionState state,
        CancellationToken cancellationToken);

    /// <summary>
    /// 從事件流中提取執行結果（output + 下一個節點）。
    /// 預設實作：從 AgentCompleted 取 output，用 output_1 導航。
    /// </summary>
    Task<NodeExecutionResult> BuildResultAsync(
        string nodeId, WorkflowNode node,
        ImperativeExecutionState state, List<ExecutionEvent> collectedEvents,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new NodeExecutionResult
        {
            Output = collectedEvents.LastOrDefault(e => e.Type == EventTypes.AgentCompleted)?.Text,
            OutputPort = OutputPorts.Output1
        });
}
