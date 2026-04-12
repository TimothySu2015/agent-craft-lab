using System.ComponentModel;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// RAG 資料節點（Meta）— 宣告 workflow 使用的知識庫來源。
/// 執行時由 WorkflowPreprocessor 擷取 + 檢索後注入下游 agent 的 system prompt。
/// </summary>
public sealed record RagNode : NodeConfig
{
    [Description("RAG 設定 — 資料來源、分塊、檢索策略")]
    public RagConfig Rag { get; init; } = new();

    [Description("要掛載的知識庫 ID 清單（對應 KnowledgeBaseStore）")]
    public IReadOnlyList<string> KnowledgeBaseIds { get; init; } = [];
}

/// <summary>
/// RAG 檢索設定（取代舊 <see cref="Models.RagSettings"/> class）。
/// </summary>
public sealed record RagConfig
{
    public string DataSource { get; init; } = "upload";
    public int ChunkSize { get; init; } = 800;
    public int ChunkOverlap { get; init; } = 80;
    public int TopK { get; init; } = 5;
    public string EmbeddingModel { get; init; } = "text-embedding-3-small";
    public string SearchMode { get; init; } = "hybrid";
    public float MinScore { get; init; } = 0.005f;
    public bool QueryExpansion { get; init; } = true;
    public string? FileNameFilter { get; init; }
    public bool ContextCompression { get; init; }
    public int TokenBudget { get; init; } = 1500;
}
