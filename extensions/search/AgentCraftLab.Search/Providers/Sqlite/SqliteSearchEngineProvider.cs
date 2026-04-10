using AgentCraftLab.Search.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Search.Providers.Sqlite;

/// <summary>
/// SQLite 搜尋引擎 Provider — 根據 ConfigJson 的 dbPath 建立 SqliteSearchEngine。
/// </summary>
public class SqliteSearchEngineProvider : ISearchEngineProvider
{
    private const string DefaultDbPath = "Data/craftsearch.db";
    private readonly ILoggerFactory _loggerFactory;

    public SqliteSearchEngineProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string ProviderName => "sqlite";

    public ISearchEngine Create(string configJson)
    {
        var dbPath = DefaultDbPath;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("dbPath", out var pathProp) &&
                pathProp.ValueKind == System.Text.Json.JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(pathProp.GetString()))
            {
                dbPath = pathProp.GetString()!;
            }
        }
        catch
        {
            // ConfigJson 解析失敗，用預設路徑
        }

        return SqliteSearchEngine.Create(dbPath, _loggerFactory);
    }
}
