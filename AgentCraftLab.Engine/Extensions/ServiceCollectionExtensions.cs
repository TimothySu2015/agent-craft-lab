using AgentCraftLab.Cleaner.Extensions;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Middleware;
using AgentCraftLab.Engine.Pii;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Extensions;

/// <summary>
/// AgentCraftLab.Engine 的 DI 擴展方法。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 註冊 AgentCraftLab Engine 核心服務（不含資料層，需另外呼叫 AddSqliteDataProvider / AddMongoDbProvider）。
    /// </summary>
    public static IServiceCollection AddAgentCraftEngine(this IServiceCollection services, string dataDir = "Data", string? outputDir = null, string? workingDir = null, List<string>? emailWhitelist = null)
    {
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            ToolImplementations.OutputDirectory = outputDir;
        }

        if (!string.IsNullOrWhiteSpace(workingDir))
        {
            ToolImplementations.WorkingDirectory = workingDir;
        }

        if (emailWhitelist is { Count: > 0 })
        {
            ToolImplementations.EmailWhitelist = emailWhitelist;
        }

        // 使用者（預設單人模式）
        services.AddScoped<IUserContext, LocalUserContext>();

        services.AddHttpClient();
        services.AddSingleton<ToolRegistryService>();
        services.AddSingleton<ToolManagementService>();
        services.AddSingleton<SkillRegistryService>();
        services.AddSingleton<McpClientService>();
        services.AddSingleton<A2AClientService>();
        services.AddSingleton<HttpApiToolService>();
        // CraftSearch 基礎設施（擷取器 + 分塊器 + reranker，不含搜尋引擎實例）
        // 搜尋引擎由 SearchEngineFactory 根據使用者建立的 DataSource 動態建立
        services.AddCraftSearch();
        // CraftCleaner 資料清洗引擎（Partition → Clean，RagService 可選使用）
        services.AddCraftCleaner();
        // Schema Mapper（LLM 結構化擷取 + 模板管理）
        services.AddSchemaMapper();
        services.AddSingleton<RagService>();
        services.AddSingleton<KnowledgeBaseService>();
        services.AddSingleton<RefineryService>();
        services.AddSingleton<ISearchEngineFactory, SearchEngineFactory>();
        services.AddSingleton<A2AServerService>();
        services.AddSingleton<McpServerService>();
        services.AddScoped<HumanInputBridge>();
        services.AddSingleton<IHistoryStrategy, SimpleTrimmingStrategy>();
        services.AddSingleton<ILlmClientFactory, DefaultLlmClientFactory>();

        // NodeExecutor registry（10/10 節點全部提取）
        services.AddSingleton<Strategies.NodeExecutors.INodeExecutor, Strategies.NodeExecutors.AgentNodeExecutor>();
        services.AddSingleton<Strategies.NodeExecutors.INodeExecutor, Strategies.NodeExecutors.CodeNodeExecutor>();
        services.AddSingleton<Strategies.NodeExecutors.INodeExecutor, Strategies.NodeExecutors.HttpRequestNodeExecutor>();
        services.AddSingleton<Strategies.NodeExecutors.INodeExecutor, Strategies.NodeExecutors.ConditionNodeExecutor>();
        services.AddSingleton<Strategies.NodeExecutors.INodeExecutor, Strategies.NodeExecutors.IterationNodeExecutor>();
        services.AddSingleton<Strategies.NodeExecutors.INodeExecutor, Strategies.NodeExecutors.LoopNodeExecutor>();
        services.AddSingleton<Strategies.NodeExecutors.INodeExecutor, Strategies.NodeExecutors.ParallelNodeExecutor>();
        services.AddSingleton<Strategies.NodeExecutors.INodeExecutor, Strategies.NodeExecutors.A2ANodeExecutor>();
        services.AddSingleton<Strategies.NodeExecutors.INodeExecutor, Strategies.NodeExecutors.AutonomousNodeExecutor>();
        services.AddSingleton<Strategies.NodeExecutors.INodeExecutor, Strategies.NodeExecutors.HumanNodeExecutor>();
        services.AddSingleton<Strategies.NodeExecutors.INodeExecutor, Strategies.NodeExecutors.RouterNodeExecutor>();
        services.AddSingleton<Strategies.NodeExecutors.NodeExecutorRegistry>();

        services.AddScoped<WorkflowPreprocessor>();
        services.AddSingleton<WorkflowStrategyResolver>();
        services.AddScoped<WorkflowExecutionService>();
        services.AddSingleton<FlowBuilderService>();
        services.AddSingleton<TeamsServerService>();
        services.AddSingleton<WorkflowHookRunner>();
        services.AddSingleton<ApiKeyService>();

        // PII 保護（預設 Regex 偵測器 + 記憶體 Token Vault）
        services.AddPiiProtection();

        // GuardRails（預設關鍵字規則引擎）
        services.AddSingleton<IGuardRailsPolicy>(DefaultGuardRailsPolicy.FromConfig(null));

        // Skill Prompt 載入器 + Prompt Refiner
        services.AddSingleton<SkillPromptProvider>();
        services.AddSingleton<PromptRefinerService>();

        return services;
    }

    /// <summary>
    /// 註冊 PII 保護服務（IPiiDetector + IPiiTokenVault）。
    /// </summary>
    public static IServiceCollection AddPiiProtection(
        this IServiceCollection services,
        Action<PiiProtectionOptions>? configure = null)
    {
        var options = new PiiProtectionOptions();
        configure?.Invoke(options);
        services.AddSingleton<IPiiDetector>(sp =>
            new RegexPiiDetector(options.EnabledLocales, options.CustomRules,
                sp.GetService<ILogger<RegexPiiDetector>>()));
        services.AddSingleton<IPiiTokenVault>(
            new InMemoryPiiTokenVault(options.TokenTtl));
        return services;
    }

    /// <summary>
    /// 初始化引擎層服務（選擇性清理軟刪除的知識庫和 Refinery 專案）。
    /// 搜尋引擎不再全域初始化，改由 SearchEngineFactory 根據 DataSource 動態建立。
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        try
        {
            var kbService = serviceProvider.GetRequiredService<KnowledgeBaseService>();
            await kbService.CleanupDeletedAsync();

            var refineryService = serviceProvider.GetRequiredService<RefineryService>();
            await refineryService.CleanupDeletedAsync();
        }
        catch
        {
            // 清理失敗不影響啟動
        }
    }

    /// <summary>
    /// 將 CraftCleaner 工具掛載到 ToolRegistryService。在 app build 完成後呼叫。
    /// </summary>
    public static void UseCleanerTools(this IServiceProvider provider, string? workingDirectory = null)
    {
        var cleaner = provider.GetService<Cleaner.Abstractions.IDocumentCleaner>();
        if (cleaner is null)
        {
            return;
        }

        var registry = provider.GetRequiredService<ToolRegistryService>();
        var workDir = workingDirectory ?? AppContext.BaseDirectory;
        registry.RegisterCleanerTools(cleaner, workDir);
    }

    /// <summary>
    /// 同步版本（向下相容）。
    /// </summary>
    public static void InitializeDatabase(this IServiceProvider serviceProvider)
    {
        serviceProvider.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }
}
