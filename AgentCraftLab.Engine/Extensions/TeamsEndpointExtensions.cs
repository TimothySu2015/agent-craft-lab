using System.Text.Json;
using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentCraftLab.Engine.Extensions;

/// <summary>
/// Teams Bot Server 端點註冊擴展方法（輕量方案：自己解析 Activity JSON）。
/// </summary>
public static class TeamsEndpointExtensions
{
    /// <summary>
    /// 註冊 Teams Bot 端點：POST /teams/{key}/api/messages。
    /// </summary>
    public static IEndpointRouteBuilder MapTeamsEndpoints(this IEndpointRouteBuilder app, string corsPolicy = "A2A")
    {
        app.MapPost("/teams/{key}/api/messages", async (string key, HttpContext ctx, TeamsServerService teams, IRequestLogStore logService) =>
        {
            JsonDocument doc;
            try
            {
                doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
            }
            catch
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { error = "Invalid JSON" });
                return;
            }

            var activityType = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : "unknown";
            var sourceIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var result = await teams.HandleActivityAsync(key, doc, ctx.RequestAborted);
            _ = logService.LogAsync(Data.RequestLogDocument.Create(key, "teams", true, activityType ?? "", null, sourceIp, sw.ElapsedMilliseconds));
            await ctx.Response.WriteAsJsonAsync(result, JsonDefaults.A2AOptions);
        }).RequireCors(corsPolicy).AllowAnonymous().AddEndpointFilter<ApiKeyEndpointFilter>();

        return app;
    }
}
