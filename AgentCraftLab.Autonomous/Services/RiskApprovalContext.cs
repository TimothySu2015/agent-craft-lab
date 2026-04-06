using System.Collections.Concurrent;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// Per-execution context for risk-based human override.
/// RiskGateFunction enqueues pending approvals; ReactExecutor drains the queue after each iteration.
/// 所有成員皆 thread-safe（ConcurrentQueue + ConcurrentDictionary），
/// 因為 FunctionInvokingChatClient 會並行呼叫多個 RiskGateFunction。
/// </summary>
public sealed class RiskApprovalContext
{
    private readonly ConcurrentQueue<PendingApproval> _pending = new();
    private readonly ConcurrentDictionary<string, byte> _approved = new();

    /// <summary>是否有待審批的工具呼叫</summary>
    public bool IsWaiting => !_pending.IsEmpty;

    /// <summary>檢查工具是否已被核准（RiskGateFunction 並行呼叫安全）</summary>
    public bool IsApproved(string toolName) => _approved.ContainsKey(toolName);

    /// <summary>核准工具（ReactExecutor 呼叫，加入白名單）</summary>
    public void Approve(string toolName) => _approved.TryAdd(toolName, 0);

    /// <summary>RiskGateFunction 呼叫：將一筆待審批加入佇列</summary>
    public void RequestApproval(string toolName, string arguments, string riskLevel)
        => _pending.Enqueue(new PendingApproval(toolName, arguments, riskLevel));

    /// <summary>ReactExecutor 呼叫：取出佇列中下一筆待審批（若佇列空則回傳 null）</summary>
    public PendingApproval? Dequeue()
        => _pending.TryDequeue(out var item) ? item : null;

    public record PendingApproval(string ToolName, string Arguments, string RiskLevel);
}
