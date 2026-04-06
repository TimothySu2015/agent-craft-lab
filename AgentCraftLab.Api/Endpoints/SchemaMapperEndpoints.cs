using System.Text.Json;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Renderers;
using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;

namespace AgentCraftLab.Api.Endpoints;

/// <summary>
/// Schema Mapper API — 從多份清洗後文件擷取結構化規格文件。
/// </summary>
public static class SchemaMapperEndpoints
{
    public static void MapSchemaMapperEndpoints(this WebApplication app)
    {
        // 列出可用的 Schema 模板
        app.MapGet("/api/schema-templates", (ISchemaTemplateProvider templates) =>
        {
            return Results.Ok(templates.ListTemplates());
        });

        // 取得單一模板的完整 Schema
        app.MapGet("/api/schema-templates/{id}", (string id, ISchemaTemplateProvider templates) =>
        {
            var schema = templates.GetTemplate(id);
            return schema is null
                ? Results.NotFound(new ApiError("TEMPLATE_NOT_FOUND", $"Template '{id}' not found"))
                : Results.Ok(new { schema.Name, schema.Description, schema.JsonSchema, schema.ExtractionGuidance });
        });

        // 執行 Schema Mapping（多檔上傳 + Schema → 結構化 JSON）
        app.MapPost("/api/schema-mapper", async (HttpContext ctx,
            IDocumentCleaner cleaner,
            ISchemaTemplateProvider templateProvider,
            ILlmClientFactory clientFactory,
            ICredentialStore credStore,
            IUserContext userCtx,
            CancellationToken ct) =>
        {
            var form = await ctx.Request.ReadFormAsync(ct);

            // 1. 解析參數
            var templateId = form["templateId"].FirstOrDefault();
            var customSchema = form["customSchema"].FirstOrDefault();
            var provider = form["provider"].FirstOrDefault() ?? Providers.OpenAI;
            var model = form["model"].FirstOrDefault() ?? "gpt-4o";
            var outputFormat = form["outputFormat"].FirstOrDefault() ?? "json";
            var outputLanguage = form["outputLanguage"].FirstOrDefault();

            // 2. 取得 Schema
            SchemaDefinition? schema = null;
            if (!string.IsNullOrWhiteSpace(templateId))
            {
                schema = templateProvider.GetTemplate(templateId);
                if (schema is null)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsJsonAsync(
                        new ApiError("TEMPLATE_NOT_FOUND", $"Template '{templateId}' not found"), ct);
                    return;
                }
            }
            else if (!string.IsNullOrWhiteSpace(customSchema))
            {
                try
                {
                    var custom = JsonSerializer.Deserialize<CustomSchemaRequest>(customSchema,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    schema = new SchemaDefinition
                    {
                        Name = custom?.Name ?? "Custom Schema",
                        Description = custom?.Description ?? "",
                        JsonSchema = custom?.JsonSchema ?? "{}",
                        ExtractionGuidance = custom?.ExtractionGuidance,
                    };
                }
                catch (JsonException)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsJsonAsync(
                        new ApiError("INVALID_SCHEMA", "Invalid customSchema JSON"), ct);
                    return;
                }
            }
            else
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(
                    new ApiError("SCHEMA_REQUIRED", "Provide templateId or customSchema"), ct);
                return;
            }

            // 3. 讀取上傳的檔案
            var files = form.Files;
            if (files.Count == 0)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(
                    new ApiError("FILES_REQUIRED", "At least one file is required"), ct);
                return;
            }

            // 4. 清洗每個檔案
            var cleanedDocs = new List<Cleaner.Elements.CleanedDocument>();
            foreach (var file in files)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                var data = ms.ToArray();
                var mimeType = file.ContentType ?? GuessMimeType(file.FileName);

                try
                {
                    var cleaned = await cleaner.CleanAsync(data, file.FileName, mimeType, ct: ct);
                    cleanedDocs.Add(cleaned);
                }
                catch (NotSupportedException)
                {
                    // 不支援的格式跳過
                }
            }

            if (cleanedDocs.Count == 0)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(
                    new ApiError("NO_EXTRACTABLE_FILES", "No files could be processed"), ct);
                return;
            }

            // 5. 建立 LLM Client
            var normalizedProvider = AgentContextBuilder.NormalizeProvider(provider);
            var userId = await userCtx.GetUserIdAsync();
            var credentials = await credStore.GetDecryptedCredentialsAsync(userId);
            var (client, clientError) = clientFactory.CreateClient(credentials, normalizedProvider, model);

            if (client is null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(
                    new ApiError("LLM_CLIENT_ERROR", clientError ?? "Failed to create LLM client"), ct);
                return;
            }

            // 6. 執行 Schema Mapping
            try
            {
                var llmProvider = new ChatClientLlmAdapter(client);
                var mapper = new Cleaner.SchemaMapper.LlmSchemaMapper(llmProvider);
                var options = new SchemaMapperOptions
                {
                    OutputLanguage = outputLanguage,
                    IncludeSourceReferences = true,
                };

                var result = await mapper.MapAsync(cleanedDocs, schema, options, ct);

                // 7. 渲染輸出
                var output = result.Json;
                if (outputFormat == "markdown")
                {
                    var renderer = new MarkdownRenderer();
                    output = await renderer.RenderAsync(result.Json, schema, ct);
                }

                await ctx.Response.WriteAsJsonAsync(new
                {
                    output,
                    format = outputFormat,
                    json = result.Json,
                    missingFields = result.MissingFields,
                    openQuestions = result.OpenQuestions,
                    sourceCount = result.SourceCount,
                }, ct);
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsJsonAsync(
                    new ApiError("SCHEMA_MAPPER_ERROR", ex.Message), ct);
            }
        });
    }

    private static string GuessMimeType(string fileName) =>
        AgentCraftLab.Cleaner.MimeTypeHelper.FromExtension(fileName);
}

file record CustomSchemaRequest(string? Name, string? Description, string? JsonSchema, string? ExtractionGuidance);
