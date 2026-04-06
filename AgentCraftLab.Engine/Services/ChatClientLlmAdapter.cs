using AgentCraftLab.Cleaner.Abstractions;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 將 MEAI IChatClient 橋接到 CraftCleaner 的 ILlmProvider。
/// 讓 Schema Mapper 可以呼叫任何已設定的 LLM。
/// </summary>
public sealed class ChatClientLlmAdapter : ILlmProvider
{
    private readonly IChatClient _chatClient;

    public ChatClientLlmAdapter(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt),
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        var usage = response.Usage;
        return new LlmResponse(
            response.Text ?? "",
            (int)(usage?.InputTokenCount ?? 0),
            (int)(usage?.OutputTokenCount ?? 0));
    }
}
