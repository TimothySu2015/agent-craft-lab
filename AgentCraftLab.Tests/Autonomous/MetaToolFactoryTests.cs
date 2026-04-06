using AgentCraftLab.Autonomous.Services;

namespace AgentCraftLab.Tests.Autonomous;

public class MetaToolFactoryTests
{
    // ─── SharedStateStore 直接測試（MetaToolFactory 的核心依賴） ───

    [Fact]
    public void SharedState_SetAndGet_Works()
    {
        var store = new SharedStateStore();
        var result = store.Set("key1", "value1", "orchestrator");

        Assert.True(result);
        var entry = store.Get("key1");
        Assert.NotNull(entry);
        Assert.Equal("value1", entry.Value);
        Assert.Equal("orchestrator", entry.SetBy);
    }

    [Fact]
    public void SharedState_OrchestratorKey_CannotBeOverwrittenBySubAgent()
    {
        var store = new SharedStateStore();
        store.Set("protected", "original", "orchestrator");

        var result = store.Set("protected", "hijacked", "sub-agent-1");

        Assert.False(result);
        Assert.Equal("original", store.Get("protected")!.Value);
    }

    [Fact]
    public void SharedState_OrchestratorKey_CanBeOverwrittenByOrchestrator()
    {
        var store = new SharedStateStore();
        store.Set("key", "v1", "orchestrator");

        var result = store.Set("key", "v2", "orchestrator");

        Assert.True(result);
        Assert.Equal("v2", store.Get("key")!.Value);
    }

    [Fact]
    public void SharedState_SubAgentKey_CanBeOverwrittenByAnyone()
    {
        var store = new SharedStateStore();
        store.Set("key", "v1", "agent-a");

        var result = store.Set("key", "v2", "agent-b");

        Assert.True(result);
        Assert.Equal("v2", store.Get("key")!.Value);
    }

    [Fact]
    public void SharedState_GetNonexistent_ReturnsNull()
    {
        var store = new SharedStateStore();
        Assert.Null(store.Get("missing"));
    }

    [Fact]
    public void SharedState_List_ReturnsAll()
    {
        var store = new SharedStateStore();
        store.Set("a", "1", "x");
        store.Set("b", "2", "y");

        var all = store.List();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void SharedState_Initialize_SeedsValues()
    {
        var store = new SharedStateStore();
        store.Initialize(new Dictionary<string, string> { ["init"] = "val" });

        var entry = store.Get("init");
        Assert.NotNull(entry);
        Assert.Equal("val", entry.Value);
        Assert.Equal("system", entry.SetBy);
    }

    [Fact]
    public void SharedState_Initialize_NullDoesNothing()
    {
        var store = new SharedStateStore();
        store.Initialize(null);
        Assert.Empty(store.List());
    }

    [Fact]
    public void SharedState_Remove_Works()
    {
        var store = new SharedStateStore();
        store.Set("key", "val", "x");

        Assert.True(store.Remove("key"));
        Assert.Null(store.Get("key"));
    }

    [Fact]
    public void SharedState_Remove_NonexistentReturnsFalse()
    {
        var store = new SharedStateStore();
        Assert.False(store.Remove("missing"));
    }

    // ─── AskUserContext 直接測試 ───

    [Fact]
    public void AskUserContext_RequestInput_SetsPending()
    {
        var ctx = new AskUserContext();
        ctx.RequestInput("What do you want?", "text", null);

        Assert.True(ctx.IsWaiting);
        Assert.Equal("What do you want?", ctx.Question);
        Assert.Equal("text", ctx.InputType);
    }

    [Fact]
    public void AskUserContext_Reset_ClearsPending()
    {
        var ctx = new AskUserContext();
        ctx.RequestInput("Q", "text", null);
        ctx.Reset();

        Assert.False(ctx.IsWaiting);
    }
}
