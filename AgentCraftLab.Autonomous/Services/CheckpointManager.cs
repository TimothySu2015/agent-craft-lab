using System.Text.Json;
using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 檢查點管理器 — 負責快照擷取、序列化、持久化、載入與狀態恢復。
/// 從 ReactExecutor 委派所有檢查點邏輯，保持 ReactExecutor 精簡。
/// </summary>
public sealed class CheckpointManager
{
    private readonly ICheckpointStore _store;
    private readonly ReactExecutorConfig _config;
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public CheckpointManager(
        ICheckpointStore store,
        ReactExecutorConfig config,
        ILogger logger)
    {
        _store = store;
        _config = config;
        _logger = logger;
    }

    /// <summary>判斷此迭代是否應儲存檢查點（iteration 或 tool call 數任一達門檻）。</summary>
    public bool ShouldSave(int iteration, int totalToolCalls = 0)
    {
        if (!_config.CheckpointEnabled)
        {
            return false;
        }

        // iteration 門檻
        if (iteration % _config.CheckpointInterval == 0)
        {
            return true;
        }

        // tool call 門檻（FunctionInvokingChatClient 合併多 tool call 為 1 iteration 時仍能觸發）
        return totalToolCalls > 0 && totalToolCalls % (_config.CheckpointInterval * 2) == 0;
    }

    /// <summary>
    /// 擷取當前 ReAct 迴圈的完整狀態快照。
    /// </summary>
    internal CheckpointSnapshot CaptureSnapshot(
        int iteration,
        List<ChatMessage> messages,
        List<ReactStep> steps,
        TokenTracker tokenTracker,
        ToolCallTracker toolCallTracker,
        ConvergenceDetector convergenceDetector,
        SharedStateStore sharedState,
        AgentPool agentPool,
        ReactLoopState loopState,
        string? finalAnswer,
        bool succeeded,
        long cachedMessageChars,
        string? plan,
        DynamicToolSet? dynamicToolSet,
        List<ExecutionEvent> toolCallEvents)
    {
        return new CheckpointSnapshot
        {
            Iteration = iteration,
            Messages = SerializableChatMessage.FromList(messages),
            Steps = [.. steps],
            InputTokensUsed = tokenTracker.InputTokensUsed,
            OutputTokensUsed = tokenTracker.OutputTokensUsed,
            ToolCallCounts = new Dictionary<string, int>(toolCallTracker.CallCounts),
            TotalToolCalls = toolCallTracker.TotalCalls,
            ConvergenceToolHistory = convergenceDetector.GetToolHistorySnapshot(),
            ConvergenceResponseLengths = convergenceDetector.GetResponseLengthsSnapshot(),
            SharedState = CaptureSharedState(sharedState),
            SubAgents = CaptureSubAgents(agentPool),
            BudgetReminderIndex = loopState.BudgetReminderIndex,
            AskUserCount = loopState.AskUserCount,
            FinalAnswer = finalAnswer,
            Succeeded = succeeded,
            CachedMessageChars = cachedMessageChars,
            Plan = plan,
            LoadedDynamicToolNames = dynamicToolSet?.LoadedCount > 0
                ? [.. dynamicToolSet.GetLoadedNames()]
                : null,
            ToolCallEvents = toolCallEvents
                .Where(e => e.Type == Engine.Models.EventTypes.ToolCall)
                .Select(e => new ToolCallEventSnapshot(e.AgentName, e.Text))
                .ToList()
        };
    }

