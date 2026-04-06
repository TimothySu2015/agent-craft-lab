using System.Diagnostics;
using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// 記錄每次 LLM 呼叫的輸入訊息與執行耗時
/// </summary>
public class AgentLoggingChatClient(IChatClient innerClient, ILogger<AgentLoggingChatClient>? logger = null) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var lastUserMsg = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        logger?.LogInformation("[LOG] Input: {Input}", StringUtils.Truncate(lastUserMsg, 100));

        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            sw.Stop();
            logger?.LogInformation("[LOG] Output: {Output} ({ElapsedMs}ms)", StringUtils.Truncate(response.Text ?? "", 100), sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger?.LogError(ex, "[LOG] Failed after {ElapsedMs}ms: {Error}", sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var lastUserMsg = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        logger?.LogInformation("[LOG] Input (stream): {Input}", StringUtils.Truncate(lastUserMsg, 100));

        ChatResponseUpdate? current = null;
        await using var enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }
                current = enumerator.Current;
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger?.LogError(ex, "[LOG] Stream failed after {ElapsedMs}ms: {Error}", sw.ElapsedMilliseconds, ex.Message);
                throw;
            }

            yield return current;
        }

        sw.Stop();
        logger?.LogInformation("[LOG] Stream completed ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
    }

}
