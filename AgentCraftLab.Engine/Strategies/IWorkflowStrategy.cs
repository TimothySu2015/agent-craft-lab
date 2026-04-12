using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Engine.Strategies;

/// <summary>
/// 工作流程執行策略介面。每種工作流程類型（Single/Sequential/Concurrent/Handoff/Imperative）各有一個實作。
/// </summary>
public interface IWorkflowStrategy
{
    IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowStrategyContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// 傳遞給策略的完整執行上下文 — Phase C 之後 Payload 為強型別 <see cref="Schema.WorkflowPayload"/>。
/// </summary>
public record WorkflowStrategyContext(
    Schema.WorkflowPayload Payload,
    List<Schema.AgentNode> AgentNodes,
    List<Schema.Connection> ResolvedConnections,
    AgentExecutionContext AgentContext,
    WorkflowExecutionRequest Request,
    Services.WorkflowHookRunner? HookRunner = null,
    Schema.WorkflowHooks? Hooks = null,
    string UserId = "",
    string? SessionId = null);

/// <summary>
/// 保存建構完成的 agents、clients、工具和日誌。
/// 實作 IAsyncDisposable 以確保 IChatClient（含 TokenBucketRateLimiter）等資源正確釋放。
/// </summary>
public record AgentExecutionContext(
    Dictionary<string, Microsoft.Agents.AI.ChatClientAgent> Agents,
    Dictionary<string, Microsoft.Extensions.AI.IChatClient> ChatClients,
    Dictionary<string, IList<Microsoft.Extensions.AI.AITool>> NodeToolsMap,
    System.Collections.Concurrent.ConcurrentQueue<(string AgentName, string Type, string Text)> ToolCallLogs,
    Microsoft.Extensions.AI.IChatClient? JudgeClient = null,
    Dictionary<string, Schema.A2AAgentNode>? A2ANodes = null,
    A2AClientService? A2AClient = null,
    HumanInputBridge? HumanBridge = null,
    DebugBridge? DebugBridge = null,
    HttpApiToolService? HttpApiService = null,
    Dictionary<string, HttpApiDefinition>? HttpApiDefs = null,
    Dictionary<string, string>? NodeInstructions = null,
    Dictionary<string, List<string>>? NodeSkillNames = null,
    Services.IAutonomousNodeExecutor? AutonomousExecutor = null,
    IReadOnlyList<IDisposable>? OwnedResources = null,
    AgentContextBuilder? ContextBuilder = null) : IAsyncDisposable
{
    /// <summary>建立不含任何 Agent 的空 context（僅 A2A 節點時使用）。</summary>
    public static AgentExecutionContext Empty => new([], [], [], new());

    public async ValueTask DisposeAsync()
    {
        foreach (var client in ChatClients.Values)
        {
            try
            {
                if (client is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (client is IDisposable disposable)
                    disposable.Dispose();
            }
            catch
            {
                // 單一 client dispose 失敗不影響其餘清理
            }
        }

        try
        {
            if (JudgeClient is IAsyncDisposable judgeAsyncDisposable)
                await judgeAsyncDisposable.DisposeAsync();
            else if (JudgeClient is IDisposable judgeDisposable)
                judgeDisposable.Dispose();
        }
        catch
        {
            // Judge client dispose 失敗不影響
        }

        // 釋放 base OpenAI/Azure clients（避免 socket 洩漏）
        if (OwnedResources is not null)
        {
            foreach (var resource in OwnedResources)
            {
                try { resource.Dispose(); }
                catch { /* 單一資源 dispose 失敗不影響其餘清理 */ }
            }
        }

        GC.SuppressFinalize(this);
    }
}
