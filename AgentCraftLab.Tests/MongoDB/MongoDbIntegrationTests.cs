using AgentCraftLab.Data;
using AgentCraftLab.Data.MongoDB;

namespace AgentCraftLab.Tests.MongoDB;

/// <summary>
/// MongoDB 整合測試 — 需要實際 MongoDB 連線。
/// 設定環境變數 MONGODB_CONN 啟用，未設定時自動 Skip。
/// </summary>
public class MongoDbIntegrationTests : IAsyncLifetime
{
    private readonly string? _connectionString;
    private readonly MongoDbContext? _db;
    private const string TestDbName = "agentcraftlab_integration_test";

    public MongoDbIntegrationTests()
    {
        _connectionString = Environment.GetEnvironmentVariable("MONGODB_CONN");
        if (!string.IsNullOrEmpty(_connectionString))
        {
            _db = new MongoDbContext(_connectionString, TestDbName);
        }
    }

    public async Task InitializeAsync()
    {
        if (_db is not null)
        {
            await _db.EnsureIndexesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task WorkflowStore_CRUD()
    {
        if (_db is null) return; // MONGODB_CONN 未設定時跳過

        var store = new MongoWorkflowStore(_db!);

        // Create
        var saved = await store.SaveAsync("test-user", "IntegrationTest", "desc", "auto", "{\"nodes\":[]}");
        Assert.NotEmpty(saved.Id);

        // Read
        var loaded = await store.GetAsync(saved.Id);
        Assert.NotNull(loaded);
        Assert.Equal("IntegrationTest", loaded.Name);

        // List
        var list = await store.ListAsync("test-user");
        Assert.Contains(list, w => w.Id == saved.Id);

        // Delete
        var deleted = await store.DeleteAsync("test-user", saved.Id);
        Assert.True(deleted);
        var afterDelete = await store.GetAsync(saved.Id);
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task SkillStore_CRUD()
    {
        if (_db is null) return;

        var store = new MongoSkillStore(_db!);

        // Save
        var saved = await store.SaveAsync("test-user", "TestSkill", "desc", "custom", "🧪", "test instructions", []);
        Assert.NotEmpty(saved.Id);

        // List
        var list = await store.ListAsync("test-user");
        Assert.Contains(list, s => s.Id == saved.Id);

        // Get
        var loaded = await store.GetAsync(saved.Id);
        Assert.NotNull(loaded);
        Assert.Equal("TestSkill", loaded.Name);

        // Delete
        await store.DeleteAsync("test-user", saved.Id);
        var afterDelete = await store.GetAsync(saved.Id);
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task TemplateStore_CRUD()
    {
        if (_db is null) return;

        var store = new MongoTemplateStore(_db!);

        // Save
        var saved = await store.SaveAsync("test-user", "TestTemplate", "desc", "general", "🧪", [], "{}");
        Assert.NotEmpty(saved.Id);

        // Read
        var loaded = await store.GetAsync(saved.Id);
        Assert.NotNull(loaded);
        Assert.Equal("TestTemplate", loaded.Name);

        // Delete
        await store.DeleteAsync("test-user", saved.Id);
        var afterDelete = await store.GetAsync(saved.Id);
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task EnsureIndexes_DoesNotThrow()
    {
        if (_db is null) return;
        await _db!.EnsureIndexesAsync();
    }
}
