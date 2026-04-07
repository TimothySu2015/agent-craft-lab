using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentCraftLab.Api;
using AgentCraftLab.Api.Diagnostics;
using AgentCraftLab.Autonomous.Extensions;
using AgentCraftLab.Autonomous.Flow.Extensions;
using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.Api.Endpoints;
using AgentCraftLab.Ocr;
using AgentCraftLab.Script;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// ============================================================
// AgentCraftLab API — AG-UI + REST 端點
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
builder.Services.AddSingleton<HumanInputBridgeRegistry>();
builder.Services.AddSingleton<DebugBridgeRegistry>();
builder.Services.AddSingleton<AgentCraftLab.Api.Services.EnhancedFlowBuildService>();

// OCR 引擎（tessdata 目錄存在時才啟用）
var tessDataPath = Path.Combine(workingDir, "Data", "tessdata");
if (Directory.Exists(tessDataPath))
{
    builder.Services.AddOcr(tessDataPath);
}

// 多語言腳本引擎（JavaScript + C# 沙箱，Code 節點 script 模式 + Agent 工具）
builder.Services.AddMultiLanguageScript();

// create_tool meta-tool 橋接器（IScriptEngine → IToolCodeRunner）
builder.Services.AddSingleton<AgentCraftLab.Autonomous.Services.IToolCodeRunner>(sp =>
{
    var engine = sp.GetRequiredService<AgentCraftLab.Script.IScriptEngine>();
    return new AgentCraftLab.Api.Services.ScriptEngineToolCodeRunner(engine);
});

var executionMode = builder.Configuration["ExecutionMode"]?.ToLowerInvariant() ?? "react";
if (executionMode == "flow")
{
    builder.Services.AddAutonomousFlowAgent();
}
else
{
    builder.Services.AddAutonomousAgent();
}

// ── OpenTelemetry ──
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("AgentCraftLab"))
    .WithTracing(tracing =>
    {
        tracing.SetSampler(new OpenTelemetry.Trace.AlwaysOnSampler());
        tracing.AddSource("AgentCraftLab.Engine");
        tracing.AddSource("*Microsoft.Extensions.AI");
        tracing.AddSource("*Microsoft.Extensions.Agents*");
        tracing.AddSource("OpenAI");

        tracing.AddTraceCollectorExporter(builder.Services);

        if (builder.Environment.IsDevelopment())
            tracing.AddConsoleExporter();

        var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        if (!string.IsNullOrEmpty(otlpEndpoint))
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    });

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:5174"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();
app.Services.UseOcrTools(workingDirectory: workingDir);
app.Services.UseCleanerTools(workingDirectory: workingDir);
app.Services.UseScriptTools();

// 強制初始化 TracerProvider，確保 ActivityListener 在第一個請求前就緒
app.Services.GetService<OpenTelemetry.Trace.TracerProvider>();

// Trace 清理：啟動時刪除超過 24 小時的 trace 檔
ExecutionTraceWriter.Cleanup();

app.UseCors();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

// ============================================================
// 端點註冊
// ============================================================
app.MapAgUiEndpoints(jsonOptions);
app.MapWorkflowEndpoints();
app.MapToolEndpoints();
app.MapFlowBuilderEndpoints();
app.MapCredentialEndpoints();
app.MapUploadEndpoints();
app.MapDiscoveryEndpoints();
app.MapKnowledgeBaseEndpoints(jsonOptions);
app.MapDataSourceEndpoints();
app.MapSkillEndpoints();
app.MapTemplateEndpoints();
app.MapAnalyticsEndpoints();
app.MapTraceEndpoints();
app.MapScriptGeneratorEndpoints();
app.MapPromptRefinerEndpoints();
app.MapSchemaMapperEndpoints();
app.MapRefineryEndpoints();

// F1 Checkpoint — 查詢 Flow 執行的最新 checkpoint（前端 Resume 按鈕用）
app.MapGet("/api/checkpoints/{executionId}/latest", async (string executionId, ICheckpointStore store) =>
{
    var doc = await store.GetLatestAsync(executionId);
    if (doc is null) return Results.NotFound();
    return Results.Ok(new
    {
        doc.Id,
        doc.ExecutionId,
        doc.Iteration,
        doc.CreatedAt,
        hasState = !string.IsNullOrEmpty(doc.StateJson)
    });
});

// F1 Checkpoint — 列出所有有 checkpoint 的 execution IDs（前端偵測 Resume 可用性）
app.MapGet("/api/checkpoints/recent", async (ICheckpointStore store) =>
{
    // 查詢最近 24 小時內的 checkpoint（避免顯示太舊的 resume 選項）
    var cutoff = DateTime.UtcNow.AddHours(-24);
    // ICheckpointStore 沒有 list by time 的方法，用 flow prefix 查詢
    // 簡化：讓前端帶 executionId 查詢，不做全域列表
    return Results.Ok(new { message = "Use GET /api/checkpoints/{executionId}/latest with specific executionId" });
});

app.MapGet("/info", () => Results.Ok(new
{
    name = "AgentCraftLab API",
    protocol = "AG-UI",
    version = "1.0.0",
    mode = executionMode,
    endpoints = new[] { "/ag-ui (workflow)", "/ag-ui/goal (autonomous)", "/api/* (REST)" }
}));

// ============================================================
// 啟動
// ============================================================
const int port = 5200;
app.Urls.Add($"http://localhost:{port}");

var modeLabel = executionMode == "flow" ? "Flow" : "ReAct";
Console.WriteLine($"""

  AgentCraftLab API
  Mode:       {modeLabel}
  AG-UI:      http://localhost:{port}/ag-ui
  Autonomous: http://localhost:{port}/ag-ui/goal
  REST:       http://localhost:{port}/api/*
  React dev:  http://localhost:5173

""");

await app.RunAsync();
