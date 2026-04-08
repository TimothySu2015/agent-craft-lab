using AgentCraftLab.Data.Sqlite;
using AgentCraftLab.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Tests.Autonomous;

/// <summary>
/// 實體記憶 + 情境記憶 Store 整合測試（SQLite in-memory，共享連線）。
/// </summary>
public class MemoryStoreTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SqliteConnection _keepAliveConnection;

    public MemoryStoreTests()
    {
        // 使用共享的 in-memory SQLite 連線，避免 scope dispose 時 DB 消失
        _keepAliveConnection = new SqliteConnection("DataSource=:memory:");
        _keepAliveConnection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlite(_keepAliveConnection));

        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        // 建立 DB schema
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    // ─── Entity Memory Store ───

    [Fact]
    public async Task EntityStore_SaveAndFind()
    {
        var store = new SqliteEntityMemoryStore(_scopeFactory);
        await store.SaveAsync(new EntityMemoryDocument
        {
            Id = "ent-001",
            UserId = "local",
            EntityName = "NVIDIA",
            EntityType = "organization",
            Facts = "[\"GPU maker\"]"
        });

        var found = await store.FindByNameAsync("local", "NVIDIA");
        Assert.NotNull(found);
        Assert.Equal("NVIDIA", found.EntityName);
    }

    [Fact]
    public async Task EntityStore_FindByName_CaseInsensitive()
    {
        var store = new SqliteEntityMemoryStore(_scopeFactory);
        await store.SaveAsync(new EntityMemoryDocument
        {
            Id = "ent-002",
            UserId = "local",
            EntityName = "Apple",
            Facts = "[\"Tech company\"]"
        });

        var found = await store.FindByNameAsync("local", "apple");
        Assert.NotNull(found);
    }

    [Fact]
    public async Task EntityStore_MergeFacts_NewEntity()
    {
        var store = new SqliteEntityMemoryStore(_scopeFactory);
        await store.MergeFactsAsync("local", "Tesla", ["Electric cars", "Founded 2003"]);

        var found = await store.FindByNameAsync("local", "Tesla");
        Assert.NotNull(found);
        Assert.Contains("Electric cars", found.Facts);
        Assert.Equal(1, found.MergedCount);
    }

    [Fact]
    public async Task EntityStore_MergeFacts_ExistingEntity_AppendsDedups()
    {
        var store = new SqliteEntityMemoryStore(_scopeFactory);
        await store.MergeFactsAsync("local", "Tesla", ["Electric cars"]);
        await store.MergeFactsAsync("local", "Tesla", ["Electric cars", "CEO: Elon Musk"]);

        var found = await store.FindByNameAsync("local", "Tesla");
        Assert.NotNull(found);
        Assert.Equal(2, found.MergedCount);
        // 去重後應有 2 個事實
        Assert.Contains("CEO: Elon Musk", found.Facts);
    }

    [Fact]
    public async Task EntityStore_Search_ByKeyword()
    {
        var store = new SqliteEntityMemoryStore(_scopeFactory);
        await store.MergeFactsAsync("local", "NVIDIA", ["GPU maker"]);
        await store.MergeFactsAsync("local", "AMD", ["CPU and GPU maker"]);
        await store.MergeFactsAsync("local", "Intel", ["CPU maker"]);

        var results = await store.SearchAsync("local", "GPU");
        Assert.True(results.Count >= 2); // NVIDIA and AMD both mention GPU
    }

    [Fact]
    public async Task EntityStore_Cleanup_RemovesOldEntries()
    {
        var store = new SqliteEntityMemoryStore(_scopeFactory);

        // 手動存入一筆過期記錄
        await store.SaveAsync(new EntityMemoryDocument
        {
            Id = "ent-old",
            UserId = "local",
            EntityName = "OldEntity",
            Facts = "[]",
            UpdatedAt = DateTime.UtcNow.AddDays(-200)
        });

        var cleaned = await store.CleanupAsync("local", maxAgeDays: 180);
        Assert.Equal(1, cleaned);
    }

    // ─── Contextual Memory Store ───

    [Fact]
    public async Task ContextualStore_UpsertAndGet()
    {
        var store = new SqliteContextualMemoryStore(_scopeFactory);
        await store.UpsertPatternAsync("local", "preference", "User prefers parallel search", 0.8f);

        var patterns = await store.GetPatternsAsync("local");
        Assert.Single(patterns);
        Assert.Equal("preference", patterns[0].PatternType);
        Assert.Equal(0.8f, patterns[0].Confidence);
    }

    [Fact]
    public async Task ContextualStore_Upsert_UpdatesExistingSimilarPattern()
    {
        var store = new SqliteContextualMemoryStore(_scopeFactory);
        await store.UpsertPatternAsync("local", "preference", "User prefers parallel search for comparison tasks", 0.6f);
        await store.UpsertPatternAsync("local", "preference", "User prefers parallel search for comparison tasks with spawning", 0.8f);

        var patterns = await store.GetPatternsAsync("local");
        Assert.Single(patterns); // 相似模式合併
        Assert.Equal(2, patterns[0].OccurrenceCount);
        Assert.True(patterns[0].Confidence >= 0.8f);
    }

    [Fact]
    public async Task ContextualStore_Upsert_CreatesDifferentPattern()
    {
        var store = new SqliteContextualMemoryStore(_scopeFactory);
        await store.UpsertPatternAsync("local", "preference", "User prefers web search", 0.7f);
        await store.UpsertPatternAsync("local", "topic_interest", "Frequently asks about AI technology", 0.9f);

        var patterns = await store.GetPatternsAsync("local");
        Assert.Equal(2, patterns.Count);
    }

    [Fact]
    public async Task ContextualStore_GetPatterns_OrderByConfidence()
    {
        var store = new SqliteContextualMemoryStore(_scopeFactory);
        await store.UpsertPatternAsync("local", "behavior", "Low confidence pattern", 0.3f);
        await store.UpsertPatternAsync("local", "preference", "High confidence pattern", 0.9f);

        var patterns = await store.GetPatternsAsync("local");
        Assert.Equal(0.9f, patterns[0].Confidence);
    }

    [Fact]
    public async Task ContextualStore_Cleanup_RemovesOldEntries()
    {
        var store = new SqliteContextualMemoryStore(_scopeFactory);
        await store.SaveAsync(new ContextualMemoryDocument
        {
            Id = "ctx-old",
            UserId = "local",
            Description = "old pattern",
            UpdatedAt = DateTime.UtcNow.AddDays(-400)
        });

        var cleaned = await store.CleanupAsync("local", maxAgeDays: 365);
        Assert.Equal(1, cleaned);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _keepAliveConnection.Dispose();
        GC.SuppressFinalize(this);
    }
}
