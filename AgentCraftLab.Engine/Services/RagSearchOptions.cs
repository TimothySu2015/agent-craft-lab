namespace AgentCraftLab.Engine.Services;

/// <summary>
/// RAG 搜尋選項 — 避免 SearchAsync 參數過多。
/// </summary>
public record RagSearchOptions
{
    public string SearchMode { get; init; } = "hybrid";
    public float? MinScore { get; init; }
    public string? EmbeddingModel { get; init; }
    public bool QueryExpansion { get; init; }
    public QueryExpander? QueryExpander { get; init; }
    public string? FileNameFilter { get; init; }
    public bool ContextCompression { get; init; }
    public int TokenBudget { get; init; } = 1500;
    public ContextCompressor? ContextCompressor { get; init; }
}
