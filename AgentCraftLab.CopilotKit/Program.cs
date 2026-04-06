using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentCraftLab.Autonomous.Extensions;
using AgentCraftLab.Autonomous.Flow.Extensions;
using AgentCraftLab.CopilotKit;
using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

// ============================================================
// AgentCraftLab CopilotKit Bridge — AG-UI Protocol SSE 端點
// 支援三種模式：Workflow / Autonomous (ReAct) / Flow
// ============================================================

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("AgentCraftLab", LogLevel.Warning);

var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Directory.GetCurrentDirectory();
builder.Services.AddAgentCraftEngine(workingDir: workingDir);

// 執行模式：appsettings.json "ExecutionMode" = "react"（預設）或 "flow"
var executionMode = builder.Configuration["ExecutionMode"]?.ToLowerInvariant() ?? "react";
if (executionMode == "flow")
{
    builder.Services.AddAutonomousFlowAgent();
}
else
{
    builder.Services.AddAutonomousAgent();
}

// CORS — 允許 Vite dev server（port 5173）
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

app.UseCors();
app.UseStaticFiles();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

// ============================================================
// AG-UI 端點 1: Workflow 模式 — 執行 Studio 設計的 workflow
// ============================================================
app.MapPost("/ag-ui", async (HttpContext ctx, WorkflowExecutionService engine, CancellationToken ct) =>
{
    var input = await JsonSerializer.DeserializeAsync<RunAgentInput>(ctx.Request.Body, jsonOptions, ct);
    if (input is null)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("Invalid request body", ct);
        return;
    }

    var userMessage = input.Messages?.LastOrDefault(m => m.Role == "user")?.Content ?? "";
    var props = input.ForwardedProps ?? input.State ?? new Dictionary<string, object>();

    var workflowJson = "";
    if (props.TryGetValue("workflowJson", out var wfObj) && wfObj is not null)
    {
        workflowJson = wfObj is JsonElement wfElem ? wfElem.GetString() ?? "" : wfObj.ToString() ?? "";
    }

    var credentials = ExtractCredentials(props, jsonOptions);

    var history = new List<ChatHistoryEntry>();
    if (input.Messages is not null)
    {
        foreach (var msg in input.Messages.SkipLast(1))
        {
            history.Add(new ChatHistoryEntry
            {
                Role = msg.Role == "assistant" ? "assistant" : "user",
                Text = msg.Content
            });
        }
    }

    var request = new WorkflowExecutionRequest
    {
        WorkflowJson = workflowJson,
        UserMessage = userMessage,
        Credentials = credentials,
        History = history
    };

    await StreamExecutionEvents(ctx, input, engine.ExecuteAsync(request, ct), jsonOptions, ct);
});

// ============================================================
// AG-UI 端點 2: Autonomous 模式 — ReAct / Flow 自主執行
// ============================================================
app.MapPost("/ag-ui/goal", async (HttpContext ctx, IGoalExecutor goalExecutor, CancellationToken ct) =>
{
    var input = await JsonSerializer.DeserializeAsync<RunAgentInput>(ctx.Request.Body, jsonOptions, ct);
    if (input is null)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("Invalid request body", ct);
        return;
    }

    var goal = input.Messages?.LastOrDefault(m => m.Role == "user")?.Content ?? "";
    if (string.IsNullOrWhiteSpace(goal))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("No goal provided", ct);
        return;
    }

    var props = input.ForwardedProps ?? input.State ?? new Dictionary<string, object>();
    var credentials = ExtractCredentials(props, jsonOptions);

    // 從 props 取得模型設定
    var provider = ExtractString(props, "provider") ?? Defaults.Provider;
    var model = ExtractString(props, "model") ?? Defaults.Model;
    var toolsCsv = ExtractString(props, "tools") ?? "";
    var tools = toolsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    var request = new GoalExecutionRequest
    {
        Goal = goal,
        Credentials = credentials,
        Provider = provider,
        Model = model,
        AvailableTools = tools,
    };

    await StreamExecutionEvents(ctx, input, goalExecutor.ExecuteAsync(request, ct), jsonOptions, ct);
});

// Info 端點
app.MapGet("/info", () => Results.Ok(new
{
    name = "AgentCraftLab CopilotKit Bridge",
    protocol = "AG-UI",
    version = "1.0.0",
    mode = executionMode,
    endpoints = new[] { "/ag-ui (workflow)", "/ag-ui/goal (autonomous)" }
}));

app.MapFallbackToFile("index.html");

const int port = 5200;
app.Urls.Add($"http://localhost:{port}");

var modeLabel = executionMode == "flow" ? "Flow（結構化）" : "ReAct（自由）";
Console.WriteLine($"""

  AgentCraftLab CopilotKit Bridge
  Mode:       {modeLabel}
  Workflow:   http://localhost:{port}/ag-ui
  Autonomous: http://localhost:{port}/ag-ui/goal
  React dev:  http://localhost:5173

""");

await app.RunAsync();

// ============================================================
// 共用 helper
// ============================================================

static async Task StreamExecutionEvents(
    HttpContext ctx,
    RunAgentInput input,
    IAsyncEnumerable<ExecutionEvent> events,
    JsonSerializerOptions jsonOptions,
    CancellationToken ct)
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    var threadId = input.ThreadId;
    var runId = input.RunId;
    var converter = new AgUiEventConverter();

    await WriteSseEvent(ctx, new AgUiEvent
    {
        Type = AgUiEventTypes.RunStarted,
        ThreadId = threadId,
        RunId = runId
    }, jsonOptions, ct);

    try
    {
        await foreach (var evt in events)
        {
            var agUiEvents = converter.Convert(evt, threadId, runId);
            foreach (var agUiEvent in agUiEvents)
            {
                await WriteSseEvent(ctx, agUiEvent, jsonOptions, ct);
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        await WriteSseEvent(ctx, new AgUiEvent
        {
            Type = AgUiEventTypes.RunError,
            Message = ex.Message
        }, jsonOptions, ct);
    }

    await WriteSseEvent(ctx, new AgUiEvent
    {
        Type = AgUiEventTypes.RunFinished,
        ThreadId = threadId,
        RunId = runId
    }, jsonOptions, ct);
}

static async Task WriteSseEvent(HttpContext ctx, AgUiEvent evt, JsonSerializerOptions options, CancellationToken ct)
{
    var json = JsonSerializer.Serialize(evt, options);
    await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
    await ctx.Response.Body.FlushAsync(ct);
}

static Dictionary<string, ProviderCredential> ExtractCredentials(
    Dictionary<string, object> props, JsonSerializerOptions jsonOptions)
{
    if (props.TryGetValue("credentials", out var credObj) && credObj is JsonElement credElem)
    {
        var parsed = JsonSerializer.Deserialize<Dictionary<string, ProviderCredential>>(
            credElem.GetRawText(), jsonOptions);
        if (parsed is not null)
        {
            return parsed;
        }
    }
    return new Dictionary<string, ProviderCredential>();
}

static string? ExtractString(Dictionary<string, object> props, string key)
{
    if (props.TryGetValue(key, out var obj) && obj is not null)
    {
        return obj is JsonElement elem ? elem.GetString() : obj.ToString();
    }
    return null;
}
