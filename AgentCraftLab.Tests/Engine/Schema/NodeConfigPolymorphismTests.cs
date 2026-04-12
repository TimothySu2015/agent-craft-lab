using System.Text.Json;
using System.Text.Json.Nodes;
using AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Tests.Engine.Schema;

/// <summary>
/// 驗證 NodeConfig discriminator union 的 round-trip 序列化 — 13 種節點各至少一個 case。
/// 加新節點型別時必須在此新增對應測試。
/// </summary>
public class NodeConfigPolymorphismTests
{
    private static readonly JsonSerializerOptions Options = SchemaJsonOptions.Default;

    private static T RoundTrip<T>(T value) where T : NodeConfig
    {
        var json = JsonSerializer.Serialize<NodeConfig>(value, Options);
        var restored = JsonSerializer.Deserialize<NodeConfig>(json, Options);
        Assert.NotNull(restored);
        return Assert.IsType<T>(restored);
    }

    [Fact]
    public void AgentNode_RoundTrips()
    {
        var node = new AgentNode
        {
            Id = "n1",
            Name = "Researcher",
            Instructions = "Summarize {{node:Previous}}.",
            Model = new ModelConfig { Provider = "openai", Model = "gpt-4o-mini", Temperature = 0.2f },
            Tools = ["web_search", "calculator"],
            Output = new OutputConfig { Kind = OutputFormat.Json }
        };

        var restored = RoundTrip(node);
        Assert.Equal("Researcher", restored.Name);
        Assert.Equal("gpt-4o-mini", restored.Model.Model);
        Assert.Equal(0.2f, restored.Model.Temperature);
        Assert.Equal(OutputFormat.Json, restored.Output.Kind);
        Assert.Equal(2, restored.Tools.Count);
    }

    [Fact]
    public void ConditionNode_RoundTrips()
    {
        var node = new ConditionNode
        {
            Id = "c1",
            Name = "IsUrgent",
            Condition = new ConditionConfig { Kind = ConditionKind.LlmJudge, Value = "Is this urgent?" }
        };

        var restored = RoundTrip(node);
        Assert.Equal(ConditionKind.LlmJudge, restored.Condition.Kind);
        Assert.Equal("Is this urgent?", restored.Condition.Value);
    }

    [Fact]
    public void LoopNode_RoundTrips()
    {
        var node = new LoopNode
        {
            Id = "l1",
            Name = "Retry",
            Condition = new ConditionConfig { Kind = ConditionKind.Contains, Value = "SUCCESS" },
            BodyAgent = new AgentNode { Instructions = "Try again." },
            MaxIterations = 3
        };

        var restored = RoundTrip(node);
        Assert.Equal(3, restored.MaxIterations);
        Assert.Equal("Try again.", restored.BodyAgent.Instructions);
    }

    [Fact]
    public void RouterNode_RoundTrips()
    {
        var node = new RouterNode
        {
            Id = "r1",
            Name = "Classify",
            Routes =
            [
                new RouteConfig { Name = "Tech", Keywords = ["code", "bug"] },
                new RouteConfig { Name = "Other", IsDefault = true }
            ]
        };

        var restored = RoundTrip(node);
        Assert.Equal(2, restored.Routes.Count);
        Assert.True(restored.Routes[1].IsDefault);
    }

    [Fact]
    public void HumanNode_RoundTrips()
    {
        var node = new HumanNode
        {
            Id = "h1",
            Name = "Approve",
            Prompt = "Approve?",
            Kind = HumanInputKind.Approval,
            TimeoutSeconds = 60
        };

        var restored = RoundTrip(node);
        Assert.Equal(HumanInputKind.Approval, restored.Kind);
        Assert.Equal(60, restored.TimeoutSeconds);
    }

    [Fact]
    public void CodeNode_RoundTrips()
    {
        var node = new CodeNode
        {
            Id = "code1",
            Name = "Trim",
            Kind = TransformKind.Script,
            Expression = "return input.trim();",
            Language = ScriptLanguage.JavaScript
        };

        var restored = RoundTrip(node);
        Assert.Equal(TransformKind.Script, restored.Kind);
        Assert.Equal(ScriptLanguage.JavaScript, restored.Language);
    }

