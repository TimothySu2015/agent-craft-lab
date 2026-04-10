using System.Text.Json;
using AgentCraftLab.Api.Diagnostics;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Api.Endpoints;

/// <summary>
/// AG-UI Protocol 端點 — Workflow 執行、Autonomous 執行、Human Input。
/// Credentials 從後端 ICredentialStore 讀取（DPAPI 加密），前端不再傳送 API Key。
/// </summary>
public static class AgUiEndpoints
{
    public static void MapAgUiEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        // ── Workflow 模式 ──
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

            // 從後端 CredentialStore 讀取加密的 credentials，不再依賴前端 forwardedProps
            var credentials = await ResolveCredentialsAsync(ctx, props, jsonOptions);

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

            // 從暫存區取出附件（前端上傳後將 fileId 放入 forwardedProps）
            FileAttachment? attachment = null;
            var fileId = ExtractString(props, "fileId");
            if (!string.IsNullOrEmpty(fileId))
            {
                var uploaded = UploadEndpoints.GetAndRemove(fileId);
                if (uploaded is not null)
                {
                    attachment = new FileAttachment
                    {
                        FileName = uploaded.FileName,
                        MimeType = uploaded.ContentType,
                        Data = uploaded.Data
                    };
                }
            }

            var request = new WorkflowExecutionRequest
            {
                WorkflowJson = workflowJson,
                UserMessage = userMessage,
                Credentials = credentials,
                History = history,
                Attachment = attachment,
                SessionId = input.RunId
            };

            var humanBridge = ctx.RequestServices.GetRequiredService<HumanInputBridge>();
            var bridgeRegistry = ctx.RequestServices.GetRequiredService<HumanInputBridgeRegistry>();
            bridgeRegistry.Register(input.ThreadId, input.RunId, humanBridge);

            // Debug Mode：從 forwardedProps 讀取 debugMode，建構 DebugBridge
            var debugMode = ExtractString(props, "debugMode") == "true";
            DebugBridge? debugBridge = null;
            DebugBridgeRegistry? debugRegistry = null;
            if (debugMode)
            {
                debugBridge = new DebugBridge();
                debugRegistry = ctx.RequestServices.GetRequiredService<DebugBridgeRegistry>();
                debugRegistry.Register(input.ThreadId, input.RunId, debugBridge);
            }
            // 透過 Scoped DI 將 DebugBridge 注入 AgentExecutionContext
            request.DebugBridge = debugBridge;

