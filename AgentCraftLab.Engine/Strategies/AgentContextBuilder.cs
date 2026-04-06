using System.ClientModel;
using System.Collections.Concurrent;
using AgentCraftLab.Engine.Middleware;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Pii;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Services.Compression;
using Anthropic;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace AgentCraftLab.Engine.Strategies;

/// <summary>
/// 負責建構所有 agent 節點的執行上下文：OpenAI clients、middleware、工具（內建/MCP/A2A/HTTP API）。
/// </summary>
public class AgentContextBuilder
{
    private readonly ToolRegistryService _toolRegistry;
    private readonly SkillRegistryService _skillRegistry;
    private readonly McpClientService _mcpClient;
    private readonly A2AClientService _a2aClient;
    private readonly HttpApiToolService _httpApiTool;
    private readonly RagService _ragService;
    private readonly IPiiDetector? _piiDetector;
    private readonly IPiiTokenVault? _piiTokenVault;
    private readonly IGuardRailsPolicy? _guardRailsPolicy;
    private readonly ILogger _logger;

    /// <summary>最近一次 RAG 搜尋找到的引用來源（由 RagChatClient callback 設定）。</summary>
    public List<RagChunk>? LastRagCitations { get; set; }

    /// <summary>最近一次 Query Expansion 生成的查詢變體。</summary>
    public List<string>? LastExpandedQueries => _ragService.LastExpandedQueries;

    public AgentContextBuilder(
        ToolRegistryService toolRegistry,
        SkillRegistryService skillRegistry,
        McpClientService mcpClient,
        A2AClientService a2aClient,
        HttpApiToolService httpApiTool,
        RagService ragService,
        ILogger logger,
        IPiiDetector? piiDetector = null,
        IPiiTokenVault? piiTokenVault = null,
        IGuardRailsPolicy? guardRailsPolicy = null)
    {
        _toolRegistry = toolRegistry;
        _skillRegistry = skillRegistry;
        _mcpClient = mcpClient;
        _a2aClient = a2aClient;
        _httpApiTool = httpApiTool;
        _ragService = ragService;
        _piiDetector = piiDetector;
        _piiTokenVault = piiTokenVault;
        _guardRailsPolicy = guardRailsPolicy;
        _logger = logger;
    }

