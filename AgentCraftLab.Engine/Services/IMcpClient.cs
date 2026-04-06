using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// MCP Client 介面 — 連接 MCP Server 取得工具定義。
/// </summary>
public interface IMcpClient
{
    /// <summary>從 MCP Server 取得工具清單。</summary>
    Task<IList<AITool>> GetToolsAsync(string serverUrl, CancellationToken ct = default);
}
