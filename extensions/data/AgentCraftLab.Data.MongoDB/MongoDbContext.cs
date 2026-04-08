using AgentCraftLab.Data;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

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

    // Memory stores
    public IMongoCollection<ExecutionMemoryDocument> ExecutionMemories => _database.GetCollection<ExecutionMemoryDocument>("executionMemories");
    public IMongoCollection<EntityMemoryDocument> EntityMemories => _database.GetCollection<EntityMemoryDocument>("entityMemories");
    public IMongoCollection<ContextualMemoryDocument> ContextualMemories => _database.GetCollection<ContextualMemoryDocument>("contextualMemories");
    public IMongoCollection<CheckpointDocument> Checkpoints => _database.GetCollection<CheckpointDocument>("checkpoints");
    public IMongoCollection<CraftMdDocument> CraftMds => _database.GetCollection<CraftMdDocument>("craftMds");
    public IMongoCollection<DataSourceDocument> DataSources => _database.GetCollection<DataSourceDocument>("dataSources");

    // Refinery stores
    public IMongoCollection<RefineryProject> RefineryProjects => _database.GetCollection<RefineryProject>("refineryProjects");
    public IMongoCollection<RefineryFile> RefineryFiles => _database.GetCollection<RefineryFile>("refineryFiles");
    public IMongoCollection<RefineryOutput> RefineryOutputs => _database.GetCollection<RefineryOutput>("refineryOutputs");

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

        // executionMemories: (UserId, CreatedAt desc)
        await ExecutionMemories.Indexes.CreateOneAsync(
            new CreateIndexModel<ExecutionMemoryDocument>(
                Builders<ExecutionMemoryDocument>.IndexKeys
                    .Ascending(m => m.UserId)
                    .Descending(m => m.CreatedAt)));

        // entityMemories: (UserId, EntityName) + (UserId, UpdatedAt desc)
        await EntityMemories.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<EntityMemoryDocument>(
                Builders<EntityMemoryDocument>.IndexKeys
                    .Ascending(e => e.UserId)
                    .Ascending(e => e.EntityName)),
            new CreateIndexModel<EntityMemoryDocument>(
                Builders<EntityMemoryDocument>.IndexKeys
                    .Ascending(e => e.UserId)
                    .Descending(e => e.UpdatedAt))
        ]);

        // contextualMemories: (UserId, UpdatedAt desc)
        await ContextualMemories.Indexes.CreateOneAsync(
            new CreateIndexModel<ContextualMemoryDocument>(
                Builders<ContextualMemoryDocument>.IndexKeys
                    .Ascending(c => c.UserId)
                    .Descending(c => c.UpdatedAt)));

        // checkpoints: (ExecutionId, Iteration) + (CreatedAt)
        await Checkpoints.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<CheckpointDocument>(
                Builders<CheckpointDocument>.IndexKeys
                    .Ascending(c => c.ExecutionId)
                    .Ascending(c => c.Iteration)),
            new CreateIndexModel<CheckpointDocument>(
                Builders<CheckpointDocument>.IndexKeys.Descending(c => c.CreatedAt))
        ]);

        // craftMds: (UserId, WorkflowId)
        await CraftMds.Indexes.CreateOneAsync(
            new CreateIndexModel<CraftMdDocument>(
                Builders<CraftMdDocument>.IndexKeys
                    .Ascending(c => c.UserId)
                    .Ascending(c => c.WorkflowId)));

        // dataSources: (UserId, CreatedAt desc)
        await DataSources.Indexes.CreateOneAsync(
            new CreateIndexModel<DataSourceDocument>(
                Builders<DataSourceDocument>.IndexKeys
                    .Ascending(d => d.UserId)
                    .Descending(d => d.CreatedAt)));

        // refineryProjects: (UserId, UpdatedAt desc) + (IsDeleted, DeletedAt)
        await RefineryProjects.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<RefineryProject>(
                Builders<RefineryProject>.IndexKeys
                    .Ascending(r => r.UserId)
                    .Descending(r => r.UpdatedAt)),
            new CreateIndexModel<RefineryProject>(
                Builders<RefineryProject>.IndexKeys
                    .Ascending(r => r.IsDeleted)
                    .Ascending(r => r.DeletedAt))
        ]);

        // refineryFiles: (RefineryProjectId)
        await RefineryFiles.Indexes.CreateOneAsync(
            new CreateIndexModel<RefineryFile>(
                Builders<RefineryFile>.IndexKeys.Ascending(f => f.RefineryProjectId)));

        // refineryOutputs: (RefineryProjectId, Version desc)
        await RefineryOutputs.Indexes.CreateOneAsync(
            new CreateIndexModel<RefineryOutput>(
                Builders<RefineryOutput>.IndexKeys
                    .Ascending(o => o.RefineryProjectId)
                    .Descending(o => o.Version)));

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
