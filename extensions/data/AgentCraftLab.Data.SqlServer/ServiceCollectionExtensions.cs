using AgentCraftLab.Data;
using AgentCraftLab.Search.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Data.SqlServer;

/// <summary>
/// SQL Server 資料層 Provider 的 DI 擴展方法。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 註冊 SQL Server 資料層（AppDbContext + CredentialProtector + 全部 15 個 Store）。
    /// </summary>
    public static IServiceCollection AddSqlServerDataProvider(this IServiceCollection services, string connectionString)
    {
        services.AddDataProtection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));
        services.AddSingleton<CredentialProtector>();
        services.AddSingleton<IWorkflowStore, SqlWorkflowStore>();
        services.AddSingleton<ICredentialStore, SqlCredentialStore>();
        services.AddSingleton<IRequestLogStore, SqlRequestLogStore>();
        services.AddSingleton<ISkillStore, SqlSkillStore>();
        services.AddSingleton<ITemplateStore, SqlTemplateStore>();
        services.AddSingleton<IExecutionMemoryStore>(sp =>
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var se = sp.GetService<ISearchEngine>();
            var memLogger = sp.GetService<ILogger<SqlExecutionMemoryStore>>();
            return new SqlExecutionMemoryStore(scopeFactory, se, memLogger);
        });
        services.AddSingleton<IEntityMemoryStore, SqlEntityMemoryStore>();
        services.AddSingleton<ICraftMdStore, SqlCraftMdStore>();
        services.AddSingleton<ICheckpointStore, SqlCheckpointStore>();
        services.AddSingleton<IContextualMemoryStore, SqlContextualMemoryStore>();
        services.AddSingleton<IKnowledgeBaseStore, SqlKnowledgeBaseStore>();
        services.AddSingleton<IDataSourceStore, SqlDataSourceStore>();
        services.AddSingleton<IApiKeyStore, SqlApiKeyStore>();
        services.AddSingleton<IRefineryStore, SqlRefineryStore>();
        services.AddSingleton<IScheduleStore, SqlScheduleStore>();

        return services;
    }

    /// <summary>
    /// 初始化 SQL Server 資料庫（自動建表）。
    /// SQL Server 的 EnsureCreated() 會完整建立所有表和索引。
    /// </summary>
    public static async Task InitializeSqlServerAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
