using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Data;

public interface IWorkflowStore
{
    Task<WorkflowDocument> SaveAsync(string userId, string name, string description, string type, string workflowJson);
    Task<WorkflowDocument?> GetAsync(string id);
    Task<List<WorkflowDocument>> ListAsync(string userId);
    Task<WorkflowDocument?> UpdateAsync(string userId, string id, string name, string description, string type, string workflowJson);
    Task<bool> DeleteAsync(string userId, string id);
    Task<bool> SetPublishedAsync(string userId, string id, bool isPublished, List<string>? inputModes = null);
    Task<bool> UpdateTypeAsync(string userId, string id, List<string> types);
    Task<List<WorkflowDocument>> ListPublishedAsync();
}

public interface ICredentialStore
{
    Task<CredentialDocument> SaveAsync(string userId, string provider, string name, string apiKey, string endpoint = "", string model = "");
    Task<List<CredentialDocument>> ListAsync(string userId);
    Task<CredentialDocument?> GetAsync(string id);
    Task<CredentialDocument?> UpdateAsync(string userId, string id, string name, string apiKey, string endpoint = "", string model = "");
    Task<bool> DeleteAsync(string userId, string id);
    Task<Dictionary<string, ProviderCredential>> GetDecryptedCredentialsAsync(string userId);
}

public interface ISkillStore
{
    Task<SkillDocument> SaveAsync(string userId, string name, string description, string category, string icon, string instructions, List<string> tools);
    Task<List<SkillDocument>> ListAsync(string userId);
    Task<SkillDocument?> GetAsync(string id);
    Task<SkillDocument?> UpdateAsync(string userId, string id, string name, string description, string category, string icon, string instructions, List<string> tools);
    Task<bool> DeleteAsync(string userId, string id);
}

public interface ITemplateStore
{
    Task<TemplateDocument> SaveAsync(string userId, string name, string description, string category, string icon, List<string> tags, string workflowJson);
    Task<List<TemplateDocument>> ListAsync(string userId);
    Task<TemplateDocument?> GetAsync(string id);
    Task<TemplateDocument?> UpdateAsync(string userId, string id, string name, string description, string category, string icon, List<string> tags, string? workflowJson);
    Task<bool> DeleteAsync(string userId, string id);
}

public interface IRequestLogStore
{
    Task LogAsync(RequestLogDocument log);
    Task<List<RequestLogDocument>> QueryAsync(DateTime? from = null, DateTime? to = null, string? protocol = null, string? workflowKey = null, string? userId = null, int limit = 200);
    Task<AnalyticsSummary> GetSummaryAsync(DateTime from, string? userId = null);
    Task<string?> GetTraceJsonAsync(string logId);
}

public interface IScheduleStore
{
    Task<List<ScheduleDocument>> GetActiveSchedulesAsync();
    Task<List<ScheduleDocument>> ListAsync(string userId);
    Task<ScheduleDocument?> GetAsync(string id);
    Task<ScheduleDocument> UpsertAsync(ScheduleDocument schedule);
    Task<bool> DeleteAsync(string userId, string id);
    Task<List<ScheduleLogDocument>> GetLogsAsync(string scheduleId, int limit = 20);
    Task AddLogAsync(ScheduleLogDocument log);
}

public interface IDataSourceStore
{
    Task<DataSourceDocument> SaveAsync(DataSourceDocument doc);
    Task<List<DataSourceDocument>> ListAsync(string userId);
    Task<DataSourceDocument?> GetAsync(string id);
    Task<DataSourceDocument?> UpdateAsync(string userId, string id, string name, string description, string provider, string configJson);
    Task<bool> DeleteAsync(string userId, string id);
    Task<int> CountKbReferencesAsync(string id);
}

public interface IKnowledgeBaseStore
{
    Task<KnowledgeBaseDocument> SaveAsync(string userId, string name, string description,
        string embeddingModel, int chunkSize, int chunkOverlap, string? dataSourceId = null,
        string chunkStrategy = "fixed");
    Task<List<KnowledgeBaseDocument>> ListAsync(string userId);
    Task<KnowledgeBaseDocument?> GetAsync(string id);
    Task<KnowledgeBaseDocument?> UpdateAsync(string userId, string id, string name, string description);
    Task<bool> DeleteAsync(string userId, string id);       // 軟刪除
    Task<KbFileDocument> AddFileAsync(string knowledgeBaseId, string fileName, string mimeType,
        long fileSize, List<string> chunkIds);
    Task<KbFileDocument?> GetFileAsync(string knowledgeBaseId, string fileId);
    Task<List<KbFileDocument>> ListFilesAsync(string knowledgeBaseId);
    Task<bool> RemoveFileAsync(string knowledgeBaseId, string fileId);
    Task UpdateStatsAsync(string id, int fileCount, long totalChunks);
    Task<List<KnowledgeBaseDocument>> GetPendingDeletionsAsync(TimeSpan delay);
    Task HardDeleteAsync(string id);
}

public interface IExecutionMemoryStore
{
    Task SaveAsync(ExecutionMemoryDocument memory);
    Task<List<ExecutionMemoryDocument>> SearchAsync(string userId, string goalKeywords, int limit = 5);
    Task<int> CleanupAsync(string userId, int maxCount = 200, int maxAgeDays = 90);

