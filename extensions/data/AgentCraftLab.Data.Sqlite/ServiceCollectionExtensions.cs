using AgentCraftLab.Data;
using AgentCraftLab.Search.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Data.Sqlite;

/// <summary>
/// SQLite 資料層 Provider 的 DI 擴展方法。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 註冊 SQLite 資料層（AppDbContext + CredentialProtector + 全部 15 個 Store）。
    /// </summary>
    public static IServiceCollection AddSqliteDataProvider(this IServiceCollection services, string dbPath = "Data/agentcraftlab.db")
    {
        var keysDir = new DirectoryInfo(Path.Combine("Data", "keys"));
        keysDir.Create();
        services.AddDataProtection()
            .PersistKeysToFileSystem(keysDir);
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
        services.AddSingleton<IRefineryStore, SqliteRefineryStore>();
        services.AddSingleton<IScheduleStore, SqliteScheduleStore>();

        return services;
    }

    /// <summary>
    /// 初始化 SQLite 資料庫（自動建表 + 欄位遷移）。
    /// </summary>
    public static Task InitializeSqliteDatabaseAsync(this IServiceProvider serviceProvider)
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
                Icon TEXT NOT NULL DEFAULT '🎯',
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
                Icon TEXT NOT NULL DEFAULT '📄',
                Tags TEXT NOT NULL DEFAULT '',
                WorkflowJson TEXT NOT NULL DEFAULT '',
                IsPublic INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            )
            """);

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

        var hasChunkStrategy = db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('KnowledgeBases') WHERE name = 'ChunkStrategy'")
            .Any();
        if (!hasChunkStrategy)
        {
            db.Database.ExecuteSqlRaw(
                "ALTER TABLE KnowledgeBases ADD COLUMN ChunkStrategy TEXT NOT NULL DEFAULT 'fixed'");
        }

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

        // 為既有 DB 補建新欄位
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

        return Task.CompletedTask;
    }
}
