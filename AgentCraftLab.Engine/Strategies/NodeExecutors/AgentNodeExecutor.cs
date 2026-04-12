using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Agent 節點執行器 — LLM 推理 + 工具呼叫 + Chat History + Hook + 附件。
/// </summary>
public sealed class AgentNodeExecutor : NodeExecutorBase<AgentNode>
{
    protected override async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, AgentNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var agentName = string.IsNullOrEmpty(node.Name) ? nodeId : node.Name;
        var input = state.PreviousResult;

        // ── Hook ③: PreAgent ──
        if (state.HookRunner is not null && state.Hooks?.PreAgent is not null)
        {
            var hookCtx = new HookContext
            {
                Input = input, AgentName = agentName, AgentId = nodeId,
                WorkflowName = state.WorkflowName
            };
            var hookResult = await state.HookRunner.ExecuteAsync(state.Hooks.PreAgent, hookCtx, cancellationToken);
            if (hookResult.IsBlocked)
            {
                yield return ExecutionEvent.HookBlocked("PreAgent", hookResult.Message ?? "Blocked");
                yield break;
            }

            input = hookResult.TransformedInput;
            if (hookResult.Message is not null)
            {
                yield return ExecutionEvent.HookExecuted("PreAgent", hookResult.Message);
            }
        }

        var skillNames = state.AgentContext.NodeSkillNames?.GetValueOrDefault(nodeId);
        yield return ExecutionEvent.AgentStarted(agentName, skillNames);

        var result = await RunAgentNodeAsync(nodeId, node, state, input, cancellationToken);

        yield return ExecutionEvent.TextChunk(agentName, result);
        var estimatedInputTokens = ModelPricing.EstimateTokens(node.Instructions) + ModelPricing.EstimateTokens(input);
        yield return ExecutionEvent.AgentCompleted(
            agentName, result, estimatedInputTokens, ModelPricing.EstimateTokens(result), node.Model.Model);

        // RAG 引用來源
        var nodeCtxBuilder = state.AgentContext.ContextBuilder;
        if (nodeCtxBuilder?.LastRagCitations is { Count: > 0 } nodeCitations)
        {
            yield return ExecutionEvent.RagCitations(nodeCitations, nodeCtxBuilder.LastExpandedQueries);
            nodeCtxBuilder.LastRagCitations = null;
        }

