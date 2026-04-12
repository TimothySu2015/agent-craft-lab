using System.ClientModel;
using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Strategies;
using AgentCraftLab.Search.Abstractions;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// Workflow 前處理器 — 從 WorkflowExecutionService 抽出的職責：
/// 節點分類、附件處理、RAG 前處理、AgentContext 建構與 enrichment。
/// </summary>
public class WorkflowPreprocessor
{
    private readonly ToolRegistryService _toolRegistry;
    private readonly SkillRegistryService _skillRegistry;
    private readonly Data.ISkillStore _skillStore;
    private readonly IUserContext _userContext;
    private readonly McpClientService _mcpClient;
    private readonly A2AClientService _a2aClient;
    private readonly HttpApiToolService _httpApiTool;
    private readonly RagService _ragService;
    private readonly ISearchEngine? _searchEngine;
    private readonly HumanInputBridge _humanBridge;
    private readonly IAutonomousNodeExecutor? _autonomousExecutor;
    private readonly Data.IKnowledgeBaseStore _kbStore;
    private readonly ILogger _logger;

    /// <summary>DI 建構子。</summary>
    public WorkflowPreprocessor(
        ToolRegistryService toolRegistry,
        SkillRegistryService skillRegistry,
        Data.ISkillStore skillStore,
        IUserContext userContext,
        McpClientService mcpClient,
        A2AClientService a2aClient,
        HttpApiToolService httpApiTool,
        RagService ragService,
        HumanInputBridge humanBridge,
        Data.IKnowledgeBaseStore kbStore,
        ILogger<WorkflowPreprocessor> logger,
        IAutonomousNodeExecutor? autonomousExecutor = null,
        ISearchEngine? searchEngine = null)
        : this(toolRegistry, skillRegistry, skillStore, userContext, mcpClient, a2aClient,
            httpApiTool, ragService, humanBridge, kbStore, (ILogger)logger, autonomousExecutor, searchEngine) { }

    /// <summary>內部建構子（接受泛型 ILogger，供向後相容建構使用）。</summary>
    internal WorkflowPreprocessor(
        ToolRegistryService toolRegistry,
        SkillRegistryService skillRegistry,
        Data.ISkillStore skillStore,
        IUserContext userContext,
        McpClientService mcpClient,
        A2AClientService a2aClient,
        HttpApiToolService httpApiTool,
        RagService ragService,
        HumanInputBridge humanBridge,
        Data.IKnowledgeBaseStore kbStore,
        ILogger logger,
        IAutonomousNodeExecutor? autonomousExecutor = null,
        ISearchEngine? searchEngine = null)
    {
        _toolRegistry = toolRegistry;
        _skillRegistry = skillRegistry;
        _skillStore = skillStore;
        _userContext = userContext;
        _mcpClient = mcpClient;
        _a2aClient = a2aClient;
        _httpApiTool = httpApiTool;
        _ragService = ragService;
        _searchEngine = searchEngine;
        _humanBridge = humanBridge;
        _kbStore = kbStore;
        _logger = logger;
        _autonomousExecutor = autonomousExecutor;
    }

    /// <summary>
    /// 前處理結果 — Schema 導向。<see cref="AllAgentNodes"/> 和 <see cref="ResolvedConnections"/>
    /// 在 PrepareAsync 內部透過 <see cref="WorkflowNodeConverter"/> 轉換為 Schema 型別。
    /// </summary>
    public record PreprocessResult(
        AgentExecutionContext Context,
        List<AgentCraftLab.Engine.Models.Schema.AgentNode> AllAgentNodes,
        List<AgentCraftLab.Engine.Models.Schema.Connection> ResolvedConnections,
        bool HasA2AOrAutonomousNodes);

