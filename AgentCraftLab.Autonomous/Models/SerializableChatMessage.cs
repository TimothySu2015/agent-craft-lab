using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Models;

/// <summary>
/// ChatMessage 的可序列化表示 — 將 MEAI 的多態 AIContent 轉為簡單 JSON。
/// 支援 TextContent、FunctionCallContent、FunctionResultContent 的完整 round-trip。
/// </summary>
public sealed record SerializableChatMessage
{
    public string Role { get; init; } = "";
    public List<SerializableContent> Contents { get; init; } = [];

    public static SerializableChatMessage FromChatMessage(ChatMessage msg)
    {
        var contents = new List<SerializableContent>();
        foreach (var content in msg.Contents)
        {
            contents.Add(content switch
            {
                FunctionCallContent call => new SerializableContent
                {
                    Type = "functionCall",
                    FunctionName = call.Name,
                    CallId = call.CallId,
                    ArgumentsJson = call.Arguments is not null
                        ? JsonSerializer.Serialize(call.Arguments)
                        : null
                },
                FunctionResultContent result => new SerializableContent
                {
                    Type = "functionResult",
                    CallId = result.CallId,
                    ResultJson = result.Result is not null
                        ? (result.Result is JsonElement je ? je.GetRawText() : result.Result.ToString())
                        : null
                },
                _ => new SerializableContent
                {
                    Type = "text",
                    Text = content switch
                    {
                        TextContent tc => tc.Text,
                        _ => content.ToString() ?? ""
                    }
                }
            });
        }

        // 如果 Contents 為空但有 Text，加入一個 TextContent
        if (contents.Count == 0 && msg.Text is not null)
        {
            contents.Add(new SerializableContent { Type = "text", Text = msg.Text });
        }

        return new SerializableChatMessage
        {
            Role = msg.Role.Value,
            Contents = contents
        };
    }

    public ChatMessage ToChatMessage()
    {
        var role = new ChatRole(Role);
        var aiContents = new List<AIContent>();

        foreach (var content in Contents)
        {
            switch (content.Type)
            {
                case "functionCall":
                    var args = content.ArgumentsJson is not null
                        ? JsonSerializer.Deserialize<Dictionary<string, object?>>(content.ArgumentsJson)
                        : null;
                    aiContents.Add(new FunctionCallContent(content.CallId ?? "", content.FunctionName ?? "", args));
                    break;

                case "functionResult":
                    aiContents.Add(new FunctionResultContent(content.CallId ?? "", content.ResultJson));
                    break;

                default: // "text"
                    aiContents.Add(new TextContent(content.Text ?? ""));
                    break;
            }
        }

        return new ChatMessage(role, aiContents);
    }

    public static List<SerializableChatMessage> FromList(List<ChatMessage> messages)
    {
        return messages.Select(FromChatMessage).ToList();
    }

    public static List<ChatMessage> ToList(List<SerializableChatMessage> messages)
    {
        return messages.Select(m => m.ToChatMessage()).ToList();
    }
}

/// <summary>
/// AIContent 的扁平化可序列化表示。
/// Type 作為判別欄位：text / functionCall / functionResult。
/// </summary>
public sealed record SerializableContent
{
    public string Type { get; init; } = "text";
    public string? Text { get; init; }
    public string? FunctionName { get; init; }
    public string? CallId { get; init; }
    public string? ArgumentsJson { get; init; }
    public string? ResultJson { get; init; }
}
