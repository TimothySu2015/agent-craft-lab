using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Strategies;
using SchemaPayload = AgentCraftLab.Engine.Models.Schema.WorkflowPayload;

namespace AgentCraftLab.Tests.Engine;

public class VariableInitializationTests
{
    [Fact]
    public void InitializeVariables_CollectsDefaults()
    {
        var payload = new SchemaPayload
        {
            Variables =
            [
                new VariableDef { Name = "counter", DefaultValue = "0" },
                new VariableDef { Name = "name", DefaultValue = "default" },
            ]
        };
        var request = new WorkflowExecutionRequest();

        var vars = ImperativeWorkflowStrategy.InitializeVariables(payload, request);

        Assert.Equal("0", vars["counter"]);
        Assert.Equal("default", vars["name"]);
    }

    [Fact]
    public void InitializeVariables_RuntimeOverridesDefaults()
    {
        var payload = new SchemaPayload
        {
            Variables = [new VariableDef { Name = "counter", DefaultValue = "0" }]
        };
        var request = new WorkflowExecutionRequest
        {
            RuntimeVariables = new() { ["counter"] = "99" }
        };

        var vars = ImperativeWorkflowStrategy.InitializeVariables(payload, request);

        Assert.Equal("99", vars["counter"]);
    }

    [Fact]
    public void InitializeVariables_RuntimeAddsNewKeys()
    {
        var payload = new SchemaPayload();
        var request = new WorkflowExecutionRequest
        {
            RuntimeVariables = new() { ["extra"] = "value" }
        };

        var vars = ImperativeWorkflowStrategy.InitializeVariables(payload, request);

        Assert.Equal("value", vars["extra"]);
    }

    [Fact]
    public void InitializeVariables_NullRuntime_DefaultsOnly()
    {
        var payload = new SchemaPayload
        {
            Variables = [new VariableDef { Name = "x", DefaultValue = "1" }]
        };
        var request = new WorkflowExecutionRequest { RuntimeVariables = null };

        var vars = ImperativeWorkflowStrategy.InitializeVariables(payload, request);

        Assert.Single(vars);
        Assert.Equal("1", vars["x"]);
    }

    [Fact]
    public void InitializeVariables_EmptyPayloadAndNullRuntime_EmptyDict()
    {
        var vars = ImperativeWorkflowStrategy.InitializeVariables(
            new SchemaPayload(), new WorkflowExecutionRequest());

        Assert.Empty(vars);
    }

    [Fact]
    public void InitializeVariables_CaseInsensitive()
    {
        var payload = new SchemaPayload
        {
            Variables = [new VariableDef { Name = "Counter", DefaultValue = "0" }]
        };
        var request = new WorkflowExecutionRequest();

        var vars = ImperativeWorkflowStrategy.InitializeVariables(payload, request);

        Assert.Equal("0", vars["counter"]);
        Assert.Equal("0", vars["COUNTER"]);
    }

    // ─── Checkpoint round-trip ───

    [Fact]
    public void CheckpointSnapshot_Variables_RoundTrip()
    {
        var snapshot = new ImperativeCheckpointSnapshot
        {
            CompletedNodeIds = ["node-1"],
            PreviousResult = "test",
            NextNodeId = "node-2",
            Variables = new() { ["counter"] = "5", ["name"] = "alice" }
        };

        var json = snapshot.Serialize();
        var restored = ImperativeCheckpointSnapshot.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal("5", restored!.Variables["counter"]);
        Assert.Equal("alice", restored.Variables["name"]);
    }

    [Fact]
    public void CheckpointSnapshot_NoVariablesInJson_DeserializesAsEmpty()
    {
        // Simulate old checkpoint JSON without variables field
        var json = """{"completedNodeIds":["n1"],"previousResult":"ok","nextNodeId":"n2"}""";
        var restored = ImperativeCheckpointSnapshot.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Empty(restored!.Variables);
    }
}