        // ── Hook ④: PostAgent ──
        if (state.HookRunner is not null && state.Hooks?.PostAgent is not null)
        {
            var hookCtx = new HookContext
            {
                Input = input, Output = result, AgentName = agentName, AgentId = nodeId,
                WorkflowName = state.WorkflowName
            };
            var hookResult = await state.HookRunner.ExecuteAsync(state.Hooks.PostAgent, hookCtx, cancellationToken);
            if (hookResult.Message is not null)
            {
                yield return ExecutionEvent.HookExecuted("PostAgent", hookResult.Message);
            }
        }
    }

    private static async Task<string> RunAgentNodeAsync(
        string nodeId, AgentNode node, ImperativeExecutionState state,
        string input, CancellationToken cancellationToken)
    {
        var agentName = string.IsNullOrEmpty(node.Name) ? nodeId : node.Name;
        var fullText = new StringBuilder();

        // 取出附件（僅第一個 Agent 使用，用完即清）
        var attachment = state.Attachment;
        if (attachment is not null)
            state.Attachment = null;

        var chatOptions = BuildResponseFormatOptions(node.Output);
        var contextPrefix = ContextPassingHelper.BuildContextPrefix(state, nodeId);

        // 解析 instructions 中的所有引用
        var rawInstructions = state.AgentContext.NodeInstructions?.GetValueOrDefault(nodeId)
            ?? AgentContextBuilder.BuildInstructions(node.Instructions, FormatOutputFormat(node.Output.Kind));

        string? resolvedInstructions = null;
        if (state.VariableResolver.HasReferences(rawInstructions))
        {
            var ctx = state.ToVariableContext();
            resolvedInstructions = state.ReferenceCompactor is not null
                ? await state.VariableResolver.ResolveAsync(
                    rawInstructions, ctx, state.ReferenceCompactor, agentName, cancellationToken)
                : state.VariableResolver.Resolve(rawInstructions, ctx);
        }

        var provider = AgentContextBuilder.NormalizeProvider(node.Model.Provider);

        if (state.ChatHistories.TryGetValue(nodeId, out var history))
        {
            if (!state.ChatClients.TryGetValue(nodeId, out var chatClient))
                return input;

            var effectiveInstructions = resolvedInstructions ?? rawInstructions;
            var systemMessages = BuildSystemMessages(effectiveInstructions, contextPrefix, provider);
            ReplaceSystemMessages(history, systemMessages);

            var userMsg = AgentContextBuilder.BuildUserMessage(input, attachment);
            history.Add(userMsg);

            var maxMessages = node.History.MaxMessages > 0 ? node.History.MaxMessages : 20;
            state.HistoryStrategy.TrimHistory(history, maxMessages);

            await foreach (var update in chatClient.GetStreamingResponseAsync(history, chatOptions, cancellationToken))
            {
                var text = update.Text ?? "";
                if (!string.IsNullOrEmpty(text))
                    fullText.Append(text);
            }

            history.Add(new ChatMessage(ChatRole.Assistant, fullText.ToString()));
        }
        else if (attachment is { Data.Length: > 0 } && state.ChatClients.TryGetValue(nodeId, out var chatClientForAttachment))
        {
            var baseInstructions = resolvedInstructions
                ?? state.AgentContext.NodeInstructions?.GetValueOrDefault(nodeId)
                ?? AgentContextBuilder.BuildInstructions(node.Instructions, FormatOutputFormat(node.Output.Kind));
            var messages = BuildSystemMessages(baseInstructions, contextPrefix, provider);
            messages.Add(AgentContextBuilder.BuildUserMessage(input, attachment));
            await foreach (var update in chatClientForAttachment.GetStreamingResponseAsync(messages, chatOptions, cancellationToken))
            {
                var text = update.Text ?? "";
                if (!string.IsNullOrEmpty(text))
                    fullText.Append(text);
            }
        }
        else if (chatOptions is not null && state.ChatClients.TryGetValue(nodeId, out var chatClientDirect))
        {
            var baseInstructions = resolvedInstructions
                ?? state.AgentContext.NodeInstructions?.GetValueOrDefault(nodeId)
                ?? AgentContextBuilder.BuildInstructions(node.Instructions, FormatOutputFormat(node.Output.Kind));
            var messages = BuildSystemMessages(baseInstructions, contextPrefix, provider);
            messages.Add(new ChatMessage(ChatRole.User, input));
            await foreach (var update in chatClientDirect.GetStreamingResponseAsync(messages, chatOptions, cancellationToken))
            {
                var text = update.Text ?? "";
                if (!string.IsNullOrEmpty(text))
                    fullText.Append(text);
            }
        }
        else
        {
            if (!state.Agents.TryGetValue(nodeId, out var agent))
                return input;

            var agentInput = string.IsNullOrEmpty(contextPrefix) ? input : contextPrefix + "\n\n" + input;

            await foreach (var update in agent.RunStreamingAsync(agentInput)
                               .WithCancellation(cancellationToken))
            {
                var text = update.ToString();
                if (!string.IsNullOrEmpty(text))
                    fullText.Append(text);
            }
        }

        return fullText.ToString();
    }

    /// <summary>
    /// 將 instructions 拆為 static/dynamic 兩條 system message，啟用 Anthropic prompt prefix caching。
    /// </summary>
    internal static List<ChatMessage> BuildSystemMessages(string instructions, string? contextPrefix, string? provider)
    {
        var cacheable = AgentContextBuilder.SplitAtDynamicBoundary(instructions);

        if (!string.IsNullOrEmpty(contextPrefix))
        {
            var dynamicPart = string.IsNullOrWhiteSpace(cacheable.DynamicPart)
                ? contextPrefix
                : contextPrefix + "\n\n" + cacheable.DynamicPart;
            cacheable = new CacheableSystemPrompt(cacheable.StaticPart, dynamicPart);
        }

        return cacheable.ToChatMessages(provider);
    }

    internal static void ReplaceSystemMessages(List<ChatMessage> history, List<ChatMessage> newSystemMessages)
    {
        var removeCount = 0;
        while (removeCount < history.Count && history[removeCount].Role == ChatRole.System)
        {
            removeCount++;
        }

        history.RemoveRange(0, removeCount);
        history.InsertRange(0, newSystemMessages);
    }

    /// <summary>
    /// 根據 <see cref="OutputConfig"/> 建構 ChatOptions。
    /// </summary>
    internal static ChatOptions? BuildResponseFormatOptions(OutputConfig output)
    {
        ChatResponseFormat? responseFormat = null;
        if (output.Kind == OutputFormat.Json)
        {
            responseFormat = ChatResponseFormat.Json;
        }
        else if (output.Kind == OutputFormat.JsonSchema && !string.IsNullOrWhiteSpace(output.SchemaJson))
        {
            try
            {
                var schemaElement = JsonDocument.Parse(output.SchemaJson).RootElement;
                responseFormat = ChatResponseFormat.ForJsonSchema(
                    schemaElement,
                    schemaName: "OutputSchema",
                    schemaDescription: "Agent output schema defined by user");
            }
            catch { /* invalid schema, fall back to text */ }
        }

        return responseFormat is not null ? new ChatOptions { ResponseFormat = responseFormat } : null;
    }

    private static string FormatOutputFormat(OutputFormat kind) => kind switch
    {
        OutputFormat.Json => "json",
        OutputFormat.JsonSchema => "json_schema",
        _ => "text"
    };
}
