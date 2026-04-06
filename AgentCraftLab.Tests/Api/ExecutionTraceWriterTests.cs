using System.Text.Json;
using AgentCraftLab.Api.Diagnostics;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Tests.Api;

public class ExecutionTraceWriterTests : IDisposable
{
    private readonly string _traceDir;

    public ExecutionTraceWriterTests()
    {
        _traceDir = Path.Combine(Path.GetTempPath(), $"trace_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_traceDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_traceDir))
        {
            Directory.Delete(_traceDir, recursive: true);
        }
    }

    private async Task<List<JsonElement>> WriteAndReadLines(Func<ExecutionTraceWriter, Task> action)
    {
        var runId = "test-run";
        await using (var writer = new ExecutionTraceWriter(runId, _traceDir))
        {
            await action(writer);
        }

        var path = Path.Combine(_traceDir, $"{runId}.jsonl");
        var lines = await File.ReadAllLinesAsync(path);
        return lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<JsonElement>(l))
            .ToList();
    }

    [Fact]
    public async Task Record_StrategySelected_WritesStrategyStage()
    {
        var lines = await WriteAndReadLines(writer =>
        {
            writer.Record(new ExecutionEvent
            {
                Type = EventTypes.StrategySelected,
                Metadata = new Dictionary<string, string>
                {
                    ["strategy"] = "Sequential",
                    ["reason"] = "linear chain",
                },
            });
            return Task.CompletedTask;
        });

        Assert.Single(lines);
        var line = lines[0];
        Assert.Equal("strategy", line.GetProperty("stage").GetString());
        Assert.Equal("Sequential", line.GetProperty("selected").GetString());
        Assert.Equal("linear chain", line.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Record_StartNodeResolved_WritesFindStartNodeStage()
    {
        var lines = await WriteAndReadLines(writer =>
        {
            writer.Record(new ExecutionEvent
            {
                Type = EventTypes.StartNodeResolved,
                Metadata = new Dictionary<string, string>
                {
                    [MetadataKeys.NodeName] = "node-1",
                    ["path"] = "node-1 → node-2",
                },
            });
            return Task.CompletedTask;
        });

        Assert.Single(lines);
        var line = lines[0];
        Assert.Equal("findStartNode", line.GetProperty("stage").GetString());
        Assert.Equal("node-1", line.GetProperty("startNodeId").GetString());
    }

    [Fact]
    public async Task Record_AgentCompleted_WritesNodeCompleteWithTokensModelCost()
    {
        var lines = await WriteAndReadLines(writer =>
        {
            writer.Record(ExecutionEvent.AgentCompleted(
                "Planner", "done", inputTokens: 100, outputTokens: 50, model: "gpt-4o"));
            return Task.CompletedTask;
        });

        Assert.Single(lines);
        var line = lines[0];
        Assert.Equal("nodeComplete", line.GetProperty("stage").GetString());
        Assert.Equal("Planner", line.GetProperty("agentName").GetString());
        Assert.Equal(150, line.GetProperty("tokens").GetInt64());
        Assert.Equal("gpt-4o", line.GetProperty("model").GetString());
        Assert.StartsWith("$", line.GetProperty("cost").GetString()!);
    }

    [Fact]
    public async Task Record_WorkflowCompleted_WritesCompleteWithTotals()
    {
        var lines = await WriteAndReadLines(writer =>
        {
            // 先記錄一個 AgentCompleted 累積 totals
            writer.Record(ExecutionEvent.AgentCompleted(
                "A", "ok", inputTokens: 200, outputTokens: 100, model: "gpt-4o"));
            writer.Record(new ExecutionEvent { Type = EventTypes.WorkflowCompleted });
            return Task.CompletedTask;
        });

        Assert.Equal(2, lines.Count);
        var complete = lines[1];
        Assert.Equal("complete", complete.GetProperty("stage").GetString());
        Assert.Equal(1, complete.GetProperty("totalSteps").GetInt32());
        Assert.Equal(300, complete.GetProperty("totalTokens").GetInt64());
        Assert.StartsWith("$", complete.GetProperty("totalCost").GetString()!);
    }

    [Fact]
    public void Cleanup_RemovesOldFiles()
    {
        // 建立一個「舊」檔案（修改時間設為 48 小時前）
        var oldFile = Path.Combine(_traceDir, "old-run.jsonl");
        File.WriteAllText(oldFile, "{}\n");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddHours(-48));

        // 建立一個「新」檔案
        var newFile = Path.Combine(_traceDir, "new-run.jsonl");
        File.WriteAllText(newFile, "{}\n");

        ExecutionTraceWriter.Cleanup(_traceDir, maxFiles: 100);

        Assert.False(File.Exists(oldFile), "Old file should be deleted");
        Assert.True(File.Exists(newFile), "New file should be kept");
    }

    [Fact]
    public void GetLatestTracePath_ReturnsMostRecentFile()
    {
        var file1 = Path.Combine(_traceDir, "run-a.jsonl");
        File.WriteAllText(file1, "{}\n");
        File.SetLastWriteTimeUtc(file1, DateTime.UtcNow.AddMinutes(-10));

        var file2 = Path.Combine(_traceDir, "run-b.jsonl");
        File.WriteAllText(file2, "{}\n");
        // file2 has the default (latest) write time

        var latest = ExecutionTraceWriter.GetLatestTracePath(_traceDir);

        Assert.NotNull(latest);
        Assert.Equal(Path.GetFullPath(file2), Path.GetFullPath(latest));
    }
}
