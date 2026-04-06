using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 預算策略 — Token/ToolCall 預算檢查與 Budget Reminder 注入。
/// </summary>
public interface IBudgetPolicy
{
    /// <summary>檢查預算是否已耗盡。回傳 null 可繼續；非 null 為要 yield 的錯誤事件。</summary>
    ExecutionEvent? CheckBudget(TokenTracker tokenTracker, ToolCallTracker toolCallTracker);

    /// <summary>注入或更新 budget reminder 訊息。</summary>
    void InjectBudgetReminder(
        List<ChatMessage> messages, ReactLoopState loopState,
        int iteration, int maxIterations,
        TokenTracker tokenTracker, ToolCallTracker toolCallTracker);

    /// <summary>注入事中自我檢查提示（每 N 步）。</summary>
    void InjectMidExecutionCheck(List<ChatMessage> messages, int iteration, int maxIterations);
}

/// <summary>
/// 可變的 ReAct 迴圈狀態，跨策略物件共享。
/// </summary>
public sealed class ReactLoopState
{
    public int BudgetReminderIndex { get; set; } = -1;
    public int AskUserCount { get; set; }
}
