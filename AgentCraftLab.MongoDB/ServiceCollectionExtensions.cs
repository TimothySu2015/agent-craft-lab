using AgentCraftLab.Engine.Data;
using AgentCraftLab.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.MongoDB;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 使用 MongoDB 作為資料庫，替換 Engine 預設的 SQLite Store。
    /// </summary>
    public static IServiceCollection AddMongoDbProvider(
        this IServiceCollection services,
        string connectionString,
        string databaseName = "agentcraftlab")
    {
        services.AddSingleton(new MongoDbContext(connectionString, databaseName));

        services.Replace(ServiceDescriptor.Singleton<IWorkflowStore, MongoWorkflowStore>());
        services.Replace(ServiceDescriptor.Singleton<ICredentialStore, MongoCredentialStore>());
        services.Replace(ServiceDescriptor.Singleton<ISkillStore, MongoSkillStore>());
        services.Replace(ServiceDescriptor.Singleton<IRequestLogStore, MongoRequestLogStore>());
        services.Replace(ServiceDescriptor.Singleton<ITemplateStore, MongoTemplateStore>());
        services.Replace(ServiceDescriptor.Singleton<IKnowledgeBaseStore, MongoKnowledgeBaseStore>());
        services.Replace(ServiceDescriptor.Singleton<IApiKeyStore, MongoApiKeyStore>());
        services.Replace(ServiceDescriptor.Singleton<IScheduleStore, MongoScheduleStore>());

        return services;
    }

    /// <summary>
    /// 使用 MongoDB Atlas 搜尋引擎，替換 Engine 預設的 SQLite SearchEngine。
    /// 需要 MongoDB Atlas 部署（Atlas Vector Search + Atlas Search）。
    /// 自架 MongoDB 全文搜尋會 fallback 到 regex。
    /// </summary>
    public static IServiceCollection AddMongoSearch(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<ISearchEngine, MongoSearchEngine>());
        return services;
    }

    /// <summary>
    /// 初始化 MongoDB 索引並檢查 Store 覆蓋完整性（應用啟動時呼叫一次）。
    /// </summary>
    public static async Task InitializeMongoDbAsync(this IServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<MongoDbContext>();
        await db.EnsureIndexesAsync();

        ValidateStoreCoverage(serviceProvider);
    }

    /// <summary>
    /// 檢查所有 Store 是否都已替換為 MongoDB 實作。
    /// 未覆蓋的 Store 會記錄警告（資料會留在 SQLite）。
    /// </summary>
    private static void ValidateStoreCoverage(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("AgentCraftLab.MongoDB");
        if (logger is null) return;

        var storeInterfaces = new (Type Interface, string Name)[]
        {
            (typeof(IWorkflowStore), "WorkflowStore"),
            (typeof(ICredentialStore), "CredentialStore"),
            (typeof(ISkillStore), "SkillStore"),
            (typeof(IRequestLogStore), "RequestLogStore"),
            (typeof(ITemplateStore), "TemplateStore"),
            (typeof(IKnowledgeBaseStore), "KnowledgeBaseStore"),
            (typeof(IApiKeyStore), "ApiKeyStore"),
            (typeof(IScheduleStore), "ScheduleStore"),
            (typeof(IDataSourceStore), "DataSourceStore"),
            (typeof(IExecutionMemoryStore), "ExecutionMemoryStore"),
            (typeof(ICraftMdStore), "CraftMdStore"),
            (typeof(ICheckpointStore), "CheckpointStore"),
            (typeof(IEntityMemoryStore), "EntityMemoryStore"),
            (typeof(IContextualMemoryStore), "ContextualMemoryStore"),
            (typeof(IRefineryStore), "RefineryStore"),
        };

        var uncovered = new List<string>();
        foreach (var (iface, name) in storeInterfaces)
        {
            var impl = serviceProvider.GetService(iface);
            if (impl is not null && !impl.GetType().Namespace?.StartsWith("AgentCraftLab.MongoDB") == true)
            {
                uncovered.Add(name);
            }
        }

        if (uncovered.Count > 0)
        {
            logger.LogWarning(
                "MongoDB Provider 啟用中，但以下 {Count} 個 Store 仍使用 SQLite：{Stores}。這些資料不會存入 MongoDB。",
                uncovered.Count, string.Join(", ", uncovered));
        }
    }
}
