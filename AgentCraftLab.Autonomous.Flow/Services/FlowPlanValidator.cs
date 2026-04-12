using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Services.Variables;
using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// Flow Plan 驗證器 — 純邏輯檢查，零 LLM 成本。
/// 在執行前驗證 Plan 的結構正確性，自動修正可修正的問題。
/// Phase F：Plan 的 Nodes 是 <see cref="Schema.NodeConfig"/>，pattern match 上各子型別。
/// </summary>
public static class FlowPlanValidator
{
    private static readonly HashSet<Type> SupportedNodeTypes =
    [
        typeof(Schema.AgentNode),
        typeof(Schema.CodeNode),
        typeof(Schema.ConditionNode),
        typeof(Schema.RouterNode),
        typeof(Schema.IterationNode),
        typeof(Schema.ParallelNode),
        typeof(Schema.LoopNode),
        typeof(Schema.HttpRequestNode)
    ];

    private const int MaxParallelBranches = 6;
    private const int MaxLoopIterations = 10;
    private const int MaxPlanNodes = 15;

    /// <summary>
    /// 驗證並自動修正 Plan。回傳修正後的 Plan + 警告清單。
    /// </summary>
    public static (FlowPlan Plan, List<string> Warnings) ValidateAndFix(
        FlowPlan plan, GoalExecutionRequest request)
    {
        var warnings = new List<string>();
        var fixedNodes = new List<Schema.NodeConfig>();

        for (var i = 0; i < plan.Nodes.Count; i++)
        {
            var node = plan.Nodes[i];

            // 1. 不支援的節點類型 → 跳過
            if (!SupportedNodeTypes.Contains(node.GetType()))
            {
                warnings.Add($"Removed unsupported node type '{NodeConfigHelpers.GetNodeTypeString(node)}' ({node.Name})");
                continue;
            }

            // 2. 工具 ID 過濾 — 移除 agent / autonomous 不存在的工具
            if (node is Schema.AgentNode agent && agent.Tools.Count > 0)
            {
                var validTools = agent.Tools.Where(t => request.AvailableTools.Contains(t)).ToList();
                var invalidTools = agent.Tools.Except(validTools).ToList();
                if (invalidTools.Count > 0)
                {
                    warnings.Add($"[{agent.Name}] Removed invalid tools: {string.Join(", ", invalidTools)}");
                    node = agent with { Tools = validTools };
                }
            }

            // 3. Parallel 分支數量 + 分支工具過濾
            if (node is Schema.ParallelNode parallel)
            {
                var branches = parallel.Branches;

                if (branches.Count > MaxParallelBranches)
                {
                    warnings.Add($"[{parallel.Name}] Trimmed parallel branches from {branches.Count} to {MaxParallelBranches}");
                    branches = branches.Take(MaxParallelBranches).ToList();
                }

                // 分支工具過濾
                var anyBranchChanged = false;
                var fixedBranches = new List<Schema.BranchConfig>(branches.Count);
                foreach (var b in branches)
                {
                    if (b.Tools is { Count: > 0 })
                    {
                        var validTools = b.Tools.Where(t => request.AvailableTools.Contains(t)).ToList();
                        if (validTools.Count != b.Tools.Count)
                        {
                            anyBranchChanged = true;
                            fixedBranches.Add(b with { Tools = validTools });
                            continue;
                        }
                    }
                    fixedBranches.Add(b);
                }

                if (fixedBranches.Count != parallel.Branches.Count || anyBranchChanged)
                {
                    node = parallel with { Branches = fixedBranches };
                }
            }

            // 4. Loop maxIterations 上限
            if (node is Schema.LoopNode loop && loop.MaxIterations > MaxLoopIterations)
            {
                warnings.Add($"[{loop.Name}] Capped loop maxIterations from {loop.MaxIterations} to {MaxLoopIterations}");
                node = loop with { MaxIterations = MaxLoopIterations };
            }

            // 5. Condition 結構驗證
            if (node is Schema.ConditionNode condNode)
            {
                if (i + 1 >= plan.Nodes.Count)
                {
                    warnings.Add($"[{condNode.Name}] Condition at end of plan has no branches — removed");
                    continue;
                }

                // 驗證 TrueBranchIndex/FalseBranchIndex（stash 在 Meta）
                var trueIdx = NodeConfigHelpers.GetBranchIndex(condNode, NodeConfigHelpers.MetaTrueBranchIndex);
                var falseIdx = NodeConfigHelpers.GetBranchIndex(condNode, NodeConfigHelpers.MetaFalseBranchIndex);
                if (trueIdx is not null && trueIdx >= plan.Nodes.Count)
                    warnings.Add($"[{condNode.Name}] TrueBranchIndex {trueIdx} out of range");
                if (falseIdx is not null && falseIdx >= plan.Nodes.Count)
                    warnings.Add($"[{condNode.Name}] FalseBranchIndex {falseIdx} out of range");
            }

            // 6. {{node:step_name}} 引用驗證 — 確認引用的節點名稱存在於前驅節點中
            var nodeTexts = GatherNodeTexts(node);
            foreach (var text in nodeTexts)
            {
                foreach (var refName in VariableResolver.ExtractNodeReferenceNames(text))
                {
                    if (!fixedNodes.Any(n => n.Name == refName))
                    {
                        warnings.Add($"[{node.Name}] References non-existent predecessor '{{{{node:{refName}}}}}' in instructions");
                    }
                }
            }

            fixedNodes.Add(node);
        }

        // 7. 節點數量上限
        if (fixedNodes.Count > MaxPlanNodes)
        {
            warnings.Add($"Plan has {fixedNodes.Count} nodes, trimmed to {MaxPlanNodes}");
            fixedNodes = fixedNodes.Take(MaxPlanNodes).ToList();
        }

        // 8. Plan 不能為空
        if (fixedNodes.Count == 0)
        {
            warnings.Add("Plan is empty after validation — no executable nodes");
        }

        // 9. 工具推薦 — agent instructions 含即時資料關鍵字但沒有搜尋工具
        foreach (var node in fixedNodes)
        {
            if (node is Schema.AgentNode agent && agent.Tools.Count == 0)
            {
                if (LikelyNeedsRealtimeData(agent.Instructions))
                {
                    warnings.Add($"[{agent.Name}] Instructions suggest real-time data needs (法規/股價/新聞/市場/最新) but no search tools assigned");
                }
            }

            if (node is Schema.ParallelNode parallel)
            {
                foreach (var branch in parallel.Branches)
                {
                    if ((branch.Tools is null or { Count: 0 }) && LikelyNeedsRealtimeData(branch.Goal))
                    {
                        warnings.Add($"[{parallel.Name}/{branch.Name}] Branch goal suggests real-time data needs but no search tools assigned");
                    }
                }
            }
        }

        // 10. Parallel 後面應有 Synthesizer — 最後一個 parallel 後面沒有 agent
        for (var i = fixedNodes.Count - 1; i >= 0; i--)
        {
            if (fixedNodes[i] is Schema.ParallelNode lastParallel)
            {
                var hasAgentAfter = fixedNodes.Skip(i + 1).OfType<Schema.AgentNode>().Any();
                if (!hasAgentAfter)
                {
                    warnings.Add($"[{lastParallel.Name}] Parallel node is not followed by a Synthesizer agent — branch results won't be merged");
                }
                break; // 只檢最後一個 parallel
            }
        }

        return (new FlowPlan { Nodes = fixedNodes }, warnings);
    }

