using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.Abstractions;

/// <summary>
/// Schema Mapper — 從清洗後的文件中，依使用者定義的 Schema 擷取結構化欄位。
/// 核心流程：CleanedDocument[] + SchemaDefinition → LLM → JSON
/// </summary>
public interface ISchemaMapper
{
    /// <summary>
    /// 從多份清洗後文件擷取結構化資料。
    /// </summary>
    /// <param name="documents">清洗後的文件（可多份）</param>
    /// <param name="schema">目標 Schema 定義</param>
    /// <param name="options">擷取選項</param>
    /// <param name="ct">取消 token</param>
    /// <returns>結構化的 JSON 字串（符合 Schema）</returns>
    Task<SchemaMapperResult> MapAsync(
        IReadOnlyList<CleanedDocument> documents,
        SchemaDefinition schema,
        SchemaMapperOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>Schema 定義 — 描述目標文件的結構。</summary>
public sealed class SchemaDefinition
{
    /// <summary>Schema 名稱（如「軟體需求規格書」）</summary>
    public required string Name { get; init; }

    /// <summary>Schema 描述（告訴 LLM 這是什麼類型的文件）</summary>
    public required string Description { get; init; }

    /// <summary>
    /// JSON Schema（標準格式），定義輸出的欄位結構。
    /// LLM 會依此 Schema 產出對應的 JSON。
    /// </summary>
    public required string JsonSchema { get; init; }

    /// <summary>
    /// 額外的擷取指引（可選）。
    /// 告訴 LLM 如何處理特定欄位，例如：
    /// - 「functional_requirements 的 priority 請用 MoSCoW 分類」
    /// - 「budget 的金額請從 Excel 表格中擷取」
    /// </summary>
    public string? ExtractionGuidance { get; init; }
}

/// <summary>Schema Mapper 選項</summary>
public sealed class SchemaMapperOptions
{
    /// <summary>輸出語言（預設跟隨來源文件語言）</summary>
    public string? OutputLanguage { get; init; }

    /// <summary>是否在輸出中標注來源引用（預設 true）</summary>
    public bool IncludeSourceReferences { get; init; } = true;

    /// <summary>找不到欄位時是否填 null（true）或省略（false），預設 true</summary>
    public bool IncludeNullFields { get; init; } = true;

    /// <summary>是否啟用 LLM Challenge 驗證（預設 false）</summary>
    public bool EnableChallenge { get; init; }
}

/// <summary>搜尋回呼 — 讓 MultiLayerSchemaMapper 不直接依賴 RagService。</summary>
public delegate Task<IReadOnlyList<string>> SearchCallback(
    string query, int topK, CancellationToken ct);

/// <summary>Schema Mapper 結果</summary>
public sealed class SchemaMapperResult
{
    /// <summary>結構化的 JSON 字串（符合 Schema）</summary>
    public required string Json { get; init; }

    /// <summary>LLM 無法填充的欄位清單</summary>
    public IReadOnlyList<string> MissingFields { get; init; } = [];

    /// <summary>LLM 標記的待確認問題</summary>
    public IReadOnlyList<string> OpenQuestions { get; init; } = [];

    /// <summary>處理的來源文件數量</summary>
    public int SourceCount { get; init; }

    /// <summary>LLM 輸入 token 總量</summary>
    public int TotalInputTokens { get; init; }

    /// <summary>LLM 輸出 token 總量</summary>
    public int TotalOutputTokens { get; init; }

    /// <summary>總 token 數</summary>
    public int TotalTokens => TotalInputTokens + TotalOutputTokens;

    /// <summary>LLM Challenge 驗證結果（啟用 Challenge 時才有值）</summary>
    public IReadOnlyList<FieldChallenge> Challenges { get; init; } = [];

    /// <summary>整體信心度（0.0 ~ 1.0，啟用 Challenge 時才有意義）</summary>
    public float OverallConfidence { get; init; } = 1.0f;
}

/// <summary>單一欄位的 Challenge 結果</summary>
public sealed class FieldChallenge
{
    /// <summary>欄位路徑（如 "budget.total" 或 "functional_requirements[1].priority"）</summary>
    public required string Field { get; init; }

    /// <summary>Layer 3 擷取的原始值</summary>
    public required string OriginalValue { get; init; }

    /// <summary>Challenge Agent 的質疑/建議</summary>
    public required string ChallengeReason { get; init; }

    /// <summary>Challenge Agent 建議的修正值（null = 無建議）</summary>
    public string? SuggestedValue { get; init; }

    /// <summary>信心度（0.0 ~ 1.0）</summary>
    public float Confidence { get; init; }

    /// <summary>處理動作</summary>
    public ChallengeAction Action { get; init; }
}

/// <summary>Challenge 處理動作</summary>
public enum ChallengeAction
{
    /// <summary>信心度 > 0.8 — 兩個 LLM 一致，直接採用</summary>
    Accept,

    /// <summary>信心度 0.5 ~ 0.8 — 有疑慮，標記待人工確認</summary>
    Flag,

    /// <summary>信心度 < 0.5 — 明確不一致，設為 null</summary>
    Reject
}
