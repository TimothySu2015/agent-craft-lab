using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Strategies;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// LLM Client 工廠介面 — 統一 credential 解析 + provider fallback + client 建構。
/// 取代散佈在 Engine/Autonomous/Flow 三處的重複邏輯。
/// </summary>
public interface ILlmClientFactory
{
    /// <summary>
    /// 建立 IChatClient。自動處理 provider 正規化、credential 解析、OpenAI fallback。
    /// </summary>
    /// <param name="credentials">所有 provider 的憑證</param>
    /// <param name="provider">指定的 provider（會自動正規化）</param>
    /// <param name="model">模型名稱</param>
    /// <returns>Client + 錯誤訊息（二選一）</returns>
    (IChatClient? Client, string? Error) CreateClient(
        Dictionary<string, ProviderCredential> credentials,
        string provider,
        string model);
}

/// <summary>
/// 預設 LLM Client 工廠 — 複用 AgentContextBuilder.CreateChatClient，加上統一的 credential 解析。
/// </summary>
public sealed class DefaultLlmClientFactory : ILlmClientFactory
{
    public (IChatClient? Client, string? Error) CreateClient(
        Dictionary<string, ProviderCredential> credentials,
        string provider,
        string model)
    {
        var normalizedProvider = AgentContextBuilder.NormalizeProvider(provider);

        if (!credentials.TryGetValue(normalizedProvider, out var cred) ||
            (!Providers.IsKeyOptional(normalizedProvider) && string.IsNullOrWhiteSpace(cred.ApiKey)))
        {
            // fallback: 嘗試 openai
            if (normalizedProvider != Providers.OpenAI &&
                credentials.TryGetValue(Providers.OpenAI, out var fallback) &&
                !string.IsNullOrWhiteSpace(fallback.ApiKey))
            {
                cred = fallback;
                normalizedProvider = Providers.OpenAI;
            }
            else
            {
                return (null, $"No API key found for provider '{normalizedProvider}'");
            }
        }

        try
        {
            var client = AgentContextBuilder.CreateChatClient(
                normalizedProvider, cred.ApiKey!, cred.Endpoint ?? "", model);
            return (client, null);
        }
        catch (Exception ex)
        {
            return (null, $"Failed to create LLM client: {ex.Message}");
        }
    }
}
