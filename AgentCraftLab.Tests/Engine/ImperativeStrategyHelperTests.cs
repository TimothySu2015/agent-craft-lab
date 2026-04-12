using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Strategies;
using AgentCraftLab.Engine.Strategies.NodeExecutors;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

/// <summary>
/// ImperativeWorkflowStrategy 的 internal static helper 方法測試。
/// 這些是重構時會被提取到 INodeExecutor 的純邏輯方法。
/// </summary>
public class ImperativeStrategyHelperTests
{
    // ════════════════════════════════════════
    // FindStartNode
    // ════════════════════════════════════════

    [Fact]
    public void FindStartNode_WithStartNode_ReturnsFirstAgent()
    {
        var payload = new AgentCraftLab.Engine.Models.Schema.WorkflowPayload
        {
            Nodes =
            [
                new StartNode { Id = "s1" },
                new AgentNode { Id = "a1", Name = "Agent1" }
            ],
            Connections = [new AgentCraftLab.Engine.Models.Schema.Connection { From = "s1", To = "a1" }]
        };
        var (result, path) = ImperativeWorkflowStrategy.FindStartNode(payload);
        Assert.NotNull(result);
        Assert.Equal("a1", result!.Id);
        Assert.Equal("start-node", path);
    }

    [Fact]
    public void FindStartNode_NoStartNode_ReturnsFirstExecutable()
    {
        var payload = new AgentCraftLab.Engine.Models.Schema.WorkflowPayload
        {
            Nodes =
            [
                new AgentNode { Id = "a1", Name = "Agent1" },
                new AgentNode { Id = "a2", Name = "Agent2" }
            ],
            Connections = []
        };
        var (result, _) = ImperativeWorkflowStrategy.FindStartNode(payload);
        Assert.NotNull(result);
    }

    [Fact]
    public void FindStartNode_EmptyPayload_ReturnsNull()
    {
        var payload = new AgentCraftLab.Engine.Models.Schema.WorkflowPayload { Nodes = [], Connections = [] };
        var (result, _) = ImperativeWorkflowStrategy.FindStartNode(payload);
        Assert.Null(result);
    }

    // ════════════════════════════════════════
    // InitializeChatHistories
    // ════════════════════════════════════════

    [Fact]
    public void InitializeChatHistories_InmemoryNodes_GetHistory()
    {
        var nodes = new List<NodeConfig>
        {
            new AgentNode
            {
                Id = "a1",
                Name = "A1",
                Instructions = "Be helpful",
                History = new HistoryConfig { Provider = HistoryProviderKind.InMemory }
            },
            new AgentNode { Id = "a2", Name = "A2" } // no history
        };
        var histories = ImperativeWorkflowStrategy.InitializeChatHistories(nodes);
        Assert.True(histories.ContainsKey("a1"));
        Assert.False(histories.ContainsKey("a2"));
        Assert.Single(histories["a1"]); // system message
        Assert.Equal(ChatRole.System, histories["a1"][0].Role);
    }

    [Fact]
    public void InitializeChatHistories_NoInmemoryNodes_Empty()
    {
        var nodes = new List<NodeConfig>
        {
            new AgentNode { Id = "a1", Name = "A1" }
        };
        var histories = ImperativeWorkflowStrategy.InitializeChatHistories(nodes);
        Assert.Empty(histories);
    }

    // ════════════════════════════════════════
    // SplitIterationInput
    // ════════════════════════════════════════

    [Fact]
    public void SplitIteration_JsonArray_ParsesCorrectly()
    {
        var items = ImperativeWorkflowStrategy.SplitIterationInput("json-array", "\n", "[\"apple\",\"banana\",\"cherry\"]");
        Assert.Equal(3, items.Count);
        Assert.Equal("apple", items[0]);
        Assert.Equal("banana", items[1]);
    }

