namespace AgentCraftLab.Autonomous.Models;

/// <summary>
/// ReAct 迴圈完整狀態快照 — 可序列化為 JSON，用於檢查點持久化與恢復。
/// 所有欄位皆為 init-only，snapshot 建立後不可變。
/// </summary>
public sealed record CheckpointSnapshot
{
    // ─── 迴圈位置 ───
    public int Iteration { get; init; }
    public string? FinalAnswer { get; init; }
    public bool Succeeded { get; init; }
    public long CachedMessageChars { get; init; }

    // ─── 對話歷史 ───
    public List<SerializableChatMessage> Messages { get; init; } = [];

    // ─── 執行步驟 ───
    public List<ReactStep> Steps { get; init; } = [];

    // ─── Token 追蹤 ───
    public long InputTokensUsed { get; init; }
    public long OutputTokensUsed { get; init; }

    // ─── 工具呼叫追蹤 ───
    public Dictionary<string, int> ToolCallCounts { get; init; } = new();
    public int TotalToolCalls { get; init; }

    // ─── 收斂偵測 ───
    public List<ConvergenceEntry> ConvergenceToolHistory { get; init; } = [];
    public List<int> ConvergenceResponseLengths { get; init; } = [];

    // ─── 共享狀態 ───
    public Dictionary<string, SharedStateSnapshot> SharedState { get; init; } = new();

    // ─── 持久 Sub-agent ───
    public Dictionary<string, SubAgentSnapshot> SubAgents { get; init; } = new();

    // ─── 迴圈狀態 ───
    public int BudgetReminderIndex { get; init; } = -1;
    public int AskUserCount { get; init; }

    // ─── 規劃 ───
    public string? Plan { get; init; }

    // ─── Tool Search 動態載入 ───
    public List<string>? LoadedDynamicToolNames { get; init; }

    // ─── ToolCall 事件（軌跡轉換用）───
    public List<ToolCallEventSnapshot> ToolCallEvents { get; init; } = [];
}

/// <summary>收斂偵測歷史條目。</summary>
public sealed record ConvergenceEntry(string ToolName, string ResultSnippet);

/// <summary>共享狀態快照條目。</summary>
public sealed record SharedStateSnapshot(string Key, string Value, string SetBy, DateTime UpdatedAt);

/// <summary>持久 Sub-agent 快照（不含 IChatClient，恢復時重建）。</summary>
public sealed record SubAgentSnapshot
{
    public string Name { get; init; } = "";
    public string Instructions { get; init; } = "";
    public List<string> ToolIds { get; init; } = [];
    public List<SerializableChatMessage> History { get; init; } = [];
    public int CallCount { get; init; }
}

/// <summary>ToolCall 事件快照（精簡版，只保留軌跡轉換所需欄位）。</summary>
public sealed record ToolCallEventSnapshot(string AgentName, string Text);