    /// <summary>
    /// 啟發式判斷：instructions 是否暗示需要即時/最新資料。
    /// keyword 匹配作為確定性安全網 — 主力判斷在 Planner LLM prompt。
    /// </summary>
    private static bool LikelyNeedsRealtimeData(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        ReadOnlySpan<string> keywords =
        [
            "最新", "即時", "今年", "今日", "現在", "當前", "目前",
            "股價", "財報", "財務報表", "營收", "市值",
            "法規", "法律", "法案", "條例", "修法",
            "新聞", "報導", "趨勢", "市場",
            "real-time", "latest", "current", "today",
            "stock", "financial", "regulation", "news", "market",
        ];

        foreach (var kw in keywords)
        {
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>收集節點中所有可能含 {{node:}} 引用的文字欄位。</summary>
    private static List<string> GatherNodeTexts(Schema.NodeConfig node)
    {
        var texts = new List<string>();

        switch (node)
        {
            case Schema.AgentNode agent:
                if (!string.IsNullOrEmpty(agent.Instructions)) texts.Add(agent.Instructions);
                break;

            case Schema.ParallelNode parallel:
                foreach (var b in parallel.Branches)
                {
                    if (!string.IsNullOrEmpty(b.Goal)) texts.Add(b.Goal);
                }
                break;

            case Schema.LoopNode loop:
                if (!string.IsNullOrEmpty(loop.BodyAgent.Instructions))
                    texts.Add(loop.BodyAgent.Instructions);
                break;

            case Schema.IterationNode iter:
                if (!string.IsNullOrEmpty(iter.BodyAgent.Instructions))
                    texts.Add(iter.BodyAgent.Instructions);
                break;
        }

        return texts;
    }
}