    [Fact]
    public void IterationNode_RoundTrips()
    {
        var node = new IterationNode
        {
            Id = "i1",
            Name = "ForEach",
            Split = SplitModeKind.JsonArray,
            MaxItems = 20,
            MaxConcurrency = 4,
            BodyAgent = new AgentNode { Instructions = "Process {{var:item}}." }
        };

        var restored = RoundTrip(node);
        Assert.Equal(SplitModeKind.JsonArray, restored.Split);
        Assert.Equal(4, restored.MaxConcurrency);
    }

    [Fact]
    public void ParallelNode_RoundTrips()
    {
        var node = new ParallelNode
        {
            Id = "p1",
            Name = "FanOut",
            Branches =
            [
                new BranchConfig { Name = "EN", Goal = "Translate to English" },
                new BranchConfig { Name = "JA", Goal = "Translate to Japanese" }
            ],
            Merge = MergeStrategyKind.Labeled
        };

        var restored = RoundTrip(node);
        Assert.Equal(2, restored.Branches.Count);
        Assert.Equal(MergeStrategyKind.Labeled, restored.Merge);
    }

    [Fact]
    public void HttpRequestNode_Catalog_RoundTrips()
    {
        var node = new HttpRequestNode
        {
            Id = "h1",
            Name = "Notify",
            Spec = new CatalogHttpRef
            {
                ApiId = "slack-webhook",
                Args = JsonNode.Parse("""{"channel":"#ops","text":"{{node:Report}}"}""")
            }
        };

        var restored = RoundTrip(node);
        var catalog = Assert.IsType<CatalogHttpRef>(restored.Spec);
        Assert.Equal("slack-webhook", catalog.ApiId);
        Assert.NotNull(catalog.Args);
    }

    [Fact]
    public void HttpRequestNode_Inline_WithBearer_RoundTrips()
    {
        var node = new HttpRequestNode
        {
            Id = "h2",
            Name = "Call",
            Spec = new InlineHttpRequest
            {
                Url = "https://api.example.com/notify",
                Method = HttpMethodKind.Post,
                Headers =
                [
                    new HttpHeader("X-Trace", "{{sys:runId}}"),
                    new HttpHeader("Accept", "application/json")
                ],
                Body = new HttpBody { Content = JsonNode.Parse("""{"msg":"{{node:R}}"}""") },
                Auth = new BearerAuth("secret-token"),
                Response = new JsonPathParser("data.id"),
                Retry = new RetryConfig { Count = 2, DelayMs = 500 }
            }
        };

        var restored = RoundTrip(node);
        var inline = Assert.IsType<InlineHttpRequest>(restored.Spec);
        Assert.Equal(HttpMethodKind.Post, inline.Method);
        Assert.Equal(2, inline.Headers.Count);
        var bearer = Assert.IsType<BearerAuth>(inline.Auth);
        Assert.Equal("secret-token", bearer.Token);
        var parser = Assert.IsType<JsonPathParser>(inline.Response);
        Assert.Equal("data.id", parser.Path);
        Assert.Equal(2, inline.Retry.Count);
    }

    [Fact]
    public void HttpAuth_AllVariants_RoundTrip()
    {
        HttpAuth[] variants =
        [
            new NoneAuth(),
            new BearerAuth("token"),
            new BasicAuth("user:pass"),
            new ApiKeyHeaderAuth("X-Api-Key", "value"),
            new ApiKeyQueryAuth("api_key", "value")
        ];

        foreach (var auth in variants)
        {
            var json = JsonSerializer.Serialize(auth, Options);
            var restored = JsonSerializer.Deserialize<HttpAuth>(json, Options);
            Assert.NotNull(restored);
            Assert.Equal(auth.GetType(), restored.GetType());
        }
    }

    [Fact]
    public void A2AAgentNode_RoundTrips()
    {
        var node = new A2AAgentNode
        {
            Id = "a1",
            Name = "Remote",
            Url = "https://partner.com/a2a",
            Format = A2AFormat.Microsoft,
            Instructions = "Analyze"
        };

        var restored = RoundTrip(node);
        Assert.Equal(A2AFormat.Microsoft, restored.Format);
    }

    [Fact]
    public void AutonomousNode_RoundTrips()
    {
        var node = new AutonomousNode
        {
            Id = "auto1",
            Name = "Investigate",
            Instructions = "Find the root cause",
            Model = new ModelConfig { Model = "gpt-4o" },
            MaxIterations = 15
        };

        var restored = RoundTrip(node);
        Assert.Equal(15, restored.MaxIterations);
        Assert.Equal("gpt-4o", restored.Model.Model);
    }

