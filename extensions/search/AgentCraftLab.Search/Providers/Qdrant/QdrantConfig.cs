namespace AgentCraftLab.Search.Providers.Qdrant;

/// <summary>
/// Qdrant Provider 連線設定。
/// </summary>
public class QdrantConfig
{
    public string Url { get; set; } = "http://localhost:6333";
    public string? ApiKey { get; set; }
}
