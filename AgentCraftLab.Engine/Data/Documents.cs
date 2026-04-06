using System.Text.Json.Serialization;

namespace AgentCraftLab.Engine.Data;

/// <summary>
/// 使用者文件（OAuth 登入後自動建立）。
/// </summary>
public class UserDocument
{
    public string Id { get; set; } = "";

    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public List<string> Roles { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Workflow 文件（含使用者隔離）。
/// </summary>
public class WorkflowDocument
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "a2a";
    public string WorkflowJson { get; set; } = "";
    public bool IsPublished { get; set; }
    public string AcceptedInputModes { get; set; } = "text/plain";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<string> GetInputModes() =>
        AcceptedInputModes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public void SetInputModes(List<string> modes) =>
        AcceptedInputModes = string.Join(",", modes);

    public List<string> GetTypes() =>
        Type.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public void SetTypes(List<string> types) =>
        Type = string.Join(",", types);

    public bool HasType(string type) =>
        GetTypes().Contains(type, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 自訂 Skill 文件（含使用者隔離）。
/// </summary>
public class SkillDocument
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "DomainKnowledge";
    public string Icon { get; set; } = "&#x1F3AF;";
    public string Instructions { get; set; } = "";
    public string Tools { get; set; } = "";   // 逗號分隔的 tool IDs
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<string> GetTools() =>
        string.IsNullOrWhiteSpace(Tools)
            ? []
            : Tools.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static string SerializeTools(List<string> tools) =>
        string.Join(",", tools);

    public void SetTools(List<string> tools) =>
        Tools = SerializeTools(tools);
}

/// <summary>
/// 自訂 Template 文件（含使用者隔離）。使用者可將 Workflow 存為可重用模板。
/// </summary>
public class TemplateDocument
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "My Templates";
    public string Icon { get; set; } = "&#x1F4C4;";
    public string Tags { get; set; } = "";   // 逗號分隔
    public string WorkflowJson { get; set; } = "";
    public bool IsPublic { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<string> GetTags() =>
        string.IsNullOrWhiteSpace(Tags)
            ? []
            : Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public void SetTags(List<string> tags) =>
        Tags = string.Join(",", tags);
}

/// <summary>
/// Credential 文件（含使用者隔離，API Key 加密存儲）。
/// </summary>
public class CredentialDocument
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Name { get; set; } = "";
    public string EncryptedApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 請求日誌文件（A2A/MCP/API/Teams 端點呼叫記錄）。
/// </summary>
public class RequestLogDocument
{
    public string Id { get; set; } = "";

    public string? UserId { get; set; }
    public string WorkflowKey { get; set; } = "";
    public string WorkflowName { get; set; } = "";
    public string Protocol { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? FileName { get; set; }
    public string SourceIp { get; set; } = "";
    public long ElapsedMs { get; set; }
    public string? ErrorText { get; set; }
    public string? TraceId { get; set; }
    public string? TraceJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public static RequestLogDocument Create(string key, string protocol, bool success,
        string message, string? fileName, string sourceIp, long elapsedMs,
        string? error = null, string? workflowName = null, string? userId = null) => new()
    {
        Id = $"log-{Guid.NewGuid():N}"[..16],
        UserId = userId,
        WorkflowKey = key,
        WorkflowName = workflowName ?? key,
        Protocol = protocol,
        Success = success,
        Message = message.Length > 100 ? message[..100] : message,
        FileName = fileName,
        SourceIp = sourceIp,
        ElapsedMs = elapsedMs,
        ErrorText = error
    };
}

/// <summary>
/// 排程定義文件（商業版 — Cron 排程已發布 workflow）。
/// </summary>
public class ScheduleDocument
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";
    public string WorkflowId { get; set; } = "";
    public string WorkflowName { get; set; } = "";
    public string CronExpression { get; set; } = "";  // 5-field cron: "0 9 * * 1-5"
    public string TimeZone { get; set; } = "UTC";      // IANA timezone
    public bool Enabled { get; set; } = true;
    public string DefaultInput { get; set; } = "";     // 排程執行時的預設輸入
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 排程執行記錄文件。
/// </summary>
public class ScheduleLogDocument
{
    public string Id { get; set; } = "";

    public string ScheduleId { get; set; } = "";
    public string WorkflowId { get; set; } = "";
    public string UserId { get; set; } = "";
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public long ElapsedMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string StatusText => Success ? "Success" : "Failed";
}

/// <summary>
/// Autonomous Agent 執行記憶 — 記錄每次執行的策略與成敗，供跨 Session 學習。
/// </summary>
public class ExecutionMemoryDocument
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "local";

    /// <summary>目標關鍵字（從 Goal 萃取，用於相似匹配）。</summary>
    public string GoalKeywords { get; set; } = "";

    /// <summary>是否成功完成。</summary>
    public bool Succeeded { get; set; }

    /// <summary>使用的工具序列（逗號分隔）。</summary>
    public string ToolSequence { get; set; } = "";

    /// <summary>總步驟數。</summary>
    public int StepCount { get; set; }

    /// <summary>總 Token 消耗。</summary>
    public long TokensUsed { get; set; }

    /// <summary>執行耗時（毫秒）。</summary>
    public long ElapsedMs { get; set; }

    /// <summary>LLM 產生的自然語言反思（Reflexion 模式）。</summary>
    public string Reflection { get; set; } = "";

    /// <summary>萃取的關鍵經驗（JSON array of strings）。</summary>
    public string KeyInsights { get; set; } = "[]";

    /// <summary>Auditor 審查反饋（JSON array of strings，記錄審查發現的問題）。</summary>
    public string AuditIssues { get; set; } = "[]";

    /// <summary>執行結果摘要 — AI 最終回答的前 N 字元，供跨 Session 多輪接續使用。</summary>
    public string ResultSummary { get; set; } = "";

    /// <summary>ReAct 軌跡轉換的 FlowPlan JSON（null = 軌跡太簡單未轉換）。</summary>
    public string? PlanJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Agent 行為規範文件（craft.md）— 使用者自訂的行為偏好與規則，注入 system prompt。
/// 每個使用者一份預設 + 每個 workflow 可覆蓋。
/// </summary>
public class CraftMdDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "local";

    /// <summary>關聯的 Workflow ID（null = 使用者預設規範）。</summary>
    public string? WorkflowId { get; set; }

    /// <summary>Markdown 格式的行為規範內容。</summary>
    public string Content { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 執行檢查點文件 — 儲存 ReAct 迴圈的完整中間狀態快照。ID 前綴 "ckpt-"。
/// StateJson 包含序列化的完整執行狀態（messages, steps, trackers 等）。
/// </summary>
public class CheckpointDocument
{
    public string Id { get; set; } = "";
    public string ExecutionId { get; set; } = "";
    public int Iteration { get; set; }
    public int MessageCount { get; set; }
    public long TokensUsed { get; set; }

    /// <summary>完整序列化狀態 JSON。</summary>
    public string StateJson { get; set; } = "";

    /// <summary>StateJson 的位元組大小（監控用）。</summary>
    public long StateSizeBytes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 實體記憶 — 記錄 Agent 執行中發現的具名實體及其事實。
/// 跨 Session 累積，同名實體自動合併事實。
/// </summary>
public class EntityMemoryDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "local";

    /// <summary>實體名稱（如 "NVIDIA"、"Customer X"）。</summary>
    public string EntityName { get; set; } = "";

    /// <summary>實體類型：person / organization / product / concept / location。</summary>
    public string EntityType { get; set; } = "concept";

    /// <summary>事實清單（JSON array of strings）。</summary>
    public string Facts { get; set; } = "[]";

    /// <summary>來源執行 ID。</summary>
    public string SourceExecutionId { get; set; } = "";

    /// <summary>合併次數（同實體累計寫入次數）。</summary>
    public int MergedCount { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 情境記憶 — 記錄使用者互動模式與偏好，從多次執行中聚合。
/// </summary>
public class ContextualMemoryDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "local";

    /// <summary>模式類型：preference / behavior / topic_interest。</summary>
    public string PatternType { get; set; } = "preference";

    /// <summary>模式描述。</summary>
    public string Description { get; set; } = "";

    /// <summary>信心度（0-1）。</summary>
    public float Confidence { get; set; } = 0.5f;

    /// <summary>觀察到此模式的次數。</summary>
    public int OccurrenceCount { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 資料來源連線定義（向量 DB 連線設定）。ID 前綴 "ds-"。
/// 同一 Provider 可建立多組，分別指向不同 server/instance。
/// </summary>
public class DataSourceDocument
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";                // "研發部 Qdrant"
    public string Provider { get; set; } = "";             // "sqlite" | "pgvector" | "qdrant"
    public string ConfigJson { get; set; } = "{}";         // Provider-specific 設定（敏感欄位加密）
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 知識庫文件（持久化文件集合，可跨 Workflow 共用）。ID 前綴 "kb-"。
/// </summary>
public class KnowledgeBaseDocument
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string IndexName { get; set; } = "";          // CraftSearch 索引名 {userId}_kb_{id}
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int ChunkSize { get; set; } = 1000;
    public int ChunkOverlap { get; set; } = 100;
    public string ChunkStrategy { get; set; } = "fixed";   // "fixed" | "structural"
    public int FileCount { get; set; }
    public long TotalChunks { get; set; }
    public string? DataSourceId { get; set; }             // null = 預設全域 SQLite
    public bool IsDeleted { get; set; }                   // 軟刪除
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 知識庫檔案文件（記錄每個已上傳的檔案及其 chunk IDs）。ID 前綴 "kbf-"。
/// ChunkIds 用逗號分隔字串（同 SkillDocument.Tools 模式）。
/// </summary>
public class KbFileDocument
{
    public string Id { get; set; } = "";

    public string KnowledgeBaseId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long FileSize { get; set; }
    public string ChunkIds { get; set; } = "";
    public int ChunkCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<string> GetChunkIds() =>
        string.IsNullOrWhiteSpace(ChunkIds)
            ? []
            : ChunkIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public void SetChunkIds(List<string> chunkIds) =>
        ChunkIds = string.Join(",", chunkIds);
}

// ═══════════════════════════════════════════════════════
// DocRefinery — 文件精煉專案
// ═══════════════════════════════════════════════════════

/// <summary>
/// DocRefinery 精煉專案。ID 前綴 "ref-"。
/// </summary>
public class RefineryProject
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? SchemaTemplateId { get; set; }
    public string? CustomSchemaJson { get; set; }
    public string Provider { get; set; } = "openai";
    public string Model { get; set; } = "gpt-4o";
    public string? OutputLanguage { get; set; }
    public string ExtractionMode { get; set; } = "fast"; // "fast" | "precise"
    public bool EnableChallenge { get; set; }             // LLM Challenge 驗證
    public string ImageProcessingMode { get; set; } = "skip"; // "skip" | "ocr" | "ai-describe" | "hybrid"
    public string IndexName { get; set; } = "";           // 搜尋索引名稱（精準模式用）
    public int FileCount { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// DocRefinery 檔案。ID 前綴 "reff-"。
/// CleanedJson 快取清洗結果，避免重複清洗。
/// 檔案二進位存磁碟 Data/refinery-files/{projectId}/{fileId}.bin。
/// </summary>
public class RefineryFile
{
    public string Id { get; set; } = "";
    public string RefineryProjectId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long FileSize { get; set; }
    public string CleanedJson { get; set; } = "";
    public int ElementCount { get; set; }
    public bool IsIncluded { get; set; } = true;            // 是否納入 Generate 來源
    public string IndexStatus { get; set; } = "Pending";  // Pending | Indexing | Indexed | Failed | Skipped
    public string ChunkIds { get; set; } = "";
    public int ChunkCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<string> GetChunkIds() =>
        string.IsNullOrWhiteSpace(ChunkIds)
            ? []
            : ChunkIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public void SetChunkIds(List<string> chunkIds) =>
        ChunkIds = string.Join(",", chunkIds);
}

/// <summary>
/// DocRefinery 輸出版本。ID 前綴 "refo-"。
/// 每次 Generate 產生一個新版本，不可修改。
/// </summary>
public class RefineryOutput
{
    public string Id { get; set; } = "";
    public string RefineryProjectId { get; set; } = "";
    public int Version { get; set; } = 1;
    public string? SchemaTemplateId { get; set; }
    public string SchemaName { get; set; } = "";
    public string OutputJson { get; set; } = "";
    public string OutputMarkdown { get; set; } = "";
    public string MissingFields { get; set; } = "[]";
    public string OpenQuestions { get; set; } = "[]";
    public string Challenges { get; set; } = "[]";
    public float OverallConfidence { get; set; } = 1.0f;
    public string SourceFiles { get; set; } = "[]";       // 來源檔案名稱 JSON 陣列
    public int SourceFileCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// API Key 文件（用於已發布端點的存取控制）。ID 前綴 "ak-"。
/// </summary>
public class ApiKeyDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string KeyHash { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public string ScopedWorkflowIds { get; set; } = "";
    public bool IsRevoked { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<string> GetScopedWorkflowIds() =>
        string.IsNullOrWhiteSpace(ScopedWorkflowIds)
            ? []
            : ScopedWorkflowIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
