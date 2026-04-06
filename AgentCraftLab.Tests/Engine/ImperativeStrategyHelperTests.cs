using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Strategies;
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
        var payload = new WorkflowPayload
        {
            Nodes =
            [
                new WorkflowNode { Id = "s1", Type = NodeTypes.Start },
                new WorkflowNode { Id = "a1", Type = NodeTypes.Agent, Name = "Agent1" }
            ],
            Connections = [new WorkflowConnection { From = "s1", To = "a1" }]
        };
        var (result, path) = ImperativeWorkflowStrategy.FindStartNode(payload);
        Assert.NotNull(result);
        Assert.Equal("a1", result!.Id);
        Assert.Equal("start-node", path);
    }

    [Fact]
    public void FindStartNode_NoStartNode_ReturnsFirstExecutable()
    {
        var payload = new WorkflowPayload
        {
            Nodes =
            [
                new WorkflowNode { Id = "a1", Type = NodeTypes.Agent, Name = "Agent1" },
                new WorkflowNode { Id = "a2", Type = NodeTypes.Agent, Name = "Agent2" }
            ],
            Connections = []
        };
        var (result, _) = ImperativeWorkflowStrategy.FindStartNode(payload);
        Assert.NotNull(result);
    }

    [Fact]
    public void FindStartNode_EmptyPayload_ReturnsNull()
    {
        var payload = new WorkflowPayload { Nodes = [], Connections = [] };
        var (result, _) = ImperativeWorkflowStrategy.FindStartNode(payload);
        Assert.Null(result);
    }

    // ════════════════════════════════════════
    // InitializeChatHistories
    // ════════════════════════════════════════

    [Fact]
    public void InitializeChatHistories_InmemoryNodes_GetHistory()
    {
        var nodes = new List<WorkflowNode>
        {
            new() { Id = "a1", Type = NodeTypes.Agent, Name = "A1", Instructions = "Be helpful", HistoryProvider = "inmemory" },
            new() { Id = "a2", Type = NodeTypes.Agent, Name = "A2" } // no history
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
        var nodes = new List<WorkflowNode>
        {
            new() { Id = "a1", Type = NodeTypes.Agent, Name = "A1" }
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
        var node = new WorkflowNode { Id = "i1", Type = NodeTypes.Iteration, SplitMode = "json-array" };
        var items = ImperativeWorkflowStrategy.SplitIterationInput(node, "[\"apple\",\"banana\",\"cherry\"]");
        Assert.Equal(3, items.Count);
        Assert.Equal("apple", items[0]);
        Assert.Equal("banana", items[1]);
    }

    [Fact]
    public void SplitIteration_JsonArrayWithSurroundingText_ExtractsBrackets()
    {
        var node = new WorkflowNode { Id = "i1", Type = NodeTypes.Iteration, SplitMode = "json-array" };
        var items = ImperativeWorkflowStrategy.SplitIterationInput(node, "Here are the items: [\"a\",\"b\"] done.");
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void SplitIteration_Delimiter_SplitsCorrectly()
    {
        var node = new WorkflowNode { Id = "i1", Type = NodeTypes.Iteration, SplitMode = "delimiter", IterationDelimiter = "," };
        var items = ImperativeWorkflowStrategy.SplitIterationInput(node, "x,y,z");
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void SplitIteration_DefaultNewline()
    {
        var node = new WorkflowNode { Id = "i1", Type = NodeTypes.Iteration, SplitMode = "delimiter" };
        var items = ImperativeWorkflowStrategy.SplitIterationInput(node, "line1\nline2\nline3");
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void SplitIteration_EmptyInput_ReturnsEmpty()
    {
        var node = new WorkflowNode { Id = "i1", Type = NodeTypes.Iteration };
        var items = ImperativeWorkflowStrategy.SplitIterationInput(node, "");
        Assert.Empty(items);
    }

    [Fact]
    public void SplitIteration_InvalidJson_FallbackToDelimiter()
    {
        var node = new WorkflowNode { Id = "i1", Type = NodeTypes.Iteration, SplitMode = "json-array" };
        var items = ImperativeWorkflowStrategy.SplitIterationInput(node, "not json at all");
        Assert.Single(items); // entire string as one item
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
    // BuildResponseFormatOptions
    // ════════════════════════════════════════

    [Fact]
    public void BuildResponseFormat_Text_ReturnsNull()
    {
        var node = new WorkflowNode { Id = "a1", Type = NodeTypes.Agent };
        var result = ImperativeWorkflowStrategy.BuildResponseFormatOptions(node);
        Assert.Null(result);
    }

    [Fact]
    public void BuildResponseFormat_Json_ReturnsChatOptions()
    {
        var node = new WorkflowNode { Id = "a1", Type = NodeTypes.Agent, OutputFormat = "json" };
        var result = ImperativeWorkflowStrategy.BuildResponseFormatOptions(node);
        Assert.NotNull(result);
        Assert.NotNull(result!.ResponseFormat);
    }

    [Fact]
    public void BuildResponseFormat_JsonSchema_WithValidSchema()
    {
        var node = new WorkflowNode
        {
            Id = "a1", Type = NodeTypes.Agent,
            OutputFormat = "json_schema",
            OutputSchema = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}"
        };
        var result = ImperativeWorkflowStrategy.BuildResponseFormatOptions(node);
        Assert.NotNull(result);
    }

    [Fact]
    public void BuildResponseFormat_JsonSchema_InvalidSchema_ReturnsNull()
    {
        var node = new WorkflowNode
        {
            Id = "a1", Type = NodeTypes.Agent,
            OutputFormat = "json_schema",
            OutputSchema = "not valid json"
        };
        var result = ImperativeWorkflowStrategy.BuildResponseFormatOptions(node);
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
