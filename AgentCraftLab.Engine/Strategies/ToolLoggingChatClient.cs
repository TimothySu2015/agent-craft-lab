using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Strategies;

/// <summary>
/// 攔截 LLM 回應中的工具調用訊息，記錄到共享 queue。
/// </summary>
internal class ToolLoggingChatClient : DelegatingChatClient
{
    private readonly string _agentName;
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string AgentName, string Type, string Text)> _logs;

    public ToolLoggingChatClient(IChatClient inner, string agentName,
        System.Collections.Concurrent.ConcurrentQueue<(string, string, string)> logs) : base(inner)
    {
        _agentName = agentName;
        _logs = logs;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        var response = await base.GetResponseAsync(messages, options, ct);
        foreach (var msg in response.Messages)
            foreach (var content in msg.Contents)
                LogContent(content);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, ct).WithCancellation(ct))
        {
            foreach (var content in update.Contents)
                LogContent(content);
            yield return update;
        }
    }

    private void LogContent(AIContent content)
    {
        if (content is FunctionCallContent call)
        {
            var argsStr = FormatArguments(call.Arguments);
            _logs.Enqueue((_agentName, "call", $"{call.Name}({argsStr})"));
        }
        else if (content is FunctionResultContent result)
        {
            var text = StringUtils.Truncate(result.Result?.ToString() ?? "", Defaults.TruncateLength);
            _logs.Enqueue((_agentName, "result", $"{result.CallId}: {text}"));
        }
    }

    private static string FormatArguments(IDictionary<string, object?>? arguments)
        => arguments != null
            ? string.Join(", ", arguments.Select(kv => $"{kv.Key}=\"{kv.Value}\""))
            : "";

}
