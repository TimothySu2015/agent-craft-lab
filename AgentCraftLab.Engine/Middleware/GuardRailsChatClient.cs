using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// 內容安全中介層。掃描使用者輸入和 LLM 回應，封鎖、警告或記錄違反規則的內容。
/// 支援關鍵字/Regex 規則、Prompt Injection 偵測、Topic 限制。
/// </summary>
public class GuardRailsChatClient : DelegatingChatClient
{
    private readonly IGuardRailsPolicy _policy;
    private readonly GuardRailsOptions _options;
    private readonly ILogger<GuardRailsChatClient>? _logger;

    /// <summary>暴露 Policy 供外部平行評估使用（如 ParallelGuardRailsEvaluator）。</summary>
    public IGuardRailsPolicy Policy => _policy;

    /// <summary>暴露 Options 供外部讀取。</summary>
    public GuardRailsOptions Options => _options;

    /// <summary>
    /// 新版建構子：使用 IGuardRailsPolicy 提供完整功能。
    /// </summary>
    public GuardRailsChatClient(
        IChatClient innerClient,
        IGuardRailsPolicy policy,
        GuardRailsOptions? options = null,
        ILogger<GuardRailsChatClient>? logger = null)
        : base(innerClient)
    {
        _policy = policy;
        _options = options ?? new GuardRailsOptions();
        _logger = logger;
    }

    /// <summary>
    /// 舊版建構子（向下相容）：從 config dictionary 建立內部 DefaultGuardRailsPolicy。
    /// </summary>
    public GuardRailsChatClient(
        IChatClient innerClient,
        Dictionary<string, string>? config = null,
        ILogger<GuardRailsChatClient>? logger = null)
        : this(
            innerClient,
            DefaultGuardRailsPolicy.FromConfig(config),
            GuardRailsOptions.FromConfig(config),
            logger)
    {
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Input 掃描
        var inputBlock = EvaluateInput(messages);
        if (inputBlock is not null)
        {
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, _options.BlockedResponse));
        }

        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        // Output 掃描
        if (_options.ScanOutput && response.Text is { } responseText)
        {
            var outputBlock = EvaluateText(responseText, GuardRailsDirection.Output);
            if (outputBlock)
            {
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, _options.BlockedResponse));
            }
        }

        return response;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Input 掃描
        var inputBlock = EvaluateInput(messages);
        if (inputBlock is not null)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, _options.BlockedResponse);
            yield break;
        }

        if (!_options.ScanOutput)
        {
            // 不掃描 Output → 直接透傳
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                yield return update;
            }
            yield break;
        }

        // 掃描 Output：累積全部 chunk → 結束後掃描
        var buffer = new StringBuilder();
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (update.Text is { } chunk)
            {
                buffer.Append(chunk);
            }
            updates.Add(update);
        }

        // 掃描完整回應
        var fullText = buffer.ToString();
        if (fullText.Length > 0 && EvaluateText(fullText, GuardRailsDirection.Output))
        {
            // 封鎖：回傳 BlockedResponse 取代全部 chunk
            yield return new ChatResponseUpdate(ChatRole.Assistant, _options.BlockedResponse);
            yield break;
        }

        // 通過：回放所有原始 chunk
        foreach (var update in updates)
        {
            yield return update;
        }
    }

    /// <summary>掃描輸入訊息，回傳第一個 Block match（null = 通過）。</summary>
    private GuardRailsMatch? EvaluateInput(IEnumerable<ChatMessage> messages)
    {
        var userMessages = _options.ScanAllMessages
            ? messages.Where(m => m.Role == ChatRole.User)
            : messages.Where(m => m.Role == ChatRole.User).TakeLast(1);

        foreach (var msg in userMessages)
        {
            if (msg.Text is not { } text)
            {
                continue;
            }

            var matches = _policy.Evaluate(text, GuardRailsDirection.Input);
            foreach (var match in matches)
            {
                LogMatch(match);
                if (match.Rule.Action == GuardRailsAction.Block)
                {
                    return match;
                }
            }
        }

        return null;
    }

    /// <summary>掃描文字，回傳是否有 Block match。Warn/Log 僅記錄。</summary>
    private bool EvaluateText(string text, GuardRailsDirection direction)
    {
        var matches = _policy.Evaluate(text, direction);
        var blocked = false;

        foreach (var match in matches)
        {
            LogMatch(match);
            if (match.Rule.Action == GuardRailsAction.Block)
            {
                blocked = true;
            }
        }

        return blocked;
    }

    /// <summary>結構化審計日誌。</summary>
    private void LogMatch(GuardRailsMatch match)
    {
        var label = match.Rule.Label ?? match.Rule.Pattern;
        switch (match.Rule.Action)
        {
            case GuardRailsAction.Block:
                _logger?.LogWarning(
                    "[GUARD] Direction={Direction}, Action=Block, Rule=\"{Rule}\", Match=\"{Match}\"",
                    match.Direction, label, Truncate(match.MatchedText, 50));
                break;
            case GuardRailsAction.Warn:
                _logger?.LogWarning(
                    "[GUARD] Direction={Direction}, Action=Warn, Rule=\"{Rule}\", Match=\"{Match}\"",
                    match.Direction, label, Truncate(match.MatchedText, 50));
                break;
            case GuardRailsAction.Log:
                _logger?.LogInformation(
                    "[GUARD] Direction={Direction}, Action=Log, Rule=\"{Rule}\", Match=\"{Match}\"",
                    match.Direction, label, Truncate(match.MatchedText, 50));
                break;
        }
    }

    /// <summary>截斷文字。</summary>
    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
