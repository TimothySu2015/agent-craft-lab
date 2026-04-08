using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 工具註冊表介面 — 管理所有可供 Agent 使用的內建工具。
/// </summary>
public interface IToolRegistry
{
    /// <summary>根據工具 ID 清單解析出對應的 AITool 實例。</summary>
    IList<AITool> Resolve(List<string> toolIds, Dictionary<string, ProviderCredential>? credentials = null);

    /// <summary>檢查工具是否可用（不需憑證或已有憑證）。</summary>
    bool IsToolAvailable(string toolId, Dictionary<string, ProviderCredential>? credentials);

    /// <summary>取得所有已註冊的工具定義。</summary>
    IReadOnlyList<ToolDefinition> GetAvailableTools();

    /// <summary>取得所有需要憑證的工具類型。</summary>
    IReadOnlyList<ToolCredentialType> GetToolCredentialTypes();

    /// <summary>依分類取得工具。</summary>
    IReadOnlyDictionary<ToolCategory, List<ToolDefinition>> GetByCategory();
}
