using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services.Compression;

/// <summary>
/// 對話訊息去重 + 合併（零 LLM 成本）。
/// 策略：移除冗餘 Tool 結果 → 合併連續同角色短訊息。
/// </summary>
public static class MessageDeduplicator
{
    /// <summary>
    /// 組合執行：去重 → 合併 → 回傳是否達到目標數量。
    /// </summary>
    public static bool TryCompress(List<ChatMessage> messages, int targetCount = 12, int shortMessageThreshold = 100)
    {
        if (messages.Count <= targetCount)
        {
            return true;
        }

        RemoveRedundantToolResults(messages);
        if (messages.Count <= targetCount)
        {
            return true;
        }

        MergeConsecutiveMessages(messages, shortMessageThreshold);
        return messages.Count <= targetCount;
    }

    /// <summary>
    /// 移除重複 tool results：同一工具以相同參數被呼叫多次時，只保留最後一次的結果。
    /// 使用 (工具名稱, 序列化參數) 作為去重鍵。保留所有 FunctionCallContent。
    /// </summary>
    public static void RemoveRedundantToolResults(List<ChatMessage> messages)
    {
        // 建立 CallId → (工具名稱, 序列化參數) 的對照表
        var callIdToKey = new Dictionary<string, string>();
        foreach (var msg in messages)
        {
            foreach (var call in msg.Contents.OfType<FunctionCallContent>())
            {
                var callId = call.CallId ?? call.Name;
                var argsKey = call.Arguments is not null
                    ? JsonSerializer.Serialize(call.Arguments)
                    : "";
                callIdToKey[callId] = $"{call.Name}:{argsKey}";
            }
        }

        // 從後往前掃描，記錄已見過的去重鍵
        var seenKeys = new HashSet<string>();
        var toRemove = new List<int>();

        for (var i = messages.Count - 1; i >= 1; i--) // 跳過 index 0（system prompt）
        {
            var msg = messages[i];
            if (msg.Role != ChatRole.Tool)
            {
                continue;
            }

            var resultContents = msg.Contents.OfType<FunctionResultContent>().ToList();
            if (resultContents.Count == 0)
            {
                continue;
            }

            var callId = resultContents[0].CallId ?? "unknown";
            var dedupeKey = callIdToKey.GetValueOrDefault(callId, callId);

            if (!seenKeys.Add(dedupeKey))
            {
                toRemove.Add(i);
            }
        }

        // 移除標記的訊息（從後往前移除避免 index 偏移）
        foreach (var idx in toRemove.OrderByDescending(x => x))
        {
            if (idx > 0 && idx < messages.Count)
            {
                messages.RemoveAt(idx);
            }
        }
    }

    /// <summary>
    /// 合併連續同角色的短訊息。
    /// 跳過包含 FunctionCallContent 或 FunctionResultContent 的訊息。
    /// </summary>
    public static void MergeConsecutiveMessages(List<ChatMessage> messages, int shortMessageThreshold = 100)
    {
        var i = 1; // 跳過 system prompt
        while (i < messages.Count - 1)
        {
            var current = messages[i];
            var next = messages[i + 1];

            if (current.Role == next.Role &&
                current.Contents.All(c => c is TextContent) &&
                next.Contents.All(c => c is TextContent) &&
                (current.Text?.Length ?? 0) < shortMessageThreshold &&
                (next.Text?.Length ?? 0) < shortMessageThreshold)
            {
                var merged = $"{current.Text}\n{next.Text}";
                messages[i] = new ChatMessage(current.Role, merged);
                messages.RemoveAt(i + 1);
            }
            else
            {
                i++;
            }
        }
    }
}