    public async Task<(AgentExecutionContext? Context, string? Error)> BuildAsync(
        List<WorkflowNode> agentNodes,
        WorkflowExecutionRequest request,
        CancellationToken cancellationToken,
        RagContext? ragContext = null,
        WorkflowPayload? payload = null,
        List<Data.SkillDocument>? customSkills = null)
    {
        var clientCache = new ConcurrentDictionary<string, object>();
        var toolCallLogs = new ConcurrentQueue<(string AgentName, string Type, string Text)>();
        var flowSkillIds = payload?.Skills ?? [];

        // ── Phase 1（順序，快）：憑證驗證 + 建立 ChatClient ──
        // CreateChatClient 含 middleware 包裝，共用 clientCache。無 I/O，毫秒級完成。
        var perNodeClients = new Dictionary<string, IChatClient>();
        foreach (var node in agentNodes)
        {
            var provider = NormalizeProvider(node.Provider);
            if (!request.Credentials.TryGetValue(provider, out var cred) ||
                (!Providers.IsKeyOptional(provider) && string.IsNullOrWhiteSpace(cred.ApiKey)))
            {
                return (null,
                    $"No API Key configured for provider '{node.Provider}' (used by {node.Name}). Please set credentials in the API Key settings.");
            }

            perNodeClients[node.Id] = CreateChatClient(node, clientCache, cred, provider);
        }

        // ── Phase 2（並行，慢）：工具解析 + Skill 合併 + RAG/Tools 包裝 + 組裝 ──
        // ResolveToolsAsync 含 MCP/A2A HTTP discovery，是主要耗時點。各節點獨立，可並行。
        var agents = new ConcurrentDictionary<string, ChatClientAgent>();
        var chatClients = new ConcurrentDictionary<string, IChatClient>();
        var nodeToolsMap = new ConcurrentDictionary<string, IList<AITool>>();
        var nodeInstructions = new ConcurrentDictionary<string, string>();
        var nodeSkillNames = new ConcurrentDictionary<string, List<string>>();

        var tasks = agentNodes.Select(async node =>
        {
            var chatClient = perNodeClients[node.Id];
            var tools = await ResolveToolsAsync(node, request, cancellationToken, payload);

            // 合併 Skill 聲明的工具（跳過需要憑證但未設定的工具，並去重）
            var allSkillIds = flowSkillIds.Concat(node.Skills).Distinct().ToList();
            var skills = _skillRegistry.Resolve(allSkillIds, customSkills);
            var addedToolIds = new HashSet<string>(node.Tools);
            foreach (var skill in skills)
            {
                if (skill.Tools is { Count: > 0 })
                {
                    var availableToolIds = skill.Tools
                        .Where(t => !addedToolIds.Contains(t) && _toolRegistry.IsToolAvailable(t, request.Credentials))
                        .ToList();
                    if (availableToolIds.Count > 0)
                    {
                        var skillTools = _toolRegistry.Resolve(availableToolIds, request.Credentials);
                        foreach (var t in skillTools)
                        {
                            tools.Add(t);
                        }

                        addedToolIds.UnionWith(availableToolIds);
                    }
                }
            }

            chatClient = WrapWithRag(chatClient, node, ragContext);
            var chatClientForHistory = WrapWithTools(chatClient, node, tools, toolCallLogs);

            var instructions = BuildInstructions(node.Instructions, node.OutputFormat, flowSkillIds, node.Skills, _skillRegistry, customSkills);
            nodeInstructions[node.Id] = instructions;
            nodeSkillNames[node.Id] = skills.Select(s => s.DisplayName).ToList();

            chatClients[node.Id] = chatClientForHistory;
            nodeToolsMap[node.Id] = tools;
            agents[node.Id] = new ChatClientAgent(
                chatClientForHistory, instructions, GetAgentName(node), tools: tools);
        }).ToList();

        await Task.WhenAll(tasks);

        var judgeClient = CreateJudgeClient(request);
        // clientCache 中的 base OpenAI/Azure clients 需要在執行結束時 dispose，避免 socket 洩漏
        var ownedResources = clientCache.Values.OfType<IDisposable>().ToList();
        return (new AgentExecutionContext(
            new Dictionary<string, ChatClientAgent>(agents),
            new Dictionary<string, IChatClient>(chatClients),
            new Dictionary<string, IList<AITool>>(nodeToolsMap),
            toolCallLogs, judgeClient,
            NodeInstructions: new Dictionary<string, string>(nodeInstructions),
            NodeSkillNames: new Dictionary<string, List<string>>(nodeSkillNames),
            OwnedResources: ownedResources,
            ContextBuilder: this), null);
    }

    private IChatClient WrapWithRag(IChatClient chatClient, WorkflowNode node, RagContext? ragContext)
    {
        if (ragContext is not { RagNodes.Count: > 0 })
        {
            return chatClient;
        }

        var connectedRagNode = ragContext.RagNodes
            .FirstOrDefault(r => ragContext.WorkflowConnections.Any(c => c.From == r.Id && c.To == node.Id));
        if (connectedRagNode is null)
        {
            return chatClient;
        }

        var ragSettings = connectedRagNode.RagConfig ?? new RagSettings();

        // 合併 RAG 節點引用的知識庫索引 + RagContext 層級的知識庫索引
        var kbIndexNames = ragContext.KnowledgeBaseIndexNames;

        var searchOptions = new RagSearchOptions
        {
            SearchMode = ragSettings.SearchMode,
            MinScore = ragSettings.MinScore,
            EmbeddingModel = ragSettings.EmbeddingModel,
            QueryExpansion = ragSettings.QueryExpansion,
            QueryExpander = ragSettings.QueryExpansion ? new QueryExpander(chatClient) : null,
            FileNameFilter = ragSettings.FileNameFilter,
            ContextCompression = ragSettings.ContextCompression,
            TokenBudget = ragSettings.TokenBudget,
            ContextCompressor = ragSettings.ContextCompression ? new ContextCompressor(chatClient) : null,
        };

        return new RagChatClient(
            chatClient, _ragService, ragContext.EmbeddingGenerator, ragContext.IndexName, ragSettings.TopK,
            kbIndexNames, searchOptions: searchOptions,
            onCitationsFound: citations => LastRagCitations = citations);
    }

