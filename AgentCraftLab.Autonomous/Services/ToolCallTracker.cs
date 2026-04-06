using System.Collections.Concurrent;
using AgentCraftLab.Autonomous.Models;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 工具呼叫次數追蹤器 — 限制每個工具和總呼叫次數，防止濫用。
/// </summary>
public sealed class ToolCallTracker
{
    private readonly ToolCallLimits _limits;
    private readonly ConcurrentDictionary<string, int> _callCounts = new();
    private int _totalCalls;

    public ToolCallTracker(ToolCallLimits limits)
    {
        _limits = limits;
    }

    /// <summary>從 checkpoint 恢復呼叫次數。</summary>
    internal void Restore(Dictionary<string, int> callCounts, int totalCalls)
    {
        _callCounts.Clear();
        foreach (var (key, value) in callCounts)
        {
            _callCounts[key] = value;
        }

        Interlocked.Exchange(ref _totalCalls, totalCalls);
    }

    public int TotalCalls => _totalCalls;
    public IReadOnlyDictionary<string, int> CallCounts => _callCounts;

    /// <summary>
    /// 檢查指定工具是否還能呼叫。
    /// </summary>
    public bool CanCall(string toolId)
    {
        if (_totalCalls >= _limits.MaxTotalCalls)
        {
            return false;
        }

        var currentCount = _callCounts.GetValueOrDefault(toolId, 0);
        var limit = _limits.PerToolLimits.GetValueOrDefault(toolId, _limits.DefaultPerToolLimit);
        return currentCount < limit;
    }

    /// <summary>
    /// 記錄一次工具呼叫。回傳 false 表示已達上限（不應該呼叫）。
    /// </summary>
    public bool Record(string toolId)
    {
        if (!CanCall(toolId))
        {
            return false;
        }

        _callCounts.AddOrUpdate(toolId, 1, (_, count) => count + 1);
        Interlocked.Increment(ref _totalCalls);
        return true;
    }

    /// <summary>
    /// 取得指定工具的剩餘呼叫次數。
    /// </summary>
    public int Remaining(string toolId)
    {
        var currentCount = _callCounts.GetValueOrDefault(toolId, 0);
        var limit = _limits.PerToolLimits.GetValueOrDefault(toolId, _limits.DefaultPerToolLimit);
        return Math.Max(0, limit - currentCount);
    }

    /// <summary>
    /// 取得總剩餘呼叫次數。
    /// </summary>
    public int TotalRemaining => Math.Max(0, _limits.MaxTotalCalls - _totalCalls);

    /// <summary>
    /// 產生目前使用狀況摘要（供 AI system prompt 使用）。
    /// </summary>
    public string GetUsageSummary()
    {
        if (_totalCalls == 0)
        {
            return $"Tool call budget: {_limits.MaxTotalCalls} total calls remaining.";
        }

        var lines = new List<string>
        {
            $"Tool calls used: {_totalCalls}/{_limits.MaxTotalCalls}"
        };

        foreach (var (toolId, count) in _callCounts.OrderByDescending(x => x.Value))
        {
            var limit = _limits.PerToolLimits.GetValueOrDefault(toolId, _limits.DefaultPerToolLimit);
            lines.Add($"  - {toolId}: {count}/{limit}");
        }

        return string.Join('\n', lines);
    }
}