    /// <summary>
    /// 語義搜索 — 有 CraftSearch 時用 FTS5（trigram + BM25），否則 fallback 到關鍵字搜索。
    /// </summary>
    Task<List<ExecutionMemoryDocument>> SemanticSearchAsync(
        string userId, string goalDescription, int limit = 5)
        => SearchAsync(userId, goalDescription, limit);
}

public interface ICraftMdStore
{
    Task<CraftMdDocument> SaveAsync(string userId, string? workflowId, string content);
    /// <summary>取得 craft.md 內容（先找 workflow 專屬 → 使用者預設 → null）。</summary>
    Task<string?> GetContentAsync(string userId, string? workflowId);
    Task<CraftMdDocument?> GetAsync(string userId, string? workflowId);
    Task<bool> DeleteAsync(string userId, string? workflowId);
}

public interface ICheckpointStore
{
    Task SaveAsync(CheckpointDocument checkpoint);
    Task<List<CheckpointDocument>> ListAsync(string executionId);
    /// <summary>列出檢查點 metadata（不含 StateJson，減少記憶體用量）。</summary>
    Task<List<CheckpointDocument>> ListMetadataAsync(string executionId);
    Task<CheckpointDocument?> GetAsync(string executionId, int iteration);
    Task<CheckpointDocument?> GetLatestAsync(string executionId);
    Task CleanupAsync(string executionId);
    Task CleanupOlderThanAsync(TimeSpan maxAge);
}

public interface IEntityMemoryStore
{
    Task SaveAsync(EntityMemoryDocument entity);
    Task<EntityMemoryDocument?> FindByNameAsync(string userId, string entityName);
    Task<List<EntityMemoryDocument>> SearchAsync(string userId, string query, int limit = 10);
    Task MergeFactsAsync(string userId, string entityName, List<string> newFacts, string entityType = "concept", string sourceExecutionId = "");
    Task<int> CleanupAsync(string userId, int maxCount = 500, int maxAgeDays = 180);
}

public interface IContextualMemoryStore
{
    Task SaveAsync(ContextualMemoryDocument pattern);
    Task<List<ContextualMemoryDocument>> GetPatternsAsync(string userId, int limit = 10);
    Task UpsertPatternAsync(string userId, string patternType, string description, float confidence);
    Task<int> CleanupAsync(string userId, int maxCount = 50, int maxAgeDays = 365);
}

public interface IApiKeyStore
{
    Task<ApiKeyDocument> SaveAsync(string userId, string name, string keyHash, string keyPrefix,
        string? scopedWorkflowIds = null, DateTime? expiresAt = null);
    Task<List<ApiKeyDocument>> ListAsync(string userId);
    Task<ApiKeyDocument?> GetAsync(string id);
    Task<ApiKeyDocument?> FindByHashAsync(string keyHash);
    Task<bool> RevokeAsync(string userId, string id);
    Task<bool> DeleteAsync(string userId, string id);
    Task UpdateLastUsedAsync(string id);
}

public interface IRefineryStore
{
    // Project CRUD
    Task<RefineryProject> SaveAsync(string userId, string name, string description,
        string? schemaTemplateId, string? customSchemaJson,
        string provider, string model, string? outputLanguage,
        string extractionMode = "fast", bool enableChallenge = false,
        string imageProcessingMode = "skip");
    Task<List<RefineryProject>> ListAsync(string userId);
    Task<RefineryProject?> GetAsync(string id);
    Task<RefineryProject?> UpdateAsync(string userId, string id, string name, string description,
        string? schemaTemplateId, string? customSchemaJson,
        string provider, string model, string? outputLanguage,
        string extractionMode = "fast", bool enableChallenge = false,
        string imageProcessingMode = "skip");
    Task<bool> DeleteAsync(string userId, string id);

    // File management
    Task<RefineryFile> AddFileAsync(string projectId, string fileName, string mimeType,
        long fileSize, string cleanedJson, int elementCount, string indexStatus = "Pending");
    Task<List<RefineryFile>> ListFilesAsync(string projectId);
    Task<RefineryFile?> GetFileAsync(string projectId, string fileId);
    Task<bool> RemoveFileAsync(string projectId, string fileId);
    Task UpdateStatsAsync(string id, int fileCount);
    Task UpdateFileIndexStatusAsync(string fileId, string status, string? chunkIds = null, int? chunkCount = null);
    Task ToggleFileIncludedAsync(string fileId, bool isIncluded);

    // Output versioning
    Task<RefineryOutput> AddOutputAsync(string projectId, int version,
        string? schemaTemplateId, string schemaName,
        string outputJson, string outputMarkdown,
        string missingFields, string openQuestions,
        string challenges, float overallConfidence,
        string sourceFiles, int sourceFileCount);
    Task<List<RefineryOutput>> ListOutputsAsync(string projectId);
    Task<RefineryOutput?> GetOutputAsync(string projectId, int version);
    Task<RefineryOutput?> GetLatestOutputAsync(string projectId);

    // Cleanup
    Task<List<RefineryProject>> GetPendingDeletionsAsync(TimeSpan delay);
    Task HardDeleteAsync(string id);
}

public class AnalyticsSummary
{
    public int TotalCalls { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public long AvgElapsedMs { get; set; }
    public Dictionary<string, int> ByProtocol { get; set; } = new();
    public Dictionary<string, int> ByWorkflow { get; set; } = new();

    public double SuccessRate => TotalCalls > 0 ? Math.Round((double)SuccessCount / TotalCalls * 100, 1) : 0;
}
