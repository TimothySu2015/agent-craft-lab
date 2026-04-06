using System.Text.Json;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Tests.Flow;

public class WorkflowCrystallizerTests
{
    private readonly WorkflowCrystallizer _crystallizer = new();

    private static ExecutionTrace CreateTrace(params TraceStep[] steps)
    {
        var trace = new ExecutionTrace { Goal = "test" };
        foreach (var s in steps) trace.Steps.Add(s);
        return trace;
    }

    private static TraceStep AgentStep(int seq, string name, string? instructions = null, List<string>? tools = null) => new()
    {
        Sequence = seq,
        NodeType = NodeTypes.Agent,
        NodeName = name,
        Config = new NodeConfig { Instructions = instructions ?? "Do something", Tools = tools }
    };

    private static TraceStep CodeStep(int seq, string name) => new()
    {
        Sequence = seq,
        NodeType = NodeTypes.Code,
        NodeName = name,
        Config = new NodeConfig { TransformType = "template", TransformPattern = "{{input}}" }
    };

    private static TraceStep ParallelStep(int seq, string name, params string[] branchNames) => new()
    {
        Sequence = seq,
        NodeType = NodeTypes.Parallel,
        NodeName = name,
        Config = new NodeConfig
        {
            Branches = branchNames.Select(n => new ParallelBranchConfig { Name = n, Goal = $"Handle {n}" }).ToList(),
            MergeStrategy = "labeled"
        }
    };

    private static TraceStep LoopStep(int seq, string name) => new()
    {
        Sequence = seq,
        NodeType = NodeTypes.Loop,
        NodeName = name,
        Config = new NodeConfig
        {
            ConditionType = "contains",
            ConditionValue = "done",
            MaxIterations = 3,
            Instructions = "Improve the content"
        }
    };

    private static TraceStep IterationStep(int seq, string name) => new()
    {
        Sequence = seq,
        NodeType = NodeTypes.Iteration,
        NodeName = name,
        Config = new NodeConfig
        {
            SplitMode = "json-array",
            Delimiter = "\n",
            MaxItems = 10,
            Instructions = "Process each item"
        }
    };

    [Fact]
    public void SingleAgent_ProducesCorrectJson()
    {
        var trace = CreateTrace(AgentStep(1, "Researcher"));
        var json = _crystallizer.Crystallize(trace);
        var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        Assert.Equal(1, nodes.GetArrayLength());
        Assert.Equal("agent", nodes[0].GetProperty("type").GetString());
    }

    [Fact]
    public void TwoAgents_Connected()
    {
        var trace = CreateTrace(AgentStep(1, "A"), AgentStep(2, "B"));
        var json = _crystallizer.Crystallize(trace);
        var doc = JsonDocument.Parse(json);
        var connections = doc.RootElement.GetProperty("connections");
        Assert.Equal(1, connections.GetArrayLength());
        Assert.Equal(0, connections[0].GetProperty("from").GetInt32());
        Assert.Equal(1, connections[0].GetProperty("to").GetInt32());
    }

    [Fact]
    public void AgentWithTools_PreservedInOutput()
    {
        var trace = CreateTrace(AgentStep(1, "Searcher", tools: ["web_search", "calculator"]));
        var json = _crystallizer.Crystallize(trace);
        var doc = JsonDocument.Parse(json);
        var tools = doc.RootElement.GetProperty("nodes")[0].GetProperty("tools");
        Assert.Equal(2, tools.GetArrayLength());
    }

    [Fact]
    public void CodeNode_TransformFields()
    {
        var trace = CreateTrace(CodeStep(1, "Formatter"));
        var json = _crystallizer.Crystallize(trace);
        var doc = JsonDocument.Parse(json);
        var node = doc.RootElement.GetProperty("nodes")[0];
        Assert.Equal("code", node.GetProperty("type").GetString());
        Assert.Equal("template", node.GetProperty("transformType").GetString());
    }

    [Fact]
    public void ParallelNode_Expanded()
    {
        var trace = CreateTrace(ParallelStep(1, "Research", "AAPL", "MSFT"));
        var json = _crystallizer.Crystallize(trace);
        var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        // parallel + 2 branch agents = 3 nodes
        Assert.Equal(3, nodes.GetArrayLength());
        Assert.Equal("parallel", nodes[0].GetProperty("type").GetString());
        Assert.Equal("agent", nodes[1].GetProperty("type").GetString());
        Assert.Equal("AAPL", nodes[1].GetProperty("name").GetString());
    }

    [Fact]
    public void LoopNode_Expanded()
    {
        var trace = CreateTrace(LoopStep(1, "Refine"));
        var json = _crystallizer.Crystallize(trace);
        var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        // loop + body agent = 2 nodes
        Assert.Equal(2, nodes.GetArrayLength());
        Assert.Equal("loop", nodes[0].GetProperty("type").GetString());
        Assert.Equal("agent", nodes[1].GetProperty("type").GetString());
        Assert.Contains("Body", nodes[1].GetProperty("name").GetString());
    }

    [Fact]
    public void IterationNode_Expanded()
    {
        var trace = CreateTrace(IterationStep(1, "Process"));
        var json = _crystallizer.Crystallize(trace);
        var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        // iteration + body agent = 2 nodes
        Assert.Equal(2, nodes.GetArrayLength());
        Assert.Equal("iteration", nodes[0].GetProperty("type").GetString());
        Assert.Equal("agent", nodes[1].GetProperty("type").GetString());
    }

    [Fact]
    public void ParallelWithNextNode_HasDonePortConnection()
    {
        var trace = CreateTrace(ParallelStep(1, "P", "A", "B"), AgentStep(2, "Summary"));
        var json = _crystallizer.Crystallize(trace);
        var doc = JsonDocument.Parse(json);
        var connections = doc.RootElement.GetProperty("connections");
        // output_1→A, output_2→B, output_3(done)→Summary
        var doneConn = connections.EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("fromOutput").GetString() == "output_3");
        Assert.NotEqual(default, doneConn);
    }
}
