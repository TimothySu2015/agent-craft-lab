using System.Collections.Concurrent;
using System.Diagnostics;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace AgentCraftLab.Api.Diagnostics;

/// <summary>
/// Trace 收集器 — 雙軌：
/// 1. Event-based（UI 用）：從 ExecutionEvent 即時組裝 span，無 AsyncLocal 依賴
/// 2. OTel Exporter（外部工具用）：攔截已完成的 Activity，可選匯出到 Aspire/Jaeger
/// </summary>
public sealed class TraceCollectorExporter : BaseExporter<Activity>
{
    private readonly ConcurrentDictionary<string, TraceSession> _sessions = new();

    /// <summary>以 sessionId（= AG-UI runId）註冊 trace session。</summary>
    public void Register(string sessionId)
    {
        _sessions[sessionId] = new TraceSession();
    }

    // ═══════════════════════════════════════════════════════════
    // Track 1: Event-based（UI 用）— 從 ExecutionEvent 即時組裝
    // ═══════════════════════════════════════════════════════════

    /// <summary>從 ExecutionEvent 即時組裝 span。每個事件呼叫一次。</summary>
    public void RecordEvent(string sessionId, ExecutionEvent evt)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return;

        var nodeName = evt.Metadata?.GetValueOrDefault(MetadataKeys.NodeName, "") ?? "";
        var nodeType = evt.Metadata?.GetValueOrDefault(MetadataKeys.NodeType, "") ?? "";

