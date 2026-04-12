using System.Text.Json;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Engine.Models;
using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Tests.Flow;

/// <summary>
/// Phase F：FlowPlan 從 LLM JSON 反序列化的單元測試。
/// LLM 輸出的是 Schema.NodeConfig 格式（nested + discriminator），<see cref="JsonSerializer"/>
/// 透過 <see cref="Schema.SchemaJsonOptions.Default"/> 直接解析為強型別 sealed record 清單。
/// </summary>
public class FlowPlanDeserializationTests
{
    private static FlowPlan Deserialize(string json)
    {
        var plan = JsonSerializer.Deserialize<FlowPlan>(json, Schema.SchemaJsonOptions.Default);
        Assert.NotNull(plan);
        return plan!;
    }

    [Fact]
    public void DeserializesExactLlmOutput_FromSmokeTest()
    {
        // 模擬 simple-agent scenario 實際回傳的 JSON
        var json = """
        {
          "nodes": [
            {
              "type": "agent",
              "name": "詩人",
              "instructions": "請用繁體中文創作一首五言絕句，主題為秋天。詩句需符合格律，意境優美。",
              "tools": [],
              "output": { "kind": "text" }
            }
          ]
        }
        """;
        var plan = Deserialize(json);
        var agent = Assert.IsType<Schema.AgentNode>(plan.Nodes[0]);
        Assert.Equal(Schema.OutputFormat.Text, agent.Output.Kind);
        Assert.Empty(agent.Tools);
    }

    [Fact]
    public void DeserializesOutputFormat_Text()
    {
        // 測試 OutputFormat.Text（enum 預設值 0）能正確從 "text" 反序列化
        var json = """
        {
          "nodes": [
            { "type": "agent", "name": "A", "instructions": "x", "output": { "kind": "text" } }
          ]
        }
        """;
        var plan = Deserialize(json);
        var agent = Assert.IsType<Schema.AgentNode>(plan.Nodes[0]);
        Assert.Equal(Schema.OutputFormat.Text, agent.Output.Kind);
    }

    [Fact]
    public void DeserializesAgentNode()
    {
        var json = """
        {
          "nodes": [
            {
              "type": "agent",
              "name": "Researcher",
              "instructions": "Do stuff",
              "tools": ["search"],
              "model": { "provider": "openai", "model": "gpt-4o" },
              "output": { "kind": "json", "schemaJson": "{\"type\":\"object\"}" }
            }
          ]
        }
        """;
        var plan = Deserialize(json);

        Assert.Single(plan.Nodes);
        var agent = Assert.IsType<Schema.AgentNode>(plan.Nodes[0]);
        Assert.Equal("Do stuff", agent.Instructions);
        Assert.Single(agent.Tools);
        Assert.Equal("search", agent.Tools[0]);
        Assert.Equal("openai", agent.Model.Provider);
        Assert.Equal("gpt-4o", agent.Model.Model);
        Assert.Equal(Schema.OutputFormat.Json, agent.Output.Kind);
    }

    [Fact]
    public void DeserializesCodeNode_TemplateKind()
    {
        var json = """
        {
          "nodes": [
            { "type": "code", "name": "Formatter", "kind": "template", "expression": "{{input}}" }
          ]
        }
        """;
        var plan = Deserialize(json);
        var code = Assert.IsType<Schema.CodeNode>(plan.Nodes[0]);
        Assert.Equal(Schema.TransformKind.Template, code.Kind);
        Assert.Equal("{{input}}", code.Expression);
    }

    [Fact]
    public void DeserializesCodeNode_ScriptKind()
    {
        var json = """
        {
          "nodes": [
            { "type": "code", "name": "Filter", "kind": "script", "expression": "result = input.toUpperCase();" }
          ]
        }
        """;
        var plan = Deserialize(json);
        var code = Assert.IsType<Schema.CodeNode>(plan.Nodes[0]);
        Assert.Equal(Schema.TransformKind.Script, code.Kind);
    }

    [Fact]
    public void DeserializesConditionNode_WithBranchIndexMeta()
    {
        var json = """
        {
          "nodes": [
            {
              "type": "condition",
              "name": "Check",
              "condition": { "kind": "contains", "value": "done" },
              "meta": { "flow:trueBranchIndex": "3", "flow:falseBranchIndex": "5" }
            }
          ]
        }
        """;
        var plan = Deserialize(json);
        var cond = Assert.IsType<Schema.ConditionNode>(plan.Nodes[0]);
        Assert.Equal(Schema.ConditionKind.Contains, cond.Condition.Kind);
        Assert.Equal("done", cond.Condition.Value);
        Assert.Equal(3, NodeConfigHelpers.GetBranchIndex(cond, NodeConfigHelpers.MetaTrueBranchIndex));
        Assert.Equal(5, NodeConfigHelpers.GetBranchIndex(cond, NodeConfigHelpers.MetaFalseBranchIndex));
    }

