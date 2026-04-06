using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Extensions;

/// <summary>
/// <see cref="CacheableSystemPrompt"/> 的擴展方法 — 將可快取提示詞轉為 ChatMessage 清單。
/// </summary>
public static class CacheablePromptExtensions
{
    /// <summary>
    /// 將 CacheableSystemPrompt 轉為 ChatMessage 清單。
    /// DynamicPart 為空時回傳 1 條 system message；有值時回傳 2 條（靜態 + 動態）。
    /// provider 為 "anthropic" 時在靜態 message 加入 cache_control metadata。
    /// OpenAI 相容 provider 自動 prefix caching，不需額外標記。
    /// </summary>
    public static List<ChatMessage> ToChatMessages(this CacheableSystemPrompt prompt, string? provider = null)
    {
        if (string.IsNullOrWhiteSpace(prompt.DynamicPart))
        {
            var msg = new ChatMessage(ChatRole.System, prompt.StaticPart);
            ApplyCacheControl(msg, provider);
            return [msg];
        }

        var staticMsg = new ChatMessage(ChatRole.System, prompt.StaticPart);
        ApplyCacheControl(staticMsg, provider);

        var dynamicMsg = new ChatMessage(ChatRole.System, prompt.DynamicPart);

        return [staticMsg, dynamicMsg];
    }

    /// <summary>
    /// 對 Anthropic provider 在訊息的 AdditionalProperties 中加入 cache_control。
    /// </summary>
    private static void ApplyCacheControl(ChatMessage message, string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return;
        }

        if (!provider.Contains("anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        message.AdditionalProperties ??= [];
        message.AdditionalProperties["cache_control"] = new Dictionary<string, string> { ["type"] = "ephemeral" };
    }
}