        switch (evt.Type)
        {
            case EventTypes.NodeExecuting:
                if (!string.IsNullOrEmpty(nodeName))
                    OpenSpan(session, $"node_{nodeName}", nodeName, nodeType);
                break;

            case EventTypes.NodeCompleted:
                if (!string.IsNullOrEmpty(nodeName))
                    CloseSpan(session, $"node_{nodeName}");
                break;

            case EventTypes.NodeCancelled:
                if (!string.IsNullOrEmpty(nodeName))
                    CloseSpan(session, $"node_{nodeName}", "cancelled");
                break;

            case EventTypes.AgentStarted:
            {
                // Sequential/Concurrent 策略沒有 NodeExecuting，用 AgentStarted 開 span
                var name = evt.AgentName ?? "";
                if (!string.IsNullOrEmpty(name))
                    OpenSpan(session, $"node_{name}", name, "agent");
                break;
            }

            case EventTypes.AgentCompleted:
            {
                var name = evt.AgentName ?? "";
                if (string.IsNullOrEmpty(name)) break;

                var spanId = $"node_{name}";
                if (!session.Spans.TryGetValue(spanId, out var span)) break;

                // 補充 metadata
                if (evt.Metadata?.TryGetValue(MetadataKeys.InputTokens, out var inStr) == true)
                    if (int.TryParse(inStr, out var it)) span.InputTokens = (span.InputTokens ?? 0) + it;
                if (evt.Metadata?.TryGetValue(MetadataKeys.OutputTokens, out var outStr) == true)
                    if (int.TryParse(outStr, out var ot)) span.OutputTokens = (span.OutputTokens ?? 0) + ot;
                span.Tokens = (span.InputTokens ?? 0) + (span.OutputTokens ?? 0);

                var model = evt.Metadata?.GetValueOrDefault(MetadataKeys.Model, "");
                if (!string.IsNullOrEmpty(model)) span.Model = model;

                if (evt.Text is { Length: > 0 })
                    span.Result = evt.Text.Length > 2000 ? evt.Text[..2000] : evt.Text;

                // Sequential/Concurrent 沒有 NodeCompleted，用 AgentCompleted 關 span
                CloseSpan(session, spanId);
                break;
            }

            case EventTypes.ToolCall:
            {
                var span = FindSpan(session, evt.AgentName);
                if (span is null) break;

                span.ToolCalls.Add(new LiveToolCall
                {
                    Name = evt.Text ?? "tool",
                    StartMs = session.Stopwatch.Elapsed.TotalMilliseconds,
                });
                break;
            }

            case EventTypes.ToolResult:
            {
                var span = FindSpan(session, evt.AgentName);
                if (span is null) break;

                var endMs = session.Stopwatch.Elapsed.TotalMilliseconds;
                var pending = span.ToolCalls.LastOrDefault(t => t.Result is null);
                if (pending is not null)
                {
                    pending.Result = evt.Text is { Length: > 2000 }
                        ? evt.Text[..2000] : evt.Text;
                    pending.EndMs = endMs;
                }
                else
                {
                    span.ToolCalls.Add(new LiveToolCall
                    {
                        Name = evt.AgentName ?? "",
                        Result = evt.Text is { Length: > 2000 } ? evt.Text[..2000] : evt.Text,
                        StartMs = endMs,
                        EndMs = endMs,
                    });
                }
                break;
            }
        }
    }

    /// <summary>STATE_SNAPSHOT 時拉取即時 span 列表（UI 用）。</summary>
    public IReadOnlyList<TraceSpanModel> GetSpans(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return [];
        var now = session.Stopwatch.Elapsed.TotalMilliseconds;

        return session.Spans.Values.Select(s => new TraceSpanModel
        {
            Id = s.Id,
            Name = s.Name,
            Type = s.Type,
            Source = "platform",
            Status = s.Status,
            StartMs = s.StartMs,
            EndMs = s.Status == "running" ? now : s.EndMs,
            InputTokens = s.InputTokens,
            OutputTokens = s.OutputTokens,
            Tokens = s.Tokens,
            Model = s.Model,
            Result = s.Result,
            ToolCalls = s.ToolCalls.Count > 0
                ? s.ToolCalls.Select(t => new TraceToolCall
                {
                    Name = t.Name,
                    Result = t.Result,
                    DurationMs = t.EndMs > t.StartMs ? t.EndMs - t.StartMs : 0,
                }).ToList()
                : null,
        }).ToList();
    }

    /// <summary>執行結束時呼叫。組裝 TraceDataModel 並移除 session。</summary>
    public TraceDataModel? Complete(string sessionId, string? workflowName = null)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
            return null;

        var spans = GetSpansFromSession(session);
        return TraceDataModel.Build(sessionId, spans, workflowName);
    }

    private IReadOnlyList<TraceSpanModel> GetSpansFromSession(TraceSession session)
    {
        var now = session.Stopwatch.Elapsed.TotalMilliseconds;
        return session.Spans.Values.Select(s => new TraceSpanModel
        {
            Id = s.Id, Name = s.Name, Type = s.Type, Source = "platform",
            Status = s.Status, StartMs = s.StartMs,
            EndMs = s.Status == "running" ? now : s.EndMs,
            InputTokens = s.InputTokens, OutputTokens = s.OutputTokens,
            Tokens = s.Tokens, Model = s.Model, Result = s.Result,
            ToolCalls = s.ToolCalls.Count > 0
                ? s.ToolCalls.Select(t => new TraceToolCall { Name = t.Name, Result = t.Result, DurationMs = t.EndMs > t.StartMs ? t.EndMs - t.StartMs : 0 }).ToList()
                : null,
        }).ToList();
    }

    // ═══════════════════════════════════════════════════════════
    // Track 2: OTel Exporter（外部工具用）— 可選，不影響 UI
    // ═══════════════════════════════════════════════════════════

    /// <summary>OTel SDK 回呼 — 外部工具（Aspire/Jaeger）用。UI 不依賴此路徑。</summary>
    public override ExportResult Export(in Batch<Activity> batch)
    {
        // Activity 直接由 OTel pipeline 匯出到外部工具（Console/OTLP Exporter）
        // 此方法保留介面相容，不需要額外處理
        return ExportResult.Success;
    }

    /// <summary>背景清理：移除超過指定時間未 Complete 的 session。</summary>
    public void CleanupStale(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var (id, session) in _sessions)
        {
            if (session.CreatedAt < cutoff)
                _sessions.TryRemove(id, out _);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // Private helpers
    // ═══════════════════════════════════════════════════════════

    /// <summary>查找 span：先精確匹配 node_{agentName}，再 fallback 到最後一個 running span。</summary>
    private static LiveSpan? FindSpan(TraceSession session, string? agentName)
    {
        if (!string.IsNullOrEmpty(agentName) &&
            session.Spans.TryGetValue($"node_{agentName}", out var exact))
            return exact;

        // Fallback：executor 的 agentName 可能和策略層的 nodeName 不同
        // 找最後一個 running span（通常就是當前正在執行的節點）
        return session.Spans.Values.LastOrDefault(s => s.Status == "running")
            ?? session.Spans.Values.LastOrDefault();
    }

    private static void OpenSpan(TraceSession session, string spanId, string name, string type)
    {
        if (session.Spans.TryGetValue(spanId, out var existing))
        {
            // 迴圈重入：保留原始 startMs，重設為 running
            existing.Status = "running";
            return;
        }

        session.Spans[spanId] = new LiveSpan
        {
            Id = spanId,
            Name = type == "agent" ? name : $"{name} ({type})",
            Type = type,
            StartMs = session.Stopwatch.Elapsed.TotalMilliseconds,
            Status = "running",
        };
    }

    private static void CloseSpan(TraceSession session, string spanId, string status = "completed")
    {
        if (!session.Spans.TryGetValue(spanId, out var span)) return;
        var endMs = session.Stopwatch.Elapsed.TotalMilliseconds;
        if (endMs > span.EndMs) span.EndMs = endMs;
        span.Status = status;
    }

    private sealed class TraceSession
    {
        public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public ConcurrentDictionary<string, LiveSpan> Spans { get; } = new();
    }

    private sealed class LiveSpan
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
        public double StartMs { get; init; }
        public double EndMs { get; set; }
        public string Status { get; set; } = "running";
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public int? Tokens { get; set; }
        public string? Model { get; set; }
        public string? Result { get; set; }
        public List<LiveToolCall> ToolCalls { get; } = [];
    }

    private sealed class LiveToolCall
    {
        public string Name { get; init; } = "";
        public string? Result { get; set; }
        public double StartMs { get; init; }
        public double EndMs { get; set; }
    }
}

/// <summary>DI 註冊擴充方法。</summary>
public static class TraceCollectorExtensions
{
    public static TracerProviderBuilder AddTraceCollectorExporter(
        this TracerProviderBuilder builder, IServiceCollection services)
    {
        var exporter = new TraceCollectorExporter();
        services.AddSingleton(exporter);
        return builder.AddProcessor(new SimpleActivityExportProcessor(exporter));
    }
}
