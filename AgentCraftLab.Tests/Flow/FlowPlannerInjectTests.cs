using AgentCraftLab.Data;
using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Flow;

public class FlowPlannerInjectTests
{
    private static GoalExecutionRequest CreateRequest(string goal = "test") => new()
    {
        Goal = goal,
        Credentials = new Dictionary<string, ProviderCredential>(),
        AvailableTools = ["AzureWebSearch"]
    };

    [Fact]
    public void Build_WithExperienceHint_ContainsReferencePlan()
    {
        var hint = "{\"nodes\":[{\"nodeType\":\"parallel\",\"name\":\"Research\"}]}";
        var prompt = FlowPlannerPrompt.Build(CreateRequest(), experienceHint: hint);

        Assert.Contains("Reference Plan", prompt);
        Assert.Contains("past execution", prompt);
        Assert.Contains("parallel", prompt);
    }

    [Fact]
    public void Build_WithNullExperienceHint_NoReferencePlan()
    {
        var prompt = FlowPlannerPrompt.Build(CreateRequest(), experienceHint: null);
        Assert.DoesNotContain("Reference Plan", prompt);
    }

    [Fact]
    public void Build_WithEmptyExperienceHint_NoReferencePlan()
    {
        var prompt = FlowPlannerPrompt.Build(CreateRequest(), experienceHint: "");
        Assert.DoesNotContain("Reference Plan", prompt);
    }

    [Fact]
    public void Build_ExperienceHint_AppearsAfterRules()
    {
        var hint = "{\"nodes\":[]}";
        var prompt = FlowPlannerPrompt.Build(CreateRequest(), experienceHint: hint);

        var rulesIndex = prompt.IndexOf("Optimization Rules", StringComparison.Ordinal);
        var hintIndex = prompt.IndexOf("Reference Plan", StringComparison.Ordinal);

        Assert.True(rulesIndex > 0);
        Assert.True(hintIndex > rulesIndex, "Reference Plan should appear after Optimization Rules");
    }

    [Fact]
    public void Build_ExperienceHint_WrappedInJsonCodeBlock()
    {
        var hint = "{\"nodes\":[{\"nodeType\":\"agent\"}]}";
        var prompt = FlowPlannerPrompt.Build(CreateRequest(), experienceHint: hint);

        Assert.Contains("```json", prompt);
        Assert.Contains(hint, prompt);
    }
}
