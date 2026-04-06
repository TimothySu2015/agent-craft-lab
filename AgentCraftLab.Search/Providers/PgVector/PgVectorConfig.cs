namespace AgentCraftLab.Search.Providers.PgVector;

/// <summary>
/// pgvector Provider 連線設定。
/// </summary>
public class PgVectorConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public string ToConnectionString() =>
        $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}";
}
