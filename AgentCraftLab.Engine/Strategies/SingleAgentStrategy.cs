using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentCraftLab.Engine.Diagnostics;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Strategies.NodeExecutors;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Strategies;

/// <summary>
/// 單一 agent 執行策略：統一 streaming 模式（MEAI 10.4.1 已修復 streaming + tool call bug）。
/// </summary>
public class SingleAgentStrategy : IWorkflowStrategy
{
    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowStrategyContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var node = context.AgentNodes[0];
        var agentName = node.Name ?? node.Id;
        using var nodeActivity = EngineActivitySource.StartNodeExecution(
            node.Type, agentName, node.Id, context.SessionId);
        var agentChatClient = context.AgentContext.ChatClients[node.Id];
        var userMessage = context.Request.UserMessage;

        // ── Hook ③: PreAgent ──
        var (preInput, preEvt) = await WorkflowStreamHelper.RunPreAgentHookAsync(
            context, agentName, node.Id, userMessage, cancellationToken);
        if (preInput is null)
        {
            yield return preEvt!;
            yield break;
        }

        if (preEvt is not null)
        {
            yield return preEvt;
        }

        // PreAgent hook 可能修改了 userMessage（僅 code 類型）
        if (preInput != userMessage)
        {
            context.Request.UserMessage = preInput;
        }

        var skillNames = context.AgentContext.NodeSkillNames?.GetValueOrDefault(node.Id);
        yield return ExecutionEvent.AgentStarted(agentName, skillNames);

        var instructions = context.AgentContext.NodeInstructions?.GetValueOrDefault(node.Id)
            ?? AgentContextBuilder.BuildInstructions(node.Instructions, node.OutputFormat);
        // Prompt Cache: 拆為 static/dynamic，啟用 Anthropic prefix caching
        var provider = AgentContextBuilder.NormalizeProvider(node.Provider);
        var messages = AgentNodeExecutor.BuildSystemMessages(instructions, null, provider);

        foreach (var h in context.Request.History)
        {
            messages.Add(new ChatMessage(
                h.Role == "assistant" ? ChatRole.Assistant : ChatRole.User,
                h.Text));
        }

        messages.Add(AgentContextBuilder.BuildUserMessage(
            context.Request.UserMessage, context.Request.Attachment));

        var allTools = context.AgentContext.NodeToolsMap.TryGetValue(node.Id, out var nt) ? nt : new List<AITool>();
        var fullText = new StringBuilder();
        long inputTokens = 0, outputTokens = 0;

        ChatResponseFormat? responseFormat = null;
        if (node.OutputFormat == "json")
        {
            responseFormat = ChatResponseFormat.Json;
        }
        else if (node.OutputFormat == "json_schema" && !string.IsNullOrWhiteSpace(node.OutputSchema))
        {
            try
            {
                var schemaElement = JsonDocument.Parse(node.OutputSchema).RootElement;
                responseFormat = ChatResponseFormat.ForJsonSchema(
                    schemaElement,
                    schemaName: "OutputSchema",
                    schemaDescription: "Agent output schema defined by user");
            }
            catch (Exception ex) { _ = ex; /* invalid schema, fall back to text */ }
        }

        // 統一 streaming 模式（MEAI 10.4.1 已修復 streaming + tool call bug）
        {
            var chatOptions = new ChatOptions();
            if (allTools.Count > 0)
            {
                chatOptions.Tools = allTools.Cast<AITool>().ToList();
            }

            if (responseFormat != null)
            {
                chatOptions.ResponseFormat = responseFormat;
            }

            await foreach (var update in agentChatClient.GetStreamingResponseAsync(messages, chatOptions, cancellationToken))
            {
                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent call)
                    {
                        var argsStr = call.Arguments != null
                            ? string.Join(", ", call.Arguments.Select(kv => $"{kv.Key}=\"{kv.Value}\""))
                            : "";
                        yield return ExecutionEvent.ToolCall(agentName, call.Name ?? "?", argsStr);
                    }
                    else if (content is FunctionResultContent result)
                    {
                        var resultStr = result.Result?.ToString() ?? "";
                        if (resultStr.Length > Defaults.TruncateLength)
                        {
                            resultStr = resultStr[..Defaults.TruncateLength] + "...";
                        }

                        yield return ExecutionEvent.ToolResult(agentName, result.CallId ?? "?", resultStr);
                    }
                    else if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                    {
                        fullText.Append(textContent.Text);
                        yield return ExecutionEvent.TextChunk(agentName, textContent.Text);
                    }
                }

                // streaming 模式無 Usage — 由下方估算邏輯處理
            }
        }

        var finalText = fullText.ToString();

        // 串流模式無 Usage，依字元組成估算 tokens
        if (inputTokens == 0 && outputTokens == 0)
        {
            inputTokens = messages.Sum(m => ModelPricing.EstimateTokens(m.Text ?? ""));
            outputTokens = ModelPricing.EstimateTokens(finalText);
        }

        yield return ExecutionEvent.AgentCompleted(agentName, finalText, inputTokens, outputTokens, node.Model);

        // RAG 引用來源
        var ctxBuilder = context.AgentContext.ContextBuilder;
        if (ctxBuilder?.LastRagCitations is { Count: > 0 } ragCitations)
        {
            yield return ExecutionEvent.RagCitations(ragCitations, ctxBuilder.LastExpandedQueries);
        }

        // ── Hook ④: PostAgent ──
        var postEvt = await WorkflowStreamHelper.RunPostAgentHookAsync(
            context, agentName, node.Id, context.Request.UserMessage, finalText, cancellationToken);
        if (postEvt is not null)
        {
            yield return postEvt;
        }
    }
}
