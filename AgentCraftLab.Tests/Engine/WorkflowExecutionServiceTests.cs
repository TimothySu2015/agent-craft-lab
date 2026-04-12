using System.Text.Json;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SchemaPayload = AgentCraftLab.Engine.Models.Schema.WorkflowPayload;

namespace AgentCraftLab.Tests.Engine;

public class WorkflowExecutionServiceTests
{
    // ════════════════════════════════════════
    // ParseAndValidatePayload
    // ════════════════════════════════════════

    [Fact]
    public void Parse_FlatJsonWithoutDiscriminator_ReturnsError()
    {
        // F2b 之後後端只接受 Schema v2（需有 type discriminator + nested 結構）
        // 送 flat JSON 沒有 type discriminator 會被 polymorphic deserializer 拒絕
        var json = JsonSerializer.Serialize(new
        {
            nodes = new[]
            {
                new { id = "a1", name = "Agent 1", provider = "openai", model = "gpt-4o", instructions = "test" }
            },
            connections = Array.Empty<object>()
        });

        var (payload, _, _, error) = WorkflowExecutionService.ParseAndValidatePayload(json);

        Assert.Null(payload);
        Assert.NotNull(error);
        Assert.Contains("type discriminator", error);
    }

    [Fact]
    public void Parse_ValidSchemaJson_ReturnsSchemaPayload()
    {
        // 新 Schema v2 格式：頂層有 version + settings，節點是 nested discriminator union
        var json = """
        {
          "version": "2.0",
          "settings": { "strategy": "auto" },
          "nodes": [
            {
              "type": "agent",
              "id": "a1",
              "name": "Agent 1",
              "instructions": "test",
              "model": { "provider": "openai", "model": "gpt-4o" }
            }
          ],
          "connections": []
        }
        """;

        var (payload, _, _, error) = WorkflowExecutionService.ParseAndValidatePayload(json);

        Assert.NotNull(payload);
        Assert.Null(error);
        Assert.Equal("2.0", payload.Version);
        Assert.Single(payload.Nodes);
        var agent = Assert.IsType<AgentCraftLab.Engine.Models.Schema.AgentNode>(payload.Nodes[0]);
        Assert.Equal("a1", agent.Id);
        Assert.Equal("gpt-4o", agent.Model.Model);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNullWithError()
    {
        var (payload, _, _, error) = WorkflowExecutionService.ParseAndValidatePayload("not json at all");

        Assert.Null(payload);
        Assert.NotNull(error);
        Assert.Contains("Invalid workflow JSON", error);
    }

    [Fact]
    public void Parse_EmptyNodes_ReturnsNullWithError()
    {
        var json = JsonSerializer.Serialize(new { nodes = Array.Empty<object>(), connections = Array.Empty<object>() });

        var (payload, _, _, error) = WorkflowExecutionService.ParseAndValidatePayload(json);

        Assert.Null(payload);
        Assert.Equal("Workflow has no nodes.", error);
    }

    [Fact]
    public void Parse_NullDeserialization_ReturnsNullWithError()
    {
        var (payload, _, _, error) = WorkflowExecutionService.ParseAndValidatePayload("null");

        Assert.Null(payload);
        Assert.NotNull(error);
        Assert.Contains("deserialization returned null", error);
    }

    [Fact]
    public void Parse_LongInvalidJson_TruncatesPreview()
    {
        var longJson = new string('x', 1000);

        var (payload, _, _, error) = WorkflowExecutionService.ParseAndValidatePayload(longJson);

        Assert.Null(payload);
        Assert.NotNull(error);
        Assert.Contains("...", error); // 確認有截斷
    }

    [Fact]
    public void Parse_SchemaJson_PreservesConnections()
    {
        var json = """
        {
          "version": "2.0",
          "settings": { "strategy": "auto" },
          "nodes": [
            { "type": "agent", "id": "a1", "name": "A1", "model": { "provider": "openai", "model": "gpt-4o" } },
            { "type": "agent", "id": "a2", "name": "A2", "model": { "provider": "openai", "model": "gpt-4o" } }
          ],
          "connections": [
            { "from": "a1", "to": "a2", "port": "output_1" }
          ]
        }
        """;

        var (payload, _, _, _) = WorkflowExecutionService.ParseAndValidatePayload(json);

        Assert.NotNull(payload);
        Assert.Equal(2, payload.Nodes.Count);
        Assert.Single(payload.Connections);
        Assert.Equal("a1", payload.Connections[0].From);
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

    private static SchemaPayload CreatePayload(params (string Id, string Type)[] nodeDefs)
    {
        // Phase C：測試直接建構 Schema.WorkflowPayload，避開 old→new 轉換
        var nodes = nodeDefs
            .Select(d => (AgentCraftLab.Engine.Models.Schema.NodeConfig)(d.Type switch
            {
                NodeTypes.Agent => new AgentCraftLab.Engine.Models.Schema.AgentNode { Id = d.Id, Name = d.Id },
                NodeTypes.A2AAgent => new AgentCraftLab.Engine.Models.Schema.A2AAgentNode { Id = d.Id, Name = d.Id },
                NodeTypes.Autonomous => new AgentCraftLab.Engine.Models.Schema.AutonomousNode { Id = d.Id, Name = d.Id },
                NodeTypes.Human => new AgentCraftLab.Engine.Models.Schema.HumanNode { Id = d.Id, Name = d.Id },
                NodeTypes.Code => new AgentCraftLab.Engine.Models.Schema.CodeNode { Id = d.Id, Name = d.Id },
                NodeTypes.Condition => new AgentCraftLab.Engine.Models.Schema.ConditionNode { Id = d.Id, Name = d.Id },
                NodeTypes.Loop => new AgentCraftLab.Engine.Models.Schema.LoopNode { Id = d.Id, Name = d.Id },
                NodeTypes.Router => new AgentCraftLab.Engine.Models.Schema.RouterNode { Id = d.Id, Name = d.Id },
                NodeTypes.Iteration => new AgentCraftLab.Engine.Models.Schema.IterationNode { Id = d.Id, Name = d.Id },
                NodeTypes.Parallel => new AgentCraftLab.Engine.Models.Schema.ParallelNode { Id = d.Id, Name = d.Id },
                NodeTypes.HttpRequest => new AgentCraftLab.Engine.Models.Schema.HttpRequestNode { Id = d.Id, Name = d.Id },
                NodeTypes.Start => new AgentCraftLab.Engine.Models.Schema.StartNode { Id = d.Id, Name = d.Id },
                NodeTypes.End => new AgentCraftLab.Engine.Models.Schema.EndNode { Id = d.Id, Name = d.Id },
                _ => throw new NotSupportedException($"Unknown type: {d.Type}")
            }))
            .ToList();
        return new AgentCraftLab.Engine.Models.Schema.WorkflowPayload { Nodes = nodes };
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
        payload = payload with { Settings = new AgentCraftLab.Engine.Models.Schema.WorkflowSettings { Strategy = WorkflowTypes.Sequential } };
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
        payload = payload with { Settings = new AgentCraftLab.Engine.Models.Schema.WorkflowSettings { Strategy = WorkflowTypes.Concurrent } };
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
        payload = payload with { Settings = new AgentCraftLab.Engine.Models.Schema.WorkflowSettings { Strategy = WorkflowTypes.Handoff } };
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
        payload = payload with { Settings = new AgentCraftLab.Engine.Models.Schema.WorkflowSettings { Strategy = WorkflowTypes.Sequential } };
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

    // ════════════════════════════════════════
    // Parse — All Node Types Round-Trip
    // ════════════════════════════════════════

    [Fact]
    public void Parse_SchemaJson_AllNodeTypes_DeserializesCorrectly()
    {
        var json = """
        {
          "version": "2.0",
          "settings": { "strategy": "auto" },
          "nodes": [
            { "type": "agent", "id": "n1", "name": "A", "model": { "provider": "openai", "model": "gpt-4o" } },
            { "type": "a2a-agent", "id": "n2", "name": "B", "url": "http://x", "format": "auto" },
            { "type": "autonomous", "id": "n3", "name": "C", "model": { "provider": "openai", "model": "gpt-4o" } },
            { "type": "condition", "id": "n4", "name": "D", "condition": { "kind": "contains", "value": "yes" } },
            { "type": "loop", "id": "n5", "name": "E", "condition": { "kind": "regex", "value": "done" }, "maxIterations": 3, "bodyAgent": { "type": "agent", "id": "ba", "name": "BA" } },
            { "type": "router", "id": "n6", "name": "F", "routes": [{ "name": "r1", "keywords": [], "isDefault": false }] },
            { "type": "human", "id": "n7", "name": "G", "prompt": "ok?", "kind": "approval" },
            { "type": "code", "id": "n8", "name": "H", "kind": "template", "expression": "{{input}}" },
            { "type": "iteration", "id": "n9", "name": "I", "split": "jsonArray", "bodyAgent": { "type": "agent", "id": "iba", "name": "IBA" } },
            { "type": "parallel", "id": "n10", "name": "J", "branches": [{ "name": "b1", "goal": "g1" }], "merge": "labeled" },
            { "type": "http-request", "id": "n11", "name": "K", "spec": { "kind": "inline", "url": "http://x", "method": "get", "headers": [], "contentType": "application/json", "auth": { "kind": "none" }, "retry": { "count": 0, "delayMs": 1000 }, "timeoutSeconds": 15, "response": { "kind": "text" }, "responseMaxLength": 2000 } },
            { "type": "rag", "id": "n12", "name": "L", "rag": { "dataSource": "upload", "chunkSize": 800, "chunkOverlap": 80, "topK": 5, "embeddingModel": "text-embedding-3-small", "searchMode": "hybrid", "minScore": 0.005, "queryExpansion": true, "contextCompression": false, "tokenBudget": 1500 }, "knowledgeBaseIds": [] },
            { "type": "start", "id": "n13", "name": "S" },
            { "type": "end", "id": "n14", "name": "E" }
          ],
          "connections": []
        }
        """;

        var (payload, _, _, error) = WorkflowExecutionService.ParseAndValidatePayload(json);

        Assert.NotNull(payload);
        Assert.Null(error);
        Assert.Equal(14, payload.Nodes.Count);
        Assert.IsType<AgentNode>(payload.Nodes[0]);
        Assert.IsType<A2AAgentNode>(payload.Nodes[1]);
        Assert.IsType<AutonomousNode>(payload.Nodes[2]);
        Assert.IsType<ConditionNode>(payload.Nodes[3]);
        Assert.IsType<LoopNode>(payload.Nodes[4]);
        Assert.IsType<RouterNode>(payload.Nodes[5]);
        Assert.IsType<HumanNode>(payload.Nodes[6]);
        Assert.IsType<CodeNode>(payload.Nodes[7]);
        Assert.IsType<IterationNode>(payload.Nodes[8]);
        Assert.IsType<ParallelNode>(payload.Nodes[9]);
        Assert.IsType<HttpRequestNode>(payload.Nodes[10]);
        Assert.IsType<RagNode>(payload.Nodes[11]);
        Assert.IsType<StartNode>(payload.Nodes[12]);
        Assert.IsType<EndNode>(payload.Nodes[13]);
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
