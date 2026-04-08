using AgentCraftLab.Data;
using AgentCraftLab.Search.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Data.PostgreSQL;

/// <summary>
/// PostgreSQL 資料層 Provider 的 DI 擴展方法。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 註冊 PostgreSQL 資料層（AppDbContext + CredentialProtector + 全部 15 個 Store）。
    /// </summary>
    public static IServiceCollection AddPostgreSqlDataProvider(this IServiceCollection services, string connectionString)
    {
        services.AddDataProtection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));
        services.AddSingleton<CredentialProtector>();
        services.AddSingleton<IWorkflowStore, PgWorkflowStore>();
        services.AddSingleton<ICredentialStore, PgCredentialStore>();
        services.AddSingleton<IRequestLogStore, PgRequestLogStore>();
        services.AddSingleton<ISkillStore, PgSkillStore>();
        services.AddSingleton<ITemplateStore, PgTemplateStore>();
        services.AddSingleton<IExecutionMemoryStore>(sp =>
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var se = sp.GetService<ISearchEngine>();
            var memLogger = sp.GetService<ILogger<PgExecutionMemoryStore>>();
            return new PgExecutionMemoryStore(scopeFactory, se, memLogger);
        });
        services.AddSingleton<IEntityMemoryStore, PgEntityMemoryStore>();
        services.AddSingleton<ICraftMdStore, PgCraftMdStore>();
        services.AddSingleton<ICheckpointStore, PgCheckpointStore>();
        services.AddSingleton<IContextualMemoryStore, PgContextualMemoryStore>();
        services.AddSingleton<IKnowledgeBaseStore, PgKnowledgeBaseStore>();
        services.AddSingleton<IDataSourceStore, PgDataSourceStore>();
        services.AddSingleton<IApiKeyStore, PgApiKeyStore>();
        services.AddSingleton<IRefineryStore, PgRefineryStore>();
        services.AddSingleton<IScheduleStore, PgScheduleStore>();

        return services;
    }

    /// <summary>
    /// 初始化 PostgreSQL 資料庫（自動建表）。
    /// PostgreSQL 的 EnsureCreated() 會完整建立所有表和索引，不需要手動 DDL 遷移。
    /// </summary>
    public static async Task InitializePostgreSqlAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
