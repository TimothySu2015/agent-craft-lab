using System.Text.Json;
using System.Text.Json.Serialization;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Engine.Models;
using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// 將 <see cref="FlowPlan"/>（LLM 規劃結果，Schema.NodeConfig 清單）轉換為 AI Build JSON
/// 格式（前端 handleApply 可直接套用）。負責：nodeType 標記、展開 parallel / loop /
/// iteration / condition 為多個節點 + 連線。
/// Phase F：輸入從 PlannedNode 改為 Schema.NodeConfig，pattern match on 子型別。
/// </summary>
public static class FlowPlanConverter
{
    /// <summary>
    /// 將 FlowPlan 轉換為 AI Build JSON 字串。
    /// 前端 AiBuildPanel.handleApply 可直接解析此格式。
    /// </summary>
    public static string ConvertToAiBuildJson(FlowPlan plan)
    {
        var nodes = new List<CrystallizedNode>();
        var connections = new List<CrystallizedConnection>();

        for (var i = 0; i < plan.Nodes.Count; i++)
        {
            var pn = plan.Nodes[i];

            switch (pn)
            {
                case Schema.ParallelNode { Branches: { Count: > 0 } } parallel:
                    ExpandParallel(parallel, i, plan, nodes, connections);
                    break;

                case Schema.LoopNode loop:
                    ExpandLoop(loop, i, plan, nodes, connections);
                    break;

                case Schema.IterationNode iteration:
                    ExpandIteration(iteration, i, plan, nodes, connections);
                    break;

                case Schema.ConditionNode condition:
                    ExpandCondition(condition, i, plan, nodes, connections);
                    break;

                default:
                    var nodeIndex = nodes.Count;
                    nodes.Add(NodeConfigToCrystallized(pn));

                    // 下一個非展開節點的 connection
                    if (i + 1 < plan.Nodes.Count)
                    {
                        connections.Add(new CrystallizedConnection
                        {
                            From = nodeIndex,
                            To = nodeIndex + 1,
                            FromOutput = OutputPorts.Output1
                        });
                    }
                    break;
            }
        }

        var workflow = new CrystallizedWorkflow
        {
            Nodes = nodes,
            Connections = connections
        };

        return JsonSerializer.Serialize(workflow, ConvertJsonOptions);
    }

    // ═══════════════════════════════════════════════
    // NodeConfig → CrystallizedNode 映射：委派給 WorkflowCrystallizer.FromNodeConfig
    // ═══════════════════════════════════════════════

    private static CrystallizedNode NodeConfigToCrystallized(Schema.NodeConfig node)
        => WorkflowCrystallizer.FromNodeConfig(node.Name, node);

    // ═══════════════════════════════════════════════
    // Parallel 展開
    // ═══════════════════════════════════════════════

    private static void ExpandParallel(Schema.ParallelNode pn, int stepIndex, FlowPlan plan,
        List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var parallelIndex = nodes.Count;
        nodes.Add(NodeConfigToCrystallized(pn));

        foreach (var branch in pn.Branches)
        {
            var branchIndex = nodes.Count;
            nodes.Add(new CrystallizedNode
            {
                Type = NodeTypes.Agent,
                Name = branch.Name,
                Instructions = branch.Goal,
                Tools = branch.Tools?.ToList() ?? []
            });

            var portIndex = branchIndex - parallelIndex;
            connections.Add(new CrystallizedConnection
            {
                From = parallelIndex, To = branchIndex,
                FromOutput = $"output_{portIndex}"
            });
        }

        // Done port → 下一個主節點
        if (stepIndex + 1 < plan.Nodes.Count)
        {
            connections.Add(new CrystallizedConnection
            {
                From = parallelIndex, To = nodes.Count,
                FromOutput = $"output_{pn.Branches.Count + 1}"
            });
        }
    }

    // ═══════════════════════════════════════════════
    // Loop 展開
    // ═══════════════════════════════════════════════

