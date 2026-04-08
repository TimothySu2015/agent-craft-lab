using System.Text.Json;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;

namespace AgentCraftLab.Api.Endpoints;

/// <summary>
/// Prompt Refiner API — 使用 LLM + Prompt Engineering 指南優化使用者的 Agent Instructions。
/// </summary>
public static class PromptRefinerEndpoints
{
    public static void MapPromptRefinerEndpoints(this WebApplication app)
    {
        app.MapPost("/api/prompt-refiner", async (HttpContext ctx,
            ILlmClientFactory clientFactory,
            ICredentialStore credStore,
            IUserContext userCtx,
            PromptRefinerService refiner,
            CancellationToken ct) =>
        {
            var request = await JsonSerializer.DeserializeAsync<PromptRefinerRequest>(
                ctx.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                ct);

            if (request is null || string.IsNullOrWhiteSpace(request.Prompt))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ApiError("REFINER_PROMPT_REQUIRED", "Prompt is required"), ct);
                return;
            }

            // 解析 provider + credential
            var provider = AgentContextBuilder.NormalizeProvider(request.Provider ?? Providers.OpenAI);
            var credential = await ResolveCredentialAsync(credStore, userCtx, provider, request.ApiKey, request.Endpoint);

            if (string.IsNullOrWhiteSpace(credential.ApiKey))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ApiError("REFINER_KEY_REQUIRED", "API key is required. Configure in Settings or provide apiKey."), ct);
                return;
            }

            var model = request.Model ?? "gpt-4o-mini";
            var (client, error) = clientFactory.CreateClient(
                new Dictionary<string, ProviderCredential> { [provider] = credential },
                provider, model);

            if (client is null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ApiError("REFINER_CLIENT_ERROR", error), ct);
                return;
            }

            try
            {
                var result = await refiner.RefineAsync(client, request.Prompt, model, provider, ct);
                await ctx.Response.WriteAsJsonAsync(new
                {
                    original = result.Original,
                    refined = result.Refined,
                    changes = result.Changes,
                }, ct);
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsJsonAsync(new ApiError("REFINER_ERROR", ex.Message), ct);
            }
        });
    }

    private static async Task<ProviderCredential> ResolveCredentialAsync(
        ICredentialStore store, IUserContext userCtx, string provider, string? fallbackApiKey, string? fallbackEndpoint)
    {
        var userId = await userCtx.GetUserIdAsync();
        var stored = await store.GetDecryptedCredentialsAsync(userId);
        if (stored.TryGetValue(provider, out var cred) && !string.IsNullOrWhiteSpace(cred.ApiKey))
        {
            return cred;
        }

        return new ProviderCredential
        {
            ApiKey = fallbackApiKey ?? "",
            Endpoint = fallbackEndpoint ?? "",
        };
    }
}

file record PromptRefinerRequest(string? Prompt, string? Provider, string? Model, string? ApiKey, string? Endpoint);
