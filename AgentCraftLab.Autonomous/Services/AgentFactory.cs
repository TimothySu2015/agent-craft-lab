using System.Collections.Concurrent;
using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// Agent 工廠 — 動態建構和管理 Agent，複用 Engine 的 AgentContextBuilder。
/// </summary>
public sealed class AgentFactory : IAsyncDisposable
{
    private readonly ToolRegistryService _toolRegistry;
    private readonly SkillRegistryService _skillRegistry;
    private readonly McpClientService _mcpClient;
    private readonly A2AClientService _a2aClient;
    private readonly HttpApiToolService _httpApiTool;
    private readonly IToolDelegationStrategy _toolStrategy;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, IChatClient> _clientPool = new();

    /// <summary>
    /// LLM base client 快取 — 同一 provider+endpoint 共用底層連線（OpenAIClient / AnthropicClient 等）。
    /// </summary>
    private readonly ConcurrentDictionary<string, object> _baseClientCache = new();

    public AgentFactory(
        ToolRegistryService toolRegistry,
        SkillRegistryService skillRegistry,
        McpClientService mcpClient,
        A2AClientService a2aClient,
        HttpApiToolService httpApiTool,
        IToolDelegationStrategy toolStrategy,
        ILogger<AgentFactory> logger)
    {
        _toolRegistry = toolRegistry;
        _skillRegistry = skillRegistry;
        _mcpClient = mcpClient;
        _a2aClient = a2aClient;
        _httpApiTool = httpApiTool;
        _toolStrategy = toolStrategy;
        _logger = logger;
    }

    /// <summary>
    /// 建立 Orchestrator Agent 的 IChatClient — 帶有所有可用工具的主控 agent。
    /// 當 splitForToolSearch 為 true 時，將工具分為 AlwaysAvailable（安全白名單）和 Searchable（其他）兩組。
    /// </summary>
    public async Task<(IChatClient Client, IList<AITool> AlwaysAvailableTools, IList<AITool> SearchableTools, string? Error)> CreateOrchestratorAsync(
        AutonomousRequest request,
        CancellationToken cancellationToken,
        bool splitForToolSearch = false,
        string? middleware = null)
    {
        // 1. 建立 LLM client
        var provider = AgentContextBuilder.NormalizeProvider(request.Provider);
        if (!request.Credentials.TryGetValue(provider, out var credential))
        {
            return (null!, [], [], $"Missing credential for provider: {provider}");
        }

        var client = AgentContextBuilder.CreateChatClient(
            provider, credential.ApiKey, credential.Endpoint, request.Model, _baseClientCache);

        // 2. 合併所有工具來源
        var tools = new List<AITool>();

        // 內建工具
        if (request.AvailableTools.Count > 0)
        {
            tools.AddRange(_toolRegistry.Resolve(request.AvailableTools, request.Credentials));
        }

        // Skill 聲明的工具
        foreach (var skillId in request.AvailableSkills)
        {
            var skill = _skillRegistry.GetById(skillId);
            if (skill?.Tools is { Count: > 0 } skillTools)
            {
                var resolved = skillTools
                    .Where(t => !request.AvailableTools.Contains(t))
                    .Where(t => _toolRegistry.IsToolAvailable(t, request.Credentials))
                    .ToList();
                if (resolved.Count > 0)
                {
                    tools.AddRange(_toolRegistry.Resolve(resolved, request.Credentials));
                }
            }
        }

        // MCP 工具（並行發現）
        if (request.McpServers.Count > 0)
        {
            var mcpTasks = request.McpServers.Select(async url =>
            {
                try
                {
                    return await _mcpClient.GetToolsAsync(url, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MCP discovery failed for {Url}", url);
                    return (IList<AITool>)[];
                }
            }).ToList();

            var mcpResults = await Task.WhenAll(mcpTasks);
            foreach (var mcpTools in mcpResults)
            {
                tools.AddRange(mcpTools);
            }
        }

        // A2A 工具（並行發現：先 discover card，再 wrap 為 tool）
        if (request.A2AAgents.Count > 0)
        {
            var a2aTasks = request.A2AAgents.Select(async url =>
            {
                try
                {
                    var card = await _a2aClient.DiscoverAsync(url, "auto", cancellationToken);
                    return _a2aClient.WrapAsAITool(card, "auto");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "A2A discovery failed for {Url}", url);
                    return (AITool?)null;
                }
            }).ToList();

            var a2aResults = await Task.WhenAll(a2aTasks);
            foreach (var a2aTool in a2aResults)
            {
                if (a2aTool is not null)
                {
                    tools.Add(a2aTool);
                }
            }
        }

        // HTTP API 工具
        foreach (var (_, apiDef) in request.HttpApis)
        {
            tools.Add(_httpApiTool.WrapAsAITool(apiDef));
        }

        // 3. 應用 middleware（預設含 recovery 支援 output 截斷/context overflow 自動恢復）
        var compactor = new LlmContextCompactor(client);
        client = AgentContextBuilder.ApplyMiddleware(
            client, middleware ?? "logging,retry,recovery", contextCompactor: compactor);

        if (!splitForToolSearch)
        {
            // 傳統模式：所有工具都是 always-available，searchable 為空
            return (client, tools, [], null);
        }

        // Tool Search 模式：分類工具
        var alwaysAvailable = new List<AITool>();
        var searchable = new List<AITool>();

        foreach (var tool in tools)
        {
            if (tool is AIFunction func &&
                SafeWhitelistToolDelegation.IsSafeTool(func.Name))
            {
                alwaysAvailable.Add(tool);
            }
            else
            {
                searchable.Add(tool);
            }
        }

        return (client, alwaysAvailable, searchable, null);
    }