    /// <summary>序列化快照並儲存到持久層。</summary>
    public async Task SaveAsync(string executionId, CheckpointSnapshot snapshot, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            var doc = new CheckpointDocument
            {
                Id = $"ckpt-{executionId}-{snapshot.Iteration}",
                ExecutionId = executionId,
                Iteration = snapshot.Iteration,
                MessageCount = snapshot.Messages.Count,
                TokensUsed = snapshot.InputTokensUsed + snapshot.OutputTokensUsed,
                StateJson = json,
                StateSizeBytes = System.Text.Encoding.UTF8.GetByteCount(json),
                CreatedAt = DateTime.UtcNow
            };

            await _store.SaveAsync(doc);
            _logger.LogDebug("已儲存檢查點: execution={ExecutionId}, iteration={Iteration}, size={Size}KB",
                executionId, snapshot.Iteration, doc.StateSizeBytes / 1024);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "儲存檢查點失敗: execution={ExecutionId}, iteration={Iteration}",
                executionId, snapshot.Iteration);
        }
    }

    /// <summary>載入指定 iteration 的快照（null = 該 checkpoint 不存在）。</summary>
    public async Task<CheckpointSnapshot?> LoadAsync(string executionId, int iteration, CancellationToken ct)
    {
        var doc = await _store.GetAsync(executionId, iteration);
        return doc is not null ? Deserialize(doc) : null;
    }

    /// <summary>載入最新的快照。</summary>
    public async Task<CheckpointSnapshot?> LoadLatestAsync(string executionId, CancellationToken ct)
    {
        var doc = await _store.GetLatestAsync(executionId);
        return doc is not null ? Deserialize(doc) : null;
    }

    /// <summary>列出所有檢查點（不含 StateJson，僅 metadata）。</summary>
    public async Task<List<CheckpointInfo>> ListAsync(string executionId, CancellationToken ct)
    {
        var docs = await _store.ListMetadataAsync(executionId);
        return docs.Select(d => new CheckpointInfo(
            d.ExecutionId, d.Iteration, d.MessageCount, d.TokensUsed, d.CreatedAt)).ToList();
    }

    /// <summary>
    /// 從快照恢復 ReAct 迴圈的可變狀態。
    /// 不可序列化的狀態（IChatClient, AITool, SemaphoreSlim）由呼叫者重建。
    /// </summary>
    internal void RestoreState(
        CheckpointSnapshot snapshot,
        List<ChatMessage> messages,
        List<ReactStep> steps,
        TokenTracker tokenTracker,
        ToolCallTracker toolCallTracker,
        ConvergenceDetector convergenceDetector,
        SharedStateStore sharedState,
        ReactLoopState loopState,
        List<ExecutionEvent> toolCallEvents)
    {
        // 對話歷史
        messages.Clear();
        messages.AddRange(SerializableChatMessage.ToList(snapshot.Messages));

        // 執行步驟
        steps.Clear();
        steps.AddRange(snapshot.Steps);

        // Token 追蹤
        tokenTracker.Restore(snapshot.InputTokensUsed, snapshot.OutputTokensUsed);

        // 工具呼叫追蹤
        toolCallTracker.Restore(snapshot.ToolCallCounts, snapshot.TotalToolCalls);

        // 收斂偵測
        convergenceDetector.RestoreFromSnapshot(
            snapshot.ConvergenceToolHistory, snapshot.ConvergenceResponseLengths);

        // 共享狀態
        foreach (var (key, entry) in snapshot.SharedState)
        {
            sharedState.Set(entry.Key, entry.Value, entry.SetBy);
        }

        // 迴圈狀態
        loopState.BudgetReminderIndex = snapshot.BudgetReminderIndex;
        loopState.AskUserCount = snapshot.AskUserCount;

        // ToolCall 事件（軌跡轉換用）
        toolCallEvents.Clear();
        foreach (var evt in snapshot.ToolCallEvents)
        {
            toolCallEvents.Add(ExecutionEvent.ToolCall(evt.AgentName, evt.Text, ""));
        }

        _logger.LogInformation(
            "已從檢查點恢復: iteration={Iteration}, messages={Messages}, tokens={Tokens}",
            snapshot.Iteration, snapshot.Messages.Count,
            snapshot.InputTokensUsed + snapshot.OutputTokensUsed);
    }

    /// <summary>清除指定執行的所有檢查點。</summary>
    public async Task CleanupAsync(string executionId, CancellationToken ct)
    {
        await _store.CleanupAsync(executionId);
    }

    /// <summary>清除過期檢查點。</summary>
    public async Task CleanupExpiredAsync(CancellationToken ct)
    {
        await _store.CleanupOlderThanAsync(TimeSpan.FromHours(_config.CheckpointRetentionHours));
    }

    private CheckpointSnapshot? Deserialize(CheckpointDocument doc)
    {
        try
        {
            return JsonSerializer.Deserialize<CheckpointSnapshot>(doc.StateJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "反序列化檢查點失敗: {Id}", doc.Id);
            return null;
        }
    }

    private static Dictionary<string, SharedStateSnapshot> CaptureSharedState(SharedStateStore store)
    {
        var result = new Dictionary<string, SharedStateSnapshot>();
        foreach (var (key, entry) in store.List())
        {
            result[key] = new SharedStateSnapshot(entry.Key, entry.Value, entry.SetBy, entry.UpdatedAt);
        }

        return result;
    }

    private static Dictionary<string, SubAgentSnapshot> CaptureSubAgents(AgentPool pool)
    {
        var result = new Dictionary<string, SubAgentSnapshot>();
        foreach (var (name, entry) in pool.GetPersistentAgentsSnapshot())
        {
            result[name] = new SubAgentSnapshot
            {
                Name = entry.Name,
                Instructions = entry.Instructions,
                ToolIds = entry.Tools.OfType<AIFunction>().Select(f => f.Name).ToList(),
                History = SerializableChatMessage.FromList(entry.History),
                CallCount = entry.CallCount
            };
        }

        return result;
    }
}

/// <summary>檢查點摘要資訊（不含完整 StateJson）。</summary>
public sealed record CheckpointInfo(
    string ExecutionId, int Iteration, int MessageCount, long TokensUsed, DateTime SavedAt);
