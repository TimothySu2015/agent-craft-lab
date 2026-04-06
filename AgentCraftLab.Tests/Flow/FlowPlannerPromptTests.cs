using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Flow;

public class FlowPlannerPromptTests
{
    [Fact]
    public void Build_WithTools_ContainsToolDescriptions()
    {
        var request = new GoalExecutionRequest
        {
            Goal = "test",
            Credentials = new Dictionary<string, ProviderCredential>(),
            AvailableTools = ["web_search"]
        };
        var descriptions = new Dictionary<string, string>
        {
            ["web_search"] = "Search the web"
        };
        var prompt = FlowPlannerPrompt.Build(request, descriptions);
        Assert.Contains("web_search", prompt);
        Assert.Contains("Search the web", prompt);
    }

    [Fact]
    public void Build_NoTools_ShowsNoToolsAvailable()
    {
        var request = new GoalExecutionRequest
        {
            Goal = "test",
            Credentials = new Dictionary<string, ProviderCredential>(),
            AvailableTools = []
        };
        var prompt = FlowPlannerPrompt.Build(request);
        Assert.Contains("no tools available", prompt);
    }

    [Fact]
    public void Build_ContainsNodeTypeDescriptions()
    {
        var request = new GoalExecutionRequest
        {
            Goal = "test",
            Credentials = new Dictionary<string, ProviderCredential>()
        };
        var prompt = FlowPlannerPrompt.Build(request);
        Assert.Contains("agent", prompt);
        Assert.Contains("code", prompt);
        Assert.Contains("condition", prompt);
        Assert.Contains("parallel", prompt);
        Assert.Contains("iteration", prompt);
        Assert.Contains("loop", prompt);
    }

    [Fact]
    public void Build_ContainsOptimizationRules()
    {
        var request = new GoalExecutionRequest
        {
            Goal = "test",
            Credentials = new Dictionary<string, ProviderCredential>()
        };
        var prompt = FlowPlannerPrompt.Build(request);
        Assert.Contains("REDUNDANT", prompt);
        Assert.Contains("PARALLEL BRANCH ISOLATION", prompt);
    }
}
