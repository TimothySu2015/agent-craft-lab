using System.Collections.Concurrent;
using AgentCraftLab.Autonomous.Models;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 共享狀態儲存 — 跨 Orchestrator 和 Sub-agent 的 key-value 資料交換。
/// 非持久化，隨執行結束消失。
/// </summary>
public sealed class SharedStateStore
{
    private readonly ConcurrentDictionary<string, SharedStateEntry> _state = new();

    /// <summary>初始化共享狀態（從 AutonomousRequest.SharedStateInit 注入）</summary>
    public void Initialize(Dictionary<string, string>? initial)
    {
        if (initial is null)
        {
            return;
        }

        foreach (var (key, value) in initial)
        {
            _state[key] = new SharedStateEntry
            {
                Key = key,
                Value = value,
                SetBy = "system"
            };
        }
    }

    /// <summary>
    /// 設定或更新共享狀態。
    /// 若 key 已存在且原始設定者為 "orchestrator"，則僅允許 "orchestrator" 覆寫，
    /// 防止 sub-agent 汙染 Orchestrator 設定的重要狀態。
    /// </summary>
    /// <returns>true 表示寫入成功；false 表示因權限不足而被忽略。</returns>
    public bool Set(string key, string value, string setBy)
    {
        // 檢查命名空間隔離：orchestrator 設定的 key 不允許被其他 agent 覆寫
        if (_state.TryGetValue(key, out var existing)
            && string.Equals(existing.SetBy, "orchestrator", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(setBy, "orchestrator", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _state[key] = new SharedStateEntry
        {
            Key = key,
            Value = value,
            SetBy = setBy
        };
        return true;
    }

    /// <summary>取得共享狀態（null = 不存在）</summary>
    public SharedStateEntry? Get(string key)
    {
        return _state.GetValueOrDefault(key);
    }

    /// <summary>列出所有共享狀態</summary>
    public IReadOnlyDictionary<string, SharedStateEntry> List()
    {
        return _state;
    }

    /// <summary>移除共享狀態</summary>
    public bool Remove(string key)
    {
        return _state.TryRemove(key, out _);
    }
}
