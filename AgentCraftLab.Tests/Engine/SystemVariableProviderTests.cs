using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class SystemVariableProviderTests
{
    [Fact]
    public void Build_ContainsAllExpectedKeys()
    {
        var vars = SystemVariableProvider.Build("user1", "exec-1", "TestWorkflow", "hello");

        Assert.True(vars.ContainsKey("user_id"));
        Assert.True(vars.ContainsKey("timestamp"));
        Assert.True(vars.ContainsKey("execution_id"));
        Assert.True(vars.ContainsKey("workflow_name"));
        Assert.True(vars.ContainsKey("user_message"));
    }

    [Fact]
    public void Build_ValuesMatchInput()
    {
        var vars = SystemVariableProvider.Build("alice", "exec-42", "MyFlow", "test message");

        Assert.Equal("alice", vars["user_id"]);
        Assert.Equal("exec-42", vars["execution_id"]);
        Assert.Equal("MyFlow", vars["workflow_name"]);
        Assert.Equal("test message", vars["user_message"]);
    }

    [Fact]
    public void Build_TimestampIsValidIso8601()
    {
        var vars = SystemVariableProvider.Build("u", "e", "w", "m");

        Assert.True(DateTimeOffset.TryParse(vars["timestamp"], out _));
    }
}