    [Fact]
    public void DeserializesLoopNode_WithBodyAgent()
    {
        var json = """
        {
          "nodes": [
            {
              "type": "loop",
              "name": "Refine",
              "condition": { "kind": "contains", "value": "ready" },
              "maxIterations": 7,
              "bodyAgent": { "name": "Rewriter", "instructions": "Rewrite", "tools": ["formatter"] }
            }
          ]
        }
        """;
        var plan = Deserialize(json);
        var loop = Assert.IsType<Schema.LoopNode>(plan.Nodes[0]);
        Assert.Equal(Schema.ConditionKind.Contains, loop.Condition.Kind);
        Assert.Equal("ready", loop.Condition.Value);
        Assert.Equal(7, loop.MaxIterations);
        Assert.Equal("Rewrite", loop.BodyAgent.Instructions);
        Assert.Contains("formatter", loop.BodyAgent.Tools);
    }

    [Fact]
    public void DeserializesParallelNode_WithBranches()
    {
        var json = """
        {
          "nodes": [
            {
              "type": "parallel",
              "name": "Fan",
              "branches": [
                { "name": "A", "goal": "Do A", "tools": ["tool_a"] },
                { "name": "B", "goal": "Do B" }
              ],
              "merge": "join"
            }
          ]
        }
        """;
        var plan = Deserialize(json);
        var parallel = Assert.IsType<Schema.ParallelNode>(plan.Nodes[0]);
        Assert.Equal(2, parallel.Branches.Count);
        Assert.Equal("A", parallel.Branches[0].Name);
        Assert.Equal("Do A", parallel.Branches[0].Goal);
        Assert.NotNull(parallel.Branches[0].Tools);
        Assert.Equal("tool_a", parallel.Branches[0].Tools![0]);
        Assert.Equal(Schema.MergeStrategyKind.Join, parallel.Merge);
    }

    [Fact]
    public void DeserializesIterationNode_WithBodyAgent()
    {
        var json = """
        {
          "nodes": [
            {
              "type": "iteration",
              "name": "Process",
              "split": "jsonArray",
              "maxItems": 10,
              "bodyAgent": { "instructions": "Process each item" }
            }
          ]
        }
        """;
        var plan = Deserialize(json);
        var iter = Assert.IsType<Schema.IterationNode>(plan.Nodes[0]);
        Assert.Equal(Schema.SplitModeKind.JsonArray, iter.Split);
        Assert.Equal(10, iter.MaxItems);
        Assert.Equal("Process each item", iter.BodyAgent.Instructions);
    }

    [Fact]
    public void DeserializesHttpRequestNode_InlineSpec()
    {
        var json = """
        {
          "nodes": [
            {
              "type": "http-request",
              "name": "Call",
              "spec": {
                "kind": "inline",
                "url": "https://api.example.com",
                "method": "post"
              }
            }
          ]
        }
        """;
        var plan = Deserialize(json);
        var http = Assert.IsType<Schema.HttpRequestNode>(plan.Nodes[0]);
        var inline = Assert.IsType<Schema.InlineHttpRequest>(http.Spec);
        Assert.Equal("https://api.example.com", inline.Url);
        Assert.Equal(Schema.HttpMethodKind.Post, inline.Method);
    }

    [Fact]
    public void DeserializesHttpRequestNode_CatalogSpec()
    {
        var json = """
        {
          "nodes": [
            {
              "type": "http-request",
              "name": "CatalogCall",
              "spec": {
                "kind": "catalog",
                "apiId": "api1",
                "args": { "x": 1 }
              }
            }
          ]
        }
        """;
        var plan = Deserialize(json);
        var http = Assert.IsType<Schema.HttpRequestNode>(plan.Nodes[0]);
        var catalog = Assert.IsType<Schema.CatalogHttpRef>(http.Spec);
        Assert.Equal("api1", catalog.ApiId);
        Assert.NotNull(catalog.Args);
    }

    [Fact]
    public void NodeConfigHelpers_GetNodeTypeString_MatchesNodeType()
    {
        Assert.Equal(NodeTypes.Agent, NodeConfigHelpers.GetNodeTypeString(new Schema.AgentNode()));
        Assert.Equal(NodeTypes.Code, NodeConfigHelpers.GetNodeTypeString(new Schema.CodeNode()));
        Assert.Equal(NodeTypes.Loop, NodeConfigHelpers.GetNodeTypeString(new Schema.LoopNode()));
        Assert.Equal(NodeTypes.Parallel, NodeConfigHelpers.GetNodeTypeString(new Schema.ParallelNode()));
    }
}
