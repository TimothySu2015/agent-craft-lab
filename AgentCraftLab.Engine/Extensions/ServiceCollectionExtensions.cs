using AgentCraftLab.Cleaner.Extensions;
using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Middleware;
using AgentCraftLab.Engine.Pii;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Search.Abstractions;
using AgentCraftLab.Search.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Extensions;

/// <summary>
/// AgentCraftLab.Engine 的 DI 擴展方法。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 註冊 AgentCraftLab Engine 所需的所有服務（開源預設：SQLite + 單人模式）。
    /// </summary>
    public static IServiceCollection AddAgentCraftEngine(this IServiceCollection services, string dbPath = "Data/agentcraftlab.db", string? outputDir = null, string? workingDir = null, List<string>? emailWhitelist = null)
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

        // 資料層（預設 SQLite）
        services.AddDataProtection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));
        services.AddSingleton<CredentialProtector>();
        services.AddSingleton<IWorkflowStore, SqliteWorkflowStore>();
        services.AddSingleton<ICredentialStore, SqliteCredentialStore>();
        services.AddSingleton<IRequestLogStore, SqliteRequestLogStore>();
        services.AddSingleton<ISkillStore, SqliteSkillStore>();
        services.AddSingleton<ITemplateStore, SqliteTemplateStore>();
        services.AddSingleton<IExecutionMemoryStore>(sp =>
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var se = sp.GetService<ISearchEngine>(); // 可選，有就用 FTS5 語義搜索
            var memLogger = sp.GetService<ILogger<SqliteExecutionMemoryStore>>();
            return new SqliteExecutionMemoryStore(scopeFactory, se, memLogger);
        });
        services.AddSingleton<IEntityMemoryStore, SqliteEntityMemoryStore>();
        services.AddSingleton<ICraftMdStore, SqliteCraftMdStore>();
        services.AddSingleton<ICheckpointStore, SqliteCheckpointStore>();
        services.AddSingleton<IContextualMemoryStore, SqliteContextualMemoryStore>();
        services.AddSingleton<IKnowledgeBaseStore, SqliteKnowledgeBaseStore>();
        services.AddSingleton<IDataSourceStore, SqliteDataSourceStore>();
        services.AddSingleton<IApiKeyStore, SqliteApiKeyStore>();

        // 使用者（預設單人模式）
        services.AddScoped<IUserContext, LocalUserContext>();

        services.AddHttpClient();
        services.AddSingleton<ToolRegistryService>();
        services.AddSingleton<ToolManagementService>();
        services.AddSingleton<SkillRegistryService>();
        services.AddSingleton<McpClientService>();
        services.AddSingleton<A2AClientService>();
        services.AddSingleton<HttpApiToolService>();
        // CraftSearch 搜尋引擎（擷取器 + 分塊器 + SQLite 持久化）
        services.AddCraftSearch();
        services.AddCraftSearchSqlite(
            dbPath: Path.Combine(Path.GetDirectoryName(dbPath) ?? "Data", "craftsearch.db"),
            configureOptions: o => o.IndexTtl = null);  // 停用 Search 層自動清理，改由 Engine 選擇性清理
        // CraftCleaner 資料清洗引擎（Partition → Clean，RagService 可選使用）
        services.AddCraftCleaner();
        // Schema Mapper（LLM 結構化擷取 + 模板管理）
        services.AddSchemaMapper();
        services.AddSingleton<RagService>();
        services.AddSingleton<KnowledgeBaseService>();
        services.AddSingleton<IRefineryStore, SqliteRefineryStore>();
        services.AddSingleton<RefineryService>();
        services.AddSingleton<SearchEngineFactory>();
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
    /// 初始化資料庫（自動建表）。
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbPath = db.Database.GetConnectionString();
        if (dbPath is not null)
        {
            var match = System.Text.RegularExpressions.Regex.Match(dbPath, @"Data Source=(.+)");
            if (match.Success)
            {
                var dir = Path.GetDirectoryName(match.Groups[1].Value);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
        }

        db.Database.EnsureCreated();

        // 為既有 DB 補建新表（EnsureCreated 不會對已存在的 DB 加表）
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS Skills (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT 'DomainKnowledge',
                Icon TEXT NOT NULL DEFAULT '&#x1F3AF;',
                Instructions TEXT NOT NULL DEFAULT '',
                Tools TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS Schedules (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT '',
                WorkflowId TEXT NOT NULL DEFAULT '',
                WorkflowName TEXT NOT NULL DEFAULT '',
                CronExpression TEXT NOT NULL DEFAULT '',
                TimeZone TEXT NOT NULL DEFAULT 'UTC',
                Enabled INTEGER NOT NULL DEFAULT 1,
                DefaultInput TEXT NOT NULL DEFAULT '',
                LastRunAt TEXT,
                NextRunAt TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ScheduleLogs (
                Id TEXT NOT NULL PRIMARY KEY,
                ScheduleId TEXT NOT NULL DEFAULT '',
                WorkflowId TEXT NOT NULL DEFAULT '',
                UserId TEXT NOT NULL DEFAULT '',
                Success INTEGER NOT NULL DEFAULT 0,
                Output TEXT,
                Error TEXT,
                ElapsedMs INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS Templates (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT 'My Templates',
                Icon TEXT NOT NULL DEFAULT '&#x1F4C4;',
                Tags TEXT NOT NULL DEFAULT '',
                WorkflowJson TEXT NOT NULL DEFAULT '',
                IsPublic INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);

        // 為既有 DB 補建 ExecutionMemories 表
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ExecutionMemories (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT 'local',
                GoalKeywords TEXT NOT NULL DEFAULT '',
                Succeeded INTEGER NOT NULL DEFAULT 0,
                ToolSequence TEXT NOT NULL DEFAULT '',
                StepCount INTEGER NOT NULL DEFAULT 0,
                TokensUsed INTEGER NOT NULL DEFAULT 0,
                ElapsedMs INTEGER NOT NULL DEFAULT 0,
                Reflection TEXT NOT NULL DEFAULT '',
                KeyInsights TEXT NOT NULL DEFAULT '[]',
                AuditIssues TEXT NOT NULL DEFAULT '[]',
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);

        // 為既有 DB 補建 KnowledgeBases 表
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS KnowledgeBases (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                IndexName TEXT NOT NULL DEFAULT '',
                EmbeddingModel TEXT NOT NULL DEFAULT 'text-embedding-3-small',
                ChunkSize INTEGER NOT NULL DEFAULT 1000,
                ChunkOverlap INTEGER NOT NULL DEFAULT 100,
                ChunkStrategy TEXT NOT NULL DEFAULT 'fixed',
                FileCount INTEGER NOT NULL DEFAULT 0,
                TotalChunks INTEGER NOT NULL DEFAULT 0,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                DeletedAt TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);

        // 為既有 KnowledgeBases 表補建 ChunkStrategy 欄位（先檢查避免 EF log 噪音）
        var hasChunkStrategy = db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('KnowledgeBases') WHERE name = 'ChunkStrategy'")
            .Any();
        if (!hasChunkStrategy)
        {
            db.Database.ExecuteSqlRaw(
                "ALTER TABLE KnowledgeBases ADD COLUMN ChunkStrategy TEXT NOT NULL DEFAULT 'fixed'");
        }

        // 為既有 DB 補建 KbFiles 表
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS KbFiles (
                Id TEXT NOT NULL PRIMARY KEY,
                KnowledgeBaseId TEXT NOT NULL DEFAULT '',
                FileName TEXT NOT NULL DEFAULT '',
                MimeType TEXT NOT NULL DEFAULT '',
                FileSize INTEGER NOT NULL DEFAULT 0,
                ChunkIds TEXT NOT NULL DEFAULT '',
                ChunkCount INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);

        // 為既有 DB 補建 ApiKeys 表
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ApiKeys (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL DEFAULT '',
                KeyHash TEXT NOT NULL DEFAULT '',
                KeyPrefix TEXT NOT NULL DEFAULT '',
                ScopedWorkflowIds TEXT NOT NULL DEFAULT '',
                IsRevoked INTEGER NOT NULL DEFAULT 0,
                LastUsedAt TEXT,
                ExpiresAt TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);

        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ApiKeys_KeyHash ON ApiKeys(KeyHash)");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ApiKeys_UserId ON ApiKeys(UserId)");

        // 為既有 DB 補建 DataSources 表
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS DataSources (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL DEFAULT '',
                Provider TEXT NOT NULL DEFAULT '',
                ConfigJson TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);

        // DocRefinery 表
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS RefineryProjects (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                SchemaTemplateId TEXT,
                CustomSchemaJson TEXT,
                Provider TEXT NOT NULL DEFAULT 'openai',
                Model TEXT NOT NULL DEFAULT 'gpt-4o',
                OutputLanguage TEXT,
                FileCount INTEGER NOT NULL DEFAULT 0,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                DeletedAt TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS RefineryFiles (
                Id TEXT NOT NULL PRIMARY KEY,
                RefineryProjectId TEXT NOT NULL DEFAULT '',
                FileName TEXT NOT NULL DEFAULT '',
                MimeType TEXT NOT NULL DEFAULT '',
                FileSize INTEGER NOT NULL DEFAULT 0,
                CleanedJson TEXT NOT NULL DEFAULT '',
                ElementCount INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS RefineryOutputs (
                Id TEXT NOT NULL PRIMARY KEY,
                RefineryProjectId TEXT NOT NULL DEFAULT '',
                Version INTEGER NOT NULL DEFAULT 1,
                SchemaTemplateId TEXT,
                SchemaName TEXT NOT NULL DEFAULT '',
                OutputJson TEXT NOT NULL DEFAULT '',
                OutputMarkdown TEXT NOT NULL DEFAULT '',
                MissingFields TEXT NOT NULL DEFAULT '[]',
                OpenQuestions TEXT NOT NULL DEFAULT '[]',
                SourceFileCount INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_RefineryProjects_UserId ON RefineryProjects(UserId)");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_RefineryFiles_ProjectId ON RefineryFiles(RefineryProjectId)");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_RefineryOutputs_ProjectVersion ON RefineryOutputs(RefineryProjectId, Version)");

        // 為既有 DB 補建 EntityMemories 表
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS EntityMemories (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT 'local',
                EntityName TEXT NOT NULL DEFAULT '',
                EntityType TEXT NOT NULL DEFAULT 'concept',
                Facts TEXT NOT NULL DEFAULT '[]',
                SourceExecutionId TEXT NOT NULL DEFAULT '',
                MergedCount INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_EntityMemories_UserId ON EntityMemories(UserId)");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_EntityMemories_UserEntity ON EntityMemories(UserId, EntityName)");

        // 為既有 DB 補建 ContextualMemories 表
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ContextualMemories (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT 'local',
                PatternType TEXT NOT NULL DEFAULT 'preference',
                Description TEXT NOT NULL DEFAULT '',
                Confidence REAL NOT NULL DEFAULT 0.5,
                OccurrenceCount INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ContextualMemories_UserId ON ContextualMemories(UserId)");

        // 為既有 DB 補建 CraftMds 表
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS CraftMds (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT 'local',
                WorkflowId TEXT,
                Content TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_CraftMds_UserWorkflow ON CraftMds(UserId, WorkflowId)");

        // 為既有 DB 補建 Checkpoints 表
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS Checkpoints (
                Id TEXT NOT NULL PRIMARY KEY,
                ExecutionId TEXT NOT NULL DEFAULT '',
                Iteration INTEGER NOT NULL DEFAULT 0,
                MessageCount INTEGER NOT NULL DEFAULT 0,
                TokensUsed INTEGER NOT NULL DEFAULT 0,
                StateJson TEXT NOT NULL DEFAULT '',
                StateSizeBytes INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Checkpoints_ExecutionId ON Checkpoints(ExecutionId)");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Checkpoints_ExecIteration ON Checkpoints(ExecutionId, Iteration)");

        // 為既有 DB 補建新欄位（已存在的表不會被 CREATE TABLE IF NOT EXISTS 修改）
        // 使用 PRAGMA table_info 檢查欄位是否存在，避免 ALTER TABLE 失敗產生 EF Core Error log
        (string Table, string Column, string TypeDef)[] columnMigrations =
        [
            ("ExecutionMemories", "AuditIssues", "TEXT NOT NULL DEFAULT '[]'"),
            ("ExecutionMemories", "ResultSummary", "TEXT NOT NULL DEFAULT ''"),
            ("Credentials", "UserId", "TEXT NOT NULL DEFAULT 'local'"),
            ("Credentials", "Endpoint", "TEXT NOT NULL DEFAULT ''"),
            ("Credentials", "Model", "TEXT NOT NULL DEFAULT ''"),
            ("Workflows", "UserId", "TEXT NOT NULL DEFAULT 'local'"),
            ("Workflows", "AcceptedInputModes", "TEXT NOT NULL DEFAULT 'text/plain'"),
            ("Workflows", "IsPublished", "INTEGER NOT NULL DEFAULT 0"),
            ("RequestLogs", "UserId", "TEXT"),
            ("RequestLogs", "FileName", "TEXT"),
            ("RequestLogs", "WorkflowName", "TEXT NOT NULL DEFAULT ''"),
            ("ExecutionMemories", "PlanJson", "TEXT"),
            ("KnowledgeBases", "DataSourceId", "TEXT"),
            ("RefineryProjects", "ExtractionMode", "TEXT NOT NULL DEFAULT 'fast'"),
            ("RefineryProjects", "IndexName", "TEXT NOT NULL DEFAULT ''"),
            ("RefineryFiles", "IndexStatus", "TEXT NOT NULL DEFAULT 'Pending'"),
            ("RefineryFiles", "ChunkIds", "TEXT NOT NULL DEFAULT ''"),
            ("RefineryFiles", "ChunkCount", "INTEGER NOT NULL DEFAULT 0"),
            ("RequestLogs", "TraceId", "TEXT"),
            ("RequestLogs", "TraceJson", "TEXT"),
            ("RefineryProjects", "EnableChallenge", "INTEGER NOT NULL DEFAULT 0"),
            ("RefineryOutputs", "Challenges", "TEXT NOT NULL DEFAULT '[]'"),
            ("RefineryOutputs", "OverallConfidence", "REAL NOT NULL DEFAULT 1.0"),
            ("RefineryFiles", "IsIncluded", "INTEGER NOT NULL DEFAULT 1"),
            ("RefineryOutputs", "SourceFiles", "TEXT NOT NULL DEFAULT '[]'"),
            ("RefineryProjects", "ImageProcessingMode", "TEXT NOT NULL DEFAULT 'skip'"),
        ];
        foreach (var (table, column, typeDef) in columnMigrations)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            if (cmd.Connection!.State != System.Data.ConnectionState.Open)
            {
                cmd.Connection.Open();
            }

            var columnExists = false;
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    {
                        columnExists = true;
                        break;
                    }
                }
            }

            if (!columnExists)
            {
#pragma warning disable EF1003 // table/column/typeDef are hardcoded constants, not user input
                db.Database.ExecuteSqlRaw("ALTER TABLE " + table + " ADD COLUMN " + column + " " + typeDef);
#pragma warning restore EF1003
            }
        }

        // 初始化 CraftSearch 搜尋引擎 SQLite 資料庫（建表 + FTS5 虛擬表）
        await serviceProvider.InitializeSearchDatabaseAsync();

        // 選擇性清理：只刪除 _rag_ 臨時索引（保留 _kb_ 知識庫索引）+ 軟刪除知識庫清理
        try
        {
            var searchEngine = serviceProvider.GetRequiredService<Search.Abstractions.ISearchEngine>();
            var indexes = await searchEngine.ListIndexesAsync();
            var ttl = TimeSpan.FromHours(24);
            var cutoff = DateTimeOffset.UtcNow - ttl;
            foreach (var idx in indexes.Where(i => i.Name.Contains("_rag_") && i.CreatedAt < cutoff))
            {
                await searchEngine.DeleteIndexAsync(idx.Name);
            }

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
