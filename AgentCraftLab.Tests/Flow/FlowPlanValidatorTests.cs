using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Flow;

public class FlowPlanValidatorTests
{
    private static GoalExecutionRequest CreateRequest(params string[] tools) => new()
    {
        Goal = "test",
        Credentials = new Dictionary<string, ProviderCredential>(),
        AvailableTools = tools.ToList()
    };

    [Fact]
    public void RemovesUnsupportedNodeType()
    {
        var plan = new FlowPlan
        {
            Nodes = [new PlannedNode { NodeType = "unknown-type", Name = "Bad" }]
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
            Nodes = [new PlannedNode { NodeType = "agent", Name = "A", Tools = ["valid", "invalid"] }]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest("valid"));
        Assert.Single(result.Nodes);
        Assert.Single(result.Nodes[0].Tools!);
        Assert.Equal("valid", result.Nodes[0].Tools![0]);
        Assert.Contains(warnings, w => w.Contains("invalid"));
    }

    [Fact]
    public void CapsParallelBranches()
    {
        var branches = Enumerable.Range(1, 10)
            .Select(i => new ParallelBranchConfig { Name = $"B{i}", Goal = $"Goal {i}" })
            .ToList();
        var plan = new FlowPlan
        {
            Nodes = [new PlannedNode { NodeType = "parallel", Name = "P", Branches = branches }]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.Equal(6, result.Nodes[0].Branches!.Count);
        Assert.Contains(warnings, w => w.Contains("Trimmed"));
    }

    [Fact]
    public void CapsLoopMaxIterations()
    {
        var plan = new FlowPlan
        {
            Nodes = [new PlannedNode { NodeType = "loop", Name = "L", MaxIterations = 50 }]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.Equal(10, result.Nodes[0].MaxIterations);
        Assert.Contains(warnings, w => w.Contains("Capped"));
    }

    [Fact]
    public void ConditionAtEnd_Removed()
    {
        var plan = new FlowPlan
        {
            Nodes = [new PlannedNode { NodeType = "condition", Name = "C" }]
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
                new PlannedNode { NodeType = "condition", Name = "C", TrueBranchIndex = 99 },
                new PlannedNode { NodeType = "agent", Name = "A" }
            ]
        };
        var (_, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.Contains(warnings, w => w.Contains("TrueBranchIndex"));
    }

    [Fact]
    public void TotalNodesExceedMax_Trimmed()
    {
        var nodes = Enumerable.Range(1, 20)
            .Select(i => new PlannedNode { NodeType = "agent", Name = $"A{i}" })
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
                new PlannedNode { NodeType = "agent", Name = "A", Tools = ["search"] },
                new PlannedNode { NodeType = "code", Name = "C" }
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
            Nodes = [new PlannedNode { NodeType = "loop", Name = "L", MaxIterations = 5 }]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.Equal(5, result.Nodes[0].MaxIterations);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ParallelBranchTools_Filtered()
    {
        var branches = new List<ParallelBranchConfig>
        {
            new() { Name = "B1", Goal = "G1", Tools = ["valid", "bad"] }
        };
        var plan = new FlowPlan
        {
            Nodes = [new PlannedNode { NodeType = "parallel", Name = "P", Branches = branches }]
        };
        var (result, _) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest("valid"));
        Assert.Single(result.Nodes[0].Branches![0].Tools!);
        Assert.Equal("valid", result.Nodes[0].Branches![0].Tools![0]);
    }

    [Fact]
    public void MixedNodeTypes_OnlyUnsupportedRemoved()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new PlannedNode { NodeType = "agent", Name = "A" },
                new PlannedNode { NodeType = "unknown-type", Name = "Bad" },
                new PlannedNode { NodeType = "code", Name = "C" }
            ]
        };
        var (result, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.Equal(2, result.Nodes.Count);
        Assert.Equal("A", result.Nodes[0].Name);
        Assert.Equal("C", result.Nodes[1].Name);
        Assert.Single(warnings);
    }
}
