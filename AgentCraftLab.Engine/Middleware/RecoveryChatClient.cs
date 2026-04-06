using System.Net;
using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Services.Compression;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// RecoveryChatClient 的設定選項。
/// </summary>
public sealed record RecoveryOptions
{
    /// <summary>是否啟用輸出截斷自動恢復（偵測 FinishReason == Length 時加倍 MaxOutputTokens 重試）。</summary>
    public bool EnableTruncationRecovery { get; init; } = true;

    /// <summary>截斷恢復最大重試次數。</summary>
    public int MaxTruncationRetries { get; init; } = 2;

    /// <summary>MaxOutputTokens 上限天花板（防止無限加倍）。</summary>
    public int MaxOutputTokensCeiling { get; init; } = 32_768;

    /// <summary>是否啟用 context overflow 偵測（400 + context_length 錯誤訊息）。</summary>
    public bool EnableContextOverflowDetection { get; init; } = true;

    /// <summary>是否啟用模型不可用偵測（404/403 + model 相關錯誤訊息）。</summary>
    public bool EnableModelUnavailableDetection { get; init; } = true;

    /// <summary>context overflow 時的回呼（若未設定則僅 log warning 並 rethrow）。</summary>
    public Func<Exception, CancellationToken, Task>? OnContextOverflow { get; init; }

    /// <summary>模型不可用時的回呼（若未設定則僅 log warning 並 rethrow）。第二個參數為 model 名稱。</summary>
    public Func<Exception, string, CancellationToken, Task>? OnModelUnavailable { get; init; }

    /// <summary>
    /// Context overflow 時用於壓縮對話的壓縮器。
    /// 設定後 L4 會自動壓縮中間歷史 + 重試，取代單純的 callback。
    /// </summary>
    public IContextCompactor? ContextCompactor { get; init; }

    /// <summary>壓縮狀態追蹤器（可選）。設定後 L4 壓縮成功時自動記錄統計。</summary>
    public CompressionState? CompressionState { get; init; }

    /// <summary>
    /// 主動壓縮閾值（token 數）。設定後會在 LLM 呼叫前主動檢查並壓縮，避免 context overflow 的浪費 API 呼叫。
    /// 建議值：ModelContextWindows.GetCompressionThreshold(modelName)。
    /// null = 停用主動壓縮（維持現有錯誤驅動模式）。
    /// </summary>
    public int? ProactiveCompressionThreshold { get; init; }

    /// <summary>從前端 config dictionary 解析設定。</summary>
    public static RecoveryOptions FromConfig(Dictionary<string, string>? config)
    {
        if (config is null)
        {
            return new RecoveryOptions();
        }

        var options = new RecoveryOptions();

        if (config.TryGetValue("maxTruncationRetries", out var maxRetries) && int.TryParse(maxRetries, out var mr))
        {
            options = options with { MaxTruncationRetries = mr };
        }

        if (config.TryGetValue("maxOutputTokensCeiling", out var ceiling) && int.TryParse(ceiling, out var c))
        {
            options = options with { MaxOutputTokensCeiling = c };
        }

        if (config.TryGetValue("enableTruncation", out var et))
        {
            options = options with { EnableTruncationRecovery = !string.Equals(et, "false", StringComparison.OrdinalIgnoreCase) };
        }

        if (config.TryGetValue("enableContextOverflow", out var eco))
        {
            options = options with { EnableContextOverflowDetection = !string.Equals(eco, "false", StringComparison.OrdinalIgnoreCase) };
        }

        if (config.TryGetValue("enableModelUnavailable", out var emu))
        {
            options = options with { EnableModelUnavailableDetection = !string.Equals(emu, "false", StringComparison.OrdinalIgnoreCase) };
        }

        return options;
    }
}