    private static IChatClient WrapWithTools(IChatClient chatClient, WorkflowNode node, IList<AITool> tools,
        System.Collections.Concurrent.ConcurrentQueue<(string AgentName, string Type, string Text)> toolCallLogs)
    {
        if (tools is not { Count: > 0 })
        {
            return chatClient;
        }

        var loggingClient = new ToolLoggingChatClient(chatClient, GetAgentName(node), toolCallLogs);
        var funcClient = new FunctionInvokingChatClient(loggingClient);
        funcClient.AdditionalTools = tools;
        return funcClient;
    }

    private static IChatClient? CreateJudgeClient(WorkflowExecutionRequest request)
    {
        if (request.Credentials.TryGetValue(Providers.OpenAI, out var openaiCred) &&
            !string.IsNullOrWhiteSpace(openaiCred.ApiKey))
        {
            return new OpenAIClient(openaiCred.ApiKey)
                .GetChatClient(Defaults.JudgeModel)
                .AsIChatClient();
        }

        if (request.Credentials.TryGetValue(Providers.AzureOpenAI, out var azureCred) &&
            !string.IsNullOrWhiteSpace(azureCred.ApiKey))
        {
            return new AzureOpenAIClient(
                    new Uri(azureCred.Endpoint), new ApiKeyCredential(azureCred.ApiKey))
                .GetChatClient(Defaults.JudgeModel)
                .AsIChatClient();
        }

        return null;
    }

    private IChatClient CreateChatClient(
        WorkflowNode node,
        ConcurrentDictionary<string, object> clientCache,
        ProviderCredential cred,
        string provider)
    {
        var (apiKey, endpoint) = NormalizeCredential(provider, cred.ApiKey, cred.Endpoint ?? "");

        var timeout = TimeSpan.FromMinutes(Timeouts.LlmNetworkTimeoutMinutes);
        var cacheKey = $"{provider}:{endpoint}";
        var baseClient = clientCache.GetOrAdd(cacheKey, _ => CreateLlmClient(provider, apiKey, endpoint, timeout));

        IChatClient chatClient = GetChatClientFromBase(baseClient, node.Model);

        if (node.Temperature.HasValue || node.TopP.HasValue || node.MaxOutputTokens.HasValue)
            chatClient = new ChatOptionsChatClient(chatClient, node.Temperature, node.TopP, node.MaxOutputTokens);

        // 預設啟用基礎 middleware（logging + retry + recovery），確保 context overflow 自動恢復
        var middleware = string.IsNullOrWhiteSpace(node.Middleware) ? "logging,retry,recovery" : node.Middleware;
        return ApplyMiddleware(chatClient, middleware, node.MiddlewareConfig, _piiDetector, _piiTokenVault, _guardRailsPolicy,
            modelName: node.Model);
    }

    /// <summary>
    /// 建立簡易 IChatClient（供 Tuner 等輕量場景使用，不含 middleware/tools）。
    /// </summary>
    public static IChatClient CreateChatClient(string provider, string apiKey, string endpoint, string model, IDictionary<string, object>? cache = null)
    {
        (apiKey, endpoint) = NormalizeCredential(provider, apiKey, endpoint);

        cache ??= new Dictionary<string, object>();
        var cacheKey = $"{provider}:{endpoint}";
        if (!cache.TryGetValue(cacheKey, out var baseClient))
        {
            var timeout = TimeSpan.FromMinutes(Timeouts.LlmNetworkTimeoutMinutes);
            baseClient = CreateLlmClient(provider, apiKey, endpoint, timeout);
            cache[cacheKey] = baseClient;
        }

        return GetChatClientFromBase(baseClient, model);
    }

