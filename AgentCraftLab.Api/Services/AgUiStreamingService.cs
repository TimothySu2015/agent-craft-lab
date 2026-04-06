using System.Text.Json;
using AgentCraftLab.Api.Diagnostics;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Api;

/// <summary>
/// AG-UI SSE 串流服務 — 將 ExecutionEvent 流轉為 AG-UI SSE 事件。
/// Trace 資料從 ExecutionEvent 即時組裝（不依賴 OTel Activity）。
/// </summary>
public static class AgUiStreamingService
{
    public static async Task StreamExecutionEventsAsync(
        HttpContext ctx,
        RunAgentInput input,
        IAsyncEnumerable<ExecutionEvent> events,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct,
        HumanInputBridgeRegistry? bridgeRegistry = null,
        TraceCollectorExporter? traceCollector = null)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        var threadId = input.ThreadId;
        var runId = input.RunId;

        traceCollector?.Register(runId);

        var converter = new AgUiEventConverter(runId);
        await using var trace = new ExecutionTraceWriter(runId);

        await WriteSseEventAsync(ctx, new AgUiEvent
        {
            Type = AgUiEventTypes.RunStarted,
            ThreadId = threadId,
            RunId = runId
        }, jsonOptions, ct);

        var hasError = false;
        try
        {
            await foreach (var evt in events)
            {
                trace.Record(evt);

                // Event-based trace：每個事件即時組裝 span（不依賴 OTel Activity）
                traceCollector?.RecordEvent(runId, evt);

                if (evt.Type == EventTypes.WaitingForInput && bridgeRegistry is not null)
                {
                    bridgeRegistry.SetPending(threadId, runId, evt.Text ?? "", evt.InputType ?? "text", evt.Choices);
                }

                var traceSpans = traceCollector?.GetSpans(runId);
                var agUiEvents = converter.Convert(evt, threadId, runId, traceSpans);
                foreach (var agUiEvent in agUiEvents)
                {
                    await WriteSseEventAsync(ctx, agUiEvent, jsonOptions, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            hasError = true;
            Console.Error.WriteLine($"[AG-UI] RunError: {ex}");
            trace.Record(ExecutionEvent.Error(ex.Message));
            foreach (var e in converter.BuildErrorMessage(ex.Message))
                await WriteSseEventAsync(ctx, e, jsonOptions, ct);
            await WriteSseEventAsync(ctx, new AgUiEvent
            {
                Type = AgUiEventTypes.RunError,
                Message = ex.Message
            }, jsonOptions, ct);
        }

        if (!hasError)
        {
            await WriteSseEventAsync(ctx, new AgUiEvent
            {
                Type = AgUiEventTypes.RunFinished,
                ThreadId = threadId,
                RunId = runId
            }, jsonOptions, ct);
        }

        if (traceCollector is not null)
        {
            var traceData = traceCollector.Complete(runId);
            if (traceData is not null)
                ctx.Items["TraceData"] = traceData;
        }
    }

    private static async Task WriteSseEventAsync(
        HttpContext ctx, AgUiEvent evt, JsonSerializerOptions options, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(evt, options);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
}
