using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 預設預算策略 — 依 config 間隔注入預算提醒與事中自我檢查。
/// </summary>
public sealed class DefaultBudgetPolicy : IBudgetPolicy
{
    private const string AgentName = "Autonomous Agent";

    private readonly ReactExecutorConfig _config;

    public DefaultBudgetPolicy(ReactExecutorConfig? config = null)
    {
        _config = config ?? new ReactExecutorConfig();
    }

    /// <summary>
    /// 檢查 Token 預算和工具呼叫次數是否已耗盡。
    /// 回傳 null 表示可繼續；非 null 為要 yield 的錯誤事件。
    /// </summary>
    public ExecutionEvent? CheckBudget(TokenTracker tokenTracker, ToolCallTracker toolCallTracker)
    {
        // Token 預算檢查
        if (tokenTracker.ShouldStop)
        {
            return ExecutionEvent.TextChunk(AgentName,
                $"\n\n[Token budget exceeded: {tokenTracker.TotalTokensUsed} tokens used]");
        }

        // 工具呼叫次數檢查
        if (toolCallTracker.TotalRemaining <= 0)
        {
            return ExecutionEvent.TextChunk(AgentName,
                $"\n\n[Tool call limit reached: {toolCallTracker.TotalCalls} calls]");
        }

        return null;
    }

    /// <summary>
    /// 注入或就地更新 budget reminder 訊息（每 5 步或最後 3 步）。
    /// 使用 loopState.BudgetReminderIndex 追蹤插入位置，避免訊息累加。
    /// </summary>
    public void InjectBudgetReminder(
        List<ChatMessage> messages, ReactLoopState loopState,
        int iteration, int maxIterations,
        TokenTracker tokenTracker, ToolCallTracker toolCallTracker)
    {
        if (iteration % _config.BudgetReminderInterval != 0 && iteration < maxIterations - (_config.FinalStepsThreshold - 1))
        {
            return;
        }

        var budgetReminder = $"[Budget status: {tokenTracker.TotalTokensUsed} tokens used, " +
                             $"{toolCallTracker.TotalRemaining} tool calls remaining, " +
                             $"iteration {iteration}/{maxIterations}]";

        if (loopState.BudgetReminderIndex >= 0 && loopState.BudgetReminderIndex < messages.Count)
        {
            // 就地更新既有的 budget reminder 訊息
            messages[loopState.BudgetReminderIndex] = new ChatMessage(ChatRole.System, budgetReminder);
        }
        else
        {
            // 首次插入
            loopState.BudgetReminderIndex = messages.Count;
            messages.Add(new ChatMessage(ChatRole.System, budgetReminder));
        }
    }

    /// <summary>
    /// 事中自我檢查（每 8 步提醒 AI 評估方向，輕量注入不需額外 LLM 呼叫）。
    /// 智能觸發：進度不到一半且預算充裕時跳過，避免過度干擾 LLM。
    /// </summary>
    public void InjectMidExecutionCheck(List<ChatMessage> messages, int iteration, int maxIterations)
    {
        if (iteration <= 1 || iteration % _config.MidExecutionCheckInterval != 0)
        {
            return;
        }

        // 進度不到一半時跳過 — 還太早，不需要中斷 LLM 思考
        if (iteration < maxIterations / 2)
        {
            return;
        }

        messages.Add(new ChatMessage(ChatRole.User,
            $"[Mid-execution check] You are at step {iteration}/{maxIterations}. " +
            $"Before continuing, briefly assess: (1) Are you making progress toward the original goal? " +
            $"(2) Should you change strategy? (3) Is it time to synthesize and give a final answer? " +
            $"If you have enough information, provide your final answer now."));
    }
}
