using AgentCraftLab.Autonomous.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Middleware;

/// <summary>
/// Token 預算 Middleware — 包裝 IChatClient，追蹤每次呼叫的 token 消耗。
/// 可獨立於 ReactExecutor 使用（例如未來整合到 Engine 的 Middleware 管線）。
/// </summary>
public sealed class TokenBudgetChatClient : DelegatingChatClient
{
    private readonly TokenTracker _tracker;

    public TokenBudgetChatClient(IChatClient innerClient, TokenTracker tracker)
        : base(innerClient)
    {
        _tracker = tracker;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_tracker.ShouldStop)
        {
            throw new TokenBudgetExceededException(_tracker.TotalTokensUsed);
        }

        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        var inputTokens = response.Usage?.InputTokenCount ?? 0;
        var outputTokens = response.Usage?.OutputTokenCount ?? 0;
        _tracker.Record(inputTokens, outputTokens);

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_tracker.ShouldStop)
        {
            throw new TokenBudgetExceededException(_tracker.TotalTokensUsed);
        }

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }
}

/// <summary>
/// Token 預算超出例外。
/// </summary>
public sealed class TokenBudgetExceededException : Exception
{
    public long TokensUsed { get; }

    public TokenBudgetExceededException(long tokensUsed)
        : base($"Token budget exceeded: {tokensUsed} tokens used")
    {
        TokensUsed = tokensUsed;
    }
}
