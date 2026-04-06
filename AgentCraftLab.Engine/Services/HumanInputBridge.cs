namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 橋接 Human Node 的暫停/恢復機制。Scoped 生命週期，每次執行一個實例。
/// </summary>
public class HumanInputBridge
{
    private TaskCompletionSource<string>? _tcs;

    /// <summary>
    /// 在 Human 節點呼叫，暫停執行直到使用者回應。
    /// </summary>
    public async Task<string> WaitForInputAsync(CancellationToken ct)
    {
        _tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var _ = ct.Register(() => _tcs.TrySetCanceled());
        return await _tcs.Task;
    }

    /// <summary>
    /// 由 UI 呼叫，提供使用者的回應以恢復執行。
    /// </summary>
    public void SubmitInput(string response)
    {
        _tcs?.TrySetResult(response);
    }

    /// <summary>
    /// 檢查是否正在等待輸入。
    /// </summary>
    public bool IsWaiting => _tcs is not null && !_tcs.Task.IsCompleted;
}
