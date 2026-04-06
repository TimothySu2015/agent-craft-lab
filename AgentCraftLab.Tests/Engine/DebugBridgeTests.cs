using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class DebugBridgeTests
{
    [Fact]
    public async Task WaitForAction_BlocksUntilSubmit()
    {
        var bridge = new DebugBridge();

        var waitTask = bridge.WaitForActionAsync(CancellationToken.None);
        Assert.False(waitTask.IsCompleted);

        bridge.SubmitAction(DebugAction.Continue);
        var result = await waitTask;

        Assert.Equal(DebugAction.Continue, result);
    }

    [Fact]
    public async Task SubmitAction_Rerun_ResolvesCorrectly()
    {
        var bridge = new DebugBridge();

        var waitTask = bridge.WaitForActionAsync(CancellationToken.None);
        bridge.SubmitAction(DebugAction.Rerun);

        Assert.Equal(DebugAction.Rerun, await waitTask);
    }

    [Fact]
    public async Task SubmitAction_Skip_ResolvesCorrectly()
    {
        var bridge = new DebugBridge();

        var waitTask = bridge.WaitForActionAsync(CancellationToken.None);
        bridge.SubmitAction(DebugAction.Skip);

        Assert.Equal(DebugAction.Skip, await waitTask);
    }

    [Fact]
    public async Task CancellationToken_CancelsWait()
    {
        var bridge = new DebugBridge();
        using var cts = new CancellationTokenSource();

        var waitTask = bridge.WaitForActionAsync(cts.Token);
        Assert.False(waitTask.IsCompleted);

        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => waitTask);
    }

    [Fact]
    public async Task IsWaiting_CorrectState()
    {
        var bridge = new DebugBridge();

        Assert.False(bridge.IsWaiting);

        var waitTask = bridge.WaitForActionAsync(CancellationToken.None);
        Assert.True(bridge.IsWaiting);

        bridge.SubmitAction(DebugAction.Continue);
        await waitTask;
        Assert.False(bridge.IsWaiting);
    }

    [Fact]
    public void SubmitAction_WithoutWait_DoesNotThrow()
    {
        var bridge = new DebugBridge();
        // No WaitForActionAsync called — SubmitAction should be safe (TrySetResult on null _tcs)
        bridge.SubmitAction(DebugAction.Continue);
    }
}
