using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;

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
/// 以 <see cref="NodeConfig"/> 子型別作為分派鍵（透過 <see cref="NodeConfigType"/> property），
/// 由 <see cref="NodeExecutorRegistry"/> 根據節點實際型別派遣。
/// 子類別應繼承 <see cref="NodeExecutorBase{TNode}"/> 獲得強型別 NodeConfig 存取。
/// </summary>
public interface INodeExecutor
{
    /// <summary>此 executor 支援的 NodeConfig 子型別（例如 <c>typeof(AgentNode)</c>）。</summary>
    Type NodeConfigType { get; }

    /// <summary>
    /// 執行節點，串流回傳事件。
    /// </summary>
    IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId,
        NodeConfig node,
        ImperativeExecutionState state,
        CancellationToken cancellationToken);

    /// <summary>
    /// 從事件流中提取執行結果（output + 下一個節點）。
    /// </summary>
    Task<NodeExecutionResult> BuildResultAsync(
        string nodeId,
        NodeConfig node,
        ImperativeExecutionState state,
        List<ExecutionEvent> collectedEvents,
        CancellationToken cancellationToken = default);
}
