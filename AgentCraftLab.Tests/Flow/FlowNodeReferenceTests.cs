using AgentCraftLab.Data;
using System.Text.Json;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Services.Variables;
using Microsoft.Extensions.Logging.Abstractions;
using Schema = AgentCraftLab.Engine.Models.Schema;

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
            new VariableResolver(),
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
                new Schema.AgentNode { Name = "search", Instructions = "Search for data" },
                new Schema.AgentNode { Name = "summarize",
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
                new Schema.AgentNode { Name = "search", Instructions = "Search" },
                new Schema.AgentNode { Name = "summarize",
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
                new Schema.AgentNode { Name = "raw_data", Instructions = "Get data" },
                new Schema.ParallelNode
                {
                    Name = "analyze",
                    Branches =
                    [
                        new Schema.BranchConfig { Name = "A", Goal = "Analyze {{node:raw_data}} for trends" },
                        new Schema.BranchConfig { Name = "B", Goal = "Analyze {{node:missing}} for risks" }
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
                new Schema.AgentNode { Name = "step1",
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
    // VariableResolver — 畫布 name→ID 反向查找（透過 VariableContext.NodeNameMap）
    // ════════════════════════════════════════

    private static readonly IVariableResolver CanvasResolver = new VariableResolver();

    [Fact]
    public void Resolver_CanvasMode_ResolvesNodeByName()
    {
        // NodeOutputs key 為 node ID，NodeNameMap 為 name → ID 反查表
        var ctx = new VariableContext
        {
            NodeOutputs = new Dictionary<string, string>
            {
                ["node_1"] = "search results",
                ["node_2"] = "analysis"
            },
            NodeNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Search Data"] = "node_1",
                ["Analyze"] = "node_2"
            }
        };

        var result = CanvasResolver.Resolve(
            "Based on {{node:Search Data}}, summarize.", ctx);
        Assert.Equal("Based on search results, summarize.", result);
    }

    [Fact]
    public void Resolver_CanvasMode_ResolvesNodeById()
    {
        // 直接用 ID 查也要可以（免反向查）
        var ctx = new VariableContext
        {
            NodeOutputs = new Dictionary<string, string> { ["node_1"] = "output" },
            NodeNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Search"] = "node_1"
            }
        };

        var result = CanvasResolver.Resolve("Use {{node:node_1}} data.", ctx);
        Assert.Equal("Use output data.", result);
    }

    [Fact]
    public void Resolver_CanvasMode_CaseInsensitiveName()
    {
        var ctx = new VariableContext
        {
            NodeOutputs = new Dictionary<string, string> { ["node_1"] = "data" },
            NodeNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Search Data"] = "node_1"
            }
        };

        var result = CanvasResolver.Resolve("Use {{node:search data}}.", ctx);
        Assert.Equal("Use data.", result);
    }

    [Fact]
    public void Resolver_ExtractNodeReferenceNames_ReturnsDistinctNames()
    {
        var names = VariableResolver.ExtractNodeReferenceNames(
            "{{node:a}} and {{node:b}} and {{node:a}} again");
        Assert.Equal(2, names.Count);
        Assert.Contains("a", names);
        Assert.Contains("b", names);
    }

    [Fact]
    public void Resolver_HasReferences_DetectsPattern()
    {
        Assert.True(CanvasResolver.HasReferences("Use {{node:search}} data"));
        Assert.False(CanvasResolver.HasReferences("No references here"));
        Assert.False(CanvasResolver.HasReferences(null));
    }

    [Fact]
    public void Resolver_NoReferences_SkipsRegex()
    {
        var ctx = new VariableContext
        {
            NodeOutputs = new Dictionary<string, string> { ["x"] = "y" }
        };
        var result = CanvasResolver.Resolve("No refs", ctx);
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
        var longOutput = new string('x', VariableResolver.CompressionThreshold + 100);
        var ctx = new VariableContext
        {
            NodeOutputs = new Dictionary<string, string> { ["search"] = longOutput }
        };
        var compactor = new FakeCompactor();

        var result = await CanvasResolver.ResolveAsync(
            "Based on {{node:search}}, summarize.", ctx, compactor, "summarize context");

        Assert.Equal(1, compactor.CallCount);
        Assert.Contains("[compressed:", result);
        Assert.Contains("summarize.", result);
        Assert.DoesNotContain("{{node:", result);
    }

    [Fact]
    public async Task ResolveAsync_SkipsCompressionForShortOutput()
    {
        var ctx = new VariableContext
        {
            NodeOutputs = new Dictionary<string, string> { ["search"] = "short output" }
        };
        var compactor = new FakeCompactor();

        var result = await CanvasResolver.ResolveAsync(
            "Based on {{node:search}}, summarize.", ctx, compactor, "context");

        Assert.Equal(0, compactor.CallCount);
        Assert.Equal("Based on short output, summarize.", result);
    }

    [Fact]
    public async Task ResolveAsync_MultipleRefs_OnlyCompressesLong()
    {
        var longOutput = new string('y', VariableResolver.CompressionThreshold + 50);
        var ctx = new VariableContext
        {
            NodeOutputs = new Dictionary<string, string>
            {
                ["long_node"] = longOutput,
                ["short_node"] = "brief"
            }
        };
        var compactor = new FakeCompactor();

        var result = await CanvasResolver.ResolveAsync(
            "{{node:long_node}} and {{node:short_node}}", ctx, compactor, "context");

        Assert.Equal(1, compactor.CallCount); // 只壓了 long_node
        Assert.Contains("[compressed:", result);
        Assert.Contains("brief", result);
    }

    [Fact]
    public async Task ResolveAsync_NoReferences_SkipsCompactor()
    {
        var ctx = new VariableContext
        {
            NodeOutputs = new Dictionary<string, string> { ["x"] = "y" }
        };
        var compactor = new FakeCompactor();

        var result = await CanvasResolver.ResolveAsync(
            "No refs here", ctx, compactor, "context");

        Assert.Equal(0, compactor.CallCount);
        Assert.Equal("No refs here", result);
    }

    [Fact]
    public async Task ResolveAsync_Canvas_CompressesLongOutput()
    {
        var longOutput = new string('z', VariableResolver.CompressionThreshold + 200);
        var ctx = new VariableContext
        {
            NodeOutputs = new Dictionary<string, string> { ["node_1"] = longOutput },
            NodeNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Search"] = "node_1"
            }
        };
        var compactor = new FakeCompactor();

        var result = await CanvasResolver.ResolveAsync(
            "Use {{node:Search}}.", ctx, compactor, "context");

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
