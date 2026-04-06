using System.Text.Json;
using System.Text.Json.Serialization;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// 將 FlowPlan（LLM 規劃結果）轉換為 AI Build JSON 格式（前端 handleApply 可直接套用）。
/// 負責：nodeType→type、fields→data wrapper、生成 connections（sequential + 分支展開）。
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

            if (pn.NodeType == NodeTypes.Parallel && pn.Branches is { Count: > 0 })
            {
                ExpandParallel(pn, i, plan, nodes, connections);
            }
            else if (pn.NodeType == NodeTypes.Loop && pn.Instructions is not null)
            {
                ExpandLoop(pn, i, plan, nodes, connections);
            }
            else if (pn.NodeType == NodeTypes.Iteration && pn.Instructions is not null)
            {
                ExpandIteration(pn, i, plan, nodes, connections);
            }
            else if (pn.NodeType == NodeTypes.Condition)
            {
                ExpandCondition(pn, i, plan, nodes, connections);
            }
            else
            {
                var nodeIndex = nodes.Count;
                nodes.Add(PlannedNodeToCrystallized(pn));

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
    // PlannedNode → CrystallizedNode 映射（委託 WorkflowCrystallizer.FromConfig）
    // ═══════════════════════════════════════════════

    private static CrystallizedNode PlannedNodeToCrystallized(PlannedNode pn)
        => WorkflowCrystallizer.FromConfig(pn.NodeType, pn.Name, pn.ToConfig());

    // ═══════════════════════════════════════════════
    // Parallel 展開
    // ═══════════════════════════════════════════════

    private static void ExpandParallel(PlannedNode pn, int stepIndex, FlowPlan plan,
        List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var parallelIndex = nodes.Count;
        nodes.Add(PlannedNodeToCrystallized(pn));

        foreach (var branch in pn.Branches!)
        {
            var branchIndex = nodes.Count;
            nodes.Add(new CrystallizedNode
            {
                Type = NodeTypes.Agent,
                Name = branch.Name,
                Instructions = branch.Goal,
                Tools = branch.Tools ?? []
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

    private static void ExpandLoop(PlannedNode pn, int stepIndex, FlowPlan plan,
        List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var loopIndex = nodes.Count;
        nodes.Add(PlannedNodeToCrystallized(pn));

        // Body agent
        var bodyIndex = nodes.Count;
        nodes.Add(new CrystallizedNode
        {
            Type = NodeTypes.Agent,
            Name = $"{pn.Name} Body",
            Instructions = pn.Instructions ?? "",
            Tools = pn.Tools ?? []
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

    private static void ExpandIteration(PlannedNode pn, int stepIndex, FlowPlan plan,
        List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var iterIndex = nodes.Count;
        nodes.Add(PlannedNodeToCrystallized(pn));

        // Body agent
        var bodyIndex = nodes.Count;
        nodes.Add(new CrystallizedNode
        {
            Type = NodeTypes.Agent,
            Name = $"{pn.Name} Body",
            Instructions = pn.Instructions ?? "",
            Tools = pn.Tools ?? []
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

    private static void ExpandCondition(PlannedNode pn, int stepIndex, FlowPlan plan,
        List<CrystallizedNode> nodes, List<CrystallizedConnection> connections)
    {
        var condIndex = nodes.Count;
        nodes.Add(PlannedNodeToCrystallized(pn));

        // True branch (output_1) → 預設下一個節點，或 TrueBranchIndex
        var trueTarget = pn.TrueBranchIndex ?? (stepIndex + 1 < plan.Nodes.Count ? condIndex + 1 : -1);
        if (trueTarget >= 0)
        {
            connections.Add(new CrystallizedConnection
            {
                From = condIndex, To = trueTarget, FromOutput = OutputPorts.Output1
            });
        }

        // False branch (output_2) → 預設跳兩個，或 FalseBranchIndex
        var falseTarget = pn.FalseBranchIndex ?? (stepIndex + 2 < plan.Nodes.Count ? condIndex + 2 : -1);
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
