using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// Flow 模式的 LLM Client 工廠 — 雙模型架構。
/// 透過 ILlmClientFactory 統一 credential 解析，不再重複邏輯。
/// </summary>
public sealed class FlowAgentFactory
{
    /// <summary>規劃用模型預設值 — 可透過 GoalExecutionRequest.Options["flow:plannerModel"] 覆蓋</summary>
    public const string DefaultPlannerModel = "gpt-4.1";

    private readonly ToolRegistryService _toolRegistry;
    private readonly ILlmClientFactory _clientFactory;
    private readonly ILogger<FlowAgentFactory> _logger;

    public FlowAgentFactory(ToolRegistryService toolRegistry, ILlmClientFactory clientFactory, ILogger<FlowAgentFactory> logger)
    {
        _toolRegistry = toolRegistry;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public (IChatClient? Client, string? Error) CreatePlanner(GoalExecutionRequest request)
    {
        var plannerModel = GetPlannerModel(request);
        return _clientFactory.CreateClient(request.Credentials, request.Provider, plannerModel);
    }

    public static string GetPlannerModel(GoalExecutionRequest request)
    {
        if (request.Options?.TryGetValue(FlowOptions.PlannerModel, out var modelObj) == true &&
            modelObj is string model && !string.IsNullOrWhiteSpace(model))
        {
            return model;
        }
        return DefaultPlannerModel;
    }

    public (IChatClient? Client, IList<AITool>? Tools, string? Error) CreateAgentClient(
        GoalExecutionRequest request, List<string>? toolIds = null)
    {
        var (client, error) = _clientFactory.CreateClient(request.Credentials, request.Provider, request.Model);
        if (client is null) return (null, null, error);

        IList<AITool>? resolvedTools = null;

        if (toolIds is { Count: > 0 })
        {
            resolvedTools = _toolRegistry.Resolve(toolIds, request.Credentials);

            var funcClient = new FunctionInvokingChatClient(client)
            {
                MaximumIterationsPerRequest = 3
            };
            client = funcClient;
        }

        return (client, resolvedTools, null);
    }

    public Dictionary<string, string> GetToolDescriptions()
    {
        return _toolRegistry.GetAvailableTools()
            .ToDictionary(t => t.Id, t => t.Description);
    }
}
