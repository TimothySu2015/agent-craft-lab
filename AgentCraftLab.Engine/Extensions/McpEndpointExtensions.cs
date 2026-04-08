using System.Diagnostics;
using System.Text.Json;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentCraftLab.Engine.Extensions;

/// <summary>
/// MCP Server 端點註冊擴展方法。
/// </summary>
public static class McpEndpointExtensions
{
    /// <summary>
    /// 註冊 MCP Server 端點（Streamable HTTP）。
    /// </summary>
    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder app, string corsPolicy = "A2A")
    {
        var jsonOptions = JsonDefaults.A2AOptions;

        app.MapPost("/mcp/{key}", async (string key, HttpContext ctx, McpServerService mcp, IRequestLogStore logService) =>
        {
            var apiKeyUserId = ctx.Items[ApiKeyItemKeys.UserId] as string;
            JsonDocument doc;
            try
            {
                doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
            }
            catch
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(
                    new { jsonrpc = "2.0", id = (object?)null, error = new { code = -32700, message = "Parse error" } },
                    jsonOptions, ctx.RequestAborted);
                return;
            }

            var root = doc.RootElement;
            var method = root.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
            var id = root.TryGetProperty("id", out var idEl) ? (object?)idEl.Clone() : null;
            var @params = root.TryGetProperty("params", out var p) ? (JsonElement?)p.Clone() : null;
            var sessionId = ctx.Request.Headers["Mcp-Session-Id"].FirstOrDefault();

            var sw = Stopwatch.StartNew();
            var response = await mcp.HandleRequestAsync(key, method, @params, id, sessionId, ctx.RequestAborted);
            sw.Stop();

            // 只記錄 tools/call（實際執行），不記錄 initialize / tools/list
            if (method == "tools/call")
            {
                var userMsg = @params?.TryGetProperty("arguments", out var args) == true &&
                              args.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
                var fileName = @params?.TryGetProperty("arguments", out var a2) == true &&
                               a2.TryGetProperty("fileName", out var fn) ? fn.GetString() : null;
                var sourceIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
                var success = response.StatusCode == 200 && response.Body is Dictionary<string, object?> body &&
                              body.TryGetValue("error", out var err) && err is null;

                _ = logService.LogAsync(RequestLogDocument.Create(key, "mcp", success, userMsg, fileName, sourceIp, sw.ElapsedMilliseconds, userId: apiKeyUserId));
            }

            ctx.Response.StatusCode = response.StatusCode;
            if (response.SessionId is not null)
            {
                ctx.Response.Headers["mcp-session-id"] = response.SessionId;
            }

            if (response.Body is not null)
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(
                    JsonSerializer.Serialize(response.Body, jsonOptions), ctx.RequestAborted);
            }
        }).RequireCors(corsPolicy).AllowAnonymous().AddEndpointFilter<ApiKeyEndpointFilter>();

        return app;
    }
}
