using System.Text.Json;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Search.Providers.PgVector;
using AgentCraftLab.Search.Providers.Qdrant;
using Microsoft.AspNetCore.Mvc;

namespace AgentCraftLab.Api.Endpoints;

public static class DataSourceEndpoints
{
    /// <summary>支援的 Provider 清單。</summary>
    private static readonly HashSet<string> SupportedProviders = ["sqlite", "pgvector", "qdrant"];

    /// <summary>ConfigJson 中需要 mask 的敏感欄位。</summary>
    private static readonly HashSet<string> SensitiveFields = ["password", "apiKey", "apikey"];

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static void MapDataSourceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/data-sources");

        group.MapPost("/", async (CreateDataSourceRequest req,
            [FromServices] IDataSourceStore store,
            IUserContext userCtx) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new ApiError("DS_NAME_REQUIRED", "Name is required"));
            }

            if (!SupportedProviders.Contains(req.Provider))
            {
                return Results.BadRequest(new ApiError("DS_INVALID_PROVIDER",
                    $"Unsupported provider. Supported: {string.Join(", ", SupportedProviders)}"));
            }

            var userId = await userCtx.GetUserIdAsync();
            var doc = new DataSourceDocument
            {
                UserId = userId,
                Name = req.Name,
                Description = req.Description ?? "",
                Provider = req.Provider,
                ConfigJson = req.ConfigJson ?? "{}"
            };

            var result = await store.SaveAsync(doc);
            return Results.Created($"/api/data-sources/{result.Id}", MaskSensitiveFields(result));
        });

        group.MapGet("/", async ([FromServices] IDataSourceStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var list = await store.ListAsync(userId);
            return Results.Ok(list.Select(MaskSensitiveFields).ToList());
        });

        group.MapGet("/{id}", async (string id, [FromServices] IDataSourceStore store) =>
        {
            var doc = await store.GetAsync(id);
            return doc is not null ? Results.Ok(MaskSensitiveFields(doc)) : DsNotFound(id);
        });

        group.MapPut("/{id}", async (string id, UpdateDataSourceRequest req,
            [FromServices] IDataSourceStore store,
            IUserContext userCtx) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new ApiError("DS_NAME_REQUIRED", "Name is required"));
            }

            var userId = await userCtx.GetUserIdAsync();

            // 如果送來的 configJson 中敏感欄位為空字串或 mask 值，保留原值
            var configJson = req.ConfigJson ?? "{}";
            var existing = await store.GetAsync(id);
            if (existing is not null)
            {
                configJson = MergeConfigPreservingSecrets(existing.ConfigJson, configJson);
            }

            var doc = await store.UpdateAsync(userId, id, req.Name, req.Description ?? "",
                req.Provider ?? existing?.Provider ?? "sqlite", configJson);
            return doc is not null ? Results.Ok(MaskSensitiveFields(doc)) : DsNotFound(id);
        });

        group.MapDelete("/{id}", async (string id,
            [FromServices] IDataSourceStore store,
            IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();

            // 檢查是否有 KB 引用
            var refCount = await store.CountKbReferencesAsync(id);
            if (refCount > 0)
            {
                return Results.BadRequest(new ApiError("DS_IN_USE",
                    $"This data source is referenced by {refCount} knowledge base(s). Remove the references first.",
                    new() { ["count"] = refCount.ToString() }));
            }

            var ok = await store.DeleteAsync(userId, id);
            return ok ? Results.NoContent() : DsNotFound(id);
        });

        group.MapPost("/{id}/test", async (string id, [FromServices] IDataSourceStore store, CancellationToken ct) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null)
            {
                return Results.NotFound(new ApiError("DS_NOT_FOUND"));
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                switch (doc.Provider)
                {
                    case "sqlite":
                        return Results.Ok(new { success = true, message = "SQLite is always available.", latencyMs = 0L });

                    case "pgvector":
                        var pgConfig = JsonSerializer.Deserialize<PgVectorConfig>(doc.ConfigJson, JsonOpts) ?? new PgVectorConfig();
                        var pgEngine = new PgVectorSearchEngine(pgConfig);
                        await pgEngine.TestConnectionAsync(ct);
                        return Results.Ok(new { success = true, message = "Connected to PostgreSQL.", latencyMs = sw.ElapsedMilliseconds });

                    case "qdrant":
                        var qdConfig = JsonSerializer.Deserialize<QdrantConfig>(doc.ConfigJson, JsonOpts) ?? new QdrantConfig();
                        var qdEngine = new QdrantSearchEngine(qdConfig);
                        await qdEngine.TestConnectionAsync(ct);
                        return Results.Ok(new { success = true, message = "Connected to Qdrant.", latencyMs = sw.ElapsedMilliseconds });

                    default:
                        JsonDocument.Parse(doc.ConfigJson);
                        return Results.Ok(new { success = true, message = "Configuration is valid.", latencyMs = sw.ElapsedMilliseconds });
                }
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = ex.Message, latencyMs = sw.ElapsedMilliseconds });
            }
        });
    }

    /// <summary>
    /// Mask 敏感欄位（password, apiKey）為 "••••••••"。
    /// </summary>
    private static DataSourceDocument MaskSensitiveFields(DataSourceDocument doc)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(doc.ConfigJson);
            var root = jsonDoc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return doc;
            }

            var hasSensitive = false;
            foreach (var prop in root.EnumerateObject())
            {
                if (SensitiveFields.Contains(prop.Name) && prop.Value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(prop.Value.GetString()))
                {
                    hasSensitive = true;
                    break;
                }
            }

            if (!hasSensitive)
            {
                return doc;
            }

            var dict = new Dictionary<string, object?>();
            foreach (var prop in root.EnumerateObject())
            {
                if (SensitiveFields.Contains(prop.Name) && prop.Value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(prop.Value.GetString()))
                {
                    dict[prop.Name] = "••••••••";
                }
                else
                {
                    dict[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.GetDecimal(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.GetRawText()
                    };
                }
            }

            return new DataSourceDocument
            {
                Id = doc.Id,
                UserId = doc.UserId,
                Name = doc.Name,
                Description = doc.Description,
                Provider = doc.Provider,
                ConfigJson = JsonSerializer.Serialize(dict),
                CreatedAt = doc.CreatedAt,
                UpdatedAt = doc.UpdatedAt
            };
        }
        catch
        {
            return doc;
        }
    }

    /// <summary>
    /// 合併新舊 ConfigJson：如果新值的敏感欄位為空字串或 mask 值，保留舊值。
    /// </summary>
    private static string MergeConfigPreservingSecrets(string existingJson, string newJson)
    {
        try
        {
            using var existingDoc = JsonDocument.Parse(existingJson);
            using var newDoc = JsonDocument.Parse(newJson);

            var merged = new Dictionary<string, object?>();
            foreach (var prop in newDoc.RootElement.EnumerateObject())
            {
                if (SensitiveFields.Contains(prop.Name))
                {
                    var newVal = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : "";
                    if (string.IsNullOrEmpty(newVal) || newVal == "••••••••")
                    {
                        // 保留舊值
                        if (existingDoc.RootElement.TryGetProperty(prop.Name, out var oldProp))
                        {
                            merged[prop.Name] = oldProp.ValueKind == JsonValueKind.String ? oldProp.GetString() : "";
                        }

                        continue;
                    }
                }

                merged[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetDecimal(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.GetRawText()
                };
            }

            return JsonSerializer.Serialize(merged);
        }
        catch
        {
            return newJson;
        }
    }

    private static IResult DsNotFound(string id) =>
        Results.NotFound(new ApiError("DS_NOT_FOUND", Params: new() { ["id"] = id }));
}

public record CreateDataSourceRequest(string Name, string? Description, string Provider, string? ConfigJson);
public record UpdateDataSourceRequest(string Name, string? Description, string? Provider, string? ConfigJson);
