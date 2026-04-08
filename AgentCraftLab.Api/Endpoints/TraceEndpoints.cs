using AgentCraftLab.Api.Diagnostics;
using AgentCraftLab.Data;

namespace AgentCraftLab.Api.Endpoints;

/// <summary>
/// Trace 端點 — 診斷 + 瀑布圖資料。
/// </summary>
public static class TraceEndpoints
{
    public static void MapTraceEndpoints(this WebApplication app)
    {
        // ── 既有 JSONL 診斷端點（Claude Code 用）──
        app.MapGet("/api/traces/latest", () =>
        {
            var path = ExecutionTraceWriter.GetLatestTracePath();
            if (path is null) return Results.NotFound(new { error = "No traces found" });
            var lines = File.ReadAllLines(path);
            var runId = Path.GetFileNameWithoutExtension(path);
            return Results.Ok(new { runId, path, entries = lines });
        });

        app.MapGet("/api/traces/{runId}", (string runId) =>
        {
            var path = Path.Combine("Data/traces", $"{runId}.jsonl");
            if (!File.Exists(path)) return Results.NotFound(new { error = $"Trace '{runId}' not found" });
            var lines = File.ReadAllLines(path);
            return Results.Ok(new { runId, entries = lines });
        });

        // ── 新：從 DB 讀取結構化 TraceData（RequestLogsPage 用）──
        app.MapGet("/api/traces/log/{logId}", async (string logId, IRequestLogStore store) =>
        {
            var traceJson = await store.GetTraceJsonAsync(logId);
            return traceJson is not null
                ? Results.Content(traceJson, "application/json")
                : Results.NotFound(new { error = $"Trace for log '{logId}' not found" });
        });
    }
}