            try
            {
                var traceCollector = ctx.RequestServices.GetService<TraceCollectorExporter>();
                await AgUiStreamingService.StreamExecutionEventsAsync(
                    ctx, input, engine.ExecuteAsync(request, ct), jsonOptions, ct, bridgeRegistry, traceCollector);
            }
            finally
            {
                bridgeRegistry.Unregister(input.ThreadId, input.RunId);
                debugRegistry?.Unregister(input.ThreadId, input.RunId);
            }
        });

        // ── Autonomous 模式（ReAct / Flow） ──
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
            var credentials = await ResolveCredentialsAsync(ctx, props, jsonOptions);

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

            var traceCollector = ctx.RequestServices.GetService<TraceCollectorExporter>();
            await AgUiStreamingService.StreamExecutionEventsAsync(
                ctx, input, goalExecutor.ExecuteAsync(request, ct), jsonOptions, ct,
                traceCollector: traceCollector);
        });

        // ── 節點重跑（Rerun from checkpoint） ──
        app.MapPost("/ag-ui/rerun", async (HttpContext ctx, WorkflowExecutionService engine, CancellationToken ct) =>
        {
            var body = await JsonSerializer.DeserializeAsync<RerunRequest>(ctx.Request.Body, jsonOptions, ct);
            if (body is null || string.IsNullOrEmpty(body.ExecutionId) || string.IsNullOrEmpty(body.RerunFromNodeId))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Invalid rerun request: executionId and rerunFromNodeId are required", ct);
                return;
            }

            var credentials = await ResolveCredentialsAsync(ctx, new Dictionary<string, object>(), jsonOptions);

            var request = new WorkflowExecutionRequest
            {
                WorkflowJson = body.WorkflowJson,
                UserMessage = body.UserMessage,
                Credentials = credentials,
                SessionId = body.RunId
            };

            // 構造 RunAgentInput 供 SSE streaming 使用
            var input = new RunAgentInput
            {
                ThreadId = body.ThreadId,
                RunId = body.RunId,
            };

            var traceCollector = ctx.RequestServices.GetService<TraceCollectorExporter>();
            await AgUiStreamingService.StreamExecutionEventsAsync(
                ctx, input,
                engine.ResumeFromNodeAsync(request, body.ExecutionId, body.RerunFromNodeId, ct),
                jsonOptions, ct, traceCollector: traceCollector);
        });

        // ── Debug Action Submit ──
        app.MapPost("/ag-ui/debug-action", async (HttpContext ctx, DebugBridgeRegistry registry) =>
        {
            var body = await JsonSerializer.DeserializeAsync<DebugActionRequest>(ctx.Request.Body, jsonOptions);
            if (body is null)
            {
                return Results.BadRequest(new { error = "Invalid request body" });
            }

            var action = body.Action?.ToLowerInvariant() switch
            {
                "rerun" => Engine.Services.DebugAction.Rerun,
                "skip" => Engine.Services.DebugAction.Skip,
                _ => Engine.Services.DebugAction.Continue,
            };

            if (registry.SubmitAction(body.ThreadId ?? "", body.RunId ?? "", action))
            {
                return Results.Ok(new { success = true });
            }

            return Results.NotFound(new { error = "No pending debug action for this session" });
        });

        // ── Human Input Submit ──
        app.MapPost("/ag-ui/human-input", async (HttpContext ctx, HumanInputBridgeRegistry registry) =>
        {
            var body = await JsonSerializer.DeserializeAsync<HumanInputRequest>(ctx.Request.Body, jsonOptions);
            if (body is null)
            {
                return Results.BadRequest(new { error = "Invalid request body" });
            }

            if (registry.SubmitInput(body.ThreadId ?? "", body.RunId ?? "", body.Response ?? ""))
            {
                return Results.Ok(new { success = true });
            }

            return Results.NotFound(new { error = "No pending human input for this session" });
        });
    }

    // ─── Helper ───

    /// <summary>
    /// 根據 Credential:Mode 解析 API Key：
    /// - browser mode：優先 forwardedProps（前端 sessionStorage 帶入），fallback DB
    /// - database mode：優先 ICredentialStore（DPAPI 加密），fallback forwardedProps
    /// </summary>
    internal static async Task<Dictionary<string, ProviderCredential>> ResolveCredentialsAsync(
        HttpContext ctx, Dictionary<string, object> props, JsonSerializerOptions jsonOptions)
    {
        var credMode = ctx.RequestServices.GetService<CredentialModeConfig>();

        // Browser mode：優先從 forwardedProps 取（前端 sessionStorage → request）
        if (credMode?.IsBrowserMode == true)
        {
            var fromProps = ExtractCredentials(props, jsonOptions);
            if (fromProps.Count > 0) return fromProps;
        }

        // Database mode（或 browser fallback）：從 ICredentialStore 讀取
        var credStore = ctx.RequestServices.GetService<ICredentialStore>();
        var userCtx = ctx.RequestServices.GetService<IUserContext>();

        if (credStore is not null && userCtx is not null)
        {
            var userId = await userCtx.GetUserIdAsync();
            var stored = await credStore.GetDecryptedCredentialsAsync(userId);
            if (stored.Count > 0) return stored;
        }

        // Fallback：forwardedProps（database mode 的向後相容）
        return ExtractCredentials(props, jsonOptions);
    }

    internal static Dictionary<string, ProviderCredential> ExtractCredentials(
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

    internal static string? ExtractString(Dictionary<string, object> props, string key)
    {
        if (props.TryGetValue(key, out var obj) && obj is not null)
        {
            return obj is JsonElement elem ? elem.GetString() : obj.ToString();
        }
        return null;
    }
}
