using System.Text.Json;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class ReactTraceConverterTests
{
    private static ExecutionEvent ToolCall(string toolName, string argsJson)
        => ExecutionEvent.ToolCall("Autonomous Agent", toolName, argsJson);

    private static ExecutionEvent Completed()
        => ExecutionEvent.WorkflowCompleted();

    private static ExecutionEvent TextChunk(string text)
        => ExecutionEvent.TextChunk("Autonomous Agent", text);

    [Fact]
    public void SpawnGroup_ConvertsToParallel()
    {
        var events = new List<ExecutionEvent>
        {
            ToolCall("spawn_sub_agent", "{\"task\":\"查 NVIDIA\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查 AMD\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查 Intel\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("collect_results", "{}"),
            TextChunk("總結"),
            Completed()
        };

        var json = ReactTraceConverter.ConvertToFlowPlanJson(events, "比較三家公司");
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        Assert.Equal(2, nodes.GetArrayLength()); // parallel + summarizer

        var parallel = nodes[0];
        Assert.Equal("parallel", parallel.GetProperty("type").GetString());
        Assert.Equal(3, parallel.GetProperty("branches").GetArrayLength());
        Assert.Equal("查 NVIDIA", parallel.GetProperty("branches")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void TwoSpawnBatches_TwoParallelNodes()
    {
        var events = new List<ExecutionEvent>
        {
            ToolCall("spawn_sub_agent", "{\"task\":\"查 A\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查 B\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("collect_results", "{}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"分析 A\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"分析 B\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("collect_results", "{}"),
            Completed()
        };

        var json = ReactTraceConverter.ConvertToFlowPlanJson(events, "比較分析");
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        Assert.Equal(3, nodes.GetArrayLength()); // parallel + parallel + summarizer

        Assert.Equal("parallel", nodes[0].GetProperty("type").GetString());
        Assert.Equal("parallel", nodes[1].GetProperty("type").GetString());
        Assert.Equal("agent", nodes[2].GetProperty("type").GetString());
    }

    [Fact]
    public void CreateAndAsk_ConvertsToAgent()
    {
        var events = new List<ExecutionEvent>
        {
            ToolCall("create_sub_agent", "{\"name\":\"analyst\",\"instructions\":\"深度分析數據\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("ask_sub_agent", "{\"name\":\"analyst\",\"message\":\"分析這些\"}"),
            Completed()
        };

        var json = ReactTraceConverter.ConvertToFlowPlanJson(events, "分析任務");
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        Assert.Equal(2, nodes.GetArrayLength()); // agent + summarizer

        var agent = nodes[0];
        Assert.Equal("agent", agent.GetProperty("type").GetString());
        Assert.Equal("analyst", agent.GetProperty("name").GetString());
        Assert.Equal("深度分析數據", agent.GetProperty("instructions").GetString());
    }

    [Fact]
    public void MixedMode_SpawnAndAsk()
    {
        var events = new List<ExecutionEvent>
        {
            ToolCall("spawn_sub_agent", "{\"task\":\"查 NVIDIA\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查 AMD\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("collect_results", "{}"),
            ToolCall("create_sub_agent", "{\"name\":\"analyst\",\"instructions\":\"比較分析\",\"tools\":[]}"),
            ToolCall("ask_sub_agent", "{\"name\":\"analyst\",\"message\":\"比較\"}"),
            Completed()
        };

        var json = ReactTraceConverter.ConvertToFlowPlanJson(events, "比較");
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        Assert.Equal(3, nodes.GetArrayLength()); // parallel + agent + summarizer

        Assert.Equal("parallel", nodes[0].GetProperty("type").GetString());
        Assert.Equal("agent", nodes[1].GetProperty("type").GetString());
        Assert.Equal("agent", nodes[2].GetProperty("type").GetString()); // summarizer
    }

    [Fact]
    public void SummarizerIncludesOriginalGoal()
    {
        var events = new List<ExecutionEvent>
        {
            ToolCall("spawn_sub_agent", "{\"task\":\"查資料\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("collect_results", "{}"),
            Completed()
        };

        var json = ReactTraceConverter.ConvertToFlowPlanJson(events, "分析 AI 晶片市場");
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        var summarizer = nodes[nodes.GetArrayLength() - 1];
        Assert.Contains("分析 AI 晶片市場", summarizer.GetProperty("instructions").GetString());
    }

    [Fact]
    public void SimpleTask_ReturnsNull()
    {
        var events = new List<ExecutionEvent>
        {
            ToolCall("AzureWebSearch", "{\"query\":\"Tokyo itinerary\"}"),
            TextChunk("結果"),
            Completed()
        };

        var result = ReactTraceConverter.ConvertToFlowPlanJson(events, "東京旅遊");
        Assert.Null(result);
    }

    [Fact]
    public void EmptyEvents_ReturnsNull()
    {
        var result = ReactTraceConverter.ConvertToFlowPlanJson([], "任何目標");
        Assert.Null(result);
    }

    [Fact]
    public void NoWorkflowCompleted_ReturnsNull()
    {
        var events = new List<ExecutionEvent>
        {
            ToolCall("spawn_sub_agent", "{\"task\":\"查資料\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("collect_results", "{}")
            // 沒有 WorkflowCompleted
        };

        var result = ReactTraceConverter.ConvertToFlowPlanJson(events, "測試");
        Assert.Null(result);
    }

    [Fact]
    public void ParseToolCallText_CorrectlyParses()
    {
        var (name, args) = ReactTraceConverter.ParseToolCallText(
            "spawn_sub_agent({\"task\":\"hello\"})");
        Assert.Equal("spawn_sub_agent", name);
        Assert.Contains("hello", args);
    }

    [Fact]
    public void NormalizeToolId_InConversion()
    {
        var events = new List<ExecutionEvent>
        {
            ToolCall("spawn_sub_agent", "{\"task\":\"查資料\",\"tools\":[\"functions.AzureWebSearch\"]}"),
            ToolCall("collect_results", "{}"),
            Completed()
        };

        var json = ReactTraceConverter.ConvertToFlowPlanJson(events, "測試");
        Assert.NotNull(json);
        Assert.Contains("AzureWebSearch", json);
        Assert.DoesNotContain("functions.AzureWebSearch", json);
    }
}
