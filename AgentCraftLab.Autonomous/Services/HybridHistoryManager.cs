using System.Text.Json;
using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Services.Compression;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 混合歷史管理器 — 三層壓縮策略（參考 Claude Code 的 context compaction 機制）：
///   Layer 1：截斷超長工具結果（零 token 成本）
///   Layer 2：本地壓縮（去重 + 合併短訊息）
///   Layer 3：LLM 摘要（fallback，保留近期訊息，舊訊息壓成摘要）
/// </summary>
public sealed class HybridHistoryManager : IHistoryManager
{
    private const string AgentName = "Autonomous Agent";

    private readonly ReactExecutorConfig _config;
    private readonly ILogger<HybridHistoryManager> _logger;

    /// <summary>動態 token 門檻（字元數），由 SetModel 計算。</summary>
    private long _tokenThresholdChars;

    public HybridHistoryManager(ILogger<HybridHistoryManager> logger, ReactExecutorConfig? config = null)
    {
        _logger = logger;
        _config = config ?? new ReactExecutorConfig();
        _tokenThresholdChars = _config.HistoryCompressionTokenThreshold * _config.CharsPerTokenEstimate;
    }

    /// <summary>訊息數門檻。</summary>
    public int Threshold => _config.HistoryCompressionThreshold;

    /// <summary>設定模型名稱，動態計算壓縮門檻。</summary>
    public void SetModel(string modelName)
    {
        var thresholdTokens = ModelContextWindows.GetCompressionThreshold(
            modelName, _config.HistoryCompressionTokenThreshold);
        _tokenThresholdChars = thresholdTokens * _config.CharsPerTokenEstimate;

        var contextWindow = ModelContextWindows.GetContextWindow(modelName);
        if (contextWindow.HasValue)
        {
            _logger.LogInformation(
                "Context window for {Model}: {Window:N0} tokens, compression threshold: {Threshold:N0} tokens ({Ratio:P0})",
                modelName, contextWindow.Value, thresholdTokens, ModelContextWindows.CompressionRatio);
        }
        else
        {
            _logger.LogInformation(
                "Unknown model {Model}, using fallback compression threshold: {Threshold:N0} tokens",
                modelName, thresholdTokens);
        }
    }

    /// <summary>判斷是否需要壓縮。</summary>
    public bool ShouldCompress(List<ChatMessage> messages, long cachedMessageChars)
    {
        return cachedMessageChars > _tokenThresholdChars || messages.Count > Threshold;
    }

