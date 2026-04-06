using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// Flow Plan 驗證器 — 純邏輯檢查，零 LLM 成本。
/// 在執行前驗證 Plan 的結構正確性，自動修正可修正的問題。
/// </summary>
public static class FlowPlanValidator
{
    private static readonly HashSet<string> SupportedNodeTypes =
    [
        NodeTypes.Agent, NodeTypes.Code, NodeTypes.Condition, NodeTypes.Router,
        NodeTypes.Iteration, NodeTypes.Parallel, NodeTypes.Loop, NodeTypes.HttpRequest
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
        var fixedNodes = new List<PlannedNode>();

        for (var i = 0; i < plan.Nodes.Count; i++)
        {
            var node = plan.Nodes[i];

            // 1. 不支援的節點類型 → 跳過
            if (!SupportedNodeTypes.Contains(node.NodeType))
            {
                warnings.Add($"Removed unsupported node type '{node.NodeType}' ({node.Name})");
                continue;
            }

            // 2. 工具 ID 過濾 — 移除不存在的工具
            if (node.Tools is { Count: > 0 })
            {
                var validTools = node.Tools.Where(t => request.AvailableTools.Contains(t)).ToList();
                var invalidTools = node.Tools.Except(validTools).ToList();
                if (invalidTools.Count > 0)
                {
                    warnings.Add($"[{node.Name}] Removed invalid tools: {string.Join(", ", invalidTools)}");
                    node = Clone(node, tools: validTools);
                }
            }

            // 3. Parallel 分支數量限制
            if (node.NodeType == NodeTypes.Parallel && node.Branches is { Count: > MaxParallelBranches })
            {
                warnings.Add($"[{node.Name}] Trimmed parallel branches from {node.Branches.Count} to {MaxParallelBranches}");
                node = Clone(node, branches:node.Branches.Take(MaxParallelBranches).ToList());
            }

            // 4. Parallel 分支工具過濾
            if (node.NodeType == NodeTypes.Parallel && node.Branches is { Count: > 0 })
            {
                var fixedBranches = node.Branches.Select(b =>
                {
                    if (b.Tools is not { Count: > 0 }) return b;
                    var valid = b.Tools.Where(t => request.AvailableTools.Contains(t)).ToList();
                    return valid.Count == b.Tools.Count ? b : new ParallelBranchConfig
                    {
                        Name = b.Name, Goal = b.Goal, Tools = valid
                    };
                }).ToList();
                node = Clone(node, branches:fixedBranches);
            }

            // 5. Loop maxIterations 上限
            if (node.NodeType == NodeTypes.Loop && (node.MaxIterations ?? 5) > MaxLoopIterations)
            {
                warnings.Add($"[{node.Name}] Capped loop maxIterations from {node.MaxIterations} to {MaxLoopIterations}");
                node = Clone(node, maxIterations: MaxLoopIterations);
            }

            // 6. Condition 結構驗證
            if (node.NodeType == NodeTypes.Condition)
            {
                if (i + 1 >= plan.Nodes.Count)
                {
                    warnings.Add($"[{node.Name}] Condition at end of plan has no branches — removed");
                    continue;
                }

                // 驗證 TrueBranchIndex/FalseBranchIndex 如果有指定
                if (node.TrueBranchIndex is not null && node.TrueBranchIndex >= plan.Nodes.Count)
                    warnings.Add($"[{node.Name}] TrueBranchIndex {node.TrueBranchIndex} out of range");
                if (node.FalseBranchIndex is not null && node.FalseBranchIndex >= plan.Nodes.Count)
                    warnings.Add($"[{node.Name}] FalseBranchIndex {node.FalseBranchIndex} out of range");
            }

            // 7. {{node:step_name}} 引用驗證 — 確認引用的節點名稱存在於前驅節點中
            var nodeTexts = GatherNodeTexts(node);
            foreach (var text in nodeTexts)
            {
                foreach (var refName in NodeReferenceResolver.ExtractNames(text))
                {
                    if (!fixedNodes.Any(n => n.Name == refName))
                    {
                        warnings.Add($"[{node.Name}] References non-existent predecessor '{{{{node:{refName}}}}}' in instructions");
                    }
                }
            }

            fixedNodes.Add(node);
        }

        // 8. 節點數量上限
        if (fixedNodes.Count > MaxPlanNodes)
        {
            warnings.Add($"Plan has {fixedNodes.Count} nodes, trimmed to {MaxPlanNodes}");
            fixedNodes = fixedNodes.Take(MaxPlanNodes).ToList();
        }

        // 9. Plan 不能為空
        if (fixedNodes.Count == 0)
        {
            warnings.Add("Plan is empty after validation — no executable nodes");
        }

        return (new FlowPlan { Nodes = fixedNodes }, warnings);
    }

    /// <summary>收集節點中所有可能含 {{node:}} 引用的文字欄位。</summary>
    private static List<string> GatherNodeTexts(PlannedNode node)
    {
        var texts = new List<string>();
        if (!string.IsNullOrEmpty(node.Instructions))
            texts.Add(node.Instructions);
        if (node.Branches is { Count: > 0 })
        {
            foreach (var b in node.Branches)
            {
                if (!string.IsNullOrEmpty(b.Goal))
                    texts.Add(b.Goal);
            }
        }

        return texts;
    }

    // PlannedNode 是 sealed class 不支援 with，用通用 clone + optional 覆蓋
    private static PlannedNode Clone(PlannedNode src,
        List<string>? tools = null,
        List<ParallelBranchConfig>? branches = null,
        int? maxIterations = null) => new()
    {
        NodeType = src.NodeType, Name = src.Name, Instructions = src.Instructions,
        Tools = tools ?? src.Tools, Provider = src.Provider, Model = src.Model,
        ConditionType = src.ConditionType, ConditionValue = src.ConditionValue,
        MaxIterations = maxIterations ?? src.MaxIterations,
        TransformType = src.TransformType,
        TransformPattern = src.TransformPattern, TransformReplacement = src.TransformReplacement,
        Branches = branches ?? src.Branches, MergeStrategy = src.MergeStrategy,
        SplitMode = src.SplitMode, Delimiter = src.Delimiter, MaxItems = src.MaxItems,
        HttpApiId = src.HttpApiId, HttpArgsTemplate = src.HttpArgsTemplate,
        OutputFormat = src.OutputFormat, OutputSchema = src.OutputSchema
    };
}
