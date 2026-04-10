using System.Text.Json;
using AgentCraftLab.Search.Abstractions;

namespace AgentCraftLab.Search.Providers.Qdrant;

/// <summary>
/// Qdrant 搜尋引擎 Provider — 根據 ConfigJson 建立 QdrantSearchEngine。
/// </summary>
public class QdrantSearchEngineProvider : ISearchEngineProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public string ProviderName => "qdrant";

    public ISearchEngine Create(string configJson)
    {
        var config = JsonSerializer.Deserialize<QdrantConfig>(configJson, JsonOpts) ?? new QdrantConfig();
        return new QdrantSearchEngine(config);
    }
}
