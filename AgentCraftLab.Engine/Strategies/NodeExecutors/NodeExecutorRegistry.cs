namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// 節點執行器註冊表 — 根據 NodeType 分派到對應的 INodeExecutor。
/// 取代 ImperativeWorkflowStrategy 的 monolithic if-else chain。
/// </summary>
public sealed class NodeExecutorRegistry
{
    private readonly Dictionary<string, INodeExecutor> _executors = new(StringComparer.OrdinalIgnoreCase);

    public NodeExecutorRegistry(IEnumerable<INodeExecutor> executors)
    {
        foreach (var executor in executors)
        {
            _executors[executor.NodeType] = executor;
        }
    }

    /// <summary>取得指定節點類型的執行器，找不到回傳 null。</summary>
    public INodeExecutor? Get(string nodeType) =>
        _executors.TryGetValue(nodeType, out var executor) ? executor : null;

    /// <summary>是否有該節點類型的執行器。</summary>
    public bool Has(string nodeType) => _executors.ContainsKey(nodeType);
}
