using System.Diagnostics;
using System.Text.Json;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Api.Diagnostics;

/// <summary>
/// 將 ExecutionEvent 流寫入 JSONL trace 檔。每次執行建一個 instance。
/// Claude Code 可透過 Read 工具或 /api/traces 端點存取。
/// </summary>
public sealed class ExecutionTraceWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly string _runId;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _lineCount;
    private long _totalTokens;
    private decimal _totalCost;
    private int _totalSteps;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public ExecutionTraceWriter(string runId, string traceDir = "Data/traces")
    {
        _runId = runId;
        Directory.CreateDirectory(traceDir);
        var path = Path.Combine(traceDir, $"{SanitizeFileName(runId)}.jsonl");
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(fs, System.Text.Encoding.UTF8)
        {
            AutoFlush = false,
        };
    }

    /// <summary>記錄請求開始（節點數、連線數）</summary>
    public void RecordRequest(int nodeCount, int connectionCount)
    {
        Write(new { stage = "request", runId = _runId, nodeCount, connectionCount });
    }

    /// <summary>從 ExecutionEvent 提取 trace 資訊並記錄</summary>
    public void Record(ExecutionEvent evt)
    {
        switch (evt.Type)
        {
            case EventTypes.StrategySelected:
                Write(new
                {
                    stage = "strategy",
                    selected = evt.Metadata?.GetValueOrDefault("strategy", ""),
                    reason = evt.Metadata?.GetValueOrDefault("reason", ""),
                });
                break;

            case EventTypes.StartNodeResolved:
                Write(new
                {
                    stage = "findStartNode",
                    startNodeId = evt.Metadata?.GetValueOrDefault(MetadataKeys.NodeName, ""),
                    path = evt.Metadata?.GetValueOrDefault("path", ""),
                });
                break;

            case EventTypes.NodeExecuting:
                Write(new
                {
                    stage = "nodeDispatch",
                    nodeId = evt.Metadata?.GetValueOrDefault(MetadataKeys.NodeName, ""),
                    type = evt.Metadata?.GetValueOrDefault(MetadataKeys.NodeType, ""),
                });
                break;

            case EventTypes.AgentCompleted:
                var agentTokens = 0L;
                if (evt.Metadata?.TryGetValue(MetadataKeys.Tokens, out var tStr) == true)
                    long.TryParse(tStr, out agentTokens);
                var agentModel = evt.Metadata?.GetValueOrDefault(MetadataKeys.Model, "") ?? "";
                var agentCost = ModelPricing.EstimateCost(
                    string.IsNullOrEmpty(agentModel) ? "unknown" : agentModel, agentTokens);
                _totalTokens += agentTokens;
                _totalCost += agentCost;
                _totalSteps++;
                Write(new
                {
                    stage = "nodeComplete",
                    agentName = evt.AgentName,
                    tokens = agentTokens,
                    model = agentModel,
                    cost = ModelPricing.FormatCost(agentCost),
                    durationMs = _stopwatch.ElapsedMilliseconds,
                });
                break;

            case EventTypes.ToolCall:
                Write(new { stage = "toolCall", agentName = evt.AgentName, tool = evt.Text });
                break;

            case EventTypes.Error:
                Write(new { stage = "error", agentName = evt.AgentName, message = evt.Text });
                break;

            case EventTypes.WorkflowCompleted:
                Write(new
                {
                    stage = "complete",
                    totalSteps = _totalSteps,
                    totalTokens = _totalTokens,
                    totalCost = ModelPricing.FormatCost(_totalCost),
                    durationMs = _stopwatch.ElapsedMilliseconds,
                });
                break;
        }
    }

    private void Write(object record)
    {
        // 用 Dictionary 包裝確保 ts 永遠在第一位且 JSON 合法
        var wrapper = new Dictionary<string, object>
        {
            ["ts"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };
        foreach (var prop in record.GetType().GetProperties())
        {
            wrapper[JsonNamingPolicy.CamelCase.ConvertName(prop.Name)] = prop.GetValue(record)!;
        }
        _writer.WriteLine(JsonSerializer.Serialize(wrapper, JsonOptions));
        _lineCount++;
        if (_lineCount % 5 == 0) _writer.Flush();
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.FlushAsync();
        await _writer.DisposeAsync();
    }

    /// <summary>清理超過 24 小時的 trace 檔，最多保留 maxFiles 個。</summary>
    public static void Cleanup(string traceDir = "Data/traces", int maxFiles = 100)
    {
        if (!Directory.Exists(traceDir)) return;

        var cutoff = DateTime.UtcNow.AddHours(-24);
        var files = Directory.GetFiles(traceDir, "*.jsonl")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        for (var i = 0; i < files.Count; i++)
        {
            if (i >= maxFiles || files[i].LastWriteTimeUtc < cutoff)
            {
                try { files[i].Delete(); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>取得最新的 trace 檔路徑。</summary>
    public static string? GetLatestTracePath(string traceDir = "Data/traces")
    {
        if (!Directory.Exists(traceDir)) return null;
        return Directory.GetFiles(traceDir, "*.jsonl")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string SanitizeFileName(string name)
        => string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
}
