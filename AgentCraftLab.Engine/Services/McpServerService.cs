using System.Text;
using System.Text.Json;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// MCP Server 服務：將已發布的 Workflow 暴露為 MCP Tool。
/// </summary>
public class McpServerService(
    IWorkflowStore workflowStore,
    ICredentialStore credentialStore,
    IServiceScopeFactory scopeFactory,
    ILogger<McpServerService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.A2AOptions;

    /// <summary>
    /// 處理 MCP JSON-RPC 請求，根據 method 分派。
    /// </summary>
    public async Task<McpResponse> HandleRequestAsync(string workflowKey, string method, JsonElement? @params, object? id, string? sessionId, CancellationToken ct)
    {
        return method switch
        {
            "initialize" => HandleInitialize(workflowKey, id),
            "notifications/initialized" => McpResponse.Accepted(),
            "tools/list" => await HandleToolsListAsync(workflowKey, id),
            "tools/call" => await HandleToolsCallAsync(workflowKey, @params, id, ct),
            _ => McpResponse.JsonRpc(id, error: new { code = -32601, message = $"Method not found: {method}" })
        };
    }

    // ═══════════════════════════════════════════
    // initialize
    // ═══════════════════════════════════════════

    private static McpResponse HandleInitialize(string workflowKey, object? id)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..16];

        var result = new
        {
            protocolVersion = McpProtocol.Version,
            capabilities = new
            {
                tools = new { listChanged = false }
            },
            serverInfo = new
            {
                name = "AgentCraftLab",
                version = "1.0"
            }
        };

        return McpResponse.JsonRpc(id, result, sessionId);
    }

    // ═══════════════════════════════════════════
    // tools/list
    // ═══════════════════════════════════════════

    private async Task<McpResponse> HandleToolsListAsync(string workflowKey, object? id)
    {
        var workflow = await GetPublishedWorkflowAsync(workflowKey);
        if (workflow is null)
        {
            return McpResponse.JsonRpc(id, error: new { code = -32603, message = "Workflow not found or not published" });
        }

        var toolName = NameUtils.Sanitize(workflow.Name);
        var inputModes = workflow.GetInputModes();
        var hasFileSupport = inputModes.Exists(m => m != "text/plain");

        var properties = new Dictionary<string, object>
        {
            ["message"] = new { type = "string", description = "The message to send to the workflow" }
        };

        if (hasFileSupport)
        {
            properties["fileName"] = new { type = "string", description = "File name (e.g. photo.jpg)" };
            properties["fileMimeType"] = new { type = "string", description = $"MIME type. Supported: {string.Join(", ", inputModes)}" };
            properties["fileBase64"] = new { type = "string", description = "Base64-encoded file content" };
        }

        var tools = new[]
        {
            new
            {
                name = toolName,
                description = workflow.Description,
                inputSchema = new
                {
                    type = "object",
                    properties,
                    required = new[] { "message" }
                }
            }
        };

        return McpResponse.JsonRpc(id, new { tools });
    }

    // ═══════════════════════════════════════════
    // tools/call
    // ═══════════════════════════════════════════

    private async Task<McpResponse> HandleToolsCallAsync(string workflowKey, JsonElement? @params, object? id, CancellationToken ct)
    {
        var workflow = await GetPublishedWorkflowAsync(workflowKey);
        if (workflow is null)
        {
            return McpResponse.JsonRpc(id, error: new { code = -32603, message = "Workflow not found or not published" });
        }

        // 解析 arguments
        string? userMessage = null;
        FileAttachment? attachment = null;

        if (@params?.TryGetProperty("arguments", out var args) == true)
        {
            if (args.TryGetProperty("message", out var msg))
            {
                userMessage = msg.GetString();
            }
            else if (args.TryGetProperty("input", out var input))
            {
                userMessage = input.GetString();
            }

            // 解析檔案參數
            attachment = FileAttachment.FromJson(args);
        }

        if (string.IsNullOrEmpty(userMessage))
        {
            return McpResponse.JsonRpc(id, error: new { code = -32602, message = "Missing required parameter: message" });
        }

        try
        {
            var credentials = await credentialStore.GetDecryptedCredentialsAsync(workflow.UserId);
            var executionPayload = A2AServerService.ConvertSaveToExecution(workflow.WorkflowJson);

            var request = new WorkflowExecutionRequest
            {
                WorkflowJson = executionPayload,
                UserMessage = userMessage,
                Credentials = credentials,
                Attachment = attachment
            };

            var responseText = new StringBuilder();
            using var scope = scopeFactory.CreateScope();
            var executionService = scope.ServiceProvider.GetRequiredService<WorkflowExecutionService>();

            await foreach (var evt in executionService.ExecuteAsync(request, ct))
            {
                if (evt.Type is EventTypes.AgentCompleted or EventTypes.TextChunk)
                {
                    responseText.Append(evt.Text);
                }
                else if (evt.Type == EventTypes.Error)
                {
                    return McpResponse.JsonRpc(id, new
                    {
                        content = new[] { new { type = "text", text = evt.Text } },
                        isError = true
                    });
                }
            }

            return McpResponse.JsonRpc(id, new
            {
                content = new[] { new { type = "text", text = responseText.ToString().Trim() } }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MCP Server] Execution failed for workflow {Key}", workflowKey);
            return McpResponse.JsonRpc(id, error: new { code = -32603, message = ex.Message });
        }
    }

    private async Task<WorkflowDocument?> GetPublishedWorkflowAsync(string key)
    {
        var wf = await workflowStore.GetAsync(key);
        return wf is { IsPublished: true } ? wf : null;
    }
}

/// <summary>
/// MCP Server 回應封裝。
/// </summary>
public class McpResponse
{
    public int StatusCode { get; init; } = 200;
    public string? SessionId { get; init; }
    public object? Body { get; init; }

    public static McpResponse Accepted() => new() { StatusCode = 202 };

    public static McpResponse JsonRpc(object? id, object? result = null, string? sessionId = null, object? error = null)
    {
        var body = new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id };
        if (error is not null)
        {
            body["error"] = error;
        }
        else
        {
            body["result"] = result;
        }

        return new McpResponse { Body = body, SessionId = sessionId };
    }
}