    private static void ExpandLoop(Schema.LoopNode pn, int stepIndex, FlowPlan plan,
        List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var loopIndex = nodes.Count;
        nodes.Add(NodeConfigToCrystallized(pn));

        // Body agent
        var bodyIndex = nodes.Count;
        nodes.Add(new CrystallizedNode
        {
            Type = NodeTypes.Agent,
            Name = $"{pn.Name} Body",
            Instructions = pn.BodyAgent.Instructions,
            Tools = pn.BodyAgent.Tools.ToList()
        });

        // Loop → Body (output_1)
        connections.Add(new CrystallizedConnection
        {
            From = loopIndex, To = bodyIndex, FromOutput = OutputPorts.Output1
        });

        // Body → Loop (回頭)
        connections.Add(new CrystallizedConnection
        {
            From = bodyIndex, To = loopIndex, FromOutput = OutputPorts.Output1
        });

        // Loop → next (output_2 退出)
        if (stepIndex + 1 < plan.Nodes.Count)
        {
            connections.Add(new CrystallizedConnection
            {
                From = loopIndex, To = nodes.Count, FromOutput = OutputPorts.Output2
            });
        }
    }

    // ═══════════════════════════════════════════════
    // Iteration 展開
    // ═══════════════════════════════════════════════

    private static void ExpandIteration(Schema.IterationNode pn, int stepIndex, FlowPlan plan,
        List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var iterIndex = nodes.Count;
        nodes.Add(NodeConfigToCrystallized(pn));

        // Body agent
        var bodyIndex = nodes.Count;
        nodes.Add(new CrystallizedNode
        {
            Type = NodeTypes.Agent,
            Name = $"{pn.Name} Body",
            Instructions = pn.BodyAgent.Instructions,
            Tools = pn.BodyAgent.Tools.ToList()
        });

        // Iteration → Body (output_1)
        connections.Add(new CrystallizedConnection
        {
            From = iterIndex, To = bodyIndex, FromOutput = OutputPorts.Output1
        });

        // Iteration → next (output_2 Done)
        if (stepIndex + 1 < plan.Nodes.Count)
        {
            connections.Add(new CrystallizedConnection
            {
                From = iterIndex, To = nodes.Count, FromOutput = OutputPorts.Output2
            });
        }
    }

    // ═══════════════════════════════════════════════
    // Condition 展開
    // ═══════════════════════════════════════════════

    private static void ExpandCondition(Schema.ConditionNode pn, int stepIndex, FlowPlan plan,
        List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var condIndex = nodes.Count;
        nodes.Add(NodeConfigToCrystallized(pn));

        // Branch indices 從 Meta 讀取
        var trueBranchIdx = NodeConfigHelpers.GetBranchIndex(pn, NodeConfigHelpers.MetaTrueBranchIndex);
        var falseBranchIdx = NodeConfigHelpers.GetBranchIndex(pn, NodeConfigHelpers.MetaFalseBranchIndex);

        // True branch (output_1) → 預設下一個節點，或 TrueBranchIndex
        var trueTarget = trueBranchIdx ?? (stepIndex + 1 < plan.Nodes.Count ? condIndex + 1 : -1);
        if (trueTarget >= 0)
        {
            connections.Add(new CrystallizedConnection
            {
                From = condIndex, To = trueTarget, FromOutput = OutputPorts.Output1
            });
        }

        // False branch (output_2) → 預設跳兩個，或 FalseBranchIndex
        var falseTarget = falseBranchIdx ?? (stepIndex + 2 < plan.Nodes.Count ? condIndex + 2 : -1);
        if (falseTarget >= 0)
        {
            connections.Add(new CrystallizedConnection
            {
                From = condIndex, To = falseTarget, FromOutput = OutputPorts.Output2
            });
        }
    }

    private static readonly JsonSerializerOptions ConvertJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
