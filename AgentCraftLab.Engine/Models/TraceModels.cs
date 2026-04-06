using System.Diagnostics;
using System.Text.Json.Serialization;

namespace AgentCraftLab.Engine.Models;

/// <summary>
/// 單一 trace span — 對應一個 Activity（框架自動或平台自訂）。
/// 前後端共用 schema，序列化為 camelCase JSON。
/// </summary>
public class TraceSpanModel
{
    public string Id { get; set; } = "";
    public string? ParentId { get; set; }
    public string? NodeId { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Source { get; set; } = "platform";
    public string Status { get; set; } = "completed";
    public double StartMs { get; set; }
    public double EndMs { get; set; }
    public string? Model { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? Tokens { get; set; }
    public string? Cost { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Input { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    /// <summary>Tool call/result 記錄（Parallel 分支、Iteration 項目等）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TraceToolCall>? ToolCalls { get; set; }

    public bool Truncated { get; set; }

    /// <summary>
    /// 從 System.Diagnostics.Activity 轉換。
    /// </summary>
    public static TraceSpanModel FromActivity(Activity activity, DateTime rootStartTime)
    {
        var source = activity.Source.Name == "AgentCraftLab.Engine"
            ? "platform"
            : "framework";

        var status = activity.Status == ActivityStatusCode.Error
            ? "error"
            : "completed";

        var inputTokens = ParseNullableInt(activity.GetTagItem("gen_ai.usage.input_tokens"));
        var outputTokens = ParseNullableInt(activity.GetTagItem("gen_ai.usage.output_tokens"));

        // 平台 Activity 不用 OTel parent-child（AsyncLocal 在 yield return 後不一致），
        // 所有節點扁平顯示，用 session.id tag 配對。
        return new TraceSpanModel
        {
            Id = activity.SpanId.ToString(),
            NodeId = activity.GetTagItem("node.id")?.ToString(),
            Name = activity.DisplayName,
            Type = activity.GetTagItem("node.type")?.ToString() ?? activity.OperationName,
            Source = source,
            Status = status,
            StartMs = (activity.StartTimeUtc - rootStartTime).TotalMilliseconds,
            EndMs = (activity.StartTimeUtc - rootStartTime).TotalMilliseconds
                    + activity.Duration.TotalMilliseconds,
            Model = activity.GetTagItem("gen_ai.request.model")?.ToString(),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Tokens = inputTokens.HasValue || outputTokens.HasValue
                ? (inputTokens ?? 0) + (outputTokens ?? 0)
                : null,
            ToolCalls = ParseToolEvents(activity),
            Result = activity.GetTagItem("gen_ai.response.text")?.ToString(),
            Error = activity.StatusDescription,
        };
    }

    /// <summary>從 Activity.Events 提取 tool_call/tool_result 配對。</summary>
    private static List<TraceToolCall>? ParseToolEvents(Activity activity)
    {
        var events = activity.Events.ToList();
        if (events.Count == 0) return null;

        var calls = new List<TraceToolCall>();
        TraceToolCall? pending = null;

        foreach (var evt in events)
        {
            if (evt.Name == "tool_call")
            {
                pending = new TraceToolCall
                {
                    Name = evt.Tags.FirstOrDefault(t => t.Key == "tool.name").Value?.ToString() ?? "",
                    Args = evt.Tags.FirstOrDefault(t => t.Key == "tool.args").Value?.ToString(),
                };
                calls.Add(pending);
            }
            else if (evt.Name == "tool_result")
            {
                var name = evt.Tags.FirstOrDefault(t => t.Key == "tool.name").Value?.ToString() ?? "";
                var result = evt.Tags.FirstOrDefault(t => t.Key == "tool.result").Value?.ToString() ?? "";

                // 配對到同名的 pending call，或建立新的
                var match = calls.LastOrDefault(c => c.Result is null) ?? pending;
                if (match is not null)
                {
                    if (string.IsNullOrEmpty(match.Name)) match.Name = name;
                    match.Result = result;
                }
                else
                {
                    calls.Add(new TraceToolCall { Name = name, Result = result });
                }
            }
        }

        return calls.Count > 0 ? calls : null;
    }

    private static int? ParseNullableInt(object? value)
    {
        if (value is null) return null;
        if (value is int i) return i;
        if (value is long l) return (int)l;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
}

/// <summary>Tool call/result 記錄。</summary>
public class TraceToolCall
{
    public string Name { get; set; } = "";
    public string? Args { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; set; }

    public double DurationMs { get; set; }
}

/// <summary>
/// 完整的 trace 資料 — 包含一次 workflow 執行的所有 span。
/// </summary>
public class TraceDataModel
{
    public string TraceId { get; set; } = "";
    public string? WorkflowName { get; set; }
    public double TotalMs { get; set; }
    public int TotalTokens { get; set; }
    public string TotalCost { get; set; } = "$0";
    public string Status { get; set; } = "completed";
    public List<TraceSpanModel> Spans { get; set; } = [];

    private const int MaxTraceJsonBytes = 512 * 1024;
    private const int MaxInputResultLength = 2000;

    /// <summary>
    /// 從累積的 span 列表組裝完整 TraceData。
    /// </summary>
    public static TraceDataModel Build(
        string traceId, IEnumerable<TraceSpanModel> spans, string? workflowName = null)
    {
        var spanList = spans.ToList();

        var totalMs = spanList.Count > 0
            ? spanList.Max(s => s.EndMs) - spanList.Min(s => s.StartMs)
            : 0;

        var totalTokens = spanList
            .Where(s => s.Tokens.HasValue)
            .Sum(s => s.Tokens!.Value);

        var hasError = spanList.Any(s => s.Status == "error");

        // 截斷 input/result 欄位
        foreach (var span in spanList)
        {
            if (span.Input is { Length: > MaxInputResultLength })
            {
                span.Input = span.Input[..MaxInputResultLength];
                span.Truncated = true;
            }
            if (span.Result is { Length: > MaxInputResultLength })
            {
                span.Result = span.Result[..MaxInputResultLength];
                span.Truncated = true;
            }
        }

        return new TraceDataModel
        {
            TraceId = traceId,
            WorkflowName = workflowName,
            TotalMs = totalMs,
            TotalTokens = totalTokens,
            Status = hasError ? "error" : "completed",
            Spans = spanList,
        };
    }
}