    /// <summary>
    /// 正規化 provider 的 API Key 和 Endpoint（處理 key-optional + /v1 補正）。
    /// </summary>
    internal static (string ApiKey, string Endpoint) NormalizeCredential(string provider, string apiKey, string endpoint)
    {
        if (Providers.IsKeyOptional(provider) && string.IsNullOrWhiteSpace(apiKey))
            apiKey = Providers.DefaultLocalApiKey;

        if (Providers.RequiresV1Prefix(provider) && !string.IsNullOrWhiteSpace(endpoint)
            && !endpoint.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            endpoint = endpoint.TrimEnd('/') + "/v1";

        return (apiKey, endpoint);
    }

    /// <summary>
    /// 建立底層 LLM client（OpenAIClient / AnthropicClient 等）。
    /// 回傳 object 以支援多型 provider — 呼叫端透過 GetChatClientFromBase 轉換為 IChatClient。
    /// </summary>
    internal static object CreateLlmClient(string provider, string apiKey, string endpoint, TimeSpan timeout)
    {
        return provider switch
        {
            Providers.Anthropic => new AnthropicClient { ApiKey = apiKey },
            Providers.AzureOpenAI => new AzureOpenAIClient(
                new Uri(endpoint), new ApiKeyCredential(apiKey),
                new AzureOpenAIClientOptions { NetworkTimeout = timeout }),
            _ when !string.IsNullOrWhiteSpace(endpoint) => new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint), NetworkTimeout = timeout }),
            _ => new OpenAIClient(new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { NetworkTimeout = timeout })
        };
    }

    /// <summary>
    /// 將底層 LLM client 轉換為 IChatClient — 根據 client 型別選擇正確的轉換方式。
    /// </summary>
    internal static IChatClient GetChatClientFromBase(object baseClient, string model)
    {
        return baseClient switch
        {
            AnthropicClient anthropic => anthropic.AsIChatClient(model),
            OpenAIClient openai => openai.GetChatClient(model).AsIChatClient(),
            _ => throw new NotSupportedException($"Unsupported LLM client type: {baseClient.GetType().Name}")
        };
    }

    private async Task<List<AITool>> ResolveToolsAsync(
        WorkflowNode node,
        WorkflowExecutionRequest request,
        CancellationToken cancellationToken,
        WorkflowPayload? payload = null)
    {
        var tools = node.Tools.Count > 0
            ? new List<AITool>(_toolRegistry.Resolve(node.Tools, request.Credentials))
            : new List<AITool>();

        // MCP + A2A discovery 並行化（同一 agent 內所有 URL 同時發請求）
        var mcpTasks = node.McpServers
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(async url =>
            {
                try
                {
                    return await _mcpClient.GetToolsAsync(url, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[MCP] Connection failed ({McpUrl}): {Error}", url, ex.Message);
                    return [];
                }
            })
            .ToList();

        var a2aTasks = node.A2AAgents
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(async url =>
            {
                try
                {
                    var format = payload?.A2AAgents
                        .FirstOrDefault(a => a.Url == url)?.Format ?? "auto";
                    var card = await _a2aClient.DiscoverAsync(url, format, cancellationToken);
                    return (AITool?)_a2aClient.WrapAsAITool(card, format);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[A2A] Connection failed ({A2AUrl}): {Error}", url, ex.Message);
                    return null;
                }
            })
            .ToList();

        await Task.WhenAll(mcpTasks.Cast<Task>().Concat(a2aTasks));

        foreach (var task in mcpTasks)
        {
            foreach (var t in task.Result) tools.Add(t);
        }

        foreach (var task in a2aTasks)
        {
            if (task.Result is not null) tools.Add(task.Result);
        }

        if (request.HttpApiDefs != null)
        {
            foreach (var apiId in node.HttpApis)
            {
                if (request.HttpApiDefs.TryGetValue(apiId, out var apiDef))
                    tools.Add(_httpApiTool.WrapAsAITool(apiDef));
            }
        }

        return tools;
    }

    /// <summary>
    /// 根據逗號分隔的 middleware 字串，以裝飾者模式依序包裝 IChatClient。
    /// </summary>
    public static IChatClient ApplyMiddleware(IChatClient client, string? middleware,
        Dictionary<string, Dictionary<string, string>>? config = null,
        IPiiDetector? piiDetector = null,
        IPiiTokenVault? piiTokenVault = null,
        IGuardRailsPolicy? guardRailsPolicy = null,
        IContextCompactor? contextCompactor = null,
        CompressionState? compressionState = null,
        string? modelName = null)
    {
        if (string.IsNullOrWhiteSpace(middleware))
        {
            return client;
        }

        var items = middleware.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var set = new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);

        if (set.Contains("logging"))
        {
            client = new AgentLoggingChatClient(client);
        }

        if (set.Contains("retry"))
        {
            client = new RetryChatClient(client);
        }

        if (set.Contains("recovery"))
        {
            var recoveryConfig = config?.GetValueOrDefault("recovery");
            var recoveryOptions = RecoveryOptions.FromConfig(recoveryConfig);
            if (contextCompactor is not null)
                recoveryOptions = recoveryOptions with { ContextCompactor = contextCompactor };
            if (compressionState is not null)
                recoveryOptions = recoveryOptions with { CompressionState = compressionState };
            if (modelName is not null)
                recoveryOptions = recoveryOptions with
                {
                    ProactiveCompressionThreshold = (int)ModelContextWindows.GetCompressionThreshold(modelName)
                };

            client = new RecoveryChatClient(client, recoveryOptions);
        }

        if (set.Contains("ratelimit"))
        {
            client = new RateLimitChatClient(client);
        }

        if (set.Contains("pii"))
        {
            var piiConfig = config?.GetValueOrDefault("pii");
            if (piiDetector is not null)
            {
                // 只有前端指定了 locales 或 customRules 時才建新 detector，否則重用 DI 注入的全域實例
                var needsCustomDetector = piiConfig is not null &&
                    (piiConfig.ContainsKey("locales") || piiConfig.ContainsKey("customRules"));
                var detector = needsCustomDetector
                    ? RegexPiiDetector.FromConfig(piiConfig)
                    : piiDetector;
                var options = PiiMaskingOptions.FromConfig(piiConfig);
                var vault = options.DetokenizeOutput ? piiTokenVault : null;
                client = new PiiMaskingChatClient(client, detector, vault, options);
            }
            else
            {
                // 舊版向下相容
                client = new PiiMaskingChatClient(client, piiConfig);
            }
        }

        if (set.Contains("guardrails"))
        {
            var grConfig = config?.GetValueOrDefault("guardrails");
            if (guardRailsPolicy is not null)
            {
                var needsCustomPolicy = grConfig is not null &&
                    (grConfig.ContainsKey("blockedTerms") || grConfig.ContainsKey("regexRules") ||
                     grConfig.ContainsKey("allowedTopics") || grConfig.ContainsKey("enableInjectionDetection"));
                var policy = needsCustomPolicy
                    ? DefaultGuardRailsPolicy.FromConfig(grConfig)
                    : guardRailsPolicy;
                var grOptions = GuardRailsOptions.FromConfig(grConfig);
                client = new GuardRailsChatClient(client, policy, grOptions);
            }
            else
            {
                client = new GuardRailsChatClient(client, grConfig);
            }
        }

        return client;
    }

    /// <summary>
    /// 根據使用者指令和輸出格式建構完整的 system prompt。
    /// </summary>
    public static string BuildInstructions(string? userInstructions, string? outputFormat = null)
    {
        var baseInstructions = string.IsNullOrWhiteSpace(userInstructions)
            ? "You are a helpful assistant."
            : userInstructions;

        // 偵測非中文語言指令，在 prompt 前後加強語言強制
        var langPrefix = DetectLanguageEnforcement(baseInstructions);

        var result = langPrefix is not null
            ? $"{langPrefix}\n\n{baseInstructions}\n\nCurrent date: {DateTime.Now:yyyy-MM-dd}. Always use the current year when searching.\n\n{langPrefix}"
            : $"{baseInstructions}\n\nCurrent date: {DateTime.Now:yyyy-MM-dd}. Always use the current year when searching.";

        if ((outputFormat == "json" || outputFormat == "json_schema") &&
            !result.Contains("json", StringComparison.OrdinalIgnoreCase))
            result += "\n\nRespond in JSON format.";
        return result;
    }

    /// <summary>
    /// 偵測 instructions 中的語言輸出要求，回傳目標語言的強制前綴。
    /// 僅在要求非中文語言時觸發（中文是預設，不需要額外強制）。
    /// </summary>
    private static string? DetectLanguageEnforcement(string instructions)
    {
        // 常見語言對應：(偵測關鍵字, 目標語言前綴)
        // enforcement 必須夠強：(1) 多語言重複指示 (2) 明確說明 input 可能是其他語言 (3) 用目標語言寫
        ReadOnlySpan<(string keyword, string enforcement)> langRules =
        [
            ("日文", "[CRITICAL] You MUST respond ENTIRELY in Japanese (日本語). The input may be in Chinese or other languages — ignore the input language and write your ENTIRE response in Japanese. 入力が中国語であっても、必ず全文を日本語で出力してください。中国語での出力は禁止です。"),
            ("日本語", "[CRITICAL] You MUST respond ENTIRELY in Japanese (日本語). The input may be in Chinese or other languages — ignore the input language and write your ENTIRE response in Japanese. 入力が中国語であっても、必ず全文を日本語で出力してください。中国語での出力は禁止です。"),
            ("Japanese", "[CRITICAL] You MUST respond ENTIRELY in Japanese (日本語). The input may be in Chinese or other languages — ignore the input language and write your ENTIRE response in Japanese. 入力が中国語であっても、必ず全文を日本語で出力してください。中国語での出力は禁止です。"),
            ("英文", "[CRITICAL] You MUST respond ENTIRELY in English. The input may be in Chinese or other languages — ignore the input language and write your ENTIRE response in English. Do NOT use Chinese in your response."),
            ("English", "[CRITICAL] You MUST respond ENTIRELY in English. The input may be in Chinese or other languages — ignore the input language and write your ENTIRE response in English. Do NOT use Chinese in your response."),
            ("韓文", "[CRITICAL] You MUST respond ENTIRELY in Korean (한국어). The input may be in other languages — ignore the input language and write your ENTIRE response in Korean. 입력이 다른 언어라도 반드시 전체 답변을 한국어로 작성하세요."),
            ("韓語", "[CRITICAL] You MUST respond ENTIRELY in Korean (한국어). The input may be in other languages — ignore the input language and write your ENTIRE response in Korean. 입력이 다른 언어라도 반드시 전체 답변을 한국어로 작성하세요."),
            ("Korean", "[CRITICAL] You MUST respond ENTIRELY in Korean (한국어). The input may be in other languages — ignore the input language and write your ENTIRE response in Korean. 입력이 다른 언어라도 반드시 전체 답변을 한국어로 작성하세요."),
            ("法文", "[CRITICAL] You MUST respond ENTIRELY in French (Français). Ignore the input language. Veuillez rédiger l'intégralité de votre réponse en français."),
            ("French", "[CRITICAL] You MUST respond ENTIRELY in French (Français). Ignore the input language. Veuillez rédiger l'intégralité de votre réponse en français."),
            ("德文", "[CRITICAL] You MUST respond ENTIRELY in German (Deutsch). Ignore the input language. Bitte schreiben Sie Ihre gesamte Antwort auf Deutsch."),
            ("German", "[CRITICAL] You MUST respond ENTIRELY in German (Deutsch). Ignore the input language. Bitte schreiben Sie Ihre gesamte Antwort auf Deutsch."),
            ("西班牙文", "[CRITICAL] You MUST respond ENTIRELY in Spanish (Español). Ignore the input language. Escriba toda su respuesta en español."),
            ("Spanish", "[CRITICAL] You MUST respond ENTIRELY in Spanish (Español). Ignore the input language. Escriba toda su respuesta en español."),
        ];

        foreach (var (keyword, enforcement) in langRules)
        {
            if (instructions.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return enforcement;
            }
        }

        return null;
    }

    /// <summary>
    /// 根據使用者指令、Skill 和輸出格式建構完整的 system prompt。
    /// 合併順序：Flow Skills → Agent Instructions → Agent Skills → 系統附加。
    /// </summary>
    public static string BuildInstructions(
        string? userInstructions,
        string? outputFormat,
        List<string> flowSkillIds,
        List<string> nodeSkillIds,
        SkillRegistryService skillRegistry,
        List<Data.SkillDocument>? customSkills = null)
    {
        var allSkillIds = flowSkillIds.Concat(nodeSkillIds).Distinct().ToList();
        if (allSkillIds.Count == 0)
        {
            return BuildInstructions(userInstructions, outputFormat);
        }

        var flowSkills = skillRegistry.Resolve(flowSkillIds, customSkills);
        var nodeSkills = skillRegistry.Resolve(nodeSkillIds, customSkills);
        var sb = new System.Text.StringBuilder();

        // 1. Flow-level skills（全局規範）
        foreach (var skill in flowSkills)
        {
            sb.AppendLine($"[Skill: {skill.DisplayName}]");
            sb.AppendLine(skill.Instructions.Trim());
            sb.AppendLine();
        }

        // 2. Agent 自己的 instructions（最高優先）
        var baseInstructions = string.IsNullOrWhiteSpace(userInstructions)
            ? "You are a helpful assistant."
            : userInstructions;
        sb.AppendLine(baseInstructions);
        sb.AppendLine();

        // 3. Agent-level skills（專業補充）
        foreach (var skill in nodeSkills)
        {
            // 避免重複（已在 flow level 出現的 skill）
            if (flowSkillIds.Contains(skill.Id))
            {
                continue;
            }

            sb.AppendLine($"[Skill: {skill.DisplayName}]");
            sb.AppendLine(skill.Instructions.Trim());
            sb.AppendLine();
        }

        // 4. Few-shot examples
        var allSkills = flowSkills.Concat(nodeSkills).DistinctBy(s => s.Id);
        foreach (var skill in allSkills)
        {
            if (skill.FewShotExamples is { Count: > 0 })
            {
                sb.AppendLine($"[Examples for {skill.DisplayName}]");
                foreach (var ex in skill.FewShotExamples)
                {
                    sb.AppendLine($"User: {ex.User}");
                    sb.AppendLine($"Assistant: {ex.Assistant}");
                    sb.AppendLine();
                }
            }
        }

        // 5. 語言強制（偵測非中文語言指令，在 prompt 前後加強）
        var langEnforcement = DetectLanguageEnforcement(baseInstructions);
        if (langEnforcement is not null)
        {
            sb.Insert(0, langEnforcement + "\n\n");
            sb.AppendLine();
            sb.AppendLine(langEnforcement);
        }

        // 6. 系統附加
        sb.AppendLine($"Current date: {DateTime.Now:yyyy-MM-dd}. Always use the current year when searching.");

        var result = sb.ToString().TrimEnd();
        if ((outputFormat == "json" || outputFormat == "json_schema") &&
            !result.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            result += "\n\nRespond in JSON format.";
        }

        return result;
    }

    /// <summary>
    /// 建構可快取版本的系統提示詞（簡易版），將靜態/動態部分分離以支援 prefix caching。
    /// 在 "Current date: " 處切割：靜態部分（指令 + 語言 + 格式）可跨 session 緩存，
    /// 動態部分（日期 + 語言尾綴）每輪重算。
    /// </summary>
    public static CacheableSystemPrompt BuildCacheableInstructions(
        string? userInstructions, string? outputFormat = null)
    {
        var fullText = BuildInstructions(userInstructions, outputFormat);
        return SplitAtDynamicBoundary(fullText);
    }

    /// <summary>
    /// 建構可快取版本的系統提示詞（含 Skill），將靜態/動態部分分離以支援 prefix caching。
    /// </summary>
    public static CacheableSystemPrompt BuildCacheableInstructions(
        string? userInstructions,
        string? outputFormat,
        List<string> flowSkillIds,
        List<string> nodeSkillIds,
        SkillRegistryService skillRegistry,
        List<Data.SkillDocument>? customSkills = null)
    {
        var fullText = BuildInstructions(userInstructions, outputFormat, flowSkillIds, nodeSkillIds, skillRegistry, customSkills);
        return SplitAtDynamicBoundary(fullText);
    }

    /// <summary>
    /// 在 "Current date: " 標記處切割 system prompt 為靜態/動態兩部分。
    /// StaticPart 保留原始尾部換行，ToFullText() 直接串接可完美重建原始文字。
    /// </summary>
    internal static CacheableSystemPrompt SplitAtDynamicBoundary(string fullText)
    {
        const string marker = "Current date: ";
        var idx = fullText.IndexOf(marker, StringComparison.Ordinal);
        if (idx <= 0)
        {
            return new CacheableSystemPrompt(fullText);
        }

        return new CacheableSystemPrompt(fullText[..idx], fullText[idx..]);
    }

    /// <summary>
    /// 建構含可選附件的使用者訊息。
    /// </summary>
    public static ChatMessage BuildUserMessage(string text, FileAttachment? attachment)
    {
        var msg = new ChatMessage(ChatRole.User, text);
        if (attachment is { Data.Length: > 0 })
            msg.Contents.Add(new DataContent(attachment.Data, attachment.MimeType));
        return msg;
    }

    private static string GetAgentName(WorkflowNode node) =>
        string.IsNullOrWhiteSpace(node.Name) ? $"Agent_{node.Id}" : node.Name;

    public static string NormalizeProvider(string provider) => provider?.ToLowerInvariant() switch
    {
        "azureopenai" or "azure-openai" or "azure_openai" => Providers.AzureOpenAI,
        "openai" or null or "" => Providers.OpenAI,
        var p => p!
    };
}
