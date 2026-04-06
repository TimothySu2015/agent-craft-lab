using AgentCraftLab.Autonomous.Models;

namespace AgentCraftLab.Tests.Autonomous;

public class ReactExecutorConfigTests
{
    [Fact]
    public void Defaults_MaxSubAgents_Is10()
    {
        var config = new ReactExecutorConfig();
        Assert.Equal(10, config.MaxSubAgents);
    }

    [Fact]
    public void Defaults_MaxSpawnTasks_Is15()
    {
        var config = new ReactExecutorConfig();
        Assert.Equal(15, config.MaxSpawnTasks);
    }

    [Fact]
    public void Defaults_PlannerModel_IsGpt4o()
    {
        var config = new ReactExecutorConfig();
        Assert.Equal("gpt-4o", config.PlannerModel);
    }

    [Fact]
    public void PlannerModel_CanBeOverridden()
    {
        var config = new ReactExecutorConfig { PlannerModel = "gpt-4.1" };
        Assert.Equal("gpt-4.1", config.PlannerModel);
    }

    [Fact]
    public void PlannerModel_CanBeNull_FallbackToSameModel()
    {
        var config = new ReactExecutorConfig { PlannerModel = null };
        Assert.Null(config.PlannerModel);
    }

    [Fact]
    public void MaxSpawnTasks_CanBeCustomized()
    {
        var config = new ReactExecutorConfig { MaxSpawnTasks = 20 };
        Assert.Equal(20, config.MaxSpawnTasks);
    }
}