    /// <summary>
    /// 執行前處理：節點分類 → 附件處理 → RAG → 建構 AgentContext → enrichment → 解析連線。
    /// yield return ExecutionEvent 用於回報進度或錯誤。最後一個 yield 包含 PreprocessResult。
    /// </summary>
    public async IAsyncEnumerable<(ExecutionEvent? Event, PreprocessResult? Result)> PrepareAsync(
        Schema.WorkflowPayload payload,
        WorkflowExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 1. 篩選節點（pattern match on NodeConfig 子型別）
        var agentNodes = payload.Nodes.OfType<Schema.AgentNode>().ToList();
        var a2aAgentNodes = payload.Nodes.OfType<Schema.A2AAgentNode>().ToList();
        var autonomousNodes = payload.Nodes.OfType<Schema.AutonomousNode>().ToList();

        // Autonomous 節點預設帶入所有可用工具
        var autonomousWithTools = autonomousNodes;
        if (autonomousNodes.Count > 0)
        {
            var allToolIds = _toolRegistry.GetAvailableTools().Select(t => t.Id).ToList();
            autonomousWithTools = autonomousNodes
                .Select(n => n.Tools.Count == 0 ? n with { Tools = allToolIds } : n)
                .ToList();
        }

        // 驗證：至少要有一個可執行節點
        var hasExecutableNodes = payload.Nodes.Any(NodeTypeRegistry.IsExecutable);

        if (!hasExecutableNodes)
        {
            yield return (ExecutionEvent.Error("Workflow has no executable nodes."), null);
            yield break;
        }

        // HttpApiDefs fallback
        if (request.HttpApiDefs is null or { Count: 0 } && payload.Resources.HttpApis.Count > 0)
            request.HttpApiDefs = new Dictionary<string, Models.HttpApiDefinition>(payload.Resources.HttpApis);

        // ZIP 附件處理
        if (request.Attachment is { Data.Length: > 0 } zipAtt
            && zipAtt.MimeType is "application/zip" or "application/x-zip-compressed")
        {
            var tempDir = Path.Combine(Path.GetTempPath(), TempPaths.ZipFolder);
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{zipAtt.FileName}");
            File.WriteAllBytes(zipPath, zipAtt.Data);

            request.UserMessage = string.IsNullOrWhiteSpace(request.UserMessage)
                ? $"使用者上傳了 ZIP 檔案，暫存路徑為：{zipPath}\n請先使用解壓縮工具處理此檔案，再進行後續分析。"
                : $"{request.UserMessage}\n\n[系統提示] 使用者上傳的 ZIP 檔案暫存路徑為：{zipPath}";
            request.Attachment = null;
        }

        // 2. RAG 前處理
        RagContext? ragContext = null;
        var ragNodes = payload.Nodes.OfType<Schema.RagNode>().ToList();
        if (ragNodes.Count > 0)
        {
            await foreach (var evt in PrepareRagAsync(ragNodes, payload.Connections, request, cancellationToken))
            {
                if (evt.ragContext is not null)
                {
                    ragContext = evt.ragContext;
                    request.Attachment = null;
                }
                else if (evt.executionEvent is not null)
                {
                    yield return (evt.executionEvent, null);
                    if (evt.executionEvent.Type == EventTypes.Error) yield break;
                }
            }
        }

        // 3. 載入使用者自訂 Skill
        var userId = await _userContext.GetUserIdAsync();
        var customSkills = await _skillStore.ListAsync(userId);

        // 4. 建構 AgentContext
        AgentExecutionContext agentContext;
        if (agentNodes.Count > 0)
        {
            var contextBuilder = new AgentContextBuilder(
                _toolRegistry, _skillRegistry, _mcpClient, _a2aClient, _httpApiTool, _ragService, _logger);
            var (ctx, buildError) = await contextBuilder.BuildAsync(
                agentNodes, request, cancellationToken, ragContext, payload, customSkills);
            if (ctx is null)
            {
                yield return (ExecutionEvent.Error(buildError!), null);
                yield break;
            }

            agentContext = ctx;
        }
        else
        {
            agentContext = AgentExecutionContext.Empty;
        }

        // 5. Context enrichment
        if (a2aAgentNodes.Count > 0)
        {
            var a2aNodeMap = a2aAgentNodes.ToDictionary(n => n.Id, n => n);
            agentContext = agentContext with { A2ANodes = a2aNodeMap, A2AClient = _a2aClient };
        }

        if (payload.Nodes.OfType<Schema.HumanNode>().Any())
            agentContext = agentContext with { HumanBridge = _humanBridge };

        if (request.DebugBridge is not null)
            agentContext = agentContext with { DebugBridge = request.DebugBridge };

        if (payload.Nodes.OfType<Schema.HttpRequestNode>().Any())
            agentContext = agentContext with { HttpApiService = _httpApiTool, HttpApiDefs = request.HttpApiDefs };

        if (autonomousWithTools.Count > 0)
            agentContext = agentContext with { AutonomousExecutor = _autonomousExecutor };

        // 6. 解析連線 — Schema.Connection 已是目標型別，直接過濾 executable 節點
        var executableNodeIds = new HashSet<string>(agentContext.Agents.Keys);
        if (agentContext.A2ANodes is not null)
            executableNodeIds.UnionWith(agentContext.A2ANodes.Keys);
        executableNodeIds.UnionWith(payload.Nodes
            .Where(n => NodeTypeRegistry.IsExecutable(n) && !NodeTypeRegistry.IsAgentLike(n))
            .Select(n => n.Id));

        var resolvedConnections = WorkflowGraphHelper.ResolveAgentConnections(
            payload.Connections.ToList(), executableNodeIds);

        yield return (null, new PreprocessResult(
            agentContext, agentNodes, resolvedConnections,
            a2aAgentNodes.Count > 0 || autonomousWithTools.Count > 0));
    }

