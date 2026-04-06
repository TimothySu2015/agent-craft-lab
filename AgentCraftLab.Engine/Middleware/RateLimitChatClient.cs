using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// 限制 LLM 呼叫頻率，使用 Token Bucket 演算法（每秒 5 次請求）。
/// </summary>
public class RateLimitChatClient : DelegatingChatClient
{
    /// <summary>取得 rate limit token 的最大等待時間。</summary>
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(30);

    private readonly RateLimiter _limiter;
    private readonly ILogger<RateLimitChatClient>? _logger;

    public RateLimitChatClient(IChatClient innerClient, int permitsPerSecond = 5, ILogger<RateLimitChatClient>? logger = null)
        : base(innerClient)
    {
        _logger = logger;
        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = permitsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = permitsPerSecond,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 10
        });
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(AcquireTimeout);
        using var lease = await _limiter.AcquireAsync(1, cts.Token);
        if (!lease.IsAcquired)
        {
            _logger?.LogWarning("[RATE] Request throttled");
            throw new InvalidOperationException("Rate limit exceeded. Please try again later.");
        }

        return await base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(AcquireTimeout);
        using var lease = await _limiter.AcquireAsync(1, cts.Token);
        if (!lease.IsAcquired)
        {
            _logger?.LogWarning("[RATE] Stream request throttled");
            throw new InvalidOperationException("Rate limit exceeded. Please try again later.");
        }

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _limiter.Dispose();
        base.Dispose(disposing);
    }
}
