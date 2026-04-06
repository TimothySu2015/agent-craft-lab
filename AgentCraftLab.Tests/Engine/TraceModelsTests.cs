using System.Diagnostics;
using AgentCraftLab.Engine.Diagnostics;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Tests.Engine;

public class TraceModelsTests
{
    // ── FromActivity ──

    [Fact]
    public void FromActivity_BasicFields()
    {
        using var source = new ActivitySource("test");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        var rootStart = DateTime.UtcNow;
        using var activity = source.StartActivity("TestNode", ActivityKind.Internal)!;
        activity.SetTag("node.type", "agent");
        activity.SetTag("node.name", "Writer");
        activity.SetTag("node.id", "node-123");
        activity.SetTag("gen_ai.request.model", "gpt-4o");
        activity.SetTag("gen_ai.usage.input_tokens", "100");
        activity.SetTag("gen_ai.usage.output_tokens", "200");
        activity.Stop();

        var span = TraceSpanModel.FromActivity(activity, rootStart);

        Assert.Equal(activity.SpanId.ToString(), span.Id);
        Assert.Equal("TestNode", span.Name);
        Assert.Equal("agent", span.Type);
        Assert.Equal("node-123", span.NodeId);
        Assert.Equal("gpt-4o", span.Model);
        Assert.Equal(100, span.InputTokens);
        Assert.Equal(200, span.OutputTokens);
        Assert.Equal(300, span.Tokens);
        Assert.Equal("completed", span.Status);
    }

    [Fact]
    public void FromActivity_PlatformSource()
    {
        var rootStart = DateTime.UtcNow;
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = EngineActivitySource.Source.StartActivity("Test", ActivityKind.Internal)!;
        activity.Stop();

        var span = TraceSpanModel.FromActivity(activity, rootStart);
        Assert.Equal("platform", span.Source);
    }

    [Fact]
    public void FromActivity_FrameworkSource()
    {
        using var source = new ActivitySource("Microsoft.Extensions.AI");
        var rootStart = DateTime.UtcNow;
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("chat", ActivityKind.Internal)!;
        activity.Stop();

        var span = TraceSpanModel.FromActivity(activity, rootStart);
        Assert.Equal("framework", span.Source);
    }

    [Fact]
    public void FromActivity_ErrorStatus()
    {
        using var source = new ActivitySource("test");
        var rootStart = DateTime.UtcNow;
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("FailNode", ActivityKind.Internal)!;
        activity.SetStatus(ActivityStatusCode.Error, "timeout");
        activity.Stop();

        var span = TraceSpanModel.FromActivity(activity, rootStart);
        Assert.Equal("error", span.Status);
        Assert.Equal("timeout", span.Error);
    }

    [Fact]
    public void FromActivity_ToolEvents()
    {
        using var source = new ActivitySource("AgentCraftLab.Engine");
        var rootStart = DateTime.UtcNow;
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("Parallel", ActivityKind.Internal)!;
        activity.AddEvent(new ActivityEvent("tool_call", tags: new ActivityTagsCollection
        {
            { "tool.name", "search" },
            { "tool.args", "query=test" },
        }));
        activity.AddEvent(new ActivityEvent("tool_result", tags: new ActivityTagsCollection
        {
            { "tool.name", "search" },
            { "tool.result", "found 3 results" },
        }));
        activity.Stop();

        var span = TraceSpanModel.FromActivity(activity, rootStart);
        Assert.NotNull(span.ToolCalls);
        Assert.Single(span.ToolCalls);
        Assert.Equal("search", span.ToolCalls[0].Name);
        Assert.Equal("found 3 results", span.ToolCalls[0].Result);
    }

    [Fact]
    public void FromActivity_NullTokens()
    {
        using var source = new ActivitySource("test");
        var rootStart = DateTime.UtcNow;
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("NoTokens", ActivityKind.Internal)!;
        activity.Stop();

        var span = TraceSpanModel.FromActivity(activity, rootStart);
        Assert.Null(span.InputTokens);
        Assert.Null(span.OutputTokens);
        Assert.Null(span.Tokens);
    }

    // ── TraceDataModel.Build ──

    [Fact]
    public void Build_CalculatesTotals()
    {
        var spans = new[]
        {
            new TraceSpanModel { Id = "1", Name = "A", StartMs = 0, EndMs = 100, Tokens = 50 },
            new TraceSpanModel { Id = "2", Name = "B", StartMs = 100, EndMs = 300, Tokens = 150 },
        };

        var data = TraceDataModel.Build("trace-1", spans, "TestWorkflow");

        Assert.Equal("trace-1", data.TraceId);
        Assert.Equal("TestWorkflow", data.WorkflowName);
        Assert.Equal(300, data.TotalMs);
        Assert.Equal(200, data.TotalTokens);
        Assert.Equal("completed", data.Status);
        Assert.Equal(2, data.Spans.Count);
    }

    [Fact]
    public void Build_ErrorStatus()
    {
        var spans = new[]
        {
            new TraceSpanModel { Id = "1", Name = "A", StartMs = 0, EndMs = 100, Status = "completed" },
            new TraceSpanModel { Id = "2", Name = "B", StartMs = 100, EndMs = 200, Status = "error" },
        };

        var data = TraceDataModel.Build("trace-1", spans);
        Assert.Equal("error", data.Status);
    }

    [Fact]
    public void Build_TruncatesLongResult()
    {
        var longText = new string('x', 3000);
        var spans = new[]
        {
            new TraceSpanModel { Id = "1", Name = "A", StartMs = 0, EndMs = 100, Result = longText },
        };

        var data = TraceDataModel.Build("trace-1", spans);
        Assert.Equal(2000, data.Spans[0].Result!.Length);
        Assert.True(data.Spans[0].Truncated);
    }

    [Fact]
    public void Build_EmptySpans()
    {
        var data = TraceDataModel.Build("trace-1", []);
        Assert.Equal(0, data.TotalMs);
        Assert.Equal(0, data.TotalTokens);
        Assert.Empty(data.Spans);
    }
}
