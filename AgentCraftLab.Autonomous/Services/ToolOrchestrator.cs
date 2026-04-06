using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 工具編排器 — 集中管理工具分類、DynamicToolSet 與 ToolSearchIndex 的建構。
/// 唯一真相來源：決定哪些工具 always-available、哪些按需載入。
/// </summary>
public sealed class ToolOrchestrator
{
    /// <summary>動態工具集（always-available + 已載入的）。</summary>
    public DynamicToolSet DynamicToolSet { get; }

    /// <summary>搜尋索引（null = 未啟用 Tool Search）。</summary>
    public ToolSearchIndex? SearchIndex { get; }

    /// <summary>Meta-tool 註冊表。</summary>
    public MetaToolFactory MetaToolRegistry { get; }

    /// <summary>取得目前可用的工具清單（供 ChatOptions.Tools 使用）。</summary>
    public IList<AITool> GetActiveTools() => DynamicToolSet.GetActiveTools();

    /// <summary>搜尋索引中的可搜尋工具數量。</summary>
    public int SearchableCount => SearchIndex?.Count ?? 0;

    private ToolOrchestrator(
        DynamicToolSet dynamicToolSet,
        ToolSearchIndex? searchIndex,
        MetaToolFactory metaToolRegistry)
    {
        DynamicToolSet = dynamicToolSet;
        SearchIndex = searchIndex;
        MetaToolRegistry = metaToolRegistry;
    }

    /// <summary>
    /// 建構工具編排結果 — 分類所有工具為 always-available 與 searchable。
    /// </summary>
    /// <param name="externalTools">AgentFactory 解析的外部工具（Catalog + MCP + A2A + HTTP）。</param>
    /// <param name="agentPool">Sub-agent 管理池。</param>
    /// <param name="sharedState">共享狀態。</param>
    /// <param name="askUserCtx">使用者互動上下文（null = ask_user 不註冊）。</param>
    /// <param name="config">執行配置。</param>
    /// <param name="toolCodeRunner">JS 沙箱執行器（null = create_tool 不註冊）。</param>
    /// <param name="logger">日誌。</param>
    public static ToolOrchestrator Build(
        IList<AITool> externalTools,
        AgentPool agentPool,
        SharedStateStore sharedState,
        AskUserContext? askUserCtx,
        Models.ReactExecutorConfig config,
        IToolCodeRunner? toolCodeRunner,
        ILogger logger)
    {
        // 1. 建立 MetaToolFactory（含所有 meta-tools，不含 search/load/create）
        var toolCreator = toolCodeRunner is not null ? new ToolCreator(toolCodeRunner, logger) : null;

        // 2. 分類外部工具
        var alwaysAvailable = new List<AITool>();
        var searchable = new List<AITool>();

        foreach (var tool in externalTools)
        {
            if (tool is AIFunction func && SafeWhitelistToolDelegation.IsSafeTool(func.Name))
            {
                alwaysAvailable.Add(tool);
            }
            else
            {
                searchable.Add(tool);
            }
        }

        // 3. 建立 base MetaToolFactory（暫時不含 search/load/create）
        var baseFactory = new MetaToolFactory(agentPool, sharedState, externalTools, askUserCtx);

        // 4. 分類 meta-tools
        // 只有在有外部 searchable 工具時才分層（否則全部 always-available，避免 Agent 找不到 spawn 等核心 meta-tools）
        var hasExternalSearchable = searchable.Count > 0;
        foreach (var mt in baseFactory.Tools)
        {
            if (mt is AIFunction func)
            {
                var tier = MetaToolFactory.TierMap.GetValueOrDefault(func.Name, MetaToolTier.Delegation);
                if (!hasExternalSearchable || tier is MetaToolTier.Core)
                {
                    alwaysAvailable.Add(mt);
                }
                else
                {
                    searchable.Add(mt);
                }
            }
        }

        // 5. 建構 ToolSearchIndex + DynamicToolSet
        ToolSearchIndex? searchIndex = null;
        if (searchable.Count > 0)
        {
            searchIndex = new ToolSearchIndex(searchable);
        }

        // 6. 加入 search_tools / load_tools（如果有可搜尋工具）
        // 先建一個暫時的 DynamicToolSet 用於 MetaToolFactory 閉包
        var tempToolSet = new DynamicToolSet(alwaysAvailable);

        // 建立最終 MetaToolFactory（含 search/load/create，閉包捕獲 DynamicToolSet）
        MetaToolFactory? finalFactory;
        if (searchIndex is not null || toolCreator is not null)
        {
            finalFactory = new MetaToolFactory(
                agentPool, sharedState, externalTools, askUserCtx,
                searchIndex, toolCreator, tempToolSet);

            // 收集新增的 Discovery/Creation tools
            foreach (var mt in finalFactory.Tools)
            {
                if (mt is AIFunction func && !baseFactory.IsMetaTool(func.Name))
                {
                    alwaysAvailable.Add(mt);
                }
            }

            // 重建最終 DynamicToolSet（包含 search/load/create tools）
            var finalToolSet = new DynamicToolSet(alwaysAvailable);

            // 重建 MetaToolFactory（閉包捕獲最終 DynamicToolSet）
            finalFactory = new MetaToolFactory(
                agentPool, sharedState, externalTools, askUserCtx,
                searchIndex, toolCreator, finalToolSet);

            logger.LogInformation(
                "ToolOrchestrator: {Always} always-available, {Searchable} searchable tools",
                alwaysAvailable.Count, searchable.Count);

            return new ToolOrchestrator(finalToolSet, searchIndex, finalFactory);
        }

        // 無 searchable 且無 toolCreator：簡單模式
        logger.LogInformation(
            "ToolOrchestrator: {Always} always-available tools (no searchable)",
            alwaysAvailable.Count);

        return new ToolOrchestrator(
            new DynamicToolSet(alwaysAvailable), null, baseFactory);
    }
}
