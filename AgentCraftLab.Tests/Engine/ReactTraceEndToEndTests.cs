using AgentCraftLab.Data;
using System.Text.Json;
using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

/// <summary>
/// 端到端測試 — 模擬真實 ReAct 執行軌跡，驗證轉換出的 FlowPlan JSON 結構完整性。
/// </summary>
public class ReactTraceEndToEndTests
{
    private static ExecutionEvent ToolCall(string toolName, string argsJson)
        => ExecutionEvent.ToolCall("Autonomous Agent", toolName, argsJson);

    private static ExecutionEvent Completed()
        => ExecutionEvent.WorkflowCompleted();

    [Fact]
    public void RealWorldTrace_CloudPlatformComparison_ProducesValidPlan()
    {
        // 模擬真實的雲端平台比較軌跡（9 個 spawn + 1 collect）
        var events = new List<ExecutionEvent>
        {
            ToolCall("spawn_sub_agent", "{\"task\":\"查找 AWS 的 GPU 實例價格\",\"tools\":[\"AzureWebSearch\"],\"model\":\"gpt-4o-mini\"}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查找 Azure 的 GPU 實例價格\",\"tools\":[\"AzureWebSearch\"],\"model\":\"gpt-4o-mini\"}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查找 GCP 的 GPU 實例價格\",\"tools\":[\"AzureWebSearch\"],\"model\":\"gpt-4o-mini\"}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查找 AWS 推論 API 定價\",\"tools\":[\"AzureWebSearch\"],\"model\":\"gpt-4o-mini\"}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查找 Azure 推論 API 定價\",\"tools\":[\"AzureWebSearch\"],\"model\":\"gpt-4o-mini\"}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查找 GCP 推論 API 定價\",\"tools\":[\"AzureWebSearch\"],\"model\":\"gpt-4o-mini\"}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查找 AWS 的免費額度\",\"tools\":[\"AzureWebSearch\"],\"model\":\"gpt-4o-mini\"}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查找 Azure 的免費額度\",\"tools\":[\"AzureWebSearch\"],\"model\":\"gpt-4o-mini\"}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查找 GCP 的免費額度\",\"tools\":[\"AzureWebSearch\"],\"model\":\"gpt-4o-mini\"}"),
            ToolCall("collect_results", "{}"),
            Completed()
        };

        var json = ReactTraceConverter.ConvertToFlowPlanJson(events, "比較 AWS、Azure、GCP 的 AI 推論成本");
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");

        // 1 parallel（9 branches）+ 1 summarizer = 2 nodes
        Assert.Equal(2, nodes.GetArrayLength());

        var parallel = nodes[0];
        Assert.Equal("parallel", parallel.GetProperty("type").GetString());
        Assert.Equal(9, parallel.GetProperty("branches").GetArrayLength());
        Assert.Equal("labeled", parallel.GetProperty("merge").GetString());

        // 每個 branch 都有 tools
        foreach (var branch in parallel.GetProperty("branches").EnumerateArray())
        {
            Assert.True(branch.TryGetProperty("tools", out var tools));
            Assert.Contains("AzureWebSearch", tools[0].GetString());
        }

        var summarizer = nodes[1];
        Assert.Equal("agent", summarizer.GetProperty("type").GetString());
        Assert.Contains("比較 AWS", summarizer.GetProperty("instructions").GetString());
    }

    [Fact]
    public void RealWorldTrace_StockComparison_TwoBatchSpawn()
    {
        // 模擬真實的股票比較軌跡（2 批 spawn）
        var events = new List<ExecutionEvent>
        {
            ToolCall("spawn_sub_agent", "{\"task\":\"查 NVIDIA 股價\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"查 Tesla 股價\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("collect_results", "{}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"分析 NVIDIA 利多利空\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"分析 Tesla 利多利空\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("collect_results", "{}"),
            Completed()
        };

        var json = ReactTraceConverter.ConvertToFlowPlanJson(events, "比較 NVIDIA 和 Tesla");
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");

        // 2 parallel + 1 summarizer = 3 nodes
        Assert.Equal(3, nodes.GetArrayLength());
        Assert.Equal("parallel", nodes[0].GetProperty("type").GetString());
        Assert.Equal(2, nodes[0].GetProperty("branches").GetArrayLength());
        Assert.Equal("parallel", nodes[1].GetProperty("type").GetString());
        Assert.Equal(2, nodes[1].GetProperty("branches").GetArrayLength());
        Assert.Equal("agent", nodes[2].GetProperty("type").GetString());
    }

    [Fact]
    public void ConvertedPlanJson_IsValidForFlowPlannerPrompt()
    {
        // 驗證轉出的 JSON 可以注入 FlowPlannerPrompt 不報錯
        var events = new List<ExecutionEvent>
        {
            ToolCall("spawn_sub_agent", "{\"task\":\"研究 A\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("spawn_sub_agent", "{\"task\":\"研究 B\",\"tools\":[\"AzureWebSearch\"]}"),
            ToolCall("collect_results", "{}"),
            Completed()
        };

        var planJson = ReactTraceConverter.ConvertToFlowPlanJson(events, "比較 A 和 B");
        Assert.NotNull(planJson);

        // 注入 FlowPlannerPrompt — 不應拋例外
        var request = new GoalExecutionRequest
        {
            Goal = "比較 A 和 B",
            Credentials = new Dictionary<string, ProviderCredential>()
        };

        var prompt = FlowPlannerPrompt.Build(request, experienceHint: planJson);
        Assert.Contains("Reference Plan", prompt);
        Assert.Contains("parallel", prompt);
        Assert.Contains("AzureWebSearch", prompt);
    }

    [Fact]
    public void FunctionsPrefixInSpawn_NormalizedInOutput()
    {
        var events = new List<ExecutionEvent>
        {
            ToolCall("spawn_sub_agent", "{\"task\":\"查資料\",\"tools\":[\"functions.AzureWebSearch\",\"functions.Calculator\"]}"),
            ToolCall("collect_results", "{}"),
            Completed()
        };

        var json = ReactTraceConverter.ConvertToFlowPlanJson(events, "test");
        Assert.NotNull(json);
        Assert.Contains("AzureWebSearch", json);
        Assert.Contains("Calculator", json);
        Assert.DoesNotContain("functions.", json);
    }
}
