using System.Text.Json;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCraftLab.Tests.Flow;

/// <summary>
/// F5: {{node:step_name}} 跨節點引用語法的測試。
/// </summary>
public class FlowNodeReferenceTests
{
    private static FlowNodeRunner CreateRunner(Dictionary<string, string>? nodeOutputs = null)
    {
        var runner = new FlowNodeRunner(
            null!, // agentFactory — 不測試 LLM 呼叫
            null!, // httpApiTool
            NullLogger<FlowNodeRunner>.Instance)
        {
            NodeOutputs = nodeOutputs ?? new Dictionary<string, string>()
        };
        return runner;
    }

    // ════════════════════════════════════════
    // ResolveNodeReferences 基本行為
    // ════════════════════════════════════════

    [Fact]
    public void ResolveNodeReferences_ReplacesKnownNode()
    {
        var runner = CreateRunner(new Dictionary<string, string>
        {
            ["search_aapl"] = "Apple stock: $185"
        });

        var result = runner.ResolveNodeReferences("Based on {{node:search_aapl}}, summarize.");
        Assert.Equal("Based on Apple stock: $185, summarize.", result);
    }

    [Fact]
    public void ResolveNodeReferences_MultipleReferences()
    {
        var runner = CreateRunner(new Dictionary<string, string>
        {
            ["search_aapl"] = "AAPL: $185",
            ["search_tsmc"] = "TSMC: $150"
        });

        var result = runner.ResolveNodeReferences(
            "Compare {{node:search_aapl}} and {{node:search_tsmc}}.");
        Assert.Equal("Compare AAPL: $185 and TSMC: $150.", result);
    }

    [Fact]
    public void ResolveNodeReferences_UnknownNodePreservesMarker()
    {
        var runner = CreateRunner(new Dictionary<string, string>
        {
            ["search_aapl"] = "data"
        });

        var result = runner.ResolveNodeReferences("Use {{node:nonexistent}} data.");
        Assert.Equal("Use {{node:nonexistent}} data.", result);
    }

    [Fact]
    public void ResolveNodeReferences_NullOrEmptyInput()
    {
        var runner = CreateRunner();

        Assert.Equal("", runner.ResolveNodeReferences(null));
        Assert.Equal("", runner.ResolveNodeReferences(""));
    }

    [Fact]
    public void ResolveNodeReferences_NoReferences_ReturnsUnchanged()
    {
        var runner = CreateRunner(new Dictionary<string, string>
        {
            ["search"] = "data"
        });

        var input = "No references here.";
        Assert.Equal(input, runner.ResolveNodeReferences(input));
    }

    [Fact]
    public void ResolveNodeReferences_NullNodeOutputs_ReturnsUnchanged()
    {
        var runner = CreateRunner();
        runner.NodeOutputs = null;

        var input = "Use {{node:search}} data.";
        Assert.Equal(input, runner.ResolveNodeReferences(input));
    }

    [Fact]
    public void ResolveNodeReferences_NodeNameWithSpaces()
    {
        var runner = CreateRunner(new Dictionary<string, string>
        {
            ["Research All Companies"] = "AAPL, MSFT, GOOGL data"
        });

        var result = runner.ResolveNodeReferences("Based on {{node:Research All Companies}}, summarize.");
        Assert.Equal("Based on AAPL, MSFT, GOOGL data, summarize.", result);
    }

    [Fact]
    public void ResolveNodeReferences_TrimsWhitespace()
    {
        var runner = CreateRunner(new Dictionary<string, string>
        {
            ["search"] = "data"
        });

        var result = runner.ResolveNodeReferences("Use {{node: search }} data.");
        Assert.Equal("Use data data.", result);
    }

    // ════════════════════════════════════════
    // BuildAgentMessages 整合（不涉及 {{node:}} — 那在呼叫前已解析）
    // ════════════════════════════════════════

    [Fact]
    public void BuildAgentMessages_InputPassedAsUserMessage()
    {
        var messages = FlowNodeRunner.BuildAgentMessages(
            "You are an analyst", null, "test input");

        Assert.Equal(2, messages.Count); // system + user
        Assert.Equal("test input", messages[^1].Text);
    }

    // ════════════════════════════════════════
    // FlowPlanValidator — {{node:}} 引用驗證
    // ════════════════════════════════════════

    private static GoalExecutionRequest CreateRequest(params string[] tools) => new()
    {
        Goal = "test",
        Credentials = new Dictionary<string, ProviderCredential>(),
        AvailableTools = tools.ToList()
    };

