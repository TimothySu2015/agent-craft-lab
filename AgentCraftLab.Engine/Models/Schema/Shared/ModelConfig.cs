namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// LLM 模型設定 — 給 Agent / Autonomous / Condition(llm-judge) 共用。
/// </summary>
public sealed record ModelConfig
{
    public string Provider { get; init; } = "openai";
    public string Model { get; init; } = "gpt-4o-mini";
    public float? Temperature { get; init; }
    public float? TopP { get; init; }
    public int? MaxOutputTokens { get; init; }
}

/// <summary>
/// Agent 輸出格式設定 — 純文字、JSON mode、或 JSON schema 強制。
/// </summary>
public sealed record OutputConfig
{
    public OutputFormat Kind { get; init; } = OutputFormat.Text;
    public string? SchemaJson { get; init; }
}

public enum OutputFormat
{
    Text,
    Json,
    JsonSchema
}

/// <summary>
/// Agent 歷史訊息提供者設定。
/// </summary>
public sealed record HistoryConfig
{
    public HistoryProviderKind Provider { get; init; } = HistoryProviderKind.None;
    public int MaxMessages { get; init; } = 20;
}

public enum HistoryProviderKind
{
    None,
    Session,
    Database,
    InMemory
}
