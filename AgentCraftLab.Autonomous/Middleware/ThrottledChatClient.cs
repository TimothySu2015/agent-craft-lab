using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Middleware;

/// <summary>
/// 節流 ChatClient — 用共享 SemaphoreSlim 限制並行 LLM 呼叫數量，避免 429 rate limit。
/// 放在 FunctionInvokingChatClient 下層，確保每次 LLM HTTP 呼叫都受節流控制。
/// </summary>
public sealed class ThrottledChatClient(IChatClient innerClient, SemaphoreSlim throttle) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
        finally
        {
            throttle.Release();
        }
    }
}
