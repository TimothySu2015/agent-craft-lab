using System.Text;
using System.Text.Json;
using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// Teams Bot Server 服務（輕量方案）：直接解析 Activity JSON，不依賴 M365 Agents SDK。
/// 將 Teams 的 Activity Protocol 橋接到 WorkflowExecutionService。
/// </summary>
public class TeamsServerService(
    IWorkflowStore workflowStore,
    ICredentialStore credentialStore,
    IServiceScopeFactory scopeFactory,
    ILogger<TeamsServerService> logger)
{
    /// <summary>
    /// 處理 Teams Activity Protocol 的 POST /teams/{key}/api/messages。
    /// </summary>
    public async Task<object> HandleActivityAsync(string workflowKey, JsonDocument activity, CancellationToken ct)
    {
        // 1. 解析 Activity
        var root = activity.RootElement;
        var activityType = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

        // 只處理 message 類型
        if (activityType != "message")
        {
            return new { type = "message", text = "" };
        }

        var userMessage = root.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return BuildReply("No message text received.");
        }

        // 2. 載入已發布的 workflow 和憑證
        var workflow = await workflowStore.GetAsync(workflowKey);
        if (workflow is null || !workflow.IsPublished)
        {
            return BuildReply($"Workflow '{workflowKey}' not found or not published.");
        }

        var credentials = await credentialStore.GetDecryptedCredentialsAsync(workflow.UserId);
        var executionPayload = A2AServerService.ConvertSaveToExecution(workflow.WorkflowJson);

        var request = new WorkflowExecutionRequest
        {
            WorkflowJson = executionPayload,
            UserMessage = userMessage,
            Credentials = credentials
        };

        // 3. 執行 workflow
        try
        {
            var responseText = new StringBuilder();
            using var scope = scopeFactory.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<WorkflowExecutionService>();

            await foreach (var evt in engine.ExecuteAsync(request, ct))
            {
                if (evt.Type is EventTypes.AgentCompleted && !string.IsNullOrWhiteSpace(evt.Text))
                {
                    responseText.AppendLine(evt.Text);
                }
                else if (evt.Type == EventTypes.Error)
                {
                    responseText.AppendLine($"Error: {evt.Text}");
                }
            }

            var result = responseText.Length > 0
                ? responseText.ToString().Trim()
                : "Workflow completed (no output).";

            return BuildReply(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Teams Server] Execution failed for workflow {Key}", workflowKey);
            return BuildReply($"Execution error: {ex.Message}");
        }
    }

    private static object BuildReply(string text)
    {
        return new { type = "message", text };
    }
}
