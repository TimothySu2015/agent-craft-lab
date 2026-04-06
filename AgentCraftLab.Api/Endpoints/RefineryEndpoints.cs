using System.ClientModel;
using System.Text;
using System.Text.Json;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AgentCraftLab.Api.Endpoints;

/// <summary>
/// DocRefinery API — 精煉專案的 CRUD + 檔案管理 + 結構化輸出。
/// </summary>
public static class RefineryEndpoints
{
    private const string DefaultProvider = "openai";
    private const string DefaultModel = "gpt-4o";

    public static void MapRefineryEndpoints(this WebApplication app, JsonSerializerOptions? jsonOptions = null)
    {
        var group = app.MapGroup("/api/refinery");

        // ── Project CRUD ──

        group.MapPost("/", async (CreateRefineryRequest req,
            [FromServices] RefineryService service, IUserContext userCtx) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new ApiError("REFINERY_NAME_REQUIRED", "Name is required"));
            }

            var userId = await userCtx.GetUserIdAsync();
            var project = await service.CreateAsync(userId, req.Name, req.Description ?? "",
                req.SchemaTemplateId, req.CustomSchemaJson,
                req.Provider ?? DefaultProvider, req.Model ?? DefaultModel, req.OutputLanguage,
                req.ExtractionMode ?? "fast", req.EnableChallenge ?? false,
                req.ImageProcessingMode ?? "skip");
            return Results.Created($"/api/refinery/{project.Id}", project);
        });

        group.MapGet("/", async ([FromServices] RefineryService service, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            return Results.Ok(await service.ListAsync(userId));
        });

        group.MapGet("/{id}", async (string id, [FromServices] RefineryService service) =>
        {
            var project = await service.GetAsync(id);
            return project is null ? Results.NotFound() : Results.Ok(project);
        });

        group.MapPut("/{id}", async (string id, UpdateRefineryRequest req,
            [FromServices] RefineryService service, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var project = await service.UpdateAsync(userId, id, req.Name ?? "", req.Description ?? "",
                req.SchemaTemplateId, req.CustomSchemaJson,
                req.Provider ?? DefaultProvider, req.Model ?? DefaultModel, req.OutputLanguage,
                req.ExtractionMode ?? "fast", req.EnableChallenge ?? false,
                req.ImageProcessingMode ?? "skip");
            return project is null ? Results.NotFound() : Results.Ok(project);
        });

        group.MapDelete("/{id}", async (string id,
            [FromServices] RefineryService service, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            return await service.DeleteAsync(userId, id) ? Results.NoContent() : Results.NotFound();
        });

        // ── File management ──

        group.MapGet("/{id}/files", async (string id, [FromServices] RefineryService service) =>
            Results.Ok(await service.ListFilesAsync(id)));

        group.MapPost("/{id}/files", async (HttpContext ctx, string id,
            [FromServices] RefineryService service,
            [FromServices] ICredentialStore credStore,
            IUserContext userCtx, CancellationToken ct) =>
        {
            ConfigureSseResponse(ctx);

            var form = await ctx.Request.ReadFormAsync(ct);
            var files = form.Files;

            if (files.Count == 0)
            {
                await WriteSseEvent(ctx.Response, new { type = "error", text = "No files provided" }, ct);
                return;
            }

            // 精準模式：建立 embeddingGenerator 用於索引
            var userId = await userCtx.GetUserIdAsync();
            var credentials = await credStore.GetDecryptedCredentialsAsync(userId);
            var project = await service.GetAsync(id);
            var embeddingGen = project?.ExtractionMode == "precise"
                ? CreateEmbeddingGenerator(credentials)
                : null;

            foreach (var file in files)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                var data = ms.ToArray();
                var mimeType = file.ContentType ?? Cleaner.MimeTypeHelper.FromExtension(file.FileName);

                await foreach (var evt in service.AddFileAsync(id, file.FileName, mimeType, data, embeddingGen, ct))
                {
                    await WriteSseEvent(ctx.Response, new { type = evt.Type, text = evt.Text, fileName = file.FileName }, ct);
                }
            }

            await WriteSseEvent(ctx.Response, new { type = "done", text = $"Processed {files.Count} file(s)" }, ct);
        });

        group.MapDelete("/{id}/files/{fileId}", async (string id, string fileId,
            [FromServices] RefineryService service) =>
            await service.RemoveFileAsync(id, fileId) ? Results.NoContent() : Results.NotFound());

        // 切換檔案是否納入 Generate
        group.MapPatch("/{id}/files/{fileId}/toggle", async (string id, string fileId,
            [FromServices] IRefineryStore store) =>
        {
            var file = await store.GetFileAsync(id, fileId);
            if (file is null) return Results.NotFound();
            await store.ToggleFileIncludedAsync(fileId, !file.IsIncluded);
            return Results.Ok(new { isIncluded = !file.IsIncluded });
        });

        // 重試索引（Failed 檔案用）
        group.MapPost("/{id}/files/{fileId}/reindex", async (HttpContext ctx, string id, string fileId,
            [FromServices] RefineryService service,
            [FromServices] ICredentialStore credStore,
            IUserContext userCtx, CancellationToken ct) =>
        {
            ConfigureSseResponse(ctx);
            var userId = await userCtx.GetUserIdAsync();
            var credentials = await credStore.GetDecryptedCredentialsAsync(userId);
            var embeddingGen = CreateEmbeddingGenerator(credentials);

            if (embeddingGen is null)
            {
                await WriteSseEvent(ctx.Response, new { type = "error", text = "No embedding API key configured" }, ct);
                return;
            }

            await foreach (var evt in service.ReindexFileAsync(id, fileId, embeddingGen, ct))
            {
                await WriteSseEvent(ctx.Response, new { type = evt.Type, text = evt.Text }, ct);
            }
        });

        group.MapGet("/{id}/files/{fileId}/preview", async (string id, string fileId,
            [FromServices] RefineryService service) =>
        {
            var json = await service.PreviewFileAsync(id, fileId);
            if (json is null)
            {
                return Results.NotFound();
            }

            // 回傳原始 JSON 字串
            return Results.Content(json, "application/json");
        });

        // ── Generate output ──

        group.MapPost("/{id}/generate", async (HttpContext ctx, string id,
            [FromServices] RefineryService service,
            [FromServices] ILlmClientFactory clientFactory,
            [FromServices] ICredentialStore credStore,
            IUserContext userCtx, CancellationToken ct) =>
        {
            ConfigureSseResponse(ctx);

            var project = await service.GetAsync(id);
            if (project is null)
            {
                await WriteSseEvent(ctx.Response, new { type = "error", text = "Project not found" }, ct);
                return;
            }

            // 建立 LLM Client
            var userId = await userCtx.GetUserIdAsync();
            var credentials = await credStore.GetDecryptedCredentialsAsync(userId);
            var provider = AgentContextBuilder.NormalizeProvider(project.Provider);
            var (client, clientError) = clientFactory.CreateClient(credentials, provider, project.Model);

            if (client is null)
            {
                await WriteSseEvent(ctx.Response,
                    new { type = "error", text = clientError ?? "Failed to create LLM client" }, ct);
                return;
            }

            var llmProvider = new ChatClientLlmAdapter(client);

            // 精準模式需要 embedding generator（建搜尋索引用）
            IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null;
            if (project.ExtractionMode == "precise")
            {
                embeddingGenerator = CreateEmbeddingGenerator(credentials);
            }

            await foreach (var evt in service.GenerateOutputAsync(id, userId, llmProvider, embeddingGenerator, ct))
            {
                await WriteSseEvent(ctx.Response, new { type = evt.Type, text = evt.Text }, ct);
            }
        });

        // ── Output versioning ──

        group.MapGet("/{id}/outputs", async (string id, [FromServices] RefineryService service) =>
            Results.Ok(await service.ListOutputsAsync(id)));

        group.MapGet("/{id}/outputs/latest", async (string id, [FromServices] RefineryService service) =>
        {
            var output = await service.GetLatestOutputAsync(id);
            return output is null ? Results.NotFound() : Results.Ok(output);
        });

        group.MapGet("/{id}/outputs/{version:int}", async (string id, int version,
            [FromServices] RefineryService service) =>
        {
            var output = await service.GetOutputAsync(id, version);
            return output is null ? Results.NotFound() : Results.Ok(output);
        });
    }

    private static IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(
        Dictionary<string, ProviderCredential> credentials, string embeddingModel = "text-embedding-3-small")
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

    private static void ConfigureSseResponse(HttpContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
    }

    private static async Task WriteSseEvent(HttpResponse response, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data);
        await response.WriteAsync($"data: {json}\n\n", Encoding.UTF8, ct);
        await response.Body.FlushAsync(ct);
    }
}

file record CreateRefineryRequest(
    string? Name, string? Description,
    string? SchemaTemplateId, string? CustomSchemaJson,
    string? Provider, string? Model, string? OutputLanguage,
    string? ExtractionMode, bool? EnableChallenge,
    string? ImageProcessingMode);

file record UpdateRefineryRequest(
    string? Name, string? Description,
    string? SchemaTemplateId, string? CustomSchemaJson,
    string? Provider, string? Model, string? OutputLanguage,
    string? ExtractionMode, bool? EnableChallenge,
    string? ImageProcessingMode);
