using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services.Compression;

/// <summary>
/// 截斷超長 tool results（零 LLM 成本）。
/// 優先在換行邊界截斷，避免破壞 JSON/表格結構。
/// </summary>
public static class ToolResultTruncator
{
    /// <summary>
    /// 截斷訊息清單中超過 maxLength 的 FunctionResultContent。
    /// 跳過 index 0（system prompt）。回傳被截斷的字元數。
    /// 可選 CompressionState 記錄已截斷的 tool call IDs（避免重複截斷）。
    /// </summary>
    public static long Truncate(List<ChatMessage> messages, int maxLength = 1500, CompressionState? state = null)
    {
        long charsSaved = 0;

        for (var i = 1; i < messages.Count; i++) // 跳過 system prompt
        {
            var msg = messages[i];
            if (msg.Role != ChatRole.Tool)
            {
                continue;
            }

            foreach (var content in msg.Contents.OfType<FunctionResultContent>())
            {
                var callId = content.CallId ?? "";

                // 已截斷過的 tool result 跳過（透過 CompressionState 追蹤）
                if (state is not null && state.TruncatedToolCallIds.Contains(callId))
                {
                    continue;
                }

                var text = content.Result?.ToString();
                if (text is null || text.Length <= maxLength)
                {
                    continue;
                }

                // 優先在換行邊界截斷，避免破壞 JSON/表格結構
                var cutPoint = text.LastIndexOf('\n', maxLength);
                if (cutPoint < maxLength / 2)
                {
                    cutPoint = maxLength;
                }

                var truncated = text[..cutPoint] + $"\n... [{text.Length - cutPoint:N0} chars truncated]";
                charsSaved += text.Length - truncated.Length;

                // 記錄已截斷的 tool call ID
                if (state is not null && !string.IsNullOrEmpty(callId))
                {
                    state.TruncatedToolCallIds.Add(callId);
                }

                // 替換 FunctionResultContent（immutable，需重建訊息）
                var newContents = msg.Contents.Select(c =>
                    c == content
                        ? new FunctionResultContent(content.CallId ?? "", truncated)
                        : c).ToList();
                messages[i] = new ChatMessage(msg.Role, newContents);
                break; // 每個 Tool message 通常只有一個 FunctionResultContent
            }
        }

        return charsSaved;
    }
}
