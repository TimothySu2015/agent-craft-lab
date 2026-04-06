using AgentCraftLab.Api;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Api;

public class HumanInputBridgeRegistryTests
{
    private readonly HumanInputBridgeRegistry _registry = new();

    [Fact]
    public async Task Register_ThenSubmitInput_BridgeReceivesInput()
    {
        var bridge = new HumanInputBridge();
        _registry.Register("thread-1", "run-1", bridge);

        // 啟動等待（背景）
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waitTask = bridge.WaitForInputAsync(cts.Token);

        // 確認 bridge 正在等待
        Assert.True(bridge.IsWaiting);

        // 提交輸入
        var submitted = _registry.SubmitInput("thread-1", "run-1", "user response");

        Assert.True(submitted);
        var result = await waitTask;
        Assert.Equal("user response", result);
    }

    [Fact]
    public void Unregister_ThenSubmitInput_ReturnsFalse()
    {
        var bridge = new HumanInputBridge();
        _registry.Register("thread-1", "run-1", bridge);
        _registry.Unregister("thread-1", "run-1");

        var submitted = _registry.SubmitInput("thread-1", "run-1", "response");

        Assert.False(submitted);
    }

    [Fact]
    public void SubmitInput_WithUnknownThreadAndRun_ReturnsFalse()
    {
        var submitted = _registry.SubmitInput("unknown-thread", "unknown-run", "response");

        Assert.False(submitted);
    }

    [Fact]
    public void SetPending_ThenGetAnyPending_ReturnsCorrectInfo()
    {
        var bridge = new HumanInputBridge();
        _registry.Register("thread-1", "run-1", bridge);
        _registry.SetPending("thread-1", "run-1", "Please confirm", "approval", "yes,no");

        var pending = _registry.GetAnyPending();

        Assert.NotNull(pending);
        Assert.Equal("thread-1", pending.ThreadId);
        Assert.Equal("run-1", pending.RunId);
        Assert.Equal("Please confirm", pending.Prompt);
        Assert.Equal("approval", pending.InputType);
        Assert.Equal("yes,no", pending.Choices);
    }
}
