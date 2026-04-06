using System.Collections.Concurrent;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Api;

/// <summary>
/// Singleton 註冊表，讓 Debug Action Submit 端點能跨 HTTP 請求找到正在等待的 DebugBridge。
/// 與 HumanInputBridgeRegistry 同 pattern。
/// </summary>
public sealed class DebugBridgeRegistry
{
    private readonly ConcurrentDictionary<string, DebugBridge> _bridges = new();

    private static string Key(string threadId, string runId) => $"{threadId}:{runId}";

    public void Register(string threadId, string runId, DebugBridge bridge)
    {
        _bridges[Key(threadId, runId)] = bridge;
    }

    public void Unregister(string threadId, string runId)
    {
        _bridges.TryRemove(Key(threadId, runId), out _);
    }

    /// <summary>
    /// 提交 debug action。支援空 threadId/runId（自動找任一等待中的 bridge）。
    /// </summary>
    public bool SubmitAction(string threadId, string runId, DebugAction action)
    {
        if (string.IsNullOrEmpty(threadId) || string.IsNullOrEmpty(runId))
        {
            foreach (var kvp in _bridges)
            {
                if (kvp.Value.IsWaiting)
                {
                    kvp.Value.SubmitAction(action);
                    return true;
                }
            }
            return false;
        }

        if (_bridges.TryGetValue(Key(threadId, runId), out var bridge) && bridge.IsWaiting)
        {
            bridge.SubmitAction(action);
            return true;
        }
        return false;
    }
}
