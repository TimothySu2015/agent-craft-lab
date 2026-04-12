using AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// 節點執行器註冊表 — 依 <see cref="NodeConfig"/> 實際型別分派到對應的 <see cref="INodeExecutor"/>。
/// </summary>
public sealed class NodeExecutorRegistry
{
    private readonly Dictionary<Type, INodeExecutor> _executors = new();

    public NodeExecutorRegistry(IEnumerable<INodeExecutor> executors)
    {
        foreach (var executor in executors)
        {
            _executors[executor.NodeConfigType] = executor;
        }
    }

    /// <summary>依 node 的 runtime type 查找 executor，找不到回傳 null。</summary>
    public INodeExecutor? Get(NodeConfig node) =>
        _executors.GetValueOrDefault(node.GetType());

    /// <summary>依 NodeConfig 子型別直接查找（例如 <c>Get&lt;AgentNode&gt;()</c>）。</summary>
    public INodeExecutor? Get<TNode>() where TNode : NodeConfig =>
        _executors.GetValueOrDefault(typeof(TNode));

    /// <summary>是否有對應 NodeConfig 型別的 executor。</summary>
    public bool Has(NodeConfig node) => _executors.ContainsKey(node.GetType());
}
