using System.Text.Json;
using AgentCraftLab.Search.Abstractions;

namespace AgentCraftLab.Search.Providers.PgVector;

/// <summary>
/// PgVector 搜尋引擎 Provider — 根據 ConfigJson 建立 PgVectorSearchEngine。
/// </summary>
public class PgVectorSearchEngineProvider : ISearchEngineProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public string ProviderName => "pgvector";

    public ISearchEngine Create(string configJson)
    {
        var config = JsonSerializer.Deserialize<PgVectorConfig>(configJson, JsonOpts) ?? new PgVectorConfig();
        return new PgVectorSearchEngine(config);
    }
}
