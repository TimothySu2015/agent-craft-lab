using System.ComponentModel;
using System.Text.Json;
using AgentCraftLab.Autonomous.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>Meta-tool 分層等級。</summary>
public enum MetaToolTier
{
    /// <summary>核心（永遠可用）：shared_state, list_sub_agents。</summary>
    Core,
    /// <summary>搜尋入口（永遠可用）：search_tools, load_tools。</summary>
    Discovery,
    /// <summary>委派（按需載入）：spawn, collect, create/ask sub-agent。</summary>
    Delegation,
    /// <summary>協作（按需載入）：peer_review, challenge。</summary>
    Collaboration,
    /// <summary>創建（按需載入）：create_tool。</summary>
    Creation
}

/// <summary>
/// Meta-tool 註冊表 — 根據執行環境條件式註冊 meta-tool，
/// 供 Orchestrator 管理 sub-agent、共享狀態和使用者互動。
/// </summary>
public sealed class MetaToolFactory
{
    // Meta-tool 名稱常數（唯一真相來源，ReactExecutor 偵測時引用）
    public const string CreateSubAgent = "create_sub_agent";
    public const string AskSubAgent = "ask_sub_agent";
    public const string ListSubAgents = "list_sub_agents";
    public const string SetSharedState = "set_shared_state";
    public const string GetSharedState = "get_shared_state";
    public const string AskUser = "ask_user";
    public const string SpawnSubAgent = "spawn_sub_agent";
    public const string CollectResults = "collect_results";
    public const string StopSpawn = "stop_spawn";
    public const string SendToSpawn = "send_to_spawn";
    public const string RequestPeerReview = "request_peer_review";
    public const string ChallengeAssertion = "challenge_assertion";
    public const string SearchTools = "search_tools";
    public const string LoadTools = "load_tools";
    public const string CreateTool = "create_tool";

    /// <summary>Meta-tool 分層分類（唯一真相來源）。</summary>
    public static readonly IReadOnlyDictionary<string, MetaToolTier> TierMap = new Dictionary<string, MetaToolTier>
    {
        // Core：永遠可用
        [SetSharedState] = MetaToolTier.Core,
        [GetSharedState] = MetaToolTier.Core,
        [ListSubAgents] = MetaToolTier.Core,
        // Discovery：永遠可用（搜尋/載入入口）
        [SearchTools] = MetaToolTier.Discovery,
        [LoadTools] = MetaToolTier.Discovery,
        // Delegation：按需載入
        [SpawnSubAgent] = MetaToolTier.Delegation,
        [CollectResults] = MetaToolTier.Delegation,
        [StopSpawn] = MetaToolTier.Delegation,
        [SendToSpawn] = MetaToolTier.Delegation,
        [CreateSubAgent] = MetaToolTier.Delegation,
        [AskSubAgent] = MetaToolTier.Delegation,
        [AskUser] = MetaToolTier.Delegation,
        // Collaboration：按需載入
        [RequestPeerReview] = MetaToolTier.Collaboration,
        [ChallengeAssertion] = MetaToolTier.Collaboration,
        // Creation：按需載入
        [CreateTool] = MetaToolTier.Creation,
    };

    /// <summary>已註冊的 meta-tool 清單（唯讀對外暴露）。</summary>
    public IList<AITool> Tools => _tools;

    private readonly List<AITool> _tools;

    /// <summary>已註冊的 meta-tool 名稱集合，用於 O(1) 快速查詢。</summary>
    private readonly HashSet<string> _registeredNames;

