using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class GoalRequestTests
{
    private static readonly Dictionary<string, ProviderCredential> TestCredentials = new()
    {
        ["openai"] = new ProviderCredential { ApiKey = "test-key" }
    };

    [Fact]
    public void FromNodeRequest_MapsAllFields()
    {
        var nodeReq = new AutonomousNodeRequest
        {
            Goal = "Test goal",
            Credentials = TestCredentials,
            Provider = "openai",
            Model = "gpt-4o",
            AvailableTools = ["web_search", "calculator"],
            AvailableSkills = ["skill1"],
            McpServers = ["http://mcp.local"],
            A2AAgents = ["http://a2a.local"],
            HttpApis = new Dictionary<string, HttpApiDefinition>
            {
                ["api1"] = new() { Url = "http://api.local" }
            },
            MaxIterations = 10,
            MaxTotalTokens = 50_000,
            MaxToolCalls = 30
        };

        var goalReq = GoalExecutionRequest.FromNodeRequest(nodeReq);

        Assert.Equal("Test goal", goalReq.Goal);
        Assert.Equal("openai", goalReq.Provider);
        Assert.Equal("gpt-4o", goalReq.Model);
        Assert.Equal(2, goalReq.AvailableTools.Count);
        Assert.Single(goalReq.AvailableSkills);
        Assert.Single(goalReq.McpServers);
        Assert.Single(goalReq.A2AAgents);
        Assert.Single(goalReq.HttpApis);
        Assert.Equal(10, goalReq.MaxIterations);
        Assert.Equal(50_000, goalReq.MaxTotalTokens);
        Assert.Equal(30, goalReq.MaxToolCalls);
    }

    [Fact]
    public void FromNodeRequest_DefaultValues()
    {
        var nodeReq = new AutonomousNodeRequest
        {
            Goal = "Simple",
            Credentials = TestCredentials
        };

        var goalReq = GoalExecutionRequest.FromNodeRequest(nodeReq);

        Assert.Equal(25, goalReq.MaxIterations);
        Assert.Equal(200_000, goalReq.MaxTotalTokens);
        Assert.Equal(50, goalReq.MaxToolCalls);
        Assert.Null(goalReq.Attachment);
    }

    [Fact]
    public void GoalExecutionRequest_Defaults()
    {
        var req = new GoalExecutionRequest
        {
            Goal = "test",
            Credentials = TestCredentials
        };

        Assert.Equal("local", req.UserId);
        Assert.Equal(Defaults.Provider, req.Provider);
        Assert.Equal(Defaults.Model, req.Model);
        Assert.Empty(req.AvailableTools);
        Assert.Equal(25, req.MaxIterations);
        Assert.Equal(200_000, req.MaxTotalTokens);
        Assert.Null(req.Options);
    }

    [Fact]
    public void GoalExecutionRequest_ExecutionId_UniquePerInstance()
    {
        var req1 = new GoalExecutionRequest { Goal = "a", Credentials = TestCredentials };
        var req2 = new GoalExecutionRequest { Goal = "b", Credentials = TestCredentials };
        Assert.NotEqual(req1.ExecutionId, req2.ExecutionId);
    }
}
