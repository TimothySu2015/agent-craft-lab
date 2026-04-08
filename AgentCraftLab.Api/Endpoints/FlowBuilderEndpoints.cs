using System.Diagnostics;
using System.Text.Json;
using AgentCraftLab.Api.Services;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentCraftLab.Api.Endpoints;

public static class FlowBuilderEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static void MapFlowBuilderEndpoints(this WebApplication app)
    {
        app.MapPost("/api/flow-builder", async (
            HttpContext ctx,
            [FromServices] EnhancedFlowBuildService enhanced,
            [FromServices] FlowBuilderService legacy,
            [FromServices] ICredentialStore credStore,
            [FromServices] IUserContext userCtx,
            CancellationToken ct) =>
        {
            var request = await JsonSerializer.DeserializeAsync<FlowBuildApiRequest>(
                ctx.Request.Body, JsonOptions, ct);

            if (request is null || string.IsNullOrWhiteSpace(request.Message))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ApiError("FLOW_BUILD_MESSAGE_REQUIRED", "Message is required"), ct);
                return;
            }

            // 優先從後端 CredentialStore 讀取，fallback 到前端傳入的 apiKey
            var provider = request.Provider ?? Providers.OpenAI;
            var credential = await ResolveCredentialAsync(credStore, userCtx, provider, request.ApiKey, request.Endpoint);

            var history = request.History?.Select(h => new ChatHistoryEntry
            {
                Role = h.Role,
                Text = h.Content,
            }).ToList() ?? [];

            var buildRequest = new FlowBuildRequest
            {
                UserMessage = request.Message,
                CurrentWorkflowJson = request.CurrentPayload,
                History = history,
                Credential = credential,
                Model = request.Model ?? Defaults.Model,
                Provider = provider,
            };

            // mode=legacy 使用舊版，預設使用 Flow Planner 強化版
            var useLegacy = request.Mode?.Equals("legacy", StringComparison.OrdinalIgnoreCase) == true;

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var sw = Stopwatch.StartNew();
            var responseText = new System.Text.StringBuilder();

            try
            {
                var stream = useLegacy
                    ? legacy.GenerateFlowAsync(buildRequest, ct)
                    : enhanced.GenerateAsync(buildRequest, ct);

                await foreach (var chunk in stream)
                {
                    responseText.Append(chunk);
                    await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize($"[ERROR] {ex.Message}")}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }

            sw.Stop();

            // 發送 metadata 事件（耗時 + 預估 token 數 + 成本）
            var estimatedTokens = ModelPricing.EstimateTokens(responseText.ToString());
            var cost = ModelPricing.EstimateCost(buildRequest.Model, estimatedTokens);
            var metadata = new
            {
                type = "__metadata",
                durationMs = sw.ElapsedMilliseconds,
                estimatedTokens,
                model = buildRequest.Model,
                estimatedCost = ModelPricing.FormatCost(cost),
            };
            await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(metadata)}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);

            await ctx.Response.WriteAsync("data: [DONE]\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        });
    }

    private static async Task<ProviderCredential> ResolveCredentialAsync(
        ICredentialStore store, IUserContext userCtx, string provider, string? fallbackApiKey, string? fallbackEndpoint)
    {
        var userId = await userCtx.GetUserIdAsync();
        var stored = await store.GetDecryptedCredentialsAsync(userId);
        if (stored.TryGetValue(provider, out var cred) &&
            (!string.IsNullOrWhiteSpace(cred.ApiKey) || Providers.IsKeyOptional(provider)))
        {
            return cred;
        }

        // Fallback：前端傳入的 apiKey（向後相容）
        return new ProviderCredential
        {
            ApiKey = fallbackApiKey ?? "",
            Endpoint = fallbackEndpoint ?? "",
        };
    }
}

public record FlowBuildApiRequest(
    string Message,
    string? Provider,
    string? Model,
    string? ApiKey,
    string? Endpoint,
    string? CurrentPayload,
    List<FlowBuildHistoryEntry>? History,
    string? Mode   // "legacy" = 舊版, null/其他 = Flow Planner 強化版
);

public record FlowBuildHistoryEntry(string Role, string Content);