    /// <summary>
    /// 建立 meta-tool 註冊表，依序註冊各 meta-tool。
    /// ask_user 僅在 askUserContext 不為 null 時才註冊。
    /// toolSearchIndex/dynamicToolSet 不為 null 時註冊 search_tools / load_tools。
    /// </summary>
    public MetaToolFactory(
        AgentPool agentPool,
        SharedStateStore sharedState,
        IList<AITool> orchestratorTools,
        AskUserContext? askUserContext = null,
        ToolSearchIndex? toolSearchIndex = null,
        ToolCreator? toolCreator = null,
        DynamicToolSet? dynamicToolSet = null)
    {
        _tools =
        [
            AIFunctionFactory.Create(
                [Description("Create a sub-agent to handle a specific subtask. The sub-agent can only use tools from the orchestrator's tool set.")]
                (
                    [Description("Unique name for the sub-agent (e.g., 'nvidia_researcher')")] string name,
                    [Description("Instructions describing the sub-agent's role and task")] string instructions,
                    [Description("List of tool IDs from the available tools list")] string[]? tools = null,
                    [Description("Override model (null = inherit from orchestrator)")] string? model = null
                ) =>
                {
                    var spec = new SubAgentSpec
                    {
                        Name = name,
                        Instructions = instructions,
                        Tools = tools?.ToList() ?? [],
                        Model = model
                    };
                    return agentPool.Create(spec);
                },
                CreateSubAgent),

            AIFunctionFactory.Create(
                [Description("Send a message to a sub-agent and get its response. The sub-agent will use its tools autonomously to answer.")]
                async (
                    [Description("Name of the sub-agent to ask")] string name,
                    [Description("The question or instruction for the sub-agent")] string message,
                    CancellationToken cancellationToken
                ) =>
                {
                    return await agentPool.AskAsync(name, message, cancellationToken);
                },
                AskSubAgent),

            AIFunctionFactory.Create(
                [Description("List all active sub-agents and their status.")]
                () =>
                {
                    return agentPool.List();
                },
                ListSubAgents),

            AIFunctionFactory.Create(
                [Description("Set a key-value pair in the shared state, accessible by all agents.")]
                (
                    [Description("The state key")] string key,
                    [Description("The state value")] string value
                ) =>
                {
                    var success = sharedState.Set(key, value, "orchestrator");
                    return success
                        ? $"Shared state '{key}' set successfully."
                        : $"Error: Cannot overwrite '{key}' — it is owned by orchestrator.";
                },
                SetSharedState),

            AIFunctionFactory.Create(
                [Description("Get shared state. If key is provided, returns that specific value. If key is null/empty, returns all shared state.")]
                (
                    [Description("The state key to retrieve (null or empty = list all)")] string? key = null
                ) =>
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        var all = sharedState.List();
                        if (all.Count == 0)
                        {
                            return "Shared state is empty.";
                        }

                        return JsonSerializer.Serialize(
                            all.ToDictionary(kv => kv.Key, kv => new { kv.Value.Value, kv.Value.SetBy, kv.Value.UpdatedAt }),
                            new JsonSerializerOptions { WriteIndented = true });
                    }

                    var entry = sharedState.Get(key);
                    return entry is not null
                        ? $"{entry.Key} = {entry.Value} (set by {entry.SetBy} at {entry.UpdatedAt:HH:mm:ss})"
                        : $"Key '{key}' not found in shared state.";
                },
                GetSharedState),
        ];

        // ask_user 僅在有使用者互動橋接時才註冊
        if (askUserContext is not null)
        {
            _tools.Add(AIFunctionFactory.Create(
                [Description("Ask the user a clarifying question. Use this when the goal is ambiguous or you need the user to choose between options. The execution will pause until the user responds.")]
                (
                    [Description("The question to ask the user")] string question,
                    [Description("Input type: 'text' for free-form, 'choice' for multiple choice, 'approval' for yes/no")] string inputType,
                    [Description("Comma-separated choices (only for inputType='choice', e.g. 'Python,Rust,Go')")] string? choices = null
                ) =>
                {
                    var validTypes = new HashSet<string> { "text", "choice", "approval" };
                    var safeType = validTypes.Contains(inputType ?? "") ? inputType! : "text";
                    askUserContext.RequestInput(question, safeType, choices);
                    return $"[Waiting for user input: {question}]";
                },
                AskUser));
        }

        // Spawn 模式 — 一步建立臨時 worker + 背景執行，不需先 create_sub_agent
        _tools.Add(AIFunctionFactory.Create(
            [Description(
                "Spawn a one-shot background worker to complete a task. " +
                "Returns immediately — NO need to call create_sub_agent first. " +
                "Each spawn creates an isolated worker with its own tools and context. " +
                "Spawn multiple workers for parallel tasks, then call collect_results. " +
                "Use stop_spawn to cancel a running worker early.")]
            (
                [Description("The task to complete")] string task,
                [Description("Tool IDs for this worker (e.g., ['AzureWebSearch']). If omitted, safe read-only tools are inherited.")] string[]? tools = null,
                [Description("Model override (default: gpt-4o-mini)")] string? model = null,
                [Description("Short label for identification (e.g., 'nvidia_research'). If omitted, auto-generated from task.")] string? label = null,
                [Description("Timeout in seconds. 0 = no timeout. Default: 120s.")] int timeoutSeconds = 120
            ) =>
            {
                try
                {
                    return agentPool.SpawnTask(task, tools, model, label, timeoutSeconds == 120 ? null : timeoutSeconds);
                }
                catch (Exception ex)
                {
                    return $"Error spawning worker: {ex.GetType().Name}: {ex.Message}";
                }
            },
            SpawnSubAgent));

        _tools.Add(AIFunctionFactory.Create(
            [Description(
                "Wait for spawned workers to finish and collect their results. " +
                "If runIds is provided, only waits for and collects those specific workers (others remain active). " +
                "If runIds is omitted, waits for ALL spawned workers. " +
                "Returns a JSON array of objects, each with: id, label, status (completed/timed_out/stopped/failed), runtimeSeconds, and result. " +
                "Call this after spawning multiple workers.")]
            async (
                [Description("Optional: specific spawn IDs to collect (e.g., ['spawn-a1b2c3']). If omitted, collects ALL.")] string[]? runIds = null,
                CancellationToken cancellationToken = default
            ) =>
            {
                return await agentPool.CollectSpawnResultsAsync(runIds, cancellationToken);
            },
            CollectResults));

        _tools.Add(AIFunctionFactory.Create(
            [Description(
                "Stop a specific running spawn worker by its ID. " +
                "Use list_sub_agents to see running workers and their IDs. " +
                "Stopped workers will report 'stopped' status in collect_results.")]
            (
                [Description("The spawn worker ID (e.g., 'spawn-a1b2c3')")] string runId
            ) =>
            {
                return agentPool.StopSpawn(runId);
            },
            StopSpawn));

        _tools.Add(AIFunctionFactory.Create(
            [Description(
                "Send an additional message to a running spawn worker. " +
                "The message will be delivered after the worker's current LLM call completes. " +
                "Use this to provide follow-up instructions, corrections, or additional context. " +
                "If the worker has already completed, the message will NOT be delivered.")]
            (
                [Description("The spawn worker ID (e.g., 'spawn-a1b2c3')")] string runId,
                [Description("The message to send to the worker")] string message
            ) =>
            {
                return agentPool.SendToSpawn(runId, message);
            },
            SendToSpawn));

        // Sub-agent 對等協作工具 — 讓 sub-agent 互相審查與質詢
        _tools.Add(AIFunctionFactory.Create(
            [Description("Request another sub-agent to review and verify findings. Use this to cross-check important conclusions.")]
            async (
                [Description("Name of the sub-agent whose findings need review")] string sourceName,
                [Description("Name of the sub-agent who will perform the review")] string reviewerName,
                [Description("The findings or conclusions to be reviewed")] string findings,
                CancellationToken cancellationToken
            ) =>
            {
                var reviewPrompt = $"[Peer Review Request from {sourceName}]\n\n" +
                                   $"Please review these findings for accuracy, logical errors, or missing considerations:\n\n" +
                                   $"{findings}\n\n" +
                                   $"Provide your assessment: agree, disagree (with reasons), or suggest improvements.";
                return await agentPool.AskAsync(reviewerName, reviewPrompt, cancellationToken);
            },
            RequestPeerReview));

        _tools.Add(AIFunctionFactory.Create(
            [Description("Challenge a specific assertion from another sub-agent with counter-evidence or counter-arguments.")]
            async (
                [Description("Name of the challenging sub-agent")] string challengerName,
                [Description("Name of the sub-agent being challenged")] string targetName,
                [Description("The specific assertion being challenged")] string assertion,
                [Description("The counter-argument or counter-evidence")] string counterArgument,
                CancellationToken cancellationToken
            ) =>
            {
                var challengePrompt = $"[Challenge from {challengerName}]\n\n" +
                                      $"Your assertion: \"{assertion}\"\n\n" +
                                      $"Counter-argument: {counterArgument}\n\n" +
                                      $"Please provide a rebuttal, concede the point, or revise your position.";
                return await agentPool.AskAsync(targetName, challengePrompt, cancellationToken);
            },
            ChallengeAssertion));

        // Tool Search — 按需載入工具（僅在啟用時註冊）
        if (toolSearchIndex is not null && dynamicToolSet is not null)
        {
            _tools.Add(AIFunctionFactory.Create(
                [Description(
                    "Search for additional TOOLS (not information) by keyword. " +
                    "This searches the tool registry, NOT the web. " +
                    "Use to find capabilities like 'spawn parallel workers', 'peer review', 'create custom tool'. " +
                    "Do NOT use this to search for facts or knowledge — use web search tools instead. " +
                    "After finding a tool, use load_tools to activate it.")]
                (
                    [Description("Search query for tool capabilities (e.g., 'spawn parallel', 'peer review', 'create tool')")] string query,
                    [Description("Maximum results to return (default: 5)")] int maxResults = 5
                ) =>
                {
                    var results = toolSearchIndex.Search(query, maxResults);
                    if (results.Count == 0)
                    {
                        return "No matching tools found. Try different keywords.";
                    }

                    var lines = results.Select(r => $"  - {r.Name}: {r.Description}");
                    return $"Found {results.Count} tool(s):\n{string.Join('\n', lines)}\n\nUse load_tools([\"tool_name\"]) to activate.";
                },
                SearchTools));

            _tools.Add(AIFunctionFactory.Create(
                [Description(
                    "Activate tools by name so you can use them. Tools must be loaded before calling. " +
                    "Use search_tools first to find tool names.")]
                (
                    [Description("Tool names to load (e.g., ['AzureWebSearch', 'Calculator'])")] string[] names
                ) =>
                {
                    var loaded = dynamicToolSet.LoadTools(names, toolSearchIndex);
                    if (loaded.Count == 0)
                    {
                        return "No new tools loaded (already available or not found).";
                    }

                    // 回傳已載入工具的描述，讓 LLM 知道如何使用
                    var descriptions = loaded.Select(name =>
                    {
                        var tool = toolSearchIndex.FindByName(name);
                        var desc = tool is AIFunction func ? func.Description : "";
                        return $"  - {name}: {desc}";
                    });
                    return $"Loaded {loaded.Count} tool(s):\n{string.Join('\n', descriptions)}\n\nYou can now call these tools directly.";
                },
                LoadTools));
        }

        // create_tool — Agent 自製工具（僅在 IToolCodeRunner 可用時註冊）
        if (toolCreator is not null && dynamicToolSet is not null)
        {
            var capturedDynamicToolSet = dynamicToolSet;
            _tools.Add(AIFunctionFactory.Create(
                [Description(
                    "Create a custom JavaScript tool at runtime. The tool runs in a secure sandbox (no network, no file access). " +
                    "Use this when no existing tool can handle a specific data transformation, calculation, or text processing task. " +
                    "The code receives 'input' (string) and must assign the result to 'result'. " +
                    "Provide test_input and test_expected to verify the tool works before registration.")]
                async (
                    [Description("Tool name in snake_case (e.g., 'extract_emails')")] string name,
                    [Description("What the tool does (1-2 sentences)")] string description,
                    [Description("JavaScript code. Use 'input' variable for input, assign output to 'result' variable.")] string code,
                    [Description("Test input string to verify the tool works")] string? test_input = null,
                    [Description("Expected substring in test output (for verification)")] string? test_expected = null,
                    CancellationToken cancellationToken = default
                ) =>
                {
                    var creationResult = await toolCreator.CreateAsync(
                        name, description, code, test_input, test_expected,
                        capturedDynamicToolSet, cancellationToken);

                    if (!creationResult.Success)
                    {
                        return $"[Failed] {creationResult.ErrorMessage}";
                    }

                    // 註冊到動態工具集
                    capturedDynamicToolSet.LoadCreatedTool(creationResult.Name, creationResult.Tool!);
                    return $"Tool '{creationResult.Name}' created successfully.\n" +
                           $"Description: {creationResult.Description}\n" +
                           $"You can now call it directly: {creationResult.Name}(input)";
                },
                CreateTool));
        }

        // 建立已註冊名稱的快速查詢集合
        _registeredNames = new HashSet<string>(
            _tools.OfType<AIFunction>().Select(f => f.Name),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// 檢查指定名稱是否為已註冊的 meta-tool。O(1) 查詢。
    /// </summary>
    public bool IsMetaTool(string name)
    {
        return _registeredNames.Contains(name);
    }

    /// <summary>
    /// 建立 spawn 子集 meta-tools（供 depth-1 orchestrator sub-agent 使用）。
    /// 只包含 spawn_sub_agent, collect_results, stop_spawn, list_sub_agents。
    /// 不含 create/ask（持久 agent）、shared state、ask_user、peer review 等。
    /// </summary>
    public static IList<AITool> CreateSpawnTools(AgentPool childPool)
    {
        return
        [
            AIFunctionFactory.Create(
                [Description(
                    "Spawn a one-shot background worker to complete a task. " +
                    "Returns immediately. Spawn multiple workers for parallel tasks, then call collect_results.")]
                (
                    [Description("The task to complete")] string task,
                    [Description("Tool IDs for this worker. If omitted, safe read-only tools are inherited.")] string[]? tools = null,
                    [Description("Model override (default: gpt-4o-mini)")] string? model = null,
                    [Description("Short label for identification. If omitted, auto-generated from task.")] string? label = null,
                    [Description("Timeout in seconds. 0 = no timeout. Default: 120s.")] int timeoutSeconds = 120
                ) =>
                {
                    return childPool.SpawnTask(task, tools, model, label, timeoutSeconds == 120 ? null : timeoutSeconds);
                },
                SpawnSubAgent),

            AIFunctionFactory.Create(
                [Description(
                    "Wait for spawned workers to finish and collect their results. " +
                    "If runIds is provided, only collects those specific workers. " +
                    "If omitted, collects ALL. " +
                    "Returns a JSON array with: id, label, status, runtimeSeconds, result.")]
                async (
                    [Description("Optional: specific spawn IDs to collect. If omitted, collects ALL.")] string[]? runIds = null,
                    CancellationToken cancellationToken = default
                ) =>
                {
                    return await childPool.CollectSpawnResultsAsync(runIds, cancellationToken);
                },
                CollectResults),

            AIFunctionFactory.Create(
                [Description("Stop a specific running spawn worker by its ID.")]
                (
                    [Description("The spawn worker ID")] string runId
                ) =>
                {
                    return childPool.StopSpawn(runId);
                },
                StopSpawn),

            AIFunctionFactory.Create(
                [Description("List all active spawn workers and their status.")]
                () =>
                {
                    return childPool.List();
                },
                ListSubAgents),

            AIFunctionFactory.Create(
                [Description("Send a follow-up message to a running spawn worker.")]
                (
                    [Description("The spawn worker ID")] string runId,
                    [Description("The message to send")] string message
                ) =>
                {
                    return childPool.SendToSpawn(runId, message);
                },
                SendToSpawn),
        ];
    }
}
