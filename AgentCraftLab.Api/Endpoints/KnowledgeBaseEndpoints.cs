using System.ClientModel;
using System.Text.Json;
using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AgentCraftLab.Api.Endpoints;

public static class KnowledgeBaseEndpoints
{
    public static void MapKnowledgeBaseEndpoints(this WebApplication app, JsonSerializerOptions? jsonOptions = null)
    {
        var group = app.MapGroup("/api/knowledge-bases");

        group.MapPost("/", async (CreateKbRequest req, [FromServices] KnowledgeBaseService kbService, IUserContext userCtx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new ApiError("KB_NAME_REQUIRED", "Name is required"));
            }

            var userId = await userCtx.GetUserIdAsync();
            var doc = await kbService.CreateAsync(
                userId, req.Name, req.Description ?? "",
                req.EmbeddingModel ?? "text-embedding-3-small",
                req.ChunkSize ?? 512, req.ChunkOverlap ?? 50, ct,
                req.DataSourceId, req.ChunkStrategy ?? "fixed");
            return Results.Created($"/api/knowledge-bases/{doc.Id}", doc);
        });

        group.MapGet("/", async (IKnowledgeBaseStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            return Results.Ok(await store.ListAsync(userId));
        });

        group.MapGet("/{id}", async (string id, IKnowledgeBaseStore store) =>
        {
            var doc = await store.GetAsync(id);
            return doc is not null ? Results.Ok(doc) : KbNotFound(id);
        });

        group.MapPut("/{id}", async (string id, UpdateKbRequest req, IKnowledgeBaseStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var doc = await store.UpdateAsync(userId, id, req.Name ?? "", req.Description ?? "");
            return doc is not null ? Results.Ok(doc) : KbNotFound(id);
        });

        group.MapDelete("/{id}", async (string id, IKnowledgeBaseStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var ok = await store.DeleteAsync(userId, id);
            return ok ? Results.NoContent() : KbNotFound(id);
        });

        // ─── Files ───

        group.MapGet("/{id}/files", async (string id, IKnowledgeBaseStore store) =>
        {
            return Results.Ok(await store.ListFilesAsync(id));
        });

        group.MapDelete("/{kbId}/files/{fileId}", async (string kbId, string fileId, [FromServices] KnowledgeBaseService kbService, CancellationToken ct) =>
        {
            var ok = await kbService.RemoveFileAsync(kbId, fileId, ct);
            return ok ? Results.NoContent() : Results.NotFound(new ApiError("KB_FILE_NOT_FOUND"));
        });

        // File upload — 接收檔案 → 建立 EmbeddingGenerator → Ingest → SSE streaming 進度
        group.MapPost("/{id}/files", async (string id, HttpContext ctx,
            [FromServices] IKnowledgeBaseStore store,
            [FromServices] ICredentialStore credStore,
            [FromServices] KnowledgeBaseService kbService,
            [FromServices] IUserContext userCtx,
            CancellationToken ct) =>
        {
            var kb = await store.GetAsync(id);
            if (kb is null)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsJsonAsync(new ApiError("KB_NOT_FOUND", Params: new() { ["id"] = id }), ct);
                return;
            }

            var form = await ctx.Request.ReadFormAsync(ct);
            var files = form.Files;
            if (files.Count == 0)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ApiError("FILE_REQUIRED"), ct);
                return;
            }

            // 從 CredentialStore 取得使用者的 API Key
            var userId = await userCtx.GetUserIdAsync();
            var creds = await credStore.GetDecryptedCredentialsAsync(userId);
            var embeddingGenerator = CreateEmbeddingGenerator(creds, kb.EmbeddingModel);
            if (embeddingGenerator is null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(
                    new ApiError("EMBEDDING_KEY_MISSING", "No OpenAI or Azure OpenAI API key configured. Please add credentials in Settings."), ct);
                return;
            }

            // Dispose embedding generator（可能持有 HttpClient）
            using var _ = embeddingGenerator as IDisposable;

            const long maxFileSize = 50 * 1024 * 1024;

            // SSE streaming — 推送 ingest 進度
            var opts = jsonOptions ?? new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            // 循序處理所有檔案（避免 embedding API rate limit）
            var fileIndex = 0;
            foreach (var file in files)
            {
                fileIndex++;
                var prefix = files.Count > 1 ? $"[{fileIndex}/{files.Count}] " : "";

                if (file.Length > maxFileSize)
                {
                    var errJson = JsonSerializer.Serialize(new
                    {
                        type = EventTypes.Error,
                        text = $"{prefix}{file.FileName}: File too large (max {maxFileSize / 1024 / 1024}MB).",
                        fileName = file.FileName
                    }, opts);
                    await ctx.Response.WriteAsync($"data: {errJson}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                    continue;
                }

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                var fileData = ms.ToArray();

                try
                {
                    await foreach (var evt in kbService.AddFileAsync(kb.Id, file.FileName, file.ContentType, fileData, embeddingGenerator, ct))
                    {
                        var json = JsonSerializer.Serialize(new
                        {
                            type = evt.Type,
                            text = $"{prefix}{evt.Text}",
                            fileName = file.FileName,
                            metadata = evt.Metadata
                        }, opts);
                        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        type = EventTypes.Error,
                        text = $"{prefix}{file.FileName}: {ex.Message}",
                        fileName = file.FileName
                    }, opts);
                    await ctx.Response.WriteAsync($"data: {json}\n\n", CancellationToken.None);
                    await ctx.Response.Body.FlushAsync(CancellationToken.None);
                }
            }
        }).DisableAntiforgery();

        // ─── URL Ingest ───

        group.MapPost("/{id}/urls", async (string id, AddUrlRequest req, HttpContext ctx,
            [FromServices] IKnowledgeBaseStore store,
            [FromServices] ICredentialStore credStore,
            [FromServices] KnowledgeBaseService kbService,
            IUserContext userCtx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Url))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ApiError("URL_REQUIRED"), ct);
                return;
            }

            var kb = await store.GetAsync(id);
            if (kb is null)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsJsonAsync(new ApiError("KB_NOT_FOUND"), ct);
                return;
            }

            var userId = await userCtx.GetUserIdAsync();
            var creds = await credStore.GetDecryptedCredentialsAsync(userId);
            var embeddingGenerator = CreateEmbeddingGenerator(creds, kb.EmbeddingModel);
            if (embeddingGenerator is null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(
                    new ApiError("EMBEDDING_KEY_MISSING", "No OpenAI or Azure OpenAI API key configured."), ct);
                return;
            }

            // SSE streaming
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var lastMsg = "";
            await foreach (var evt in kbService.AddUrlAsync(kb.Id, req.Url, embeddingGenerator, ct))
            {
                lastMsg = evt.Text;
                var json = JsonSerializer.Serialize(new { type = evt.Type, text = evt.Text });
                await ctx.Response.WriteAsync($"data: {json}\n\n", CancellationToken.None);
                await ctx.Response.Body.FlushAsync(CancellationToken.None);
            }
        });

        // ─── Retrieval Test ───

        group.MapPost("/{id}/test-search", async (string id, TestSearchRequest req,
            [FromServices] IKnowledgeBaseStore store,
            [FromServices] ICredentialStore credStore,
            [FromServices] RagService ragService,
            IUserContext userCtx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Query))
            {
                return Results.BadRequest(new ApiError("QUERY_REQUIRED", "Query text is required"));
            }

            var kb = await store.GetAsync(id);
            if (kb is null)
            {
                return KbNotFound(id);
            }

            var userId = await userCtx.GetUserIdAsync();
            var creds = await credStore.GetDecryptedCredentialsAsync(userId);
            var embeddingGenerator = CreateEmbeddingGenerator(creds, kb.EmbeddingModel);
            if (embeddingGenerator is null)
            {
                return Results.BadRequest(new ApiError("EMBEDDING_KEY_MISSING",
                    "No OpenAI or Azure OpenAI API key configured. Please add credentials in Settings."));
            }

            var topK = req.TopK ?? 5;
            var useExpansion = req.QueryExpansion ?? false;

            var searchOptions = new RagSearchOptions
            {
                SearchMode = req.SearchMode ?? "hybrid",
                MinScore = req.MinScore,
                EmbeddingModel = kb.EmbeddingModel,
                QueryExpansion = useExpansion,
                QueryExpander = useExpansion ? CreateChatClient(creds) is { } cc ? new QueryExpander(cc) : null : null,
            };

            var results = await ragService.SearchAsync(
                req.Query, topK, embeddingGenerator, kb.IndexName,
                searchOptions, ct);

            return Results.Ok(new
            {
                results = results.Select(r => new
                {
                    r.Content,
                    r.FileName,
                    r.ChunkIndex,
                    r.Score
                }),
                expandedQueries = ragService.LastExpandedQueries
            });
        });
    }

    /// <summary>
    /// 從使用者的 credentials 建立 IEmbeddingGenerator。優先 OpenAI，fallback Azure OpenAI。
    /// </summary>
    private static IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(
        Dictionary<string, ProviderCredential> credentials, string embeddingModel)
    {
        if (credentials.TryGetValue(Providers.OpenAI, out var openai) &&
            !string.IsNullOrWhiteSpace(openai.ApiKey))
        {
            return new OpenAIClient(openai.ApiKey)
                .GetEmbeddingClient(embeddingModel)
                .AsIEmbeddingGenerator();
        }

        if (credentials.TryGetValue(Providers.AzureOpenAI, out var azure) &&
            !string.IsNullOrWhiteSpace(azure.ApiKey) &&
            !string.IsNullOrWhiteSpace(azure.Endpoint))
        {
            return new AzureOpenAIClient(
                    new Uri(azure.Endpoint), new ApiKeyCredential(azure.ApiKey))
                .GetEmbeddingClient(embeddingModel)
                .AsIEmbeddingGenerator();
        }

        return null;
    }

    /// <summary>
    /// 從使用者的 credentials 建立 IChatClient（Query Expansion 用）。
    /// </summary>
    private static IChatClient? CreateChatClient(Dictionary<string, ProviderCredential> credentials)
    {
        if (credentials.TryGetValue(Providers.OpenAI, out var openai) &&
            !string.IsNullOrWhiteSpace(openai.ApiKey))
        {
            return new OpenAIClient(openai.ApiKey)
                .GetChatClient("gpt-4o-mini")
                .AsIChatClient();
        }

        if (credentials.TryGetValue(Providers.AzureOpenAI, out var azure) &&
            !string.IsNullOrWhiteSpace(azure.ApiKey) &&
            !string.IsNullOrWhiteSpace(azure.Endpoint))
        {
            return new AzureOpenAIClient(
                    new Uri(azure.Endpoint), new ApiKeyCredential(azure.ApiKey))
                .GetChatClient(azure.Model ?? "gpt-4o-mini")
                .AsIChatClient();
        }

        return null;
    }

    private static IResult KbNotFound(string id) =>
        Results.NotFound(new ApiError("KB_NOT_FOUND", Params: new() { ["id"] = id }));
}

public record CreateKbRequest(string Name, string? Description, string? EmbeddingModel, int? ChunkSize, int? ChunkOverlap, string? DataSourceId, string? ChunkStrategy);
public record UpdateKbRequest(string? Name, string? Description);
public record TestSearchRequest(string? Query, int? TopK, string? SearchMode, float? MinScore, bool? QueryExpansion);
public record AddUrlRequest(string? Url);