    private async IAsyncEnumerable<(ExecutionEvent? executionEvent, RagContext? ragContext)> PrepareRagAsync(
        List<Schema.RagNode> ragNodes,
        IReadOnlyList<Schema.Connection> connections,
        WorkflowExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var hasAttachment = request.Attachment is { Data.Length: > 0 };
        var knowledgeBaseIds = ragNodes
            .SelectMany(n => n.KnowledgeBaseIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (!hasAttachment && knowledgeBaseIds.Count == 0) yield break;

        var ragSettings = ragNodes[0].Rag;
        var embeddingModel = ragSettings.EmbeddingModel;
        var knowledgeBaseIndexNames = new List<string>();
        var indexDataSourceMap = new Dictionary<string, string?>();

        if (knowledgeBaseIds.Count > 0)
        {
            var kbTasks = knowledgeBaseIds.Select(id => _kbStore.GetAsync(id));
            var kbResults = await Task.WhenAll(kbTasks);
            var kbDocs = kbResults.Where(kb => kb is not null && !kb.IsDeleted).ToList();

            foreach (var kb in kbDocs)
            {
                knowledgeBaseIndexNames.Add(kb!.IndexName);
                indexDataSourceMap[kb.IndexName] = kb.DataSourceId;
            }
            if (kbDocs.Count > 0) embeddingModel = kbDocs[0]!.EmbeddingModel;
        }

        var embeddingGenerator = CreateEmbeddingGenerator(request, embeddingModel);
        if (embeddingGenerator is null)
        {
            yield return (ExecutionEvent.Error(
                "No API Key configured for embedding generation. Please set OpenAI or Azure credentials."), null);
            yield break;
        }

        var indexName = "";
        if (hasAttachment)
        {
            var userId = await _userContext.GetUserIdAsync();
            indexName = $"{userId}_rag_{Guid.NewGuid():N}";
            await foreach (var evt in _ragService.IngestAsync(
                request.Attachment!, ragSettings, embeddingGenerator, indexName,
                cancellationToken: cancellationToken))
            {
                yield return (evt, null);
            }
        }

        if (knowledgeBaseIndexNames.Count > 0)
        {
            yield return (ExecutionEvent.RagProcessing(
                $"Using {knowledgeBaseIndexNames.Count} knowledge base(s) for RAG search"), null);
        }

        yield return (null, new RagContext
        {
            RagNodes = ragNodes,
            WorkflowConnections = connections.ToList(),
            EmbeddingGenerator = embeddingGenerator,
            SearchEngine = _searchEngine,
            IndexName = indexName,
            KnowledgeBaseIndexNames = knowledgeBaseIndexNames,
            IndexDataSourceMap = indexDataSourceMap
        });
    }


    internal static IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(
        WorkflowExecutionRequest request, string embeddingModel)
    {
        if (request.Credentials.TryGetValue(Providers.OpenAI, out var openaiCred)
            && !string.IsNullOrWhiteSpace(openaiCred.ApiKey))
        {
            return new OpenAIClient(openaiCred.ApiKey)
                .GetEmbeddingClient(embeddingModel)
                .AsIEmbeddingGenerator();
        }

        if (request.Credentials.TryGetValue(Providers.AzureOpenAI, out var azureCred)
            && !string.IsNullOrWhiteSpace(azureCred.ApiKey))
        {
            return new AzureOpenAIClient(
                    new Uri(azureCred.Endpoint), new ApiKeyCredential(azureCred.ApiKey))
                .GetEmbeddingClient(embeddingModel)
                .AsIEmbeddingGenerator();
        }

        return null;
    }
}