    /// <summary>
    /// 三層壓縮：截斷工具結果 → 本地壓縮 → LLM 摘要。
    /// </summary>
    public async Task<HistoryCompressionResult> CompressIfNeededAsync(
        List<ChatMessage> messages,
        IChatClient rawClient,
        TokenTracker tokenTracker,
        CancellationToken ct,
        CompressionState? compressionState = null)
    {
        var events = new List<ExecutionEvent>();

        // Layer 1：截斷超長工具結果（零 token 成本，最先執行）
        var charsSaved = HistoryCompressor.TruncateLongToolResults(messages, _config.ToolResultTruncateLength, compressionState);
        if (charsSaved > 0)
        {
            var tokensSaved = charsSaved / _config.CharsPerTokenEstimate;
            compressionState?.RecordCompression(tokensSaved);
            _logger.LogInformation("Layer 1: Truncated tool results, saved {Chars:N0} chars (~{Tokens} tokens)", charsSaved, tokensSaved);
            events.Add(ExecutionEvent.TextChunk(AgentName,
                $"\n[Context compaction: truncated tool results, saved {charsSaved:N0} chars]\n"));

            // 截斷後可能已經不需要進一步壓縮
            var currentChars = messages.Sum(m => (long)(m.Text?.Length ?? 0));
            if (currentChars <= _tokenThresholdChars && messages.Count <= Threshold)
            {
                return new HistoryCompressionResult(true, events, ShouldResetBudgetReminderIndex: false);
            }
        }

        // Layer 2：本地壓縮（去重 + 合併短訊息）
        var countBefore = messages.Count;
        if (HistoryCompressor.TryLocalCompress(messages, _config.HistoryLocalTargetCount, _config.HistoryShortMessageThreshold))
        {
            var messagesRemoved = countBefore - messages.Count;
            compressionState?.RecordCompression(messagesRemoved * 50); // 預估每條訊息 ~50 tokens
            _logger.LogInformation(
                "Layer 2: Local compression, {Count} messages remaining ({Removed} removed)", messages.Count, messagesRemoved);

            events.Add(ExecutionEvent.TextChunk(AgentName,
                $"\n[Context compaction: local compression, {messages.Count} messages remaining]\n"));

            return new HistoryCompressionResult(true, events, ShouldResetBudgetReminderIndex: true);
        }

        // Layer 3：LLM 摘要（保留近期訊息，舊訊息壓成摘要）
        var system = messages[0];
        var recentStart = Math.Max(1, messages.Count - _config.HistoryRecentMessageCount);
        var oldMessages = messages.Skip(1).Take(recentStart - 1).ToList();
        var recentMessages = messages.Skip(recentStart).ToList();

        var compressionResult = await CompressHistoryAsync(rawClient, oldMessages, ct);
        tokenTracker.Record(compressionResult.InputTokens, compressionResult.OutputTokens);
        compressionState?.RecordCompression(compressionResult.InputTokens + compressionResult.OutputTokens);

        messages.Clear();
        messages.Add(system);
        messages.Add(new ChatMessage(ChatRole.System,
            $"[Compressed history of previous {oldMessages.Count} messages]\n{compressionResult.Text}"));
        messages.AddRange(recentMessages);

        _logger.LogInformation("Layer 3: LLM summary, {OldCount} messages → {Chars} chars ({Tokens} tokens)",
            oldMessages.Count, compressionResult.Text.Length,
            compressionResult.InputTokens + compressionResult.OutputTokens);

        events.Add(ExecutionEvent.TextChunk(AgentName,
            $"\n[Context compaction: LLM summary, {oldMessages.Count} messages → {compressionResult.Text.Length} chars, " +
            $"{compressionResult.InputTokens + compressionResult.OutputTokens} tokens]\n"));

        return new HistoryCompressionResult(true, events, ShouldResetBudgetReminderIndex: true);
    }

    private record CompressionResult(string Text, long InputTokens, long OutputTokens);

    private static async Task<CompressionResult> CompressHistoryAsync(
        IChatClient client,
        List<ChatMessage> oldMessages,
        CancellationToken cancellationToken)
    {
        var historyText = string.Join('\n', oldMessages.Select(m =>
        {
            var role = m.Role.Value;
            var text = m.Text ?? "";

            foreach (var content in m.Contents)
            {
                if (content is FunctionCallContent call)
                {
                    text = $"[Called {call.Name}({Truncate(JsonSerializer.Serialize(call.Arguments), 100)})]";
                }
                else if (content is FunctionResultContent result)
                {
                    text = $"[Result: {Truncate(result.Result?.ToString() ?? "", 150)}]";
                }
            }

            if (text.Length > 200)
            {
                text = text[..200] + "...";
            }

            return $"{role}: {text}";
        }));

        var compressionPrompt = new List<ChatMessage>
        {
            new(ChatRole.System,
                """
                Summarize the following conversation history into a concise progress report.
                Focus on: (1) what tools were called and key results, (2) what sub-agents were created,
                (3) key findings and data collected, (4) what decisions were made.
                Use bullet points. Be brief but preserve all important facts and numbers.
                Output in the same language as the conversation.
                """),
            new(ChatRole.User, historyText)
        };

        try
        {
            var response = await client.GetResponseAsync(compressionPrompt, cancellationToken: cancellationToken);
            return new CompressionResult(
                response.Text ?? "[compression failed]",
                response.Usage?.InputTokenCount ?? 0,
                response.Usage?.OutputTokenCount ?? 0);
        }
        catch
        {
            return new CompressionResult(
                $"[Summary unavailable. {oldMessages.Count} earlier messages were removed.]", 0, 0);
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "... [truncated]";
    }
}
