using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 工具委派策略 — 決定 sub-agent 可使用哪些工具。
/// </summary>
public interface IToolDelegationStrategy
{
    /// <summary>
    /// 從 orchestrator 工具集中篩選 sub-agent 可使用的工具。
    /// </summary>
    /// <param name="orchestratorTools">Orchestrator 擁有的完整工具集。</param>
    /// <param name="requestedToolIds">Sub-agent 明確要求的工具 ID 清單（可為空）。</param>
    /// <returns>篩選後的工具清單。</returns>
    List<AITool> ResolveTools(IList<AITool> orchestratorTools, IList<string> requestedToolIds);
}

/// <summary>
/// 預設策略：安全白名單篩選。
/// 指定 tools 時從 orchestrator 子集篩選；未指定時只繼承安全唯讀工具（最小權限原則）。
/// 所有 tool ID 比對統一經過 CanonicalizeToolId 正規化（去底線/連字號 + 小寫），
/// 避免 PascalCase（AzureWebSearch）和 snake_case（azure_web_search）不匹配的問題。
/// </summary>
public sealed class SafeWhitelistToolDelegation : IToolDelegationStrategy
{
    /// <summary>
    /// 安全的唯讀工具白名單（canonical 形式）。
    /// 只需寫一次，不論來源是 PascalCase 或 snake_case 都能匹配。
    /// </summary>
    private static readonly HashSet<string> SafeToolIds =
    [
        "websearch", "azurewebsearch", "wikipedia", "calculator",
        "getdatetime", "urlfetch", "jsonparser",
        "listdirectory", "readfile", "searchcode"
    ];

    /// <inheritdoc />
    public List<AITool> ResolveTools(IList<AITool> orchestratorTools, IList<string> requestedToolIds)
    {
        if (requestedToolIds.Count > 0)
        {
            // 有指定 → 從 orchestrator 工具中篩選匹配的子集（canonical key 比對）
            var toolMap = new Dictionary<string, AITool>();
            foreach (var tool in orchestratorTools)
            {
                if (tool is AIFunction func)
                {
                    toolMap[CanonicalizeToolId(func.Name)] = tool;
                }
            }

            return requestedToolIds
                .Select(id => CanonicalizeToolId(NormalizeToolId(id)))
                .Where(toolMap.ContainsKey)
                .Select(id => toolMap[id])
                .ToList();
        }

        // 未指定 → 只繼承安全工具
        return orchestratorTools
            .Where(t => t is AIFunction func && SafeToolIds.Contains(CanonicalizeToolId(func.Name)))
            .ToList();
    }

    /// <summary>
    /// 正規化 LLM 產出的 tool ID — 移除常見的錯誤前綴（如 "functions."）。
    /// </summary>
    internal static string NormalizeToolId(string toolId)
    {
        if (toolId.StartsWith("functions.", StringComparison.OrdinalIgnoreCase))
        {
            return toolId["functions.".Length..];
        }

        return toolId;
    }

    /// <summary>
    /// 將任何格式的 tool ID 轉為 canonical 形式（去底線/連字號 + 小寫）。
    /// 例如：AzureWebSearch → azurewebsearch, azure_web_search → azurewebsearch
    /// </summary>
    internal static string CanonicalizeToolId(string toolId)
    {
        return toolId.Replace("_", "").Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// 檢查工具是否在安全白名單中（供 Tool Search 分類使用）。
    /// </summary>
    public static bool IsSafeTool(string toolName)
    {
        return SafeToolIds.Contains(CanonicalizeToolId(toolName));
    }
}
