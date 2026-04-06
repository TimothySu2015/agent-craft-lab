using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Strategies;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class WorkflowGraphHelperTests
{
    // 輔助：建立 ChatClientAgent（用 NullChatClient 繞過 LLM 依賴）
    private static Dictionary<string, ChatClientAgent> CreateAgents(params string[] ids)
    {
        var client = new NullChatClient();
        return ids.ToDictionary(id => id, id => new ChatClientAgent(client, $"Agent {id}"));
    }

    private static List<WorkflowNode> CreateNodes(params (string Id, string Type)[] defs) =>
        defs.Select(d => new WorkflowNode { Id = d.Id, Type = d.Type, Name = d.Id }).ToList();

    private static List<WorkflowConnection> CreateConnections(params (string From, string To)[] defs) =>
        defs.Select(d => new WorkflowConnection { From = d.From, To = d.To }).ToList();

    // ════════════════════════════════════════
    // DetectWorkflowType
    // ════════════════════════════════════════

    [Fact]
    public void Detect_ConditionNode_ReturnsImperative()
    {
        var nodes = CreateNodes(("a1", "agent"), ("c1", "condition"));
        var result = WorkflowGraphHelper.DetectWorkflowType(nodes, [], CreateAgents("a1"), new HashSet<string>(["a1"]));
        Assert.Equal(WorkflowTypes.Imperative, result);
    }

    [Fact]
    public void Detect_LoopNode_ReturnsImperative()
    {
        var nodes = CreateNodes(("a1", "agent"), ("l1", "loop"));
        var result = WorkflowGraphHelper.DetectWorkflowType(nodes, [], CreateAgents("a1"), new HashSet<string>(["a1"]));
        Assert.Equal(WorkflowTypes.Imperative, result);
    }

    [Fact]
    public void Detect_CodeNode_ReturnsImperative()
    {
        var nodes = CreateNodes(("a1", "agent"), ("c1", "code"));
        var result = WorkflowGraphHelper.DetectWorkflowType(nodes, [], CreateAgents("a1"), new HashSet<string>(["a1"]));
        Assert.Equal(WorkflowTypes.Imperative, result);
    }

    [Fact]
    public void Detect_MultiOutgoing_ReturnsHandoff()
    {
        var nodes = CreateNodes(("a1", "agent"), ("a2", "agent"), ("a3", "agent"));
        var connections = CreateConnections(("a1", "a2"), ("a1", "a3"));
        var agents = CreateAgents("a1", "a2", "a3");
        var result = WorkflowGraphHelper.DetectWorkflowType(nodes, connections, agents);
        Assert.Equal(WorkflowTypes.Handoff, result);
    }

    [Fact]
    public void Detect_LinearChain_ReturnsSequential()
    {
        var nodes = CreateNodes(("a1", "agent"), ("a2", "agent"));
        var connections = CreateConnections(("a1", "a2"));
        var agents = CreateAgents("a1", "a2");
        var result = WorkflowGraphHelper.DetectWorkflowType(nodes, connections, agents);
        Assert.Equal(WorkflowTypes.Sequential, result);
    }

    [Fact]
    public void Detect_NoConnections_ReturnsSequential()
    {
        var nodes = CreateNodes(("a1", "agent"));
        var agents = CreateAgents("a1");
        var result = WorkflowGraphHelper.DetectWorkflowType(nodes, [], agents);
        Assert.Equal(WorkflowTypes.Sequential, result);
    }

    // ════════════════════════════════════════
    // GetNextNodeId
    // ════════════════════════════════════════

    [Fact]
    public void GetNextNodeId_MatchingPort_ReturnsTarget()
    {
        var adj = new Dictionary<string, List<(string ToId, string FromOutput)>>
        {
            ["n1"] = [("n2", "output_1"), ("n3", "output_2")]
        };
        Assert.Equal("n2", WorkflowGraphHelper.GetNextNodeId(adj, "n1", "output_1"));
        Assert.Equal("n3", WorkflowGraphHelper.GetNextNodeId(adj, "n1", "output_2"));
    }

    [Fact]
    public void GetNextNodeId_NoMatch_ReturnsNull()
    {
        var adj = new Dictionary<string, List<(string ToId, string FromOutput)>>();
        Assert.Null(WorkflowGraphHelper.GetNextNodeId(adj, "n1", "output_1"));
    }

    [Fact]
    public void GetNextNodeId_Output1Fallback_ReturnsFirstEdge()
    {
        var adj = new Dictionary<string, List<(string ToId, string FromOutput)>>
        {
            ["n1"] = [("n2", "some_other_port")]
        };
        // output_1 不匹配，但 fallback 取第一條 edge
        Assert.Equal("n2", WorkflowGraphHelper.GetNextNodeId(adj, "n1", "output_1"));
    }

    // ════════════════════════════════════════
    // ResolveAgentConnections
    // ════════════════════════════════════════

    [Fact]
    public void ResolveAgentConnections_DirectConnection()
    {
        var connections = CreateConnections(("a1", "a2"));
        var agentIds = new HashSet<string>(["a1", "a2"]);
        var resolved = WorkflowGraphHelper.ResolveAgentConnections(connections, agentIds);
        Assert.Single(resolved);
        Assert.Equal("a1", resolved[0].From);
        Assert.Equal("a2", resolved[0].To);
    }

    [Fact]
    public void ResolveAgentConnections_SkipsNonAgents()
    {
        // a1 → tool1 → a2（tool1 不是 agent，應解析為 a1→a2）
        var connections = CreateConnections(("a1", "tool1"), ("tool1", "a2"));
        var agentIds = new HashSet<string>(["a1", "a2"]);
        var resolved = WorkflowGraphHelper.ResolveAgentConnections(connections, agentIds);
        Assert.Single(resolved);
        Assert.Equal("a1", resolved[0].From);
        Assert.Equal("a2", resolved[0].To);
    }

    // ════════════════════════════════════════
    // FindRouterAndTargets
    // ════════════════════════════════════════

    [Fact]
    public void FindRouterAndTargets_MultiOutgoing()
    {
        var nodes = CreateNodes(("r", "agent"), ("t1", "agent"), ("t2", "agent"));
        var connections = CreateConnections(("r", "t1"), ("r", "t2"));
        var agents = CreateAgents("r", "t1", "t2");
        var result = WorkflowGraphHelper.FindRouterAndTargets(nodes, connections, agents);
        Assert.NotNull(result);
        Assert.Equal("r", result.Value.RouterId);
        Assert.Equal(2, result.Value.HandoffMap["r"].Count);
    }

    [Fact]
    public void FindRouterAndTargets_NoConnections_ReturnsNull()
    {
        var nodes = CreateNodes(("a1", "agent"));
        var agents = CreateAgents("a1");
        var result = WorkflowGraphHelper.FindRouterAndTargets(nodes, [], agents);
        Assert.Null(result);
    }
}

/// <summary>
/// 最小化 IChatClient 實作 — 僅供 WorkflowGraphHelper 測試建構 ChatClientAgent 用。
/// </summary>
file sealed class NullChatClient : IChatClient
{
    public void Dispose() { }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
}
