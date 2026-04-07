using AgentCraftLab.Engine.Data;
using MongoDB.Driver;

namespace AgentCraftLab.MongoDB;

/// <summary>
/// MongoDB 資料存取層，暴露 Collection 並建立索引。
/// 註冊為 Singleton（MongoClient 是 thread-safe）。
/// </summary>
public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(string connectionString, string databaseName = "agentcraftlab")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(databaseName);
    }

    /// <summary>底層 IMongoDatabase（供 MongoSearchEngine 等進階元件使用）。</summary>
    public IMongoDatabase Database => _database;

    public IMongoCollection<WorkflowDocument> Workflows => _database.GetCollection<WorkflowDocument>("workflows");
    public IMongoCollection<CredentialDocument> Credentials => _database.GetCollection<CredentialDocument>("credentials");
    public IMongoCollection<RequestLogDocument> RequestLogs => _database.GetCollection<RequestLogDocument>("requestLogs");
    public IMongoCollection<UserDocument> Users => _database.GetCollection<UserDocument>("users");
    public IMongoCollection<SkillDocument> Skills => _database.GetCollection<SkillDocument>("skills");
    public IMongoCollection<TemplateDocument> Templates => _database.GetCollection<TemplateDocument>("templates");
    public IMongoCollection<ScheduleDocument> Schedules => _database.GetCollection<ScheduleDocument>("schedules");
    public IMongoCollection<ScheduleLogDocument> ScheduleLogs => _database.GetCollection<ScheduleLogDocument>("scheduleLogs");
    public IMongoCollection<KnowledgeBaseDocument> KnowledgeBases => _database.GetCollection<KnowledgeBaseDocument>("knowledgeBases");
    public IMongoCollection<KbFileDocument> KbFiles => _database.GetCollection<KbFileDocument>("kbFiles");
    public IMongoCollection<ApiKeyDocument> ApiKeys => _database.GetCollection<ApiKeyDocument>("apiKeys");

    /// <summary>
    /// 建立索引（啟動時呼叫一次）。
    /// </summary>
    public async Task EnsureIndexesAsync()
    {
        // workflows: (UserId, UpdatedAt desc) + IsPublished
        await Workflows.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<WorkflowDocument>(
                Builders<WorkflowDocument>.IndexKeys
                    .Ascending(w => w.UserId)
                    .Descending(w => w.UpdatedAt)),
            new CreateIndexModel<WorkflowDocument>(
                Builders<WorkflowDocument>.IndexKeys.Ascending(w => w.IsPublished))
        ]);

        // credentials: (UserId, Provider)
        await Credentials.Indexes.CreateOneAsync(
            new CreateIndexModel<CredentialDocument>(
                Builders<CredentialDocument>.IndexKeys
                    .Ascending(c => c.UserId)
                    .Ascending(c => c.Provider)));

        // requestLogs: (CreatedAt desc) + (WorkflowKey)
        await RequestLogs.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<RequestLogDocument>(
                Builders<RequestLogDocument>.IndexKeys.Descending(l => l.CreatedAt)),
            new CreateIndexModel<RequestLogDocument>(
                Builders<RequestLogDocument>.IndexKeys.Ascending(l => l.WorkflowKey))
        ]);

        // skills: (UserId, UpdatedAt desc)
        await Skills.Indexes.CreateOneAsync(
            new CreateIndexModel<SkillDocument>(
                Builders<SkillDocument>.IndexKeys
                    .Ascending(s => s.UserId)
                    .Descending(s => s.UpdatedAt)));

        // templates: (UserId, UpdatedAt desc)
        await Templates.Indexes.CreateOneAsync(
            new CreateIndexModel<TemplateDocument>(
                Builders<TemplateDocument>.IndexKeys
                    .Ascending(t => t.UserId)
                    .Descending(t => t.UpdatedAt)));

        // schedules: (UserId) + (Enabled, NextRunAt)
        await Schedules.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<ScheduleDocument>(
                Builders<ScheduleDocument>.IndexKeys.Ascending(s => s.UserId)),
            new CreateIndexModel<ScheduleDocument>(
                Builders<ScheduleDocument>.IndexKeys
                    .Ascending(s => s.Enabled)
                    .Ascending(s => s.NextRunAt))
        ]);

        // scheduleLogs: (ScheduleId, CreatedAt desc)
        await ScheduleLogs.Indexes.CreateOneAsync(
            new CreateIndexModel<ScheduleLogDocument>(
                Builders<ScheduleLogDocument>.IndexKeys
                    .Ascending(l => l.ScheduleId)
                    .Descending(l => l.CreatedAt)));

        // knowledgeBases: (UserId, UpdatedAt desc) + (IsDeleted)
        await KnowledgeBases.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<KnowledgeBaseDocument>(
                Builders<KnowledgeBaseDocument>.IndexKeys
                    .Ascending(k => k.UserId)
                    .Descending(k => k.UpdatedAt)),
            new CreateIndexModel<KnowledgeBaseDocument>(
                Builders<KnowledgeBaseDocument>.IndexKeys.Ascending(k => k.IsDeleted))
        ]);

        // kbFiles: (KnowledgeBaseId)
        await KbFiles.Indexes.CreateOneAsync(
            new CreateIndexModel<KbFileDocument>(
                Builders<KbFileDocument>.IndexKeys.Ascending(f => f.KnowledgeBaseId)));

        // apiKeys: (KeyHash) + (UserId)
        await ApiKeys.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<ApiKeyDocument>(
                Builders<ApiKeyDocument>.IndexKeys.Ascending(k => k.KeyHash)),
            new CreateIndexModel<ApiKeyDocument>(
                Builders<ApiKeyDocument>.IndexKeys.Ascending(k => k.UserId))
        ]);

        // users: unique (Provider, ProviderId) + unique Email
        await Users.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys
                    .Ascending(u => u.Provider)
                    .Ascending(u => u.ProviderId),
                new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true })
        ]);
    }
}
