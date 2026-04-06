using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Tests.Flow;

public class PlannedNodeTests
{
    [Fact]
    public void ToConfig_MapsAllFields()
    {
        var node = new PlannedNode
        {
            NodeType = NodeTypes.Agent,
            Name = "Test",
            Instructions = "Do stuff",
            Tools = ["search"],
            Provider = "openai",
            Model = "gpt-4o",
            ConditionType = "contains",
            ConditionValue = "done",
            MaxIterations = 5,
            TransformType = "template",
            TransformPattern = "{{input}}",
            TransformReplacement = "X",
            Branches = [new ParallelBranchConfig { Name = "B1", Goal = "G1" }],
            MergeStrategy = "labeled",
            SplitMode = "json-array",
            Delimiter = ",",
            MaxItems = 10,
            HttpApiId = "api1",
            HttpArgsTemplate = "{}",
            OutputFormat = "json",
            OutputSchema = "{}"
        };

        var config = node.ToConfig();

        Assert.Equal("Do stuff", config.Instructions);
        Assert.Single(config.Tools!);
        Assert.Equal("openai", config.Provider);
        Assert.Equal("gpt-4o", config.Model);
        Assert.Equal("contains", config.ConditionType);
        Assert.Equal("done", config.ConditionValue);
        Assert.Equal(5, config.MaxIterations);
        Assert.Equal("template", config.TransformType);
        Assert.Equal("{{input}}", config.TransformPattern);
        Assert.Equal("X", config.TransformReplacement);
        Assert.Single(config.Branches!);
        Assert.Equal("labeled", config.MergeStrategy);
        Assert.Equal("json-array", config.SplitMode);
        Assert.Equal(",", config.Delimiter);
        Assert.Equal(10, config.MaxItems);
        Assert.Equal("api1", config.HttpApiId);
        Assert.Equal("{}", config.HttpArgsTemplate);
        Assert.Equal("json", config.OutputFormat);
        Assert.Equal("{}", config.OutputSchema);
    }

    [Fact]
    public void ToConfig_NullFields_StayNull()
    {
        var node = new PlannedNode { NodeType = NodeTypes.Agent, Name = "Minimal" };
        var config = node.ToConfig();
        Assert.Null(config.Instructions);
        Assert.Null(config.Tools);
        Assert.Null(config.Branches);
        Assert.Null(config.OutputFormat);
    }
}
