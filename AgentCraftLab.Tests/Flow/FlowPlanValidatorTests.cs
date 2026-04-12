using AgentCraftLab.Data;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Tests.Flow;

public class FlowPlanValidatorTests
{
    private static GoalExecutionRequest CreateRequest(params string[] tools) => new()
    {
        Goal = "test",
        Credentials = new Dictionary<string, ProviderCredential>(),
        AvailableTools = tools.ToList()
    };

    // Phase F：FlowPlan.Nodes 是 Schema.NodeConfig，用 sealed record 建 fixture。
    [Fact]
    public void RemovesUnsupportedNodeType()
    {
        // 建一個 HumanNode — Flow 不支援此型別
        var plan = new FlowPlan
        {
            Nodes = [new Schema.HumanNode { Name = "Bad" }]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.Empty(result.Nodes);
        Assert.Contains(warnings, w => w.Contains("unsupported"));
    }

    [Fact]
    public void FiltersInvalidToolIds()
    {
        var plan = new FlowPlan
        {
            Nodes = [new Schema.AgentNode { Name = "A", Tools = ["valid", "invalid"] }]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest("valid"));
        Assert.Single(result.Nodes);
        var agent = Assert.IsType<Schema.AgentNode>(result.Nodes[0]);
        Assert.Single(agent.Tools);
        Assert.Equal("valid", agent.Tools[0]);
        Assert.Contains(warnings, w => w.Contains("invalid"));
    }

    [Fact]
    public void CapsParallelBranches()
    {
        var branches = Enumerable.Range(1, 10)
            .Select(i => new Schema.BranchConfig { Name = $"B{i}", Goal = $"Goal {i}" })
            .ToList();
        var plan = new FlowPlan
        {
            Nodes = [new Schema.ParallelNode { Name = "P", Branches = branches }]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        var parallel = Assert.IsType<Schema.ParallelNode>(result.Nodes[0]);
        Assert.Equal(6, parallel.Branches.Count);
        Assert.Contains(warnings, w => w.Contains("Trimmed"));
    }

    [Fact]
    public void CapsLoopMaxIterations()
    {
        var plan = new FlowPlan
        {
            Nodes = [new Schema.LoopNode { Name = "L", MaxIterations = 50 }]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        var loop = Assert.IsType<Schema.LoopNode>(result.Nodes[0]);
        Assert.Equal(10, loop.MaxIterations);
        Assert.Contains(warnings, w => w.Contains("Capped"));
    }

    [Fact]
    public void ConditionAtEnd_Removed()
    {
        var plan = new FlowPlan
        {
            Nodes = [new Schema.ConditionNode { Name = "C" }]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.Empty(result.Nodes);
        Assert.Contains(warnings, w => w.Contains("no branches"));
    }

    [Fact]
    public void ConditionBranchIndex_OutOfRange_Warning()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new Schema.ConditionNode
                {
                    Name = "C",
                    Meta = NodeConfigHelpers.WithBranchIndices(null, trueBranchIndex: 99, falseBranchIndex: null)
                },
                new Schema.AgentNode { Name = "A" }
            ]
        };
        var (_, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.Contains(warnings, w => w.Contains("TrueBranchIndex"));
    }

    [Fact]
    public void TotalNodesExceedMax_Trimmed()
    {
        var nodes = Enumerable.Range(1, 20)
            .Select(i => (Schema.NodeConfig)new Schema.AgentNode { Name = $"A{i}" })
            .ToList();
        var plan = new FlowPlan { Nodes = nodes };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.Equal(15, result.Nodes.Count);
        Assert.Contains(warnings, w => w.Contains("trimmed"));
    }

    [Fact]
    public void ValidPlan_NoChanges()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new Schema.AgentNode { Name = "A", Tools = ["search"] },
                new Schema.CodeNode { Name = "C" }
            ]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest("search"));
        Assert.Equal(2, result.Nodes.Count);
        Assert.Empty(warnings);
    }

    [Fact]
    public void LoopUnderMax_NoChange()
    {
        var plan = new FlowPlan
        {
            Nodes = [new Schema.LoopNode { Name = "L", MaxIterations = 5 }]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        var loop = Assert.IsType<Schema.LoopNode>(result.Nodes[0]);
        Assert.Equal(5, loop.MaxIterations);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ParallelBranchTools_Filtered()
    {
        var branches = new List<Schema.BranchConfig>
        {
            new() { Name = "B1", Goal = "G1", Tools = ["valid", "bad"] }
        };
        var plan = new FlowPlan
        {
            Nodes = [new Schema.ParallelNode { Name = "P", Branches = branches }]
        };
        var (result, _) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest("valid"));
        var parallel = Assert.IsType<Schema.ParallelNode>(result.Nodes[0]);
        Assert.Single(parallel.Branches[0].Tools!);
        Assert.Equal("valid", parallel.Branches[0].Tools![0]);
    }

    [Fact]
    public void MixedNodeTypes_OnlyUnsupportedRemoved()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new Schema.AgentNode { Name = "A" },
                new Schema.HumanNode { Name = "Bad" }, // unsupported in Flow
                new Schema.CodeNode { Name = "C" }
            ]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.Equal(2, result.Nodes.Count);
        Assert.Equal("A", result.Nodes[0].Name);
        Assert.Equal("C", result.Nodes[1].Name);
        Assert.Single(warnings);
    }
}
