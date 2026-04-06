using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services.Compression;

/// <summary>
/// ChatMessage 序列化工具 — 將訊息轉為可壓縮的文字，或將壓縮摘要包裝回 ChatMessage。
/// 用於 RecoveryChatClient L4 壓縮前後的轉換。
/// </summary>
public static class MessageSerializer
{
    /// <summary>
    /// 將訊息清單序列化為可壓縮的文字（保留 role + tool call 結構）。
    /// 每則訊息截斷至 maxPerMessage 字元，避免單一巨大訊息主導壓縮。
    /// </summary>
    public static string Serialize(IReadOnlyList<ChatMessage> messages, int maxPerMessage = 200)
    {
        var sb = new StringBuilder();

        foreach (var msg in messages)
        {
            var role = msg.Role.Value;
            var text = msg.Text ?? "";

            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent call)
                {
                    var args = call.Arguments is not null
                        ? JsonSerializer.Serialize(call.Arguments)
                        : "";
                    text = $"[Called {call.Name}({Truncate(args, 100)})]";
                }
                else if (content is FunctionResultContent result)
                {
                    text = $"[Result: {Truncate(result.Result?.ToString() ?? "", 150)}]";
                }
            }

            if (text.Length > maxPerMessage)
            {
                text = text[..maxPerMessage] + "...";
            }

            sb.AppendLine($"{role}: {text}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 將壓縮後的摘要文字包裝為 System ChatMessage。
    /// </summary>
    public static ChatMessage WrapAsCompressedHistory(string summary, int originalCount)
    {
        return new ChatMessage(ChatRole.System,
            $"[Compressed history of previous {originalCount} messages]\n{summary}");
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "... [truncated]";
    }
}
