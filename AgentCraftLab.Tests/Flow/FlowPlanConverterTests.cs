using System.Text.Json;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Engine.Models;
using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Tests.Flow;

/// <summary>
/// FlowPlanConverter 單元測試 — 驗證 FlowPlan → AI Build JSON 的轉換邏輯。
/// Phase F：FlowPlan.Nodes 用 Schema.NodeConfig sealed record 建 fixture。
/// </summary>
public sealed class FlowPlanConverterTests
{
    // ═══════════════════════════════════════════════
    // Sequential（純 agent chain）
    // ═══════════════════════════════════════════════

    [Fact]
    public void Sequential_AgentChain_GeneratesCorrectConnections()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new Schema.AgentNode { Name = "Researcher", Instructions = "Research" },
                new Schema.AgentNode { Name = "Writer", Instructions = "Write" },
                new Schema.AgentNode { Name = "Editor", Instructions = "Edit" },
            ]
        };

        var json = FlowPlanConverter.ConvertToAiBuildJson(plan);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 3 nodes
        var nodes = root.GetProperty("nodes");
        Assert.Equal(3, nodes.GetArrayLength());

        // 2 connections: 0→1, 1→2
        var connections = root.GetProperty("connections");
        Assert.Equal(2, connections.GetArrayLength());
        Assert.Equal(0, connections[0].GetProperty("from").GetInt32());
        Assert.Equal(1, connections[0].GetProperty("to").GetInt32());
        Assert.Equal(1, connections[1].GetProperty("from").GetInt32());
        Assert.Equal(2, connections[1].GetProperty("to").GetInt32());
    }

    [Fact]
    public void Sequential_NodeFieldsAreMappedCorrectly()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new Schema.AgentNode
                {
                    Name = "Expert",
                    Instructions = "Be thorough",
                    Tools = ["web_search"],
                    Model = new Schema.ModelConfig { Provider = "openai", Model = "gpt-4o" }
                },
            ]
        };

        var json = FlowPlanConverter.ConvertToAiBuildJson(plan);
        var doc = JsonDocument.Parse(json);
        var node = doc.RootElement.GetProperty("nodes")[0];

        Assert.Equal("agent", node.GetProperty("type").GetString());
        Assert.Equal("Expert", node.GetProperty("name").GetString());
        Assert.Equal("Be thorough", node.GetProperty("instructions").GetString());
        Assert.Equal("openai", node.GetProperty("provider").GetString());
        Assert.Equal("gpt-4o", node.GetProperty("model").GetString());
        Assert.Equal("web_search", node.GetProperty("tools")[0].GetString());
    }

    // ═══════════════════════════════════════════════
    // Parallel 展開
    // ═══════════════════════════════════════════════

    [Fact]
    public void Parallel_ExpandsBranchesToAgentNodes()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new Schema.AgentNode { Name = "Intake", Instructions = "Summarize" },
                new Schema.ParallelNode
                {
                    Name = "MultiExpert",
                    Branches =
                    [
                        new Schema.BranchConfig { Name = "Legal", Goal = "Legal analysis" },
                        new Schema.BranchConfig { Name = "Technical", Goal = "Tech analysis" },
                    ],
                    Merge = Schema.MergeStrategyKind.Labeled
                },
                new Schema.AgentNode { Name = "Synthesizer", Instructions = "Merge" },
            ]
        };

        var json = FlowPlanConverter.ConvertToAiBuildJson(plan);
        var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        var connections = doc.RootElement.GetProperty("connections");

        // Intake + Parallel + Legal + Technical + Synthesizer = 5 nodes
        Assert.Equal(5, nodes.GetArrayLength());

        // Parallel node has branches as comma-separated string in flat output
        Assert.Equal("Legal,Technical", nodes[1].GetProperty("branches").GetString());

        // Connections: Intake→Parallel, Parallel→Legal(output_1), Parallel→Technical(output_2), Parallel→Synthesizer(output_3 Done)
        var connList = connections.EnumerateArray().ToList();
        Assert.True(connList.Count >= 4);

        // Check branch port assignments
        var parallelConns = connList.Where(c => c.GetProperty("from").GetInt32() == 1).ToList();
        Assert.Contains(parallelConns, c => c.GetProperty("fromOutput").GetString() == "output_1");
        Assert.Contains(parallelConns, c => c.GetProperty("fromOutput").GetString() == "output_2");
        Assert.Contains(parallelConns, c => c.GetProperty("fromOutput").GetString() == "output_3"); // Done
    }

    // ═══════════════════════════════════════════════
    // Loop 展開
    // ═══════════════════════════════════════════════

    [Fact]
    public void Loop_ExpandsBodyAgentWithFeedbackConnection()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new Schema.AgentNode { Name = "Writer", Instructions = "Write" },
                new Schema.LoopNode
                {
                    Name = "ReviewLoop",
                    Condition = new Schema.ConditionConfig { Kind = Schema.ConditionKind.Contains, Value = "APPROVED" },
                    MaxIterations = 3,
                    BodyAgent = new Schema.AgentNode
                    {
                        Name = "ReviewLoop Body",
                        Instructions = "Review the draft",
                        Tools = ["web_search"]
                    }
                },
                new Schema.AgentNode { Name = "Publisher", Instructions = "Publish" },
            ]
        };

        var json = FlowPlanConverter.ConvertToAiBuildJson(plan);
        var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        var connections = doc.RootElement.GetProperty("connections");

        // Writer + Loop + Body + Publisher = 4 nodes
        Assert.Equal(4, nodes.GetArrayLength());

        // Body agent
        Assert.Equal("agent", nodes[2].GetProperty("type").GetString());
        Assert.Equal("ReviewLoop Body", nodes[2].GetProperty("name").GetString());
        Assert.Equal("Review the draft", nodes[2].GetProperty("instructions").GetString());

        // Loop node
        Assert.Equal("APPROVED", nodes[1].GetProperty("conditionExpression").GetString());
        Assert.Equal(3, nodes[1].GetProperty("maxIterations").GetInt32());

        // Connections include feedback: Body → Loop
        var connList = connections.EnumerateArray().ToList();
        Assert.Contains(connList, c =>
            c.GetProperty("from").GetInt32() == 2 && c.GetProperty("to").GetInt32() == 1); // Body → Loop
        Assert.Contains(connList, c =>
            c.GetProperty("from").GetInt32() == 1 && c.GetProperty("fromOutput").GetString() == "output_2"); // Exit
    }

    // ═══════════════════════════════════════════════
    // Iteration 展開
    // ═══════════════════════════════════════════════

    [Fact]
    public void Iteration_ExpandsBodyAgentWithDonePort()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new Schema.IterationNode
                {
                    Name = "ForEach",
                    Split = Schema.SplitModeKind.JsonArray,
                    MaxItems = 50,
                    BodyAgent = new Schema.AgentNode
                    {
                        Name = "ForEach Body",
                        Instructions = "Process each item",
                        Tools = ["web_search"]
                    }
                },
                new Schema.AgentNode { Name = "Summarizer", Instructions = "Summarize" },
            ]
        };

        var json = FlowPlanConverter.ConvertToAiBuildJson(plan);
        var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        var connections = doc.RootElement.GetProperty("connections");

        // Iteration + Body + Summarizer = 3 nodes
        Assert.Equal(3, nodes.GetArrayLength());

        // Body agent
        Assert.Equal("ForEach Body", nodes[1].GetProperty("name").GetString());

        // Connections: Iter→Body(output_1), Iter→Summarizer(output_2 Done)
        var connList = connections.EnumerateArray().ToList();
        Assert.Contains(connList, c =>
            c.GetProperty("from").GetInt32() == 0 && c.GetProperty("fromOutput").GetString() == "output_1");
        Assert.Contains(connList, c =>
            c.GetProperty("from").GetInt32() == 0 && c.GetProperty("fromOutput").GetString() == "output_2");
    }

    // ═══════════════════════════════════════════════
    // Condition 展開
    // ═══════════════════════════════════════════════

    [Fact]
    public void Condition_GeneratesTrueFalseBranches()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new Schema.ConditionNode
                {
                    Name = "Check",
                    Condition = new Schema.ConditionConfig { Kind = Schema.ConditionKind.Contains, Value = "YES" }
                },
                new Schema.AgentNode { Name = "TrueAgent", Instructions = "Handle true" },
                new Schema.AgentNode { Name = "FalseAgent", Instructions = "Handle false" },
            ]
        };

        var json = FlowPlanConverter.ConvertToAiBuildJson(plan);
        var doc = JsonDocument.Parse(json);
        var connections = doc.RootElement.GetProperty("connections");

        var connList = connections.EnumerateArray().ToList();

        // Condition → TrueAgent (output_1)
        Assert.Contains(connList, c =>
            c.GetProperty("from").GetInt32() == 0 &&
            c.GetProperty("to").GetInt32() == 1 &&
            c.GetProperty("fromOutput").GetString() == "output_1");

        // Condition → FalseAgent (output_2)
        Assert.Contains(connList, c =>
            c.GetProperty("from").GetInt32() == 0 &&
            c.GetProperty("to").GetInt32() == 2 &&
            c.GetProperty("fromOutput").GetString() == "output_2");
    }

    // ═══════════════════════════════════════════════
    // Code 節點
    // ═══════════════════════════════════════════════

    [Fact]
    public void Code_NodeFieldsAreMappedCorrectly()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new Schema.CodeNode
                {
                    Name = "Formatter",
                    Kind = Schema.TransformKind.Template,
                    Expression = "## Report\n{{input}}"
                },
            ]
        };

        var json = FlowPlanConverter.ConvertToAiBuildJson(plan);
        var doc = JsonDocument.Parse(json);
        var node = doc.RootElement.GetProperty("nodes")[0];

        Assert.Equal("code", node.GetProperty("type").GetString());
        Assert.Equal("template", node.GetProperty("transformType").GetString());
        Assert.Equal("## Report\n{{input}}", node.GetProperty("template").GetString());
    }

    // ═══════════════════════════════════════════════
    // Edge cases
    // ═══════════════════════════════════════════════

    [Fact]
    public void EmptyPlan_ReturnsEmptyNodesAndConnections()
    {
        var plan = new FlowPlan { Nodes = [] };
        var json = FlowPlanConverter.ConvertToAiBuildJson(plan);
        var doc = JsonDocument.Parse(json);

        Assert.Equal(0, doc.RootElement.GetProperty("nodes").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("connections").GetArrayLength());
    }

    [Fact]
    public void SingleAgent_NoConnections()
    {
        var plan = new FlowPlan
        {
            Nodes = [new Schema.AgentNode { Name = "Solo", Instructions = "Do it" }]
        };

        var json = FlowPlanConverter.ConvertToAiBuildJson(plan);
        var doc = JsonDocument.Parse(json);

        Assert.Equal(1, doc.RootElement.GetProperty("nodes").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("connections").GetArrayLength());
    }

    [Fact]
    public void HttpRequest_NodeFieldsAreMapped()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new Schema.HttpRequestNode
                {
                    Name = "APICall",
                    Spec = new Schema.CatalogHttpRef
                    {
                        ApiId = "weather-api",
                        Args = System.Text.Json.Nodes.JsonNode.Parse("{\"city\": \"{input}\"}")
                    }
                },
            ]
        };

        var json = FlowPlanConverter.ConvertToAiBuildJson(plan);
        var doc = JsonDocument.Parse(json);
        var node = doc.RootElement.GetProperty("nodes")[0];

        Assert.Equal("http-request", node.GetProperty("type").GetString());
        Assert.Equal("weather-api", node.GetProperty("httpApiId").GetString());
    }

    [Fact]
    public void OmittedProviderAndModel_FallBackToDefaults()
    {
        // Schema.AgentNode 強型別不允許 null Provider/Model，自動填入預設值。
        var plan = new FlowPlan
        {
            Nodes = [new Schema.AgentNode { Name = "A", Instructions = "Hi" }]
        };

        var json = FlowPlanConverter.ConvertToAiBuildJson(plan);
        var doc = JsonDocument.Parse(json);
        var node = doc.RootElement.GetProperty("nodes")[0];

        // Schema.ModelConfig 預設 provider="openai" / model="gpt-4o-mini"
        Assert.Equal("openai", node.GetProperty("provider").GetString());
        Assert.Equal("gpt-4o-mini", node.GetProperty("model").GetString());
    }
}