    [Fact]
    public void Validator_ValidNodeReference_NoWarning()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new PlannedNode { NodeType = "agent", Name = "search", Instructions = "Search for data" },
                new PlannedNode { NodeType = "agent", Name = "summarize",
                    Instructions = "Summarize {{node:search}} results" }
            ]
        };

        var (_, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.DoesNotContain(warnings, w => w.Contains("non-existent"));
    }

    [Fact]
    public void Validator_InvalidNodeReference_WarnsUser()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new PlannedNode { NodeType = "agent", Name = "search", Instructions = "Search" },
                new PlannedNode { NodeType = "agent", Name = "summarize",
                    Instructions = "Use {{node:nonexistent}} data" }
            ]
        };

        var (_, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.Contains(warnings, w => w.Contains("non-existent") && w.Contains("nonexistent"));
    }

    [Fact]
    public void Validator_ParallelBranchGoalReference_Validates()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new PlannedNode { NodeType = "agent", Name = "raw_data", Instructions = "Get data" },
                new PlannedNode
                {
                    NodeType = "parallel", Name = "analyze",
                    Branches =
                    [
                        new ParallelBranchConfig { Name = "A", Goal = "Analyze {{node:raw_data}} for trends" },
                        new ParallelBranchConfig { Name = "B", Goal = "Analyze {{node:missing}} for risks" }
                    ]
                }
            ]
        };

        var (_, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.DoesNotContain(warnings, w => w.Contains("raw_data"));
        Assert.Contains(warnings, w => w.Contains("missing"));
    }

    [Fact]
    public void Validator_SelfReference_Warns()
    {
        var plan = new FlowPlan
        {
            Nodes =
            [
                new PlannedNode { NodeType = "agent", Name = "step1",
                    Instructions = "Use {{node:step1}} to improve" }
            ]
        };

        var (_, warnings) = FlowPlanValidator.ValidateAndFix(plan, CreateRequest());
        Assert.Contains(warnings, w => w.Contains("non-existent") && w.Contains("step1"));
    }

    // ════════════════════════════════════════
    // FlowCheckpointSnapshot — NodeOutputs 序列化
    // ════════════════════════════════════════

    [Fact]
    public void CheckpointSnapshot_NodeOutputs_JsonRoundTrip()
    {
        var snapshot = new FlowCheckpointSnapshot
        {
            PlanJson = "{}",
            CompletedNodeIndex = 2,
            PreviousResult = "result",
            NodeOutputs = new Dictionary<string, string>
            {
                ["search"] = "search output",
                ["analyze"] = "analysis result"
            }
        };

        var json = JsonSerializer.Serialize(snapshot);
        var deserialized = JsonSerializer.Deserialize<FlowCheckpointSnapshot>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized!.NodeOutputs.Count);
        Assert.Equal("search output", deserialized.NodeOutputs["search"]);
        Assert.Equal("analysis result", deserialized.NodeOutputs["analyze"]);
    }

    [Fact]
    public void CheckpointSnapshot_NodeOutputs_DefaultEmpty()
    {
        var snapshot = new FlowCheckpointSnapshot
        {
            PlanJson = "{}",
            CompletedNodeIndex = 0,
            PreviousResult = ""
        };

        Assert.Empty(snapshot.NodeOutputs);
    }

    // ════════════════════════════════════════
    // NodeReferenceResolver（Engine 共用）— 畫布 name→ID 反向查找
    // ════════════════════════════════════════

    [Fact]
    public void Resolver_CanvasMode_ResolvesNodeByName()
    {
        var nodeResults = new Dictionary<string, string>
        {
            ["node_1"] = "search results",
            ["node_2"] = "analysis"
        };
        var nodeMap = new Dictionary<string, WorkflowNode>
        {
            ["node_1"] = new() { Id = "node_1", Name = "Search Data" },
            ["node_2"] = new() { Id = "node_2", Name = "Analyze" }
        };

        var result = NodeReferenceResolver.Resolve(
            "Based on {{node:Search Data}}, summarize.",
            nodeResults, nodeMap);
        Assert.Equal("Based on search results, summarize.", result);
    }

    [Fact]
    public void Resolver_CanvasMode_ResolvesNodeById()
    {
        var nodeResults = new Dictionary<string, string>
        {
            ["node_1"] = "output"
        };
        var nodeMap = new Dictionary<string, WorkflowNode>
        {
            ["node_1"] = new() { Id = "node_1", Name = "Search" }
        };

        var result = NodeReferenceResolver.Resolve(
            "Use {{node:node_1}} data.", nodeResults, nodeMap);
        Assert.Equal("Use output data.", result);
    }

    [Fact]
    public void Resolver_CanvasMode_CaseInsensitiveName()
    {
        var nodeResults = new Dictionary<string, string>
        {
            ["node_1"] = "data"
        };
        var nodeMap = new Dictionary<string, WorkflowNode>
        {
            ["node_1"] = new() { Id = "node_1", Name = "Search Data" }
        };

        var result = NodeReferenceResolver.Resolve(
            "Use {{node:search data}}.", nodeResults, nodeMap);
        Assert.Equal("Use data.", result);
    }

    [Fact]
    public void Resolver_ExtractNames_ReturnsDistinctNames()
    {
        var names = NodeReferenceResolver.ExtractNames(
            "{{node:a}} and {{node:b}} and {{node:a}} again");
        Assert.Equal(2, names.Count);
        Assert.Contains("a", names);
        Assert.Contains("b", names);
    }

    [Fact]
    public void Resolver_HasReferences_DetectsPattern()
    {
        Assert.True(NodeReferenceResolver.HasReferences("Use {{node:search}} data"));
        Assert.False(NodeReferenceResolver.HasReferences("No references here"));
        Assert.False(NodeReferenceResolver.HasReferences(null));
    }

    [Fact]
    public void Resolver_NoReferences_SkipsRegex()
    {
        var result = NodeReferenceResolver.Resolve(
            "No refs", new Dictionary<string, string> { ["x"] = "y" });
        Assert.Equal("No refs", result);
    }

    // ════════════════════════════════════════
    // ResolveAsync — 壓縮長引用
    // ════════════════════════════════════════

    private sealed class FakeCompactor : IContextCompactor
    {
        public int CallCount { get; private set; }

        public Task<string?> CompressAsync(string content, string context, int tokenBudget, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult<string?>($"[compressed:{content.Length}chars]");
        }
    }

    [Fact]
    public async Task ResolveAsync_CompressesLongOutput()
    {
        var longOutput = new string('x', NodeReferenceResolver.CompressionThreshold + 100);
        var nodeOutputs = new Dictionary<string, string>
        {
            ["search"] = longOutput
        };
        var compactor = new FakeCompactor();

        var result = await NodeReferenceResolver.ResolveAsync(
            "Based on {{node:search}}, summarize.",
            nodeOutputs, compactor, "summarize context");

        Assert.Equal(1, compactor.CallCount);
        Assert.Contains("[compressed:", result);
        Assert.Contains("summarize.", result);
        Assert.DoesNotContain("{{node:", result);
    }

    [Fact]
    public async Task ResolveAsync_SkipsCompressionForShortOutput()
    {
        var nodeOutputs = new Dictionary<string, string>
        {
            ["search"] = "short output"
        };
        var compactor = new FakeCompactor();

        var result = await NodeReferenceResolver.ResolveAsync(
            "Based on {{node:search}}, summarize.",
            nodeOutputs, compactor, "context");

        Assert.Equal(0, compactor.CallCount);
        Assert.Equal("Based on short output, summarize.", result);
    }

    [Fact]
    public async Task ResolveAsync_MultipleRefs_OnlyCompressesLong()
    {
        var longOutput = new string('y', NodeReferenceResolver.CompressionThreshold + 50);
        var nodeOutputs = new Dictionary<string, string>
        {
            ["long_node"] = longOutput,
            ["short_node"] = "brief"
        };
        var compactor = new FakeCompactor();

        var result = await NodeReferenceResolver.ResolveAsync(
            "{{node:long_node}} and {{node:short_node}}",
            nodeOutputs, compactor, "context");

        Assert.Equal(1, compactor.CallCount); // 只壓了 long_node
        Assert.Contains("[compressed:", result);
        Assert.Contains("brief", result);
    }

    [Fact]
    public async Task ResolveAsync_NoReferences_SkipsCompactor()
    {
        var compactor = new FakeCompactor();

        var result = await NodeReferenceResolver.ResolveAsync(
            "No refs here", new Dictionary<string, string> { ["x"] = "y" },
            compactor, "context");

        Assert.Equal(0, compactor.CallCount);
        Assert.Equal("No refs here", result);
    }

    [Fact]
    public async Task ResolveAsync_Canvas_CompressesLongOutput()
    {
        var longOutput = new string('z', NodeReferenceResolver.CompressionThreshold + 200);
        var nodeResults = new Dictionary<string, string>
        {
            ["node_1"] = longOutput
        };
        var nodeMap = new Dictionary<string, WorkflowNode>
        {
            ["node_1"] = new() { Id = "node_1", Name = "Search" }
        };
        var compactor = new FakeCompactor();

        var result = await NodeReferenceResolver.ResolveAsync(
            "Use {{node:Search}}.", nodeResults, nodeMap,
            compactor, "context");

        Assert.Equal(1, compactor.CallCount);
        Assert.Contains("[compressed:", result);
    }

    // ════════════════════════════════════════
    // FlowPlannerPrompt — 包含 {{node:}} 語法說明
    // ════════════════════════════════════════

    [Fact]
    public void PlannerPrompt_ContainsNodeReferenceDocs()
    {
        var request = new GoalExecutionRequest
        {
            Goal = "test",
            Credentials = new Dictionary<string, ProviderCredential>(),
            AvailableTools = []
        };

        var prompt = FlowPlannerPrompt.Build(request);
        Assert.Contains("{{node:step_name}}", prompt);
        Assert.Contains("Cross-Node References", prompt);
    }
}