    [Fact]
    public void SplitIteration_JsonArrayWithSurroundingText_ExtractsBrackets()
    {
        var items = ImperativeWorkflowStrategy.SplitIterationInput("json-array", "\n", "Here are the items: [\"a\",\"b\"] done.");
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void SplitIteration_Delimiter_SplitsCorrectly()
    {
        var items = ImperativeWorkflowStrategy.SplitIterationInput("delimiter", ",", "x,y,z");
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void SplitIteration_DefaultNewline()
    {
        var items = ImperativeWorkflowStrategy.SplitIterationInput("delimiter", "\n", "line1\nline2\nline3");
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void SplitIteration_EmptyInput_ReturnsEmpty()
    {
        var items = ImperativeWorkflowStrategy.SplitIterationInput("json-array", "\n", "");
        Assert.Empty(items);
    }

    [Fact]
    public void SplitIteration_InvalidJson_FallbackToDelimiter()
    {
        var items = ImperativeWorkflowStrategy.SplitIterationInput("json-array", "\n", "not json at all");
        Assert.Single(items);
    }

    // ════════════════════════════════════════
    // MergeParallelResults
    // ════════════════════════════════════════

    [Fact]
    public void MergeParallel_Labeled_Default()
    {
        var results = new List<(string Name, string Result)>
        {
            ("AAPL", "Price: $250"),
            ("MSFT", "Price: $400")
        };
        var merged = ImperativeWorkflowStrategy.MergeParallelResults(results, null);
        Assert.Contains("[AAPL]", merged);
        Assert.Contains("[MSFT]", merged);
        Assert.Contains("Price: $250", merged);
    }

    [Fact]
    public void MergeParallel_Join()
    {
        var results = new List<(string Name, string Result)>
        {
            ("A", "Result A"),
            ("B", "Result B")
        };
        var merged = ImperativeWorkflowStrategy.MergeParallelResults(results, "join");
        Assert.Equal("Result A\nResult B", merged);
    }

    [Fact]
    public void MergeParallel_Json()
    {
        var results = new List<(string Name, string Result)>
        {
            ("X", "value1"),
            ("Y", "value2")
        };
        var merged = ImperativeWorkflowStrategy.MergeParallelResults(results, "json");
        Assert.Contains("\"X\"", merged);
        Assert.Contains("\"value1\"", merged);
    }

    [Fact]
    public void MergeParallel_Empty()
    {
        var merged = ImperativeWorkflowStrategy.MergeParallelResults([], "labeled");
        Assert.Equal("", merged);
    }

    // ════════════════════════════════════════
    // BuildResponseFormatOptions — 已移到 AgentNodeExecutor，簽名收 OutputConfig
    // ════════════════════════════════════════

    [Fact]
    public void BuildResponseFormat_Text_ReturnsNull()
    {
        var output = new AgentCraftLab.Engine.Models.Schema.OutputConfig();
        var result = AgentNodeExecutor.BuildResponseFormatOptions(output);
        Assert.Null(result);
    }

    [Fact]
    public void BuildResponseFormat_Json_ReturnsChatOptions()
    {
        var output = new AgentCraftLab.Engine.Models.Schema.OutputConfig
        {
            Kind = AgentCraftLab.Engine.Models.Schema.OutputFormat.Json
        };
        var result = AgentNodeExecutor.BuildResponseFormatOptions(output);
        Assert.NotNull(result);
        Assert.NotNull(result!.ResponseFormat);
    }

    [Fact]
    public void BuildResponseFormat_JsonSchema_WithValidSchema()
    {
        var output = new AgentCraftLab.Engine.Models.Schema.OutputConfig
        {
            Kind = AgentCraftLab.Engine.Models.Schema.OutputFormat.JsonSchema,
            SchemaJson = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}"
        };
        var result = AgentNodeExecutor.BuildResponseFormatOptions(output);
        Assert.NotNull(result);
    }

    [Fact]
    public void BuildResponseFormat_JsonSchema_InvalidSchema_ReturnsNull()
    {
        var output = new AgentCraftLab.Engine.Models.Schema.OutputConfig
        {
            Kind = AgentCraftLab.Engine.Models.Schema.OutputFormat.JsonSchema,
            SchemaJson = "not valid json"
        };
        var result = AgentNodeExecutor.BuildResponseFormatOptions(output);
        Assert.Null(result);
    }

    // ════════════════════════════════════════
    // Truncate
    // ════════════════════════════════════════

    [Fact]
    public void Truncate_ShortText_NoChange()
    {
        var result = ImperativeWorkflowStrategy.Truncate("hello", 80);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Truncate_LongText_CutsWithEllipsis()
    {
        var result = ImperativeWorkflowStrategy.Truncate("1234567890", 5);
        Assert.Equal(8, result.Length); // 5 + "..."
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void Truncate_ExactLength_NoChange()
    {
        var result = ImperativeWorkflowStrategy.Truncate("12345", 5);
        Assert.Equal("12345", result);
    }

    [Fact]
    public void Truncate_EmptyString()
    {
        var result = ImperativeWorkflowStrategy.Truncate("", 10);
        Assert.Equal("", result);
    }

    [Fact]
    public void Truncate_DefaultMaxLen()
    {
        var longText = new string('x', 100);
        var result = ImperativeWorkflowStrategy.Truncate(longText);
        Assert.Equal(83, result.Length); // 80 + "..."
    }
}