    /// <summary>
    /// 建立 Sub-agent 的 IChatClient — 工具只能是 Orchestrator 已有工具的子集（不含 meta-tools）。
    /// </summary>
    public (SubAgentEntry Entry, List<string> SkippedTools) CreateSubAgent(
        SubAgentSpec spec,
        AutonomousRequest request,
        IList<AITool> orchestratorTools,
        SemaphoreSlim? llmThrottle = null)
    {
        // Provider/Model 繼承 Orchestrator（spec 可覆蓋）
        var provider = AgentContextBuilder.NormalizeProvider(spec.Provider ?? request.Provider);
        if (!request.Credentials.TryGetValue(provider, out var credential))
        {
            throw new InvalidOperationException($"Missing credential for provider: {provider}");
        }

        var model = spec.Model ?? request.Model;
        var client = AgentContextBuilder.CreateChatClient(provider, credential.ApiKey, credential.Endpoint, model, _baseClientCache);

        // 工具篩選：委派給 IToolDelegationStrategy 決定 sub-agent 可用工具
        var tools = _toolStrategy.ResolveTools(orchestratorTools, spec.Tools);

        // 計算被跳過的工具（指定了但正規化後仍未在解析結果中找到）
        var skippedTools = new List<string>();
        if (spec.Tools.Count > 0)
        {
            var resolvedNames = new HashSet<string>(
                tools.OfType<AIFunction>().Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var toolId in spec.Tools)
            {
                var normalized = SafeWhitelistToolDelegation.NormalizeToolId(toolId);

                if (!resolvedNames.Contains(normalized))
                {
                    skippedTools.Add(toolId);
                    _logger.LogWarning(
                        "Sub-agent {Name} requested tool {ToolId} not in Orchestrator tool set, skipping",
                        spec.Name, toolId);
                }
            }
        }
        else
        {
            _logger.LogWarning(
                "Sub-agent '{Name}' inheriting {Count} tools (out of {Total} orchestrator tools) via {Strategy}",
                spec.Name, tools.Count, orchestratorTools.Count, _toolStrategy.GetType().Name);
        }

        // 應用 middleware
        client = AgentContextBuilder.ApplyMiddleware(client, "logging,retry");

        // 節流：共享 SemaphoreSlim 限制並行 LLM 呼叫，避免 429
        if (llmThrottle is not null)
        {
            client = new Middleware.ThrottledChatClient(client, llmThrottle);
        }

        return (new SubAgentEntry
        {
            Name = spec.Name,
            Instructions = spec.Instructions,
            Client = client,
            Tools = tools
        }, skippedTools);
    }

    /// <summary>
    /// 取得可用工具的描述清單 — 供 system prompt 中告知 AI 有哪些工具可用。
    /// </summary>
    public static string DescribeAvailableTools(IList<AITool> tools)
    {
        if (tools.Count == 0)
        {
            return "No tools available.";
        }

        var lines = new List<string> { $"Available tools ({tools.Count}):" };
        foreach (var tool in tools)
        {
            if (tool is AIFunction func)
            {
                lines.Add($"  - {func.Name}: {func.Description}");
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// 取得可用 Skill 的描述清單 — 供 system prompt 中告知 AI 有哪些能力。
    /// </summary>
    public string DescribeAvailableSkills(List<string> skillIds)
    {
        if (skillIds.Count == 0)
        {
            return "";
        }

        var skills = _skillRegistry.Resolve(skillIds);
        if (skills.Count == 0)
        {
            return "";
        }

        var lines = new List<string> { $"Available skills ({skills.Count}):" };
        foreach (var skill in skills)
        {
            lines.Add($"  - {skill.DisplayName}: {skill.Description}");
        }

        return string.Join('\n', lines);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clientPool.Values)
        {
            if (client is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (client is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _clientPool.Clear();
    }
}
