using AgentCraftLab.Engine.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 平行 GuardRails 評估器 — 在 ReAct 迴圈中平行執行 guardrails 掃描與 LLM 呼叫。
/// 第一輪（iteration=1）序列執行（必須先擋住惡意輸入）；
/// 後續輪次平行執行 — guardrails 觸發 Block 時透過 CancellationToken 取消 LLM 呼叫。
/// </summary>
public sealed class ParallelGuardRailsEvaluator
{
    private readonly IGuardRailsPolicy _policy;
    private readonly ILogger _logger;

    public ParallelGuardRailsEvaluator(
        IGuardRailsPolicy policy,
        ILogger logger)
    {
        _policy = policy;
        _logger = logger;
    }

    /// <summary>
    /// 平行執行 guardrails input scan + LLM 呼叫。
    /// </summary>
    /// <param name="client">LLM client（已包裝 FunctionInvokingChatClient）。</param>
    /// <param name="messages">對話歷史。</param>
    /// <param name="chatOptions">LLM 選項（含工具清單）。</param>
    /// <param name="iteration">ReAct 迴圈迭代數（1=首次序列，>1=平行）。</param>
    /// <param name="outerCt">外部取消 token。</param>
    /// <returns>LLM 回應或 Block 結果。</returns>
    public async Task<ParallelScanResult> ExecuteWithGuardRailsAsync(
        IChatClient client,
        List<ChatMessage> messages,
        ChatOptions chatOptions,
        int iteration,
        CancellationToken outerCt)
    {
        // 取最後一則 User 訊息用於 guardrails 掃描
        var lastUserText = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;

        // 第一輪或沒有 User 訊息：序列執行（先掃再 LLM）
        if (iteration <= 1 || string.IsNullOrEmpty(lastUserText))
        {
            return await ExecuteSequentialAsync(client, messages, chatOptions, lastUserText, outerCt);
        }

        // 後續輪次：平行執行
        return await ExecuteParallelAsync(client, messages, chatOptions, lastUserText, outerCt);
    }

    private async Task<ParallelScanResult> ExecuteSequentialAsync(
        IChatClient client,
        List<ChatMessage> messages,
        ChatOptions chatOptions,
        string? lastUserText,
        CancellationToken ct)
    {
        // 先掃描 input
        if (lastUserText is not null)
        {
            var matches = _policy.Evaluate(lastUserText, GuardRailsDirection.Input);
            foreach (var match in matches)
            {
                if (match.Rule.Action == GuardRailsAction.Block)
                {
                    _logger.LogWarning("[ParallelGuard] Input blocked: {Rule}", match.Rule.Pattern);
                    return new ParallelScanResult(null, match, false);
                }
            }
        }

        // 通過後呼叫 LLM
        var response = await client.GetResponseAsync(messages, chatOptions, ct);
        return new ParallelScanResult(response, null, false);
    }

    private async Task<ParallelScanResult> ExecuteParallelAsync(
        IChatClient client,
        List<ChatMessage> messages,
        ChatOptions chatOptions,
        string lastUserText,
        CancellationToken outerCt)
    {
        // 建立可被 guardrails 取消的 linked CTS
        using var guardCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);

        // Guardrails scan task — 回傳 Block match（null = 通過）
        var scanTask = Task.Run<GuardRailsMatch?>(() =>
        {
            var matches = _policy.Evaluate(lastUserText, GuardRailsDirection.Input);
            foreach (var match in matches)
            {
                if (match.Rule.Action == GuardRailsAction.Block)
                {
                    _logger.LogWarning("[ParallelGuard] Input blocked (parallel): {Rule}", match.Rule.Pattern);
                    guardCts.Cancel();
                    return match;
                }
            }

            return null;
        }, outerCt);

        // LLM call task（使用 guardCts，可被 guardrails 取消）
        var llmTask = client.GetResponseAsync(messages, chatOptions, guardCts.Token);

        try
        {
            await Task.WhenAll(scanTask, llmTask);
            return new ParallelScanResult(llmTask.Result, scanTask.Result, false);
        }
        catch (OperationCanceledException) when (!outerCt.IsCancellationRequested)
        {
            // Guardrails 取消了 LLM（不是使用者取消）
            await scanTask; // 確保 scan 完成
            return new ParallelScanResult(null, scanTask.Result, true);
        }
    }
}

/// <summary>平行掃描結果。</summary>
public sealed record ParallelScanResult(
    ChatResponse? Response,
    GuardRailsMatch? BlockedMatch,
    bool WasCancelledByGuardRails);
