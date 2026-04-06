namespace AgentCraftLab.Engine.Services;

/// <summary>
/// Debug Mode 的暫停/恢復指令。
/// </summary>
public enum DebugAction
{
    /// <summary>滿意，繼續往下一個節點。</summary>
    Continue,
    /// <summary>重跑當前節點（使用者可能已修改設定）。</summary>
    Rerun,
    /// <summary>跳過下一個節點。</summary>
    Skip
}

/// <summary>
/// 橋接 Debug Mode 的暫停/恢復機制。與 HumanInputBridge 同 pattern。
/// 每個節點完成後，策略呼叫 WaitForActionAsync 暫停，前端提交 SubmitAction 恢復。
/// </summary>
public class DebugBridge
{
    private TaskCompletionSource<DebugAction>? _tcs;

    /// <summary>
    /// 在節點完成後呼叫，暫停執行直到前端提交 debug action。
    /// </summary>
    public async Task<DebugAction> WaitForActionAsync(CancellationToken ct)
    {
        _tcs = new TaskCompletionSource<DebugAction>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var _ = ct.Register(() => _tcs.TrySetCanceled());
        return await _tcs.Task;
    }

    /// <summary>
    /// 由前端呼叫，提供 debug action 以恢復執行。
    /// </summary>
    public void SubmitAction(DebugAction action)
    {
        _tcs?.TrySetResult(action);
    }

    /// <summary>
    /// 檢查是否正在等待 debug action。
    /// </summary>
    public bool IsWaiting => _tcs is not null && !_tcs.Task.IsCompleted;
}
