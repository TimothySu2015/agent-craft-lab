namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// HTTP 重試設定（指數退避）。
/// </summary>
public sealed record RetryConfig
{
    public int Count { get; init; }
    public int DelayMs { get; init; } = 1000;
    public double Backoff { get; init; } = 2.0;
}

/// <summary>
/// Parallel 節點的單一分支定義。
/// </summary>
public sealed record BranchConfig
{
    public string Name { get; init; } = "";
    public string Goal { get; init; } = "";
    public IReadOnlyList<string>? Tools { get; init; }
}

public enum MergeStrategyKind
{
    Labeled,
    Join,
    Json
}

/// <summary>
/// Router 節點的單一路由規則。
/// </summary>
public sealed record RouteConfig
{
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public bool IsDefault { get; init; }
}

/// <summary>
/// Middleware 掛載點 — Key 為 middleware 名稱（例如 "guardrails"、"pii"），Options 為該 middleware 的參數。
/// 取代舊 schema 的 Dictionary&lt;string, Dictionary&lt;string, string&gt;&gt;。
/// </summary>
public sealed record MiddlewareBinding
{
    public string Key { get; init; } = "";
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();
}
