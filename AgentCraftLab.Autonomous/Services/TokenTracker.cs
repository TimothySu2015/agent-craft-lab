using AgentCraftLab.Autonomous.Models;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// Token 預算追蹤器 — 累計每次 LLM 呼叫的 token 消耗，超過預算時觸發對應行為。
/// </summary>
public sealed class TokenTracker
{
    private readonly TokenBudget _budget;
    private long _inputTokens;
    private long _outputTokens;

    public TokenTracker(TokenBudget budget)
    {
        _budget = budget;
    }

    /// <summary>從 checkpoint 恢復累計 token 數。</summary>
    internal void Restore(long inputTokens, long outputTokens)
    {
        Interlocked.Exchange(ref _inputTokens, inputTokens);
        Interlocked.Exchange(ref _outputTokens, outputTokens);
    }

    public long InputTokensUsed => _inputTokens;
    public long OutputTokensUsed => _outputTokens;
    public long TotalTokensUsed => _inputTokens + _outputTokens;
    public TokenUsage CurrentUsage => new() { InputTokens = _inputTokens, OutputTokens = _outputTokens };

    /// <summary>
    /// 記錄一次 LLM 呼叫的 token 消耗。
    /// </summary>
    /// <returns>是否超過預算</returns>
    public bool Record(long inputTokens, long outputTokens)
    {
        Interlocked.Add(ref _inputTokens, inputTokens);
        Interlocked.Add(ref _outputTokens, outputTokens);
        return IsExceeded;
    }

    /// <summary>
    /// 檢查是否已超過任一預算限制。
    /// </summary>
    public bool IsExceeded =>
        (_budget.MaxTotalTokens > 0 && TotalTokensUsed >= _budget.MaxTotalTokens) ||
        (_budget.MaxInputTokens > 0 && _inputTokens >= _budget.MaxInputTokens) ||
        (_budget.MaxOutputTokens > 0 && _outputTokens >= _budget.MaxOutputTokens);

    /// <summary>
    /// 超過預算時應該停止還是僅警告。
    /// </summary>
    public bool ShouldStop => IsExceeded && _budget.OnExceed == BudgetExceededAction.Stop;

    /// <summary>
    /// 取得預算使用百分比（0-100+）。
    /// </summary>
    public int UsagePercent => _budget.MaxTotalTokens > 0
        ? (int)(TotalTokensUsed * 100L / _budget.MaxTotalTokens)
        : 0;

    /// <summary>
    /// 取得剩餘可用 token 數（-1 表示無限制）。
    /// </summary>
    public long Remaining => _budget.MaxTotalTokens > 0
        ? Math.Max(0, _budget.MaxTotalTokens - TotalTokensUsed)
        : -1;
}
