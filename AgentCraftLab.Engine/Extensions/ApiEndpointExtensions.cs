using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Engine.Extensions;

/// <summary>
/// REST API 端點註冊擴展方法。
/// </summary>
public static class ApiEndpointExtensions
{
    /// <summary>
    /// 註冊 REST API 端點（簡易 JSON request/response）。
    /// </summary>
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app, string corsPolicy = "A2A")
    {
        var jsonOptions = JsonDefaults.A2AOptions;

        app.MapPost("/api/{key}", async (string key, HttpContext ctx,
            IWorkflowStore workflowStore,
            ICredentialStore credentialStore,
            IServiceScopeFactory scopeFactory,
            IRequestLogStore logService) =>
        {
            var sw = Stopwatch.StartNew();
            var sourceIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            var apiKeyUserId = ctx.Items[ApiKeyItemKeys.UserId] as string;

            // 解析 request body
            JsonDocument doc;
            try
            {
                doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
            }
            catch
            {
                return Results.Json(new { success = false, error = "Invalid JSON" }, jsonOptions, statusCode: 400);
            }

            var root = doc.RootElement;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;

            if (string.IsNullOrEmpty(message))
            {
                return Results.Json(new { success = false, error = "Missing required field: message" }, jsonOptions, statusCode: 400);
            }

            // 查找已發布的 workflow
            var workflow = await workflowStore.GetAsync(key);
            if (workflow is null || !workflow.IsPublished || !workflow.HasType("api"))
            {
                return Results.Json(new { success = false, error = "Workflow not found, not published, or API not enabled" }, jsonOptions, statusCode: 404);
            }

            // 解析檔案附件
            var attachment = FileAttachment.FromJson(root);

            // 執行 workflow
            try
            {
                var credentials = await credentialStore.GetDecryptedCredentialsAsync(workflow.UserId);
                var executionPayload = A2AServerService.ConvertSaveToExecution(workflow.WorkflowJson);

                var request = new WorkflowExecutionRequest
                {
                    WorkflowJson = executionPayload,
                    UserMessage = message,
                    Credentials = credentials,
                    Attachment = attachment
                };

                var responseText = new StringBuilder();
                using var scope = scopeFactory.CreateScope();
                var executionService = scope.ServiceProvider.GetRequiredService<WorkflowExecutionService>();

                await foreach (var evt in executionService.ExecuteAsync(request, ctx.RequestAborted))
                {
                    if (evt.Type is EventTypes.AgentCompleted or EventTypes.TextChunk)
                    {
                        responseText.Append(evt.Text);
                    }
                    else if (evt.Type == EventTypes.Error)
                    {
                        sw.Stop();
                        _ = logService.LogAsync(RequestLogDocument.Create(key, "api", false, message, attachment?.FileName, sourceIp, sw.ElapsedMilliseconds, evt.Text, userId: apiKeyUserId));
                        return Results.Json(new { success = false, error = evt.Text, elapsedMs = sw.ElapsedMilliseconds }, jsonOptions);
                    }
                }

                sw.Stop();
                _ = logService.LogAsync(RequestLogDocument.Create(key, "api", true, message, attachment?.FileName, sourceIp, sw.ElapsedMilliseconds, userId: apiKeyUserId));
                return Results.Json(new
                {
                    success = true,
                    text = responseText.ToString().Trim(),
                    elapsedMs = sw.ElapsedMilliseconds
                }, jsonOptions);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _ = logService.LogAsync(RequestLogDocument.Create(key, "api", false, message, attachment?.FileName, sourceIp, sw.ElapsedMilliseconds, ex.Message, userId: apiKeyUserId));
                return Results.Json(new { success = false, error = ex.Message, elapsedMs = sw.ElapsedMilliseconds }, jsonOptions, statusCode: 500);
            }
        }).RequireCors(corsPolicy).AllowAnonymous().AddEndpointFilter<ApiKeyEndpointFilter>();

        return app;
    }
}
