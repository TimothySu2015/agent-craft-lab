using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// 為每次 LLM 呼叫注入預設的 ChatOptions（Temperature、TopP、MaxOutputTokens）
/// </summary>
public class ChatOptionsChatClient(
    IChatClient innerClient,
    float? temperature = null,
    float? topP = null,
    int? maxOutputTokens = null) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = ApplyDefaults(options);
        return base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = ApplyDefaults(options);
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }

    private ChatOptions ApplyDefaults(ChatOptions? options)
    {
        options ??= new ChatOptions();
        if (temperature.HasValue)
            options.Temperature = temperature.Value;
        if (topP.HasValue)
            options.TopP = topP.Value;
        if (maxOutputTokens.HasValue)
            options.MaxOutputTokens = maxOutputTokens.Value;
        return options;
    }
}
