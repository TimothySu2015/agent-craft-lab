using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services.Variables;
using AgentCraftLab.Engine.Strategies;

namespace AgentCraftLab.Tests.Engine.Schema;

/// <summary>
/// 驗證 <see cref="ImperativeExecutionState.ToVariableContext"/> 建構行為 — Phase C 後
/// NodeMap 直接存 <see cref="NodeConfig"/>。
/// </summary>
public class ImperativeStateVariableContextTests
{
    private static ImperativeExecutionState BuildState()
    {
        var nodeMap = new Dictionary<string, NodeConfig>
        {
            ["n1"] = new AgentNode { Id = "n1", Name = "Researcher" },
            ["n2"] = new AgentNode { Id = "n2", Name = "Writer" }
        };

        return new ImperativeExecutionState
        {
            Adjacency = new(),
            NodeMap = nodeMap,
            Agents = new(),
            ChatClients = new(),
            ChatHistories = new(),
            LoopCounters = new(),
            AgentContext = null!,
            Request = new WorkflowExecutionRequest(),
            HistoryStrategy = null!,
            SystemVariables = new Dictionary<string, string> { ["runId"] = "r-1" },
            Variables = new Dictionary<string, string> { ["topic"] = "AI" },
            EnvironmentVariables = new Dictionary<string, string> { ["API_KEY"] = "secret" },
            NodeResults = { ["n1"] = "research output" }
        };
    }

    [Fact]
    public void ToVariableContext_ExposesAllFiveSources()
    {
        var state = BuildState();
        var ctx = state.ToVariableContext();

        Assert.Equal("r-1", ctx.System["runId"]);
        Assert.Equal("AI", ctx.Workflow["topic"]);
        Assert.Equal("secret", ctx.Environment["API_KEY"]);
        Assert.Equal("research output", ctx.NodeOutputs["n1"]);
    }

    [Fact]
    public void ToVariableContext_BuildsNodeNameMapFromNodeMap()
    {
        var state = BuildState();
        var ctx = state.ToVariableContext();

        Assert.NotNull(ctx.NodeNameMap);
        Assert.Equal("n1", ctx.NodeNameMap["Researcher"]);
        Assert.Equal("n2", ctx.NodeNameMap["Writer"]);
    }

    [Fact]
    public void ToVariableContext_ResolvesNodeByName()
    {
        var state = BuildState();
        var resolver = state.VariableResolver;

        var result = resolver.Resolve("Summary: {{node:Researcher}}", state.ToVariableContext());

        Assert.Equal("Summary: research output", result);
    }

    [Fact]
    public void ToVariableContext_ResolvesMixedScopesInOnePass()
    {
        var state = BuildState();
        var resolver = state.VariableResolver;

        var result = resolver.Resolve(
            "Run {{sys:runId}} on topic {{var:topic}} with key {{env:API_KEY}}: {{node:Researcher}}",
            state.ToVariableContext());

        Assert.Equal("Run r-1 on topic AI with key secret: research output", result);
    }

    [Fact]
    public void ToVariableContext_NodeNameMapIsCached()
    {
        var state = BuildState();

        var ctx1 = state.ToVariableContext();
        var ctx2 = state.ToVariableContext();

        Assert.Same(ctx1.NodeNameMap, ctx2.NodeNameMap);
    }

    [Fact]
    public void VariableResolver_DefaultsToConcreteImplementation()
    {
        var state = BuildState();
        Assert.IsType<VariableResolver>(state.VariableResolver);
    }

    [Fact]
    public void NodeMap_HoldsStronglyTypedNodeConfig()
    {
        // Phase C 後 NodeMap 直接存 NodeConfig，不再需要 GetTypedNode helper
        var nodeMap = new Dictionary<string, NodeConfig>
        {
            ["a1"] = new AgentNode
            {
                Id = "a1",
                Name = "Researcher",
                Instructions = "Search the web",
                Model = new ModelConfig { Provider = "openai", Model = "gpt-4o" }
            }
        };

        var state = new ImperativeExecutionState
        {
            Adjacency = new(),
            NodeMap = nodeMap,
            Agents = new(),
            ChatClients = new(),
            ChatHistories = new(),
            LoopCounters = new(),
            AgentContext = null!,
            Request = new WorkflowExecutionRequest(),
            HistoryStrategy = null!
        };

        Assert.True(state.NodeMap.TryGetValue("a1", out var node));
        var agent = Assert.IsType<AgentNode>(node);
        Assert.Equal("Researcher", agent.Name);
        Assert.Equal("Search the web", agent.Instructions);
        Assert.Equal("gpt-4o", agent.Model.Model);
    }
}