    [Fact]
    public void RagNode_RoundTrips()
    {
        var node = new RagNode
        {
            Id = "rag1",
            Name = "KB",
            Rag = new RagConfig { DataSource = "kb-123", TopK = 10 },
            KnowledgeBaseIds = ["kb-123", "kb-456"]
        };

        var restored = RoundTrip(node);
        Assert.Equal(10, restored.Rag.TopK);
        Assert.Equal(2, restored.KnowledgeBaseIds.Count);
    }

    [Fact]
    public void StartAndEndNodes_RoundTrip()
    {
        var start = new StartNode { Id = "s", Name = "Start" };
        var end = new EndNode { Id = "e", Name = "End" };

        Assert.IsType<StartNode>(RoundTrip(start));
        Assert.IsType<EndNode>(RoundTrip(end));
    }

    [Fact]
    public void WorkflowPayload_WithMixedNodes_RoundTrips()
    {
        var payload = new WorkflowPayload
        {
            Nodes =
            [
                new StartNode { Id = "s", Name = "Start" },
                new AgentNode { Id = "a", Name = "Agent", Instructions = "Hi" },
                new ParallelNode
                {
                    Id = "p", Name = "Fan",
                    Branches = [new BranchConfig { Name = "X", Goal = "do X" }]
                },
                new EndNode { Id = "e", Name = "End" }
            ],
            Connections =
            [
                new Connection { From = "s", To = "a" },
                new Connection { From = "a", To = "p" },
                new Connection { From = "p", To = "e" }
            ]
        };

        var json = JsonSerializer.Serialize(payload, Options);
        var restored = JsonSerializer.Deserialize<WorkflowPayload>(json, Options);

        Assert.NotNull(restored);
        Assert.Equal("2.0", restored.Version);
        Assert.Equal(4, restored.Nodes.Count);
        Assert.IsType<StartNode>(restored.Nodes[0]);
        Assert.IsType<AgentNode>(restored.Nodes[1]);
        Assert.IsType<ParallelNode>(restored.Nodes[2]);
        Assert.IsType<EndNode>(restored.Nodes[3]);
        Assert.Equal(3, restored.Connections.Count);
    }

    [Fact]
    public void DiscriminatorPropertyIs_Type()
    {
        var agent = new AgentNode { Id = "a", Name = "A" };
        var json = JsonSerializer.Serialize<NodeConfig>(agent, Options);
        Assert.Contains("\"type\":\"agent\"", json);
    }

    [Fact]
    public void Enums_AreSerializedAsCamelCaseStrings()
    {
        // Regression lock — enum 必須以字串形式（camelCase）序列化，LLM 才能正確生成與閱讀。
        var node = new AgentNode
        {
            Id = "a",
            Name = "A",
            Output = new OutputConfig { Kind = OutputFormat.JsonSchema, SchemaJson = "{}" }
        };

        var json = JsonSerializer.Serialize<NodeConfig>(node, Options);

        Assert.Contains("\"kind\":\"jsonSchema\"", json);
        Assert.DoesNotContain("\"kind\":2", json);
    }

    [Fact]
    public void HttpMethod_SerializesAsCamelCase()
    {
        var node = new HttpRequestNode
        {
            Id = "h",
            Name = "H",
            Spec = new InlineHttpRequest { Url = "https://x", Method = HttpMethodKind.Post }
        };

        var json = JsonSerializer.Serialize<NodeConfig>(node, Options);

        Assert.Contains("\"method\":\"post\"", json);
    }

    [Fact]
    public void HookHierarchy_RoundTrips()
    {
        var hooks = new WorkflowHooks
        {
            OnInput = new CodeHook { Kind = TransformKind.Trim, Expression = "{{input}}" },
            OnComplete = new WebhookHook { Url = "https://hooks.example.com", Method = HttpMethodKind.Post }
        };

        var json = JsonSerializer.Serialize(hooks, Options);
        var restored = JsonSerializer.Deserialize<WorkflowHooks>(json, Options);

        Assert.NotNull(restored);
        Assert.IsType<CodeHook>(restored.OnInput);
        Assert.IsType<WebhookHook>(restored.OnComplete);
    }
}
