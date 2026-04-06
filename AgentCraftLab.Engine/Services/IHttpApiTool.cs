using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// HTTP API Tool 介面 — 將 HTTP API 包裝為 AI Tool 或直接呼叫。
/// </summary>
public interface IHttpApiTool
{
    /// <summary>將 HttpApiDefinition 包裝為 AITool。</summary>
    AITool WrapAsAITool(HttpApiDefinition api);

    /// <summary>呼叫 HTTP API。</summary>
    Task<string> CallApiAsync(HttpApiDefinition api, string argsJson);
}