/// <summary>
/// 語意層級錯誤恢復 Middleware — 處理輸出截斷、context overflow、模型不可用。
/// 位於 middleware 鏈中 retry 之外：retry 先解決 HTTP 瞬態錯誤，recovery 再評估回應品質。
/// <list type="bullet">
///   <item>L3：Output 截斷 → 加倍 MaxOutputTokens 重試</item>
///   <item>L4：Context overflow → 呼叫 OnContextOverflow 回呼（E1 IContextCompactor 接入點）</item>
///   <item>L5：Model 不可用 → 呼叫 OnModelUnavailable 回呼</item>
/// </list>
/// </summary>
public sealed class RecoveryChatClient(
    IChatClient innerClient,
    RecoveryOptions? options = null,
    ILogger<RecoveryChatClient>? logger = null) : DelegatingChatClient(innerClient)
{
    private readonly RecoveryOptions _options = options ?? new RecoveryOptions();

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages as List<ChatMessage> ?? messages.ToList();

        // 主動壓縮：在 LLM 呼叫前檢查 token 數，避免 context overflow 的浪費 API 呼叫
        if (_options.ProactiveCompressionThreshold is { } threshold)
        {
            messageList = await TryProactiveCompactionAsync(messageList, threshold, cancellationToken);
        }

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messageList, options, cancellationToken);
        }
        catch (Exception ex) when (ShouldHandleContextOverflow(ex))
        {
            // L4：嘗試壓縮中間歷史 + 重試
            if (_options.ContextCompactor is not null)
            {
                var compressed = await TryCompressAndRetryAsync(messageList, options, cancellationToken);
                if (compressed is not null)
                {
                    return compressed;
                }
            }

            // fallback：呼叫 callback（向下相容）
            await HandleContextOverflowAsync(ex, cancellationToken);
            throw;
        }
        catch (Exception ex) when (ShouldHandleModelUnavailable(ex))
        {
            await HandleModelUnavailableAsync(ex, options?.ModelId, cancellationToken);
            throw;
        }

        // L3：截斷偵測 + MaxOutputTokens 自動升級重試
        if (!_options.EnableTruncationRecovery || !IsOutputTruncated(response))
        {
            return response;
        }

        var currentMax = options?.MaxOutputTokens ?? 4_096;
        for (int retry = 0; retry < _options.MaxTruncationRetries; retry++)
        {
            var newMax = Math.Min(currentMax * 2, _options.MaxOutputTokensCeiling);
            if (newMax <= currentMax)
            {
                break; // 已達天花板
            }

            logger?.LogWarning(
                "[RECOVERY] Output truncated, retrying with MaxOutputTokens {OldMax} → {NewMax} (attempt {Attempt}/{Max})",
                currentMax, newMax, retry + 1, _options.MaxTruncationRetries);

            var retryOptions = CloneWithMaxTokens(options, newMax);
            currentMax = newMax;

            try
            {
                response = await base.GetResponseAsync(messageList, retryOptions, cancellationToken);
            }
            catch (Exception ex) when (ShouldHandleContextOverflow(ex))
            {
                await HandleContextOverflowAsync(ex, cancellationToken);
                throw;
            }

            if (!IsOutputTruncated(response))
            {
                return response;
            }
        }

        // 重試仍截斷，回傳最後一次結果（部分結果優於無結果）
        logger?.LogWarning("[RECOVERY] Output still truncated after {Max} retries, returning partial result", _options.MaxTruncationRetries);
        return response;
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return StreamWithRecoveryAsync(messages, options, cancellationToken);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithRecoveryAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 主動壓縮（串流版同步處理，在第一個 chunk 前完成）
        var messageList = messages as List<ChatMessage> ?? messages.ToList();
        if (_options.ProactiveCompressionThreshold is { } threshold)
        {
            messageList = await TryProactiveCompactionAsync(messageList, threshold, cancellationToken);
        }

        // 串流模式：L4/L5 在第一個 chunk 前偵測，L3 截斷僅 log（已 yield 的 chunk 無法撤回）
        ChatFinishReason? lastFinishReason = null;

        await foreach (var update in base.GetStreamingResponseAsync(messageList, options, cancellationToken))
        {
            if (update.FinishReason is not null)
            {
                lastFinishReason = update.FinishReason;
            }

            yield return update;
        }

        if (_options.EnableTruncationRecovery && lastFinishReason == ChatFinishReason.Length)
        {
            logger?.LogWarning("[RECOVERY] Streaming output was truncated (FinishReason=Length). Truncation recovery is not supported in streaming mode.");
        }
    }

    // ─── 偵測方法 ───

    private static bool IsOutputTruncated(ChatResponse response) =>
        response.FinishReason == ChatFinishReason.Length;

    private bool ShouldHandleContextOverflow(Exception ex) =>
        _options.EnableContextOverflowDetection && IsContextOverflow(ex);

    private bool ShouldHandleModelUnavailable(Exception ex) =>
        _options.EnableModelUnavailableDetection && IsModelUnavailable(ex);

    private static bool IsContextOverflow(Exception ex)
    {
        if (ex is HttpRequestException { StatusCode: HttpStatusCode.BadRequest })
        {
            var msg = ex.Message;
            return msg.Contains("context_length", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("maximum context", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("too many tokens", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("context window", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("prompt is too long", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("token limit", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsModelUnavailable(Exception ex)
    {
        if (ex is HttpRequestException { StatusCode: var code } &&
            code is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            var msg = ex.Message;
            return msg.Contains("model_not_found", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("model not found", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("deployment not found", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    // ─── 主動壓縮管線（Proactive Compaction） ───

    /// <summary>
    /// 主動壓縮管線：在 LLM 呼叫前檢查 token 數，依序嘗試零成本策略 → LLM 壓縮。
    /// 參考 Microsoft Agent Framework 1.0.0 PipelineCompactionStrategy 設計：
    /// 從溫和到激進依序執行，每步後重新評估是否仍超過閾值。
    /// </summary>
    private async Task<List<ChatMessage>> TryProactiveCompactionAsync(
        List<ChatMessage> messages, int threshold, CancellationToken ct)
    {
        var estimatedTokens = EstimateMessageTokens(messages);
        if (estimatedTokens <= threshold)
        {
            return messages;
        }

        logger?.LogInformation(
            "[RECOVERY] Proactive compaction triggered: ~{Tokens} tokens > {Threshold} threshold",
            estimatedTokens, threshold);

        // Step 1: 零成本 — 截斷超長 tool results（保留結構）
        var charsSaved = ToolResultTruncator.Truncate(messages, state: _options.CompressionState);
        if (charsSaved > 0)
        {
            var newEstimate = EstimateMessageTokens(messages);
            logger?.LogInformation(
                "[RECOVERY] Proactive step 1 (truncate tool results): ~{Saved} chars saved, ~{Tokens} tokens remaining",
                charsSaved, newEstimate);
            if (newEstimate <= threshold)
            {
                return messages;
            }
        }

        // Step 2: 零成本 — 去重 + 合併短訊息
        if (MessageDeduplicator.TryCompress(messages))
        {
            var newEstimate2 = EstimateMessageTokens(messages);
            logger?.LogInformation(
                "[RECOVERY] Proactive step 2 (deduplication): ~{Tokens} tokens remaining",
                newEstimate2);
            if (newEstimate2 <= threshold)
            {
                return messages;
            }
        }

        // Step 3: LLM 成本 — 壓縮中間歷史（同 L4 邏輯，但不重試 API 呼叫）
        if (_options.ContextCompactor is not null && messages.Count >= 3)
        {
            try
            {
                var systemPrompt = messages[0];
                var lastUserMessage = messages[^1];
                var middleMessages = messages.Skip(1).Take(messages.Count - 2).ToList();

                var serialized = MessageSerializer.Serialize(middleMessages);
                var context = lastUserMessage.Text ?? "";
                var budget = Math.Max(threshold / 2, 100);

                var compressed = await _options.ContextCompactor.CompressAsync(serialized, context, budget, ct);
                if (compressed is not null)
                {
                    var tokensSaved = Models.ModelPricing.EstimateTokens(serialized) - Models.ModelPricing.EstimateTokens(compressed);
                    logger?.LogInformation(
                        "[RECOVERY] Proactive step 3 (LLM compression): {Count} messages → {Chars} chars (~{TokensSaved} tokens saved)",
                        middleMessages.Count, compressed.Length, tokensSaved);
                    _options.CompressionState?.RecordCompression(tokensSaved);

                    return
                    [
                        systemPrompt,
                        MessageSerializer.WrapAsCompressedHistory(compressed, middleMessages.Count),
                        lastUserMessage
                    ];
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.LogWarning(ex, "[RECOVERY] Proactive step 3 (LLM compression) failed, proceeding without compression");
            }
        }

        return messages;
    }

    // ─── L4 壓縮+重試 ───

    /// <summary>
    /// 壓縮中間歷史訊息並重試 LLM 呼叫。
    /// 保留 messages[0]（System prompt）+ messages[^1]（最後 User message），壓縮中間歷史。
    /// </summary>
    private async Task<ChatResponse?> TryCompressAndRetryAsync(
        List<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken ct)
    {
        if (messages.Count < 3 || _options.ContextCompactor is null)
        {
            return null; // 太短沒有中間歷史可壓縮
        }

        try
        {
            var systemPrompt = messages[0];
            var lastUserMessage = messages[^1];
            var middleMessages = messages.Skip(1).Take(messages.Count - 2).ToList();

            // 序列化中間歷史 → 壓縮
            var serialized = MessageSerializer.Serialize(middleMessages);
            var context = lastUserMessage.Text ?? "";
            var budget = Math.Max((int)(Models.ModelPricing.EstimateTokens(serialized) / 2), 100); // 壓到 50%，最低 100

            var compressed = await _options.ContextCompactor.CompressAsync(serialized, context, budget, ct);
            if (compressed is null)
            {
                return null;
            }

            // 重建壓縮後的 message list
            var compressedMessages = new List<ChatMessage>
            {
                systemPrompt,
                MessageSerializer.WrapAsCompressedHistory(compressed, middleMessages.Count),
                lastUserMessage
            };

            var tokensSaved = Models.ModelPricing.EstimateTokens(serialized) - Models.ModelPricing.EstimateTokens(compressed);
            logger?.LogInformation(
                "[RECOVERY] L4 compressed {OriginalCount} middle messages → {CompressedChars} chars (~{TokensSaved} tokens saved), retrying...",
                middleMessages.Count, compressed.Length, tokensSaved);
            _options.CompressionState?.RecordCompression(tokensSaved);

            return await base.GetResponseAsync(compressedMessages, options, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "[RECOVERY] L4 compression+retry failed, falling back");
            return null;
        }
    }

    // ─── 處理方法 ───

    private async Task HandleContextOverflowAsync(Exception ex, CancellationToken ct)
    {
        logger?.LogWarning("[RECOVERY] Context overflow detected: {Message}", ex.Message);
        if (_options.OnContextOverflow is not null)
        {
            await _options.OnContextOverflow(ex, ct);
        }
    }

    private async Task HandleModelUnavailableAsync(Exception ex, string? modelId, CancellationToken ct)
    {
        logger?.LogWarning("[RECOVERY] Model unavailable ({Model}): {Message}", modelId ?? "unknown", ex.Message);
        if (_options.OnModelUnavailable is not null)
        {
            await _options.OnModelUnavailable(ex, modelId ?? "unknown", ct);
        }
    }

    // ─── Token 估算 ───

    private static long EstimateMessageTokens(IEnumerable<ChatMessage> messages)
    {
        long total = 0;
        foreach (var msg in messages)
        {
            total += Models.ModelPricing.EstimateTokens(msg.Text ?? "");
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fc)
                    total += Models.ModelPricing.EstimateTokens(fc.Arguments?.ToString() ?? "");
                else if (content is FunctionResultContent fr)
                    total += Models.ModelPricing.EstimateTokens(fr.Result?.ToString() ?? "");
            }
        }

        return total;
    }

    // ─── ChatOptions 複製 ───

    internal static ChatOptions CloneWithMaxTokens(ChatOptions? source, int newMax)
    {
        var clone = new ChatOptions
        {
            MaxOutputTokens = newMax,
            Temperature = source?.Temperature,
            TopP = source?.TopP,
            StopSequences = source?.StopSequences,
            ResponseFormat = source?.ResponseFormat,
            ModelId = source?.ModelId,
            Tools = source?.Tools,
            ToolMode = source?.ToolMode,
            FrequencyPenalty = source?.FrequencyPenalty,
            PresencePenalty = source?.PresencePenalty,
            Seed = source?.Seed,
            TopK = source?.TopK,
        };

        if (source?.AdditionalProperties is { Count: > 0 } props)
        {
            clone.AdditionalProperties = new AdditionalPropertiesDictionary(props);
        }

        return clone;
    }
}
