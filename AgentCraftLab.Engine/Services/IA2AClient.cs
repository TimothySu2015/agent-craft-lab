using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// A2A Client 介面 — 連接遠端 A2A Agent 進行通訊。
/// </summary>
public interface IA2AClient
{
    /// <summary>探索遠端 A2A Agent 的能力（Agent Card）。</summary>
    Task<A2AAgentCard> DiscoverAsync(string baseUrl, string format = "auto", CancellationToken ct = default);

    /// <summary>向遠端 A2A Agent 發送訊息並取得回應。</summary>
    Task<string> SendMessageAsync(string baseUrl, string message, string? contextId = null, string format = "auto", int? timeoutSeconds = null);
}
