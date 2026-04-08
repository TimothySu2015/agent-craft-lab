using AgentCraftLab.Data.Sqlite;
using System.Text.Json;
using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Autonomous.Services;
using AgentCraftLab.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCraftLab.Tests.Autonomous;

public class CheckpointTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SqliteConnection _keepAliveConnection;

    public CheckpointTests()
    {
        _keepAliveConnection = new SqliteConnection("DataSource=:memory:");
        _keepAliveConnection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(_keepAliveConnection));
        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    // ─── SqliteCheckpointStore ───

    [Fact]
    public async Task Store_SaveAndGetLatest()
    {
        var store = new SqliteCheckpointStore(_scopeFactory);
        await store.SaveAsync(MakeDoc("exec-1", 5));
        await store.SaveAsync(MakeDoc("exec-1", 10));

        var latest = await store.GetLatestAsync("exec-1");
        Assert.NotNull(latest);
        Assert.Equal(10, latest.Iteration);
    }

    [Fact]
    public async Task Store_GetByIteration()
    {
        var store = new SqliteCheckpointStore(_scopeFactory);
        await store.SaveAsync(MakeDoc("exec-1", 5));
        await store.SaveAsync(MakeDoc("exec-1", 10));

        var ckpt = await store.GetAsync("exec-1", 5);
        Assert.NotNull(ckpt);
        Assert.Equal(5, ckpt.Iteration);
    }

    [Fact]
    public async Task Store_ListReturnsOrderedByIteration()
    {
        var store = new SqliteCheckpointStore(_scopeFactory);
        await store.SaveAsync(MakeDoc("exec-1", 10));
        await store.SaveAsync(MakeDoc("exec-1", 5));
        await store.SaveAsync(MakeDoc("exec-1", 15));

        var list = await store.ListAsync("exec-1");
        Assert.Equal(3, list.Count);
        Assert.Equal(5, list[0].Iteration);
        Assert.Equal(10, list[1].Iteration);
        Assert.Equal(15, list[2].Iteration);
    }

    [Fact]
    public async Task Store_Cleanup_RemovesAllForExecution()
    {
        var store = new SqliteCheckpointStore(_scopeFactory);
        await store.SaveAsync(MakeDoc("exec-1", 5));
        await store.SaveAsync(MakeDoc("exec-1", 10));
        await store.SaveAsync(MakeDoc("exec-2", 5));

        await store.CleanupAsync("exec-1");

        var list1 = await store.ListAsync("exec-1");
        var list2 = await store.ListAsync("exec-2");
        Assert.Empty(list1);
        Assert.Single(list2);
    }

    [Fact]
    public async Task Store_SaveSameIteration_Overwrites()
    {
        var store = new SqliteCheckpointStore(_scopeFactory);
        await store.SaveAsync(MakeDoc("exec-1", 5, stateJson: "old"));
        await store.SaveAsync(MakeDoc("exec-1", 5, stateJson: "new"));

        var list = await store.ListAsync("exec-1");
        Assert.Single(list);
        Assert.Equal("new", list[0].StateJson);
    }

    [Fact]
    public async Task Store_GetLatest_EmptyReturnsNull()
    {
        var store = new SqliteCheckpointStore(_scopeFactory);
        var latest = await store.GetLatestAsync("nonexistent");
        Assert.Null(latest);
    }

    // ─── CheckpointSnapshot 序列化 ───

    [Fact]
    public void Snapshot_JsonRoundTrip()
    {
        var snapshot = new CheckpointSnapshot
        {
            Iteration = 7,
            Messages =
            [
                SerializableChatMessage.FromChatMessage(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.System, "prompt")),
                SerializableChatMessage.FromChatMessage(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.User, "goal")),
            ],
            InputTokensUsed = 5000,
            OutputTokensUsed = 3000,
            ToolCallCounts = new() { ["WebSearch"] = 3, ["Calculator"] = 1 },
            TotalToolCalls = 4,
            SharedState = new()
            {
                ["key1"] = new SharedStateSnapshot("key1", "value1", "orchestrator", DateTime.UtcNow)
            },
            BudgetReminderIndex = 2,
            Plan = "Step 1: search\nStep 2: analyze",
            FinalAnswer = null,
            Succeeded = false,
            CachedMessageChars = 1500
        };

        var json = JsonSerializer.Serialize(snapshot);
        var restored = JsonSerializer.Deserialize<CheckpointSnapshot>(json)!;

        Assert.Equal(7, restored.Iteration);
        Assert.Equal(2, restored.Messages.Count);
        Assert.Equal(5000, restored.InputTokensUsed);
        Assert.Equal(3000, restored.OutputTokensUsed);
        Assert.Equal(4, restored.TotalToolCalls);
        Assert.Equal(3, restored.ToolCallCounts["WebSearch"]);
        Assert.Single(restored.SharedState);
        Assert.Equal(2, restored.BudgetReminderIndex);
        Assert.Equal("Step 1: search\nStep 2: analyze", restored.Plan);
        Assert.Null(restored.FinalAnswer);
        Assert.False(restored.Succeeded);
    }

    [Fact]
    public void Snapshot_WithSubAgents_RoundTrip()
    {
        var snapshot = new CheckpointSnapshot
        {
            Iteration = 3,
            SubAgents = new()
            {
                ["researcher"] = new SubAgentSnapshot
                {
                    Name = "researcher",
                    Instructions = "Research NVIDIA",
                    ToolIds = ["WebSearch", "UrlFetch"],
                    History =
                    [
                        SerializableChatMessage.FromChatMessage(
                            new Microsoft.Extensions.AI.ChatMessage(
                                Microsoft.Extensions.AI.ChatRole.System, "You research.")),
                    ],
                    CallCount = 2
                }
            }
        };

        var json = JsonSerializer.Serialize(snapshot);
        var restored = JsonSerializer.Deserialize<CheckpointSnapshot>(json)!;

        Assert.Single(restored.SubAgents);
        var agent = restored.SubAgents["researcher"];
        Assert.Equal("researcher", agent.Name);
        Assert.Equal(2, agent.ToolIds.Count);
        Assert.Single(agent.History);
        Assert.Equal(2, agent.CallCount);
    }

    // ─── CheckpointManager ───

    [Fact]
    public async Task Manager_SaveAndLoad_RoundTrip()
    {
        var store = new SqliteCheckpointStore(_scopeFactory);
        var config = new ReactExecutorConfig { CheckpointEnabled = true, CheckpointInterval = 5 };
        var manager = new CheckpointManager(store, config, NullLogger.Instance);

        var snapshot = new CheckpointSnapshot
        {
            Iteration = 5,
            Messages =
            [
                SerializableChatMessage.FromChatMessage(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.User, "Hello")),
            ],
            InputTokensUsed = 1000,
            OutputTokensUsed = 500,
            Plan = "My plan"
        };

        await manager.SaveAsync("exec-test", snapshot, CancellationToken.None);
        var loaded = await manager.LoadAsync("exec-test", 5, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(5, loaded.Iteration);
        Assert.Single(loaded.Messages);
        Assert.Equal(1000, loaded.InputTokensUsed);
        Assert.Equal("My plan", loaded.Plan);
    }

    [Fact]
    public async Task Manager_LoadLatest()
    {
        var store = new SqliteCheckpointStore(_scopeFactory);
        var config = new ReactExecutorConfig { CheckpointEnabled = true };
        var manager = new CheckpointManager(store, config, NullLogger.Instance);

        await manager.SaveAsync("exec-test", new CheckpointSnapshot { Iteration = 5 }, CancellationToken.None);
        await manager.SaveAsync("exec-test", new CheckpointSnapshot { Iteration = 10 }, CancellationToken.None);

        var loaded = await manager.LoadLatestAsync("exec-test", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(10, loaded.Iteration);
    }

    [Fact]
    public async Task Manager_ListCheckpoints()
    {
        var store = new SqliteCheckpointStore(_scopeFactory);
        var config = new ReactExecutorConfig { CheckpointEnabled = true };
        var manager = new CheckpointManager(store, config, NullLogger.Instance);

        await manager.SaveAsync("exec-test", new CheckpointSnapshot { Iteration = 5 }, CancellationToken.None);
        await manager.SaveAsync("exec-test", new CheckpointSnapshot { Iteration = 10 }, CancellationToken.None);

        var list = await manager.ListAsync("exec-test", CancellationToken.None);
        Assert.Equal(2, list.Count);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(7, false)]
    public void Manager_ShouldSave_RespectsInterval(int iteration, bool expected)
    {
        var config = new ReactExecutorConfig { CheckpointEnabled = true, CheckpointInterval = 5 };
        var manager = new CheckpointManager(
            new SqliteCheckpointStore(_scopeFactory), config, NullLogger.Instance);

        Assert.Equal(expected, manager.ShouldSave(iteration));
    }

    [Fact]
    public void Manager_ShouldSave_FalseWhenDisabled()
    {
        var config = new ReactExecutorConfig { CheckpointEnabled = false };
        var manager = new CheckpointManager(
            new SqliteCheckpointStore(_scopeFactory), config, NullLogger.Instance);

        Assert.False(manager.ShouldSave(5));
    }

    // ─── Store: ListMetadataAsync ───

    [Fact]
    public async Task Store_ListMetadata_ExcludesStateJson()
    {
        var store = new SqliteCheckpointStore(_scopeFactory);
        await store.SaveAsync(MakeDoc("exec-m", 5, stateJson: "{\"big\":\"payload\"}"));

        var list = await store.ListMetadataAsync("exec-m");
        Assert.Single(list);
        Assert.Equal(5, list[0].Iteration);
        // StateJson 不載入 → 預設空字串
        Assert.Equal("", list[0].StateJson);
    }

    // ─── Store: CleanupOlderThanAsync ───

    [Fact]
    public async Task Store_CleanupOlderThan_RemovesExpired()
    {
        var store = new SqliteCheckpointStore(_scopeFactory);

        // 手動插入一筆過期 checkpoint
        var oldDoc = MakeDoc("exec-old", 5);
        oldDoc.CreatedAt = DateTime.UtcNow.AddHours(-48);
        await store.SaveAsync(oldDoc);

        // 插入一筆新 checkpoint
        await store.SaveAsync(MakeDoc("exec-new", 5));

        await store.CleanupOlderThanAsync(TimeSpan.FromHours(24));

        var oldList = await store.ListAsync("exec-old");
        var newList = await store.ListAsync("exec-new");
        Assert.Empty(oldList);
        Assert.Single(newList);
    }

    // ─── CheckpointManager.RestoreState ───

    [Fact]
    public void RestoreState_RestoresAllFields()
    {
        var store = new SqliteCheckpointStore(_scopeFactory);
        var config = new ReactExecutorConfig { CheckpointEnabled = true };
        var manager = new CheckpointManager(store, config, NullLogger.Instance);

        var snapshot = new CheckpointSnapshot
        {
            Iteration = 7,
            Messages =
            [
                SerializableChatMessage.FromChatMessage(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.System, "restored prompt")),
                SerializableChatMessage.FromChatMessage(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.User, "restored goal")),
            ],
            InputTokensUsed = 5000,
            OutputTokensUsed = 3000,
            ToolCallCounts = new() { ["WebSearch"] = 3 },
            TotalToolCalls = 3,
            BudgetReminderIndex = 2,
            AskUserCount = 1,
            SharedState = new()
            {
                ["key1"] = new SharedStateSnapshot("key1", "val1", "orchestrator", DateTime.UtcNow)
            }
        };

        // 準備空的可變狀態
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>();
        var steps = new List<ReactStep>();
        var tokenTracker = new AgentCraftLab.Autonomous.Services.TokenTracker(new TokenBudget());
        var toolCallTracker = new AgentCraftLab.Autonomous.Services.ToolCallTracker(new ToolCallLimits());
        var convergence = new AgentCraftLab.Autonomous.Services.ConvergenceDetector();
        var sharedState = new AgentCraftLab.Autonomous.Services.SharedStateStore();
        var loopState = new AgentCraftLab.Autonomous.Services.ReactLoopState();
        var toolCallEvents = new List<AgentCraftLab.Engine.Models.ExecutionEvent>();

        manager.RestoreState(snapshot, messages, steps, tokenTracker, toolCallTracker,
            convergence, sharedState, loopState, toolCallEvents);

        Assert.Equal(2, messages.Count);
        Assert.Equal("restored prompt", messages[0].Text);
        Assert.Equal(5000, tokenTracker.InputTokensUsed);
        Assert.Equal(3000, tokenTracker.OutputTokensUsed);
        Assert.Equal(3, toolCallTracker.TotalCalls);
        Assert.Equal(2, loopState.BudgetReminderIndex);
        Assert.Equal(1, loopState.AskUserCount);
        Assert.NotNull(sharedState.Get("key1"));
        Assert.Equal("val1", sharedState.Get("key1")!.Value);
    }

    // ─── 輔助方法 ───

    private static CheckpointDocument MakeDoc(string executionId, int iteration, string stateJson = "{}")
    {
        return new CheckpointDocument
        {
            Id = $"ckpt-{executionId}-{iteration}",
            ExecutionId = executionId,
            Iteration = iteration,
            MessageCount = iteration * 2,
            TokensUsed = iteration * 1000,
            StateJson = stateJson,
            StateSizeBytes = stateJson.Length,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _keepAliveConnection.Dispose();
        GC.SuppressFinalize(this);
    }
}
