using AgentCraftLab.Api.Diagnostics;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Tests.Api;

public class TraceCollectorExporterTests
{
    private readonly TraceCollectorExporter _exporter = new();

    [Fact]
    public void Register_CreatesSession()
    {
        _exporter.Register("session-1");
        var spans = _exporter.GetSpans("session-1");
        Assert.NotNull(spans);
        Assert.Empty(spans);
    }

    [Fact]
    public void GetSpans_UnregisteredSession_ReturnsEmpty()
    {
        Assert.Empty(_exporter.GetSpans("nonexistent"));
    }

    [Fact]
    public void RecordEvent_NodeExecutingAndCompleted_CreatesSpan()
    {
        _exporter.Register("s1");

        _exporter.RecordEvent("s1", ExecutionEvent.NodeExecuting("agent", "Writer"));
        var running = _exporter.GetSpans("s1");
        Assert.Single(running);
        Assert.Equal("Writer", running[0].Name);
        Assert.Equal("running", running[0].Status);

        _exporter.RecordEvent("s1", ExecutionEvent.NodeCompleted("agent", "Writer", "done"));
        var completed = _exporter.GetSpans("s1");
        Assert.Single(completed);
        Assert.Equal("completed", completed[0].Status);
        Assert.True(completed[0].EndMs > 0);
    }

    [Fact]
    public void RecordEvent_AgentCompleted_SetsTokensAndModel()
    {
        _exporter.Register("s1");

        _exporter.RecordEvent("s1", ExecutionEvent.NodeExecuting("agent", "Writer"));
        _exporter.RecordEvent("s1", ExecutionEvent.AgentCompleted("Writer", "output text", 100, 200, "gpt-4o"));

        var spans = _exporter.GetSpans("s1");
        Assert.Single(spans);
        Assert.Equal(100, spans[0].InputTokens);
        Assert.Equal(200, spans[0].OutputTokens);
        Assert.Equal(300, spans[0].Tokens);
        Assert.Equal("gpt-4o", spans[0].Model);
        Assert.Equal("output text", spans[0].Result);
    }

    [Fact]
    public void RecordEvent_AgentStartedAndCompleted_WorksWithoutNodeExecuting()
    {
        // Sequential strategy doesn't emit NodeExecuting
        _exporter.Register("s1");

        _exporter.RecordEvent("s1", ExecutionEvent.AgentStarted("Researcher"));
        var running = _exporter.GetSpans("s1");
        Assert.Single(running);
        Assert.Equal("running", running[0].Status);

        _exporter.RecordEvent("s1", ExecutionEvent.AgentCompleted("Researcher", "result", 50, 60));
        var completed = _exporter.GetSpans("s1");
        Assert.Equal("completed", completed[0].Status);
    }

    [Fact]
    public void RecordEvent_ToolCallAndResult()
    {
        _exporter.Register("s1");

        _exporter.RecordEvent("s1", ExecutionEvent.NodeExecuting("agent", "Writer"));
        _exporter.RecordEvent("s1", ExecutionEvent.ToolCall("Writer", "AzureWebSearch", "query=test"));
        _exporter.RecordEvent("s1", ExecutionEvent.ToolResult("Writer", "AzureWebSearch", "3 results found"));

        var spans = _exporter.GetSpans("s1");
        Assert.Single(spans);
        Assert.NotNull(spans[0].ToolCalls);
        Assert.Single(spans[0].ToolCalls!);
        Assert.Equal("AzureWebSearch(query=test)", spans[0].ToolCalls![0].Name);
        Assert.Equal("AzureWebSearch: 3 results found", spans[0].ToolCalls![0].Result);
    }

    [Fact]
    public void RecordEvent_LoopReentry_PreservesStartMs()
    {
        _exporter.Register("s1");

        _exporter.RecordEvent("s1", ExecutionEvent.NodeExecuting("loop", "ReviewLoop"));
        var firstStart = _exporter.GetSpans("s1")[0].StartMs;

        _exporter.RecordEvent("s1", ExecutionEvent.NodeCompleted("loop", "ReviewLoop", ""));
        // Re-entry
        _exporter.RecordEvent("s1", ExecutionEvent.NodeExecuting("loop", "ReviewLoop"));

        var spans = _exporter.GetSpans("s1");
        Assert.Single(spans); // Same span, not duplicated
        Assert.Equal(firstStart, spans[0].StartMs); // Original startMs preserved
        Assert.Equal("running", spans[0].Status);
    }

    [Fact]
    public void Complete_ReturnsTraceDataAndRemovesSession()
    {
        _exporter.Register("s1");
        _exporter.RecordEvent("s1", ExecutionEvent.NodeExecuting("agent", "Writer"));
        _exporter.RecordEvent("s1", ExecutionEvent.AgentCompleted("Writer", "done", 100, 50));
        _exporter.RecordEvent("s1", ExecutionEvent.NodeCompleted("agent", "Writer", "done"));

        var data = _exporter.Complete("s1", "TestWorkflow");

        Assert.NotNull(data);
        Assert.Equal("s1", data.TraceId);
        Assert.Equal("TestWorkflow", data.WorkflowName);
        Assert.Single(data.Spans);

        // Session removed
        Assert.Empty(_exporter.GetSpans("s1"));
    }

    [Fact]
    public void Complete_UnregisteredSession_ReturnsNull()
    {
        Assert.Null(_exporter.Complete("nonexistent"));
    }

    [Fact]
    public void MultipleNodes_SameSession()
    {
        _exporter.Register("s1");

        _exporter.RecordEvent("s1", ExecutionEvent.NodeExecuting("agent", "Writer"));
        _exporter.RecordEvent("s1", ExecutionEvent.NodeCompleted("agent", "Writer", ""));
        _exporter.RecordEvent("s1", ExecutionEvent.NodeExecuting("loop", "ReviewLoop"));
        _exporter.RecordEvent("s1", ExecutionEvent.NodeCompleted("loop", "ReviewLoop", ""));

        Assert.Equal(2, _exporter.GetSpans("s1").Count);
    }

    [Fact]
    public void ConcurrentSessions_Isolated()
    {
        _exporter.Register("sA");
        _exporter.Register("sB");

        _exporter.RecordEvent("sA", ExecutionEvent.AgentStarted("AgentA"));
        _exporter.RecordEvent("sB", ExecutionEvent.AgentStarted("AgentB"));

        Assert.Equal("AgentA", _exporter.GetSpans("sA")[0].Name);
        Assert.Equal("AgentB", _exporter.GetSpans("sB")[0].Name);
    }

    [Fact]
    public void CleanupStale_RemovesOldSessions()
    {
        _exporter.Register("old-session");
        _exporter.CleanupStale(TimeSpan.Zero);
        Assert.Null(_exporter.Complete("old-session"));
    }
}
