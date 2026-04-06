using System.Collections.Concurrent;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Api;

/// <summary>
/// Singleton 註冊表，讓 Human Input Submit 端點能跨 HTTP 請求找到正在等待的 HumanInputBridge。
/// 以 "threadId:runId" 為 key，SSE 串流開始時 Register，結束時 Unregister。
/// </summary>
public sealed class HumanInputBridgeRegistry
{
    private readonly ConcurrentDictionary<string, HumanInputBridge> _bridges = new();
    private readonly ConcurrentDictionary<string, PendingInputInfo> _pendingInputs = new();

    private static string Key(string threadId, string runId) => $"{threadId}:{runId}";

    /// <summary>
    /// 註冊 bridge（SSE 串流開始時呼叫）。
    /// </summary>
    public void Register(string threadId, string runId, HumanInputBridge bridge)
    {
        _bridges[Key(threadId, runId)] = bridge;
    }

    /// <summary>
    /// 移除 bridge（SSE 串流結束時呼叫）。
    /// </summary>
    public void Unregister(string threadId, string runId)
    {
        var key = Key(threadId, runId);
        _bridges.TryRemove(key, out _);
        _pendingInputs.TryRemove(key, out _);
    }

    /// <summary>
    /// 標記某個 session 正在等待人類輸入（由 AG-UI 串流在遇到 WaitingForInput 事件時呼叫）。
    /// </summary>
    public void SetPending(string threadId, string runId, string prompt, string inputType, string? choices)
    {
        _pendingInputs[Key(threadId, runId)] = new PendingInputInfo
        {
            ThreadId = threadId,
            RunId = runId,
            Prompt = prompt,
            InputType = inputType,
            Choices = choices
        };
    }

    /// <summary>
    /// 取得任一等待中的 human input（前端輪詢用）。
    /// </summary>
    public PendingInputInfo? GetAnyPending() =>
        _pendingInputs.Values.FirstOrDefault();

    /// <summary>
    /// 提交使用者輸入。支援指定 threadId/runId 或空字串（自動找任一等待中的 bridge）。
    /// 回傳 true 表示成功恢復等待中的 Human 節點。
    /// </summary>
    public bool SubmitInput(string threadId, string runId, string response)
    {
        // 空字串 = 找任一等待中的 bridge（前端透過 AG-UI state 知道有 pending，但不知道 key）
        if (string.IsNullOrEmpty(threadId) || string.IsNullOrEmpty(runId))
        {
            return SubmitAnyPending(response);
        }

        var key = Key(threadId, runId);
        _pendingInputs.TryRemove(key, out _);
        if (_bridges.TryGetValue(key, out var bridge) && bridge.IsWaiting)
        {
            bridge.SubmitInput(response);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 找任一等待中的 bridge 並提交。
    /// 安全性：HumanInputBridge.SubmitInput 內部用 TrySetResult，天然 idempotent。
    /// 即使兩個並發請求同時進入，只有第一個會成功 set result，第二個 TrySetResult 回傳 false。
    /// </summary>
    private bool SubmitAnyPending(string response)
    {
        foreach (var kvp in _bridges)
        {
            if (kvp.Value.IsWaiting)
            {
                _pendingInputs.TryRemove(kvp.Key, out _);
                kvp.Value.SubmitInput(response);
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// 等待中的 Human Input 資訊。
/// </summary>
public sealed class PendingInputInfo
{
    public string ThreadId { get; init; } = "";
    public string RunId { get; init; } = "";
    public string Prompt { get; init; } = "";
    public string InputType { get; init; } = "text";
    public string? Choices { get; init; }
}
