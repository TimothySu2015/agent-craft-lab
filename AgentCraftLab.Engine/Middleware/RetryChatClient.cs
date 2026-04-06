using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// 當 LLM 呼叫失敗時自動重試（指數退避），最多重試 3 次
/// </summary>
public class RetryChatClient(IChatClient innerClient, int maxRetries = 3, ILogger<RetryChatClient>? logger = null) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await base.GetResponseAsync(messages, options, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
            {
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 500);
                logger?.LogWarning("[RETRY] Attempt {Attempt} failed: {Error}. Retrying in {DelayMs}ms...", attempt + 1, ex.Message, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return StreamWithRetryAsync(messages, options, cancellationToken);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithRetryAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 串流重試策略：失敗時從頭重試整個串流
        // 注意：已 yield 的 chunk 無法撤回，因此僅在取得第一個 chunk 前的失敗才重試
        Exception? lastException = null;
        for (int attempt = 0; attempt < maxRetries + 1; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt - 1) * 500);
                logger?.LogWarning("[RETRY] Stream attempt {Attempt}, retrying in {DelayMs}ms...", attempt + 1, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }

            var succeeded = false;
            lastException = null;

            await using var enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                ChatResponseUpdate current;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        succeeded = true;
                        break;
                    }
                    current = enumerator.Current;
                }
                catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
                {
                    lastException = ex;
                    logger?.LogWarning("[RETRY] Stream attempt {Attempt} failed: {Error}", attempt + 1, ex.Message);
                    break;
                }

                yield return current;
            }

            if (succeeded)
                yield break;
        }

        if (lastException is not null)
        {
            logger?.LogError(lastException, "[RETRY] Stream retries exhausted after {MaxRetries} attempts", maxRetries);
            throw lastException;
        }
    }

    private static bool IsTransient(Exception ex)
    {
        if (ex is HttpRequestException { StatusCode: { } statusCode })
        {
            return statusCode is System.Net.HttpStatusCode.TooManyRequests
                or System.Net.HttpStatusCode.ServiceUnavailable
                or System.Net.HttpStatusCode.GatewayTimeout
                or System.Net.HttpStatusCode.BadGateway;
        }

        if (ex is TaskCanceledException or TimeoutException)
        {
            return true;
        }

        // Fallback：訊息中包含 rate limit 關鍵字（某些 SDK 不設 StatusCode）
        return ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
    }
}
