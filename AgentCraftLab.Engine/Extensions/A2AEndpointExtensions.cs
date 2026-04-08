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
/// A2A Server 端點註冊擴展方法。
/// </summary>
public static class A2AEndpointExtensions
{
    /// <summary>
    /// 註冊 A2A Server 端點（Google A2A + Microsoft 雙格式）。
    /// </summary>
    public static IEndpointRouteBuilder MapA2AEndpoints(this IEndpointRouteBuilder app, string corsPolicy = "A2A")
    {
        var jsonOptions = JsonDefaults.A2AOptions;

        // Google A2A: Agent Card
        app.MapGet("/a2a/{key}/agent-card.json", async (string key, HttpContext ctx, A2AServerService a2a) =>
        {
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var card = await a2a.BuildAgentCardAsync(key, baseUrl);
            return card is not null ? Results.Json(card, jsonOptions) : Results.NotFound();
        }).RequireCors(corsPolicy).AllowAnonymous();

        // Google A2A: JSON-RPC 端點
        app.MapPost("/a2a/{key}", async (string key, HttpContext ctx, A2AServerService a2a, IRequestLogStore logService) =>
        {
            var apiKeyUserId = ctx.Items[ApiKeyItemKeys.UserId] as string;
            JsonRpcRequest request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(ctx.Request.Body, jsonOptions, ctx.RequestAborted)
                          ?? new JsonRpcRequest();
            }
            catch
            {
                return Results.Json(JsonRpcResponse.Fail(null, A2AProtocol.ParseError, "Invalid JSON"), jsonOptions);
            }

            var sw = Stopwatch.StartNew();
            var userMsg = request.Params?.Message?.Parts?.FirstOrDefault(p => p.Kind == "text")?.Text ?? "";
            var fileName = request.Params?.Message?.Parts?.FirstOrDefault(p => p.Kind == "file")?.File?.Name;
            var sourceIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            var protocol = request.Method == A2AProtocol.MethodSendStreaming ? "a2a-sse" : "a2a";

            switch (request.Method)
            {
                case A2AProtocol.MethodSend:
                {
                    var response = await a2a.HandleSendAsync(key, request, ctx.RequestAborted);
                    sw.Stop();
                    var success = response.Error is null;
                    _ = logService.LogAsync(RequestLogDocument.Create(key, protocol, success, userMsg, fileName, sourceIp, sw.ElapsedMilliseconds,
                        success ? null : response.Error?.Message, userId: apiKeyUserId));
                    return Results.Json(response, jsonOptions);
                }
                case A2AProtocol.MethodSendStreaming:
                {
                    ctx.Response.ContentType = "text/event-stream";
                    ctx.Response.Headers.CacheControl = "no-cache";
                    ctx.Response.Headers.Connection = "keep-alive";

                    await foreach (var sseEvent in a2a.HandleSendStreamingAsync(key, request, ctx.RequestAborted))
                    {
                        await ctx.Response.WriteAsync(sseEvent, ctx.RequestAborted);
                        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                    }

                    sw.Stop();
                    _ = logService.LogAsync(RequestLogDocument.Create(key, protocol, true, userMsg, fileName, sourceIp, sw.ElapsedMilliseconds, userId: apiKeyUserId));
                    return Results.Empty;
                }
                default:
                    return Results.Json(
                        JsonRpcResponse.Fail(request.Id, A2AProtocol.MethodNotFound, $"Method not found: {request.Method}"),
                        jsonOptions);
            }
        }).RequireCors(corsPolicy).AllowAnonymous().AddEndpointFilter<ApiKeyEndpointFilter>();

        // Microsoft 格式: Agent Card
        app.MapGet("/a2a/{key}/v1/card", async (string key, HttpContext ctx, A2AServerService a2a) =>
        {
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var card = await a2a.BuildMsAgentCardAsync(key, baseUrl);
            return card is not null ? Results.Json(card, jsonOptions) : Results.NotFound();
        }).RequireCors(corsPolicy).AllowAnonymous();

        // Microsoft 格式: Send Message
        app.MapPost("/a2a/{key}/v1/message:send", async (string key, HttpContext ctx, A2AServerService a2a, IRequestLogStore logService) =>
        {
            var apiKeyUserId = ctx.Items[ApiKeyItemKeys.UserId] as string;
            var sw = Stopwatch.StartNew();
            var sourceIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            try
            {
                var body = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
                var result = await a2a.HandleMsSendAsync(key, body, ctx.RequestAborted);
                sw.Stop();
                _ = logService.LogAsync(RequestLogDocument.Create(key, "microsoft", true, "", null, sourceIp, sw.ElapsedMilliseconds, userId: apiKeyUserId));
                return Results.Json(result, jsonOptions);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _ = logService.LogAsync(RequestLogDocument.Create(key, "microsoft", false, "", null, sourceIp, sw.ElapsedMilliseconds, ex.Message, userId: apiKeyUserId));
                return Results.Json(new { error = ex.Message }, jsonOptions, statusCode: 500);
            }
        }).RequireCors(corsPolicy).AllowAnonymous().AddEndpointFilter<ApiKeyEndpointFilter>();

        return app;
    }

}
