namespace AgentCraftLab.Engine.Services.Variables;

/// <summary>
/// 變數解析上下文 — 封裝 <see cref="IVariableResolver"/> 需要的所有變數來源。
/// 五個字典分別對應 <see cref="Models.Schema.VariableScope"/> 的五種 scope。
/// </summary>
public sealed record VariableContext
{
    /// <summary>系統變數（readonly），如 user_id / run_id / now。</summary>
    public IReadOnlyDictionary<string, string> System { get; init; } = EmptyDict;

    /// <summary>Workflow 變數（使用者定義 + Code 節點寫回）。</summary>
    public IReadOnlyDictionary<string, string> Workflow { get; init; } = EmptyDict;

    /// <summary>執行時傳入的變數（覆蓋 Workflow 預設值）。</summary>
    public IReadOnlyDictionary<string, string> Runtime { get; init; } = EmptyDict;

    /// <summary>環境變數（AGENTCRAFTLAB_ 前綴 allowlist）。</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = EmptyDict;

    /// <summary>節點輸出 — key 為節點 ID 或 Name。</summary>
    public IReadOnlyDictionary<string, string> NodeOutputs { get; init; } = EmptyDict;

    /// <summary>
    /// 節點 ID → Name 反向索引（供 {{node:name}} 透過 name 查 ID 用）。
    /// 可選，若為 null 則 VariableResolver 會就地建立。
    /// </summary>
    public IReadOnlyDictionary<string, string>? NodeNameMap { get; init; }

    private static readonly IReadOnlyDictionary<string, string> EmptyDict =
        new Dictionary<string, string>();

    /// <summary>建立一個所有字典都為空的 context（單元測試常用）。</summary>
    public static VariableContext Empty { get; } = new();
}
