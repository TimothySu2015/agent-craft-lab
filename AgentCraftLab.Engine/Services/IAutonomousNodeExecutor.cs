using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// Autonomous 節點執行器介面 — 由 Autonomous 層實作，透過 DI 注入到 Engine。
/// 避免 Engine 反向引用 Autonomous 造成循環依賴。
/// </summary>
public interface IAutonomousNodeExecutor
{
    IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        AutonomousNodeRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Autonomous 節點的執行請求 — 使用 Engine 已有的型別，不依賴 Autonomous 的 AutonomousRequest。
/// </summary>
public record AutonomousNodeRequest
{
    public required string Goal { get; init; }
    public required Dictionary<string, ProviderCredential> Credentials { get; init; }
    public string Provider { get; init; } = Defaults.Provider;
    public string Model { get; init; } = Defaults.Model;
    public List<string> AvailableTools { get; init; } = [];
    public List<string> AvailableSkills { get; init; } = [];
    public List<string> McpServers { get; init; } = [];
    public List<string> A2AAgents { get; init; } = [];
    public Dictionary<string, HttpApiDefinition> HttpApis { get; init; } = [];
    public int MaxIterations { get; init; } = 25;
    public long MaxTotalTokens { get; init; } = 200_000;
    public int MaxToolCalls { get; init; } = 50;
    public FileAttachment? Attachment { get; init; }
}
