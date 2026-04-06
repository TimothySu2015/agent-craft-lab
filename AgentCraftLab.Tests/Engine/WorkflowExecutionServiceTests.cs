using System.Text.Json;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class WorkflowExecutionServiceTests
{
    // ════════════════════════════════════════
    // ParseAndValidatePayload
    // ════════════════════════════════════════

    [Fact]
    public void Parse_ValidWorkflowJson_ReturnsPayload()
    {
        var json = JsonSerializer.Serialize(new
        {
            nodes = new[]
            {
                new { id = "a1", type = "agent", name = "Agent 1", provider = "openai", model = "gpt-4o", instructions = "test" }
            },
            connections = Array.Empty<object>()
        });

        var result = WorkflowExecutionService.ParseAndValidatePayload(json, out var error);

        Assert.NotNull(result);
        Assert.Null(error);
        Assert.Single(result.Nodes);
        Assert.Equal("a1", result.Nodes[0].Id);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNullWithError()
    {
        var result = WorkflowExecutionService.ParseAndValidatePayload("not json at all", out var error);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("Invalid workflow JSON", error);
    }

    [Fact]
    public void Parse_EmptyNodes_ReturnsNullWithError()
    {
        var json = JsonSerializer.Serialize(new { nodes = Array.Empty<object>(), connections = Array.Empty<object>() });

        var result = WorkflowExecutionService.ParseAndValidatePayload(json, out var error);

        Assert.Null(result);
        Assert.Equal("Workflow has no nodes.", error);
    }

    [Fact]
    public void Parse_NullDeserialization_ReturnsNullWithError()
    {
        var result = WorkflowExecutionService.ParseAndValidatePayload("null", out var error);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("deserialization returned null", error);
    }

    [Fact]
    public void Parse_LongInvalidJson_TruncatesPreview()
    {
        var longJson = new string('x', 1000);

        var result = WorkflowExecutionService.ParseAndValidatePayload(longJson, out var error);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("...", error); // 確認有截斷
    }

    [Fact]
    public void Parse_ValidJson_PreservesConnections()
    {
        var json = JsonSerializer.Serialize(new
        {
            nodes = new[]
            {
                new { id = "a1", type = "agent", name = "A1", provider = "openai", model = "gpt-4o", instructions = "" },
                new { id = "a2", type = "agent", name = "A2", provider = "openai", model = "gpt-4o", instructions = "" }
            },
            connections = new[]
            {
                new { from = "a1", to = "a2" }
            }
        });

        var result = WorkflowExecutionService.ParseAndValidatePayload(json, out _);

        Assert.NotNull(result);
        Assert.Equal(2, result.Nodes.Count);
        Assert.Single(result.Connections);
        Assert.Equal("a1", result.Connections[0].From);
    }

    // ════════════════════════════════════════
    // ResolveStrategy
    // ════════════════════════════════════════

    private static WorkflowStrategyResolver CreateResolver()
    {
        return new WorkflowStrategyResolver();
    }

    private static Dictionary<string, ChatClientAgent> CreateAgents(params string[] ids)
    {
        var client = new NullChatClient();
        return ids.ToDictionary(id => id, id => new ChatClientAgent(client, $"Agent {id}"));
    }

    private static WorkflowPayload CreatePayload(params (string Id, string Type)[] nodeDefs)
    {
        var nodes = nodeDefs.Select(d => new WorkflowNode { Id = d.Id, Type = d.Type, Name = d.Id }).ToList();
        return new WorkflowPayload { Nodes = nodes };
    }

    [Fact]
    public void Resolve_SingleAgent_ReturnsSingleAgentStrategy()
    {
        var resolver = CreateResolver();
        var payload = CreatePayload(("a1", "agent"));
        var ctx = new AgentExecutionContext(CreateAgents("a1"), new(), new(), new());

        var (strategy, reason) = resolver.Resolve(payload, ctx, [], new WorkflowExecutionRequest());

        Assert.IsType<SingleAgentStrategy>(strategy);
        Assert.Equal("singleAgent", reason);
    }

    [Fact]
    public void Resolve_HasA2ANodes_ReturnsImperative()
    {
        var resolver = CreateResolver();
        var payload = CreatePayload(("a1", "agent"));
        var ctx = new AgentExecutionContext(CreateAgents("a1"), new(), new(), new());

        var (strategy, reason) = resolver.Resolve(payload, ctx, [], new WorkflowExecutionRequest(), hasA2AOrAutonomousNodes: true);

        Assert.IsType<ImperativeWorkflowStrategy>(strategy);
        Assert.Equal("hasA2ANodes", reason);
    }

    [Fact]
    public void Resolve_MultiAgent_HasHumanNode_ReturnsImperative()
    {
        var resolver = CreateResolver();
        var payload = CreatePayload(("a1", "agent"), ("a2", "agent"), ("h1", "human"));
        var ctx = new AgentExecutionContext(CreateAgents("a1", "a2"), new(), new(), new());

        var (strategy, reason) = resolver.Resolve(payload, ctx, [], new WorkflowExecutionRequest());

        Assert.IsType<ImperativeWorkflowStrategy>(strategy);
        Assert.Equal("hasImperativeNodes", reason);
    }

    [Fact]
    public void Resolve_MultiAgent_HasCodeNode_ReturnsImperative()
    {
        var resolver = CreateResolver();
        var payload = CreatePayload(("a1", "agent"), ("a2", "agent"), ("c1", "code"));
        var ctx = new AgentExecutionContext(CreateAgents("a1", "a2"), new(), new(), new());

        var (strategy, reason) = resolver.Resolve(payload, ctx, [], new WorkflowExecutionRequest());

        Assert.IsType<ImperativeWorkflowStrategy>(strategy);
        Assert.Equal("hasImperativeNodes", reason);
    }

    [Fact]
    public void Resolve_MultiAgent_HasAttachment_ReturnsImperative()
    {
        var resolver = CreateResolver();
        var payload = CreatePayload(("a1", "agent"), ("a2", "agent"));
        var ctx = new AgentExecutionContext(CreateAgents("a1", "a2"), new(), new(), new());
        var request = new WorkflowExecutionRequest { Attachment = new FileAttachment { Data = [1, 2, 3], FileName = "test.pdf" } };

        var (strategy, reason) = resolver.Resolve(payload, ctx, [], request);

        Assert.IsType<ImperativeWorkflowStrategy>(strategy);
        Assert.Equal("hasAttachment", reason);
    }

    [Fact]
    public void Resolve_SingleAgent_IgnoresSpecialNodes()
    {
        // SingleAgent 優先級高於 hasSpecialNodes — 只有 1 個 agent 時直接走 SingleAgentStrategy
        var resolver = CreateResolver();
        var payload = CreatePayload(("a1", "agent"), ("c1", "code"));
        var ctx = new AgentExecutionContext(CreateAgents("a1"), new(), new(), new());

        var (strategy, reason) = resolver.Resolve(payload, ctx, [], new WorkflowExecutionRequest());

        Assert.IsType<SingleAgentStrategy>(strategy);
        Assert.Equal("singleAgent", reason);
    }

    [Fact]
    public void Resolve_ExplicitSequential_ReturnsSequential()
    {
        var resolver = CreateResolver();
        var payload = CreatePayload(("a1", "agent"), ("a2", "agent"));
        payload.WorkflowSettings = new WorkflowSettings { Type = WorkflowTypes.Sequential };
        var ctx = new AgentExecutionContext(CreateAgents("a1", "a2"), new(), new(), new());

        var (strategy, reason) = resolver.Resolve(payload, ctx, [], new WorkflowExecutionRequest());

        Assert.IsType<SequentialWorkflowStrategy>(strategy);
        Assert.Contains("sequential", reason);
    }

    [Fact]
    public void Resolve_ExplicitConcurrent_ReturnsConcurrent()
    {
        var resolver = CreateResolver();
        var payload = CreatePayload(("a1", "agent"), ("a2", "agent"));
        payload.WorkflowSettings = new WorkflowSettings { Type = WorkflowTypes.Concurrent };
        var ctx = new AgentExecutionContext(CreateAgents("a1", "a2"), new(), new(), new());

        var (strategy, reason) = resolver.Resolve(payload, ctx, [], new WorkflowExecutionRequest());

        Assert.IsType<ConcurrentWorkflowStrategy>(strategy);
        Assert.Contains("concurrent", reason);
    }

    [Fact]
    public void Resolve_ExplicitHandoff_ReturnsHandoff()
    {
        var resolver = CreateResolver();
        var payload = CreatePayload(("a1", "agent"), ("a2", "agent"));
        payload.WorkflowSettings = new WorkflowSettings { Type = WorkflowTypes.Handoff };
        var ctx = new AgentExecutionContext(CreateAgents("a1", "a2"), new(), new(), new());

        var (strategy, reason) = resolver.Resolve(payload, ctx, [], new WorkflowExecutionRequest());

        Assert.IsType<HandoffWorkflowStrategy>(strategy);
        Assert.Contains("handoff", reason);
    }

    // ════════════════════════════════════════
    // ResolveStrategy — Priority 測試
    // ════════════════════════════════════════

    [Fact]
    public void Resolve_A2ANodes_TakesPriorityOverSingleAgent()
    {
        var resolver = CreateResolver();
        var payload = CreatePayload(("a1", "agent"));
        var ctx = new AgentExecutionContext(CreateAgents("a1"), new(), new(), new());

        var (strategy, _) = resolver.Resolve(payload, ctx, [], new WorkflowExecutionRequest(), hasA2AOrAutonomousNodes: true);

        // 即使只有 1 個 agent，hasA2ANodes 應優先
        Assert.IsType<ImperativeWorkflowStrategy>(strategy);
    }

    [Fact]
    public void Resolve_HumanNode_TakesPriorityOverExplicitSequential()
    {
        var resolver = CreateResolver();
        var payload = CreatePayload(("a1", "agent"), ("a2", "agent"), ("h1", "human"));
        payload.WorkflowSettings = new WorkflowSettings { Type = WorkflowTypes.Sequential };
        var ctx = new AgentExecutionContext(CreateAgents("a1", "a2"), new(), new(), new());

        var (strategy, reason) = resolver.Resolve(payload, ctx, [], new WorkflowExecutionRequest());

        // Human 節點應強制 Imperative，忽略 Sequential 設定
        Assert.IsType<ImperativeWorkflowStrategy>(strategy);
        Assert.Equal("hasImperativeNodes", reason);
    }

    // ════════════════════════════════════════
    // CreateHookContext
    // ════════════════════════════════════════

    [Fact]
    public void CreateHookContext_PopulatesAllFields()
    {
        var request = new WorkflowExecutionRequest { UserMessage = "hello" };

        var ctx = WorkflowExecutionService.CreateHookContext(request, "user-1", "MyWorkflow", output: "done", error: null);

        Assert.Equal("hello", ctx.Input);
        Assert.Equal("user-1", ctx.UserId);
        Assert.Equal("MyWorkflow", ctx.WorkflowName);
        Assert.Equal("done", ctx.Output);
        Assert.Null(ctx.Error);
    }

    [Fact]
    public void CreateHookContext_WithError()
    {
        var request = new WorkflowExecutionRequest { UserMessage = "test" };

        var ctx = WorkflowExecutionService.CreateHookContext(request, "user-1", "WF", error: "boom");

        Assert.Equal("boom", ctx.Error);
    }
}

/// <summary>測試用 NullChatClient — 不做任何 LLM 呼叫。</summary>
file class NullChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "test")));

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        => AsyncEnumerable.Empty<ChatResponseUpdate>();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
