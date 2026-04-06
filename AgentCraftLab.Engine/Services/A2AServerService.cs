using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// A2A Server 服務：橋接 Google A2A 協定與 WorkflowExecutionService。
/// </summary>
public class A2AServerService(
    IWorkflowStore workflowStore,
    ICredentialStore credentialStore,
    IServiceScopeFactory scopeFactory,
    ILogger<A2AServerService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.A2AOptions;

    // ═══════════════════════════════════════════
    // Agent Card
    // ═══════════════════════════════════════════

    public async Task<A2AServerAgentCard?> BuildAgentCardAsync(string workflowKey, string baseUrl)
    {
        var workflow = await GetPublishedWorkflowAsync(workflowKey);
        if (workflow is null)
        {
            return null;
        }

        return new A2AServerAgentCard
        {
            Name = workflow.Name,
            Description = workflow.Description,
            Url = $"{baseUrl}/a2a/{workflowKey}",
            Version = "1.0",
            Capabilities = new A2ACapabilities { Streaming = true },
            Skills = ExtractSkills(workflow),
            DefaultInputModes = workflow.GetInputModes()
        };
    }

    /// <summary>
    /// 建立 Microsoft 格式的 Agent Card（/v1/card）。
    /// </summary>
    public async Task<A2AAgentCard?> BuildMsAgentCardAsync(string workflowKey, string baseUrl)
    {
        var workflow = await GetPublishedWorkflowAsync(workflowKey);
        if (workflow is null)
        {
            return null;
        }

        return new A2AAgentCard
        {
            Name = workflow.Name,
            Description = workflow.Description,
            Version = "1.0",
            BaseUrl = $"{baseUrl}/a2a/{workflowKey}"
        };
    }

    private async Task<Data.WorkflowDocument?> GetPublishedWorkflowAsync(string key)
    {
        var wf = await workflowStore.GetAsync(key);
        return wf is { IsPublished: true } ? wf : null;
    }

    // ═══════════════════════════════════════════
    // Google A2A: message/send
    // ═══════════════════════════════════════════

    public async Task<JsonRpcResponse> HandleSendAsync(string workflowKey, JsonRpcRequest request, CancellationToken ct)
    {
        var userMessage = ExtractUserMessage(request);
        if (string.IsNullOrEmpty(userMessage))
        {
            return JsonRpcResponse.Fail(request.Id, A2AProtocol.ParseError, "No text message found in request");
        }

        var taskId = GenerateTaskId();
        var contextId = request.Params?.Message?.ContextId ?? GenerateTaskId();

        try
        {
            var (executionRequest, buildError) = await BuildExecutionRequestAsync(workflowKey, userMessage, request.Params?.Message);
            if (executionRequest is null)
            {
                return JsonRpcResponse.Fail(request.Id, A2AProtocol.InternalError, buildError ?? "Workflow not found or not published");
            }

            var responseText = new StringBuilder();
            using var scope = scopeFactory.CreateScope();
            var executionService = scope.ServiceProvider.GetRequiredService<WorkflowExecutionService>();

            await foreach (var evt in executionService.ExecuteAsync(executionRequest, ct))
            {
                switch (evt.Type)
                {
                    case EventTypes.AgentCompleted:
                        responseText.AppendLine(evt.Text);
                        break;
                    case EventTypes.TextChunk:
                        responseText.Append(evt.Text);
                        break;
                    case EventTypes.Error:
                        return JsonRpcResponse.Success(request.Id, new A2ATask
                        {
                            Id = taskId,
                            ContextId = contextId,
                            Status = new A2ATaskStatus
                            {
                                State = TaskStates.Failed,
                                Message = new A2AMessage
                                {
                                    Role = "agent",
                                    Parts = [A2APart.TextPart(evt.Text)]
                                }
                            }
                        });
                }
            }

            var result = responseText.ToString().Trim();
            return JsonRpcResponse.Success(request.Id, new A2ATask
            {
                Id = taskId,
                ContextId = contextId,
                Status = new A2ATaskStatus { State = TaskStates.Completed },
                Artifacts =
                [
                    new A2AArtifact
                    {
                        Name = "response",
                        Parts = [A2APart.TextPart(result)]
                    }
                ]
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[A2A Server] Execution failed for workflow {Key}", workflowKey);
            return JsonRpcResponse.Fail(request.Id, A2AProtocol.InternalError, ex.Message);
        }
    }

    // ═══════════════════════════════════════════
    // Google A2A: message/sendStreaming (SSE)
    // ═══════════════════════════════════════════

    public async IAsyncEnumerable<string> HandleSendStreamingAsync(
        string workflowKey, JsonRpcRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var userMessage = ExtractUserMessage(request);
        var taskId = GenerateTaskId();
        var contextId = request.Params?.Message?.ContextId ?? GenerateTaskId();

        if (string.IsNullOrEmpty(userMessage))
        {
            yield return FormatSseEvent(JsonRpcResponse.Fail(request.Id, A2AProtocol.ParseError, "No text message"));
            yield break;
        }

        var (executionRequest, buildError) = await BuildExecutionRequestAsync(workflowKey, userMessage, request.Params?.Message);
        if (executionRequest is null)
        {
            yield return FormatSseEvent(JsonRpcResponse.Fail(request.Id, A2AProtocol.InternalError, buildError ?? "Workflow not found"));
            yield break;
        }

        // Working status
        yield return FormatSseEvent(JsonRpcResponse.Success(request.Id, new TaskStatusUpdateEvent
        {
            TaskId = taskId,
            Status = new A2ATaskStatus { State = TaskStates.Working }
        }));

        using var scope = scopeFactory.CreateScope();
        var executionService = scope.ServiceProvider.GetRequiredService<WorkflowExecutionService>();
        var artifactId = Guid.NewGuid().ToString("N")[..8];

        await foreach (var evt in executionService.ExecuteAsync(executionRequest, ct))
        {
            switch (evt.Type)
            {
                case EventTypes.TextChunk:
                case EventTypes.AgentCompleted:
                    yield return FormatSseEvent(JsonRpcResponse.Success(request.Id, new TaskArtifactUpdateEvent
                    {
                        TaskId = taskId,
                        Artifact = new A2AArtifact
                        {
                            ArtifactId = artifactId,
                            Parts = [A2APart.TextPart(evt.Text)]
                        },
                        Append = true,
                        LastChunk = false
                    }));
                    break;

                case EventTypes.Error:
                    yield return FormatSseEvent(JsonRpcResponse.Success(request.Id, new TaskStatusUpdateEvent
                    {
                        TaskId = taskId,
                        Status = new A2ATaskStatus
                        {
                            State = TaskStates.Failed,
                            Message = new A2AMessage { Role = "agent", Parts = [A2APart.TextPart(evt.Text)] }
                        },
                        Final = true
                    }));
                    yield break;
            }
        }

        // Completed
        yield return FormatSseEvent(JsonRpcResponse.Success(request.Id, new TaskStatusUpdateEvent
        {
            TaskId = taskId,
            Status = new A2ATaskStatus { State = TaskStates.Completed },
            Final = true
        }));
    }

    // ═══════════════════════════════════════════
    // Microsoft 格式: /v1/message:send
    // ═══════════════════════════════════════════

    public async Task<object?> HandleMsSendAsync(string workflowKey, JsonDocument body, CancellationToken ct)
    {
        string? userMessage = null;
        FileAttachment? attachment = null;

        if (body.RootElement.TryGetProperty("message", out var msgEl) &&
            msgEl.TryGetProperty("parts", out var parts))
        {
            userMessage = ExtractPartsText(parts);
            attachment = ExtractFileAttachment(parts);
        }

        if (string.IsNullOrEmpty(userMessage))
        {
            return new { error = "No message text found" };
        }

        var (executionRequest, buildError) = await BuildExecutionRequestAsync(workflowKey, userMessage);
        if (executionRequest is null)
        {
            return new { error = buildError ?? "Workflow not found or not published" };
        }

        if (attachment is not null)
        {
            executionRequest.Attachment = attachment;
        }

        var responseText = new StringBuilder();
        using var scope = scopeFactory.CreateScope();
        var executionService = scope.ServiceProvider.GetRequiredService<WorkflowExecutionService>();

        await foreach (var evt in executionService.ExecuteAsync(executionRequest, ct))
        {
            if (evt.Type is EventTypes.AgentCompleted or EventTypes.TextChunk)
            {
                responseText.Append(evt.Text);
            }
        }

        return new
        {
            parts = new[]
            {
                new { kind = "text", text = responseText.ToString().Trim() }
            }
        };
    }

    // ═══════════════════════════════════════════
    // Private Helpers
    // ═══════════════════════════════════════════

    private async Task<(WorkflowExecutionRequest? Request, string? Error)> BuildExecutionRequestAsync(
        string workflowKey, string userMessage, A2AMessage? message = null)
    {
        var workflow = await workflowStore.GetAsync(workflowKey);
        if (workflow is null || !workflow.IsPublished)
        {
            return (null, "Workflow not found or not published");
        }

        var credentials = await credentialStore.GetDecryptedCredentialsAsync(workflow.UserId);

        // workflowJson 是 save 格式（含 drawflow + workflowSettings），需轉為 execution 格式
        var executionPayload = ConvertSaveToExecution(workflow.WorkflowJson);

        var request = new WorkflowExecutionRequest
        {
            WorkflowJson = executionPayload,
            UserMessage = userMessage,
            Credentials = credentials
        };

        // 解析 FilePart → FileAttachment（取第一個檔案）
        if (message?.Parts is not null)
        {
            var filePart = message.Parts.FirstOrDefault(p => p.Kind == "file" && p.File?.Bytes is not null);
            if (filePart?.File is not null)
            {
                var attachment = new FileAttachment
                {
                    FileName = filePart.File.Name ?? "attachment",
                    MimeType = filePart.File.MimeType ?? "application/octet-stream",
                    Data = Convert.FromBase64String(filePart.File.Bytes!)
                };

                var validationError = ValidateFileAttachment(attachment, workflow);
                if (validationError is not null)
                {
                    logger.LogWarning("[A2A Server] File rejected: {Error}", validationError);
                    return (null, validationError);
                }

                request.Attachment = attachment;
            }
        }

        return (request, null);
    }

    /// <summary>
    /// 將 Studio save 格式轉為 WorkflowExecutionService 所需的 execution payload。
    /// Save 格式含 drawflow/nodeCounter/workflowSettings；
    /// Execution 格式含 nodes/connections/workflowSettings/mcpServers/a2aAgents/httpApis。
    /// </summary>
    /// <summary>
    /// 將 Studio save 格式轉為 execution payload（供 A2A/MCP Server 共用）。
    /// </summary>
    public static string ConvertSaveToExecution(string saveJson)
    {
        try
        {
            var doc = JsonDocument.Parse(saveJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("nodes", out _))
            {
                return saveJson;
            }

            if (!root.TryGetProperty("drawflow", out var drawflow) ||
                !drawflow.TryGetProperty("drawflow", out var df) ||
                !df.TryGetProperty("Home", out var home) ||
                !home.TryGetProperty("data", out var data))
            {
                return saveJson;
            }

            var payload = new Dictionary<string, object?>
            {
                ["nodes"] = ExtractNodesFromDrawflow(data),
                ["connections"] = ExtractConnectionsFromDrawflow(data),
                ["workflowSettings"] = root.TryGetProperty("workflowSettings", out var ws) ? ws : null
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }
        catch
        {
            return saveJson;
        }
    }

    private static List<JsonElement> ExtractNodesFromDrawflow(JsonElement data)
    {
        var nodes = new List<JsonElement>();
        foreach (var nodeEntry in data.EnumerateObject())
        {
            if (nodeEntry.Value.TryGetProperty("data", out var nodeData))
            {
                nodes.Add(nodeData);
            }
        }

        return nodes;
    }

    private static List<object> ExtractConnectionsFromDrawflow(JsonElement data)
    {
        var connections = new List<object>();
        foreach (var nodeEntry in data.EnumerateObject())
        {
            var node = nodeEntry.Value;
            if (!node.TryGetProperty("outputs", out var outputs))
            {
                continue;
            }

            var fromId = node.TryGetProperty("data", out var nd) && nd.TryGetProperty("id", out var fid)
                ? fid.GetString() : nodeEntry.Name;

            foreach (var output in outputs.EnumerateObject())
            {
                if (!output.Value.TryGetProperty("connections", out var conns))
                {
                    continue;
                }

                foreach (var conn in conns.EnumerateArray())
                {
                    var toNode = conn.GetProperty("node").GetString();
                    var toId = toNode;
                    if (toNode is not null && data.TryGetProperty(toNode, out var targetNode) &&
                        targetNode.TryGetProperty("data", out var targetData) &&
                        targetData.TryGetProperty("id", out var tid))
                    {
                        toId = tid.GetString();
                    }

                    connections.Add(new { from = fromId, to = toId, fromOutput = output.Name });
                }
            }
        }

        return connections;
    }

    private static string? ExtractUserMessage(JsonRpcRequest request)
    {
        if (request.Params?.Message?.Parts is null)
        {
            return null;
        }

        var textParts = request.Params.Message.Parts
            .Where(p => p.Kind == "text" && !string.IsNullOrEmpty(p.Text))
            .Select(p => p.Text);

        return string.Join("\n", textParts);
    }

    private static string? ExtractPartsText(JsonElement partsElement)
    {
        var texts = new List<string>();
        foreach (var part in partsElement.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text))
            {
                var t = text.GetString();
                if (!string.IsNullOrEmpty(t))
                {
                    texts.Add(t);
                }
            }
        }

        return texts.Count > 0 ? string.Join("\n", texts) : null;
    }

    private static FileAttachment? ExtractFileAttachment(JsonElement partsElement)
    {
        foreach (var part in partsElement.EnumerateArray())
        {
            if (part.TryGetProperty("kind", out var kind) && kind.GetString() == "file" &&
                part.TryGetProperty("file", out var file) &&
                file.TryGetProperty("bytes", out var bytes))
            {
                var b64 = bytes.GetString();
                if (string.IsNullOrEmpty(b64))
                {
                    continue;
                }

                return new FileAttachment
                {
                    FileName = file.TryGetProperty("name", out var n) ? n.GetString() ?? "attachment" : "attachment",
                    MimeType = file.TryGetProperty("mimeType", out var m) ? m.GetString() ?? "application/octet-stream" : "application/octet-stream",
                    Data = Convert.FromBase64String(b64)
                };
            }
        }

        return null;
    }

    private string FormatSseEvent(object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        return $"data: {json}\n\n";
    }

    private static string GenerateTaskId() => Guid.NewGuid().ToString("N")[..12];

    private static List<A2ASkill> ExtractSkills(Data.WorkflowDocument workflow)
    {
        try
        {
            var doc = JsonDocument.Parse(workflow.WorkflowJson);
            var skills = new List<A2ASkill>();

            foreach (var nodeData in EnumerateNodeData(doc))
            {
                if (nodeData.TryGetProperty("type", out var type) &&
                    type.GetString() == NodeTypes.Agent)
                {
                    var name = nodeData.TryGetProperty("name", out var n) ? n.GetString() ?? "Agent" : "Agent";
                    var instructions = nodeData.TryGetProperty("instructions", out var i) ? i.GetString() ?? "" : "";

                    skills.Add(new A2ASkill
                    {
                        Id = name.ToLowerInvariant().Replace(' ', '-'),
                        Name = name,
                        Description = instructions.Length > Defaults.TruncateLength ? instructions[..Defaults.TruncateLength] + "..." : instructions
                    });
                }
            }

            if (skills.Count == 0)
            {
                skills.Add(new A2ASkill
                {
                    Id = "default",
                    Name = workflow.Name,
                    Description = workflow.Description
                });
            }

            return skills;
        }
        catch
        {
            return [new A2ASkill { Id = "default", Name = workflow.Name, Description = workflow.Description }];
        }
    }

    /// <summary>
    /// 驗證收到的檔案是否為 workflow 支援的類型（依據使用者設定的 AcceptedInputModes）。
    /// </summary>
    private static string? ValidateFileAttachment(FileAttachment attachment, Data.WorkflowDocument workflow)
    {
        var modes = workflow.GetInputModes();
        var mime = attachment.MimeType.ToLowerInvariant();

        if (modes.Contains(mime))
        {
            return null;
        }

        // 圖片類型做寬鬆比對
        if (mime.StartsWith("image/", StringComparison.Ordinal) &&
            modes.Exists(m => m.StartsWith("image/", StringComparison.Ordinal)))
        {
            return null;
        }

        return $"This workflow does not support file type '{mime}'. Supported: {string.Join(", ", modes)}";
    }

    /// <summary>
    /// 遍歷 drawflow save 格式中的所有節點 data。
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateNodeData(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("drawflow", out var drawflow) &&
            drawflow.TryGetProperty("drawflow", out var df) &&
            df.TryGetProperty("Home", out var home) &&
            home.TryGetProperty("data", out var data))
        {
            foreach (var nodeEntry in data.EnumerateObject())
            {
                if (nodeEntry.Value.TryGetProperty("data", out var nodeData))
                {
                    yield return nodeData;
                }
            }
        }
    }
}
