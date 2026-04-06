using AgentCraftLab.Engine.Models;
using Microsoft.Agents.AI;

namespace AgentCraftLab.Engine.Strategies;

/// <summary>
/// 工作流程圖形結構相關的靜態工具方法。
/// </summary>
public static class WorkflowGraphHelper
{
    /// <summary>
    /// 自動偵測 workflow 類型：
    /// - 有 condition/loop 節點 → imperative
    /// - 任一 agent 有多條 outgoing connections → handoff
    /// - 其他 → sequential
    /// </summary>
    public static string DetectWorkflowType(
        List<WorkflowNode> allNodes,
        List<WorkflowConnection> connections,
        Dictionary<string, ChatClientAgent> agents,
        HashSet<string>? executableNodeIds = null)
    {
        if (NodeTypeRegistry.HasAnyRequiringImperative(allNodes))
            return WorkflowTypes.Imperative;

        var agentIds = executableNodeIds ?? new HashSet<string>(agents.Keys);

        var outgoingCounts = new Dictionary<string, int>();
        foreach (var conn in connections)
        {
            if (agentIds.Contains(conn.From) && agentIds.Contains(conn.To))
            {
                outgoingCounts.TryGetValue(conn.From, out var count);
                outgoingCounts[conn.From] = count + 1;
            }
        }

        if (outgoingCounts.Values.Any(c => c > 1))
            return WorkflowTypes.Handoff;

        return WorkflowTypes.Sequential;
    }

    /// <summary>
    /// 將穿過非 agent 節點的連線，解析為 agent-to-agent 的直接連線。
    /// </summary>
    public static List<WorkflowConnection> ResolveAgentConnections(
        List<WorkflowConnection> connections,
        HashSet<string> agentIds)
    {
        var outgoing = new Dictionary<string, List<string>>();
        foreach (var conn in connections)
        {
            if (!outgoing.ContainsKey(conn.From))
                outgoing[conn.From] = [];
            outgoing[conn.From].Add(conn.To);
        }

        var resolved = new List<WorkflowConnection>();

        foreach (var fromId in agentIds)
        {
            if (!outgoing.TryGetValue(fromId, out var directTargets))
                continue;

            var queue = new Queue<string>(directTargets);
            var visited = new HashSet<string> { fromId };

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (visited.Contains(current)) continue;
                visited.Add(current);

                if (agentIds.Contains(current))
                {
                    resolved.Add(new WorkflowConnection { From = fromId, To = current });
                }
                else
                {
                    if (outgoing.TryGetValue(current, out var nextTargets))
                    {
                        foreach (var next in nextTargets)
                            queue.Enqueue(next);
                    }
                }
            }
        }

        return resolved;
    }

    /// <summary>
    /// 按連線順序排列 agents（拓撲排序）。
    /// </summary>
    public static ChatClientAgent[] OrderAgentsByConnections(
        List<WorkflowNode> agentNodes,
        List<WorkflowConnection> connections,
        Dictionary<string, ChatClientAgent> agents)
    {
        var agentIds = new HashSet<string>(agents.Keys);
        var graph = new Dictionary<string, string>();

        foreach (var conn in connections)
        {
            if (agentIds.Contains(conn.From) && agentIds.Contains(conn.To))
                graph[conn.From] = conn.To;
        }

        var hasIncoming = new HashSet<string>(graph.Values);
        var startNodes = agentIds.Where(id => !hasIncoming.Contains(id)).ToList();

        if (startNodes.Count == 0)
            return agents.Values.ToArray();

        var ordered = new List<ChatClientAgent>();
        var visited = new HashSet<string>();
        var current = startNodes[0];

        while (current != null && !visited.Contains(current))
        {
            visited.Add(current);
            if (agents.TryGetValue(current, out var agent))
                ordered.Add(agent);
            graph.TryGetValue(current, out current!);
        }

        foreach (var id in agentIds.Where(id => !visited.Contains(id)))
        {
            if (agents.TryGetValue(id, out var agent))
                ordered.Add(agent);
        }

        return ordered.ToArray();
    }

    /// <summary>
    /// 找出 handoff workflow 的 router 和所有 agent 的 handoff 關係圖。
    /// </summary>
    public static (string RouterId, Dictionary<string, List<string>> HandoffMap)? FindRouterAndTargets(
        List<WorkflowNode> agentNodes,
        List<WorkflowConnection> connections,
        Dictionary<string, ChatClientAgent> agents)
    {
        var agentIds = new HashSet<string>(agents.Keys);

        var outgoing = new Dictionary<string, List<string>>();
        var hasIncoming = new HashSet<string>();

        foreach (var conn in connections)
        {
            if (agentIds.Contains(conn.From) && agentIds.Contains(conn.To))
            {
                if (!outgoing.ContainsKey(conn.From))
                    outgoing[conn.From] = [];
                outgoing[conn.From].Add(conn.To);
                hasIncoming.Add(conn.To);
            }
        }

        var startNodes = agentIds.Where(id => !hasIncoming.Contains(id)).ToList();
        var routerId = startNodes.Count > 0
            ? startNodes[0]
            : outgoing.OrderByDescending(kv => kv.Value.Count).FirstOrDefault().Key;

        if (routerId is null || !agents.ContainsKey(routerId))
            return null;

        if (outgoing.Count == 0)
            return null;

        return (routerId, outgoing);
    }

    /// <summary>
    /// 根據鄰接表取得指定 output port 的下一個節點 ID。
    /// </summary>
    public static string? GetNextNodeId(
        Dictionary<string, List<(string ToId, string FromOutput)>> adj,
        string nodeId,
        string outputPort)
    {
        if (!adj.TryGetValue(nodeId, out var edges))
            return null;

        var match = edges.FirstOrDefault(e => e.FromOutput == outputPort);
        if (match.ToId is not null)
            return match.ToId;

        if (outputPort == OutputPorts.Output1 && edges.Count > 0)
            return edges[0].ToId;

        return null;
    }
}
