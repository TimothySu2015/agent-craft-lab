using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Models;

/// <summary>
/// Sub-agent 建立規格 — Orchestrator 透過 create_sub_agent 工具傳入。
/// </summary>
public record SubAgentSpec
{
    /// <summary>Sub-agent 名稱（唯一識別）</summary>
    public required string Name { get; init; }

    /// <summary>角色指令</summary>
    public required string Instructions { get; init; }

    /// <summary>可用工具 ID 子集（必須是 Orchestrator 已有的工具）</summary>
    public List<string> Tools { get; init; } = [];

    /// <summary>覆蓋 provider（null = 繼承 Orchestrator）</summary>
    public string? Provider { get; init; }

    /// <summary>覆蓋 model（null = 繼承 Orchestrator）</summary>
    public string? Model { get; init; }
}

/// <summary>
/// Sub-agent 內部追蹤記錄 — AgentPool 管理用。
/// </summary>
public sealed class SubAgentEntry : IAsyncDisposable
{
    public required string Name { get; init; }
    public required string Instructions { get; init; }
    public required IChatClient Client { get; init; }
    public required IList<AITool> Tools { get; init; }
    public List<ChatMessage> History { get; } = [];
    public int CallCount { get; set; }

    /// <summary>Per-agent 鎖，防止並行 AskAsync 同時修改 History。</summary>
    public SemaphoreSlim Lock { get; } = new(1, 1);

    public async ValueTask DisposeAsync()
    {
        Lock.Dispose();
        if (Client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (Client is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

/// <summary>
/// 共享狀態條目 — 跨 agent 共享的 key-value 資料。
/// </summary>
public record SharedStateEntry
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public required string SetBy { get; init; }
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
