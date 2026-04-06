using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Api.Endpoints;

public static class TemplateEndpoints
{
    public static void MapTemplateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/templates");

        group.MapGet("/", async (ITemplateStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            return Results.Ok(await store.ListAsync(userId));
        });

        group.MapPost("/", async (CreateTemplateRequest req, ITemplateStore store, IUserContext userCtx) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new ApiError("TEMPLATE_NAME_REQUIRED"));
            var userId = await userCtx.GetUserIdAsync();
            var doc = await store.SaveAsync(userId, req.Name, req.Description ?? "", req.Category ?? "", req.Icon ?? "", req.Tags ?? [], req.WorkflowJson ?? "");
            return Results.Created($"/api/templates/{doc.Id}", doc);
        });

        group.MapPut("/{id}", async (string id, CreateTemplateRequest req, ITemplateStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var doc = await store.UpdateAsync(userId, id, req.Name ?? "", req.Description ?? "", req.Category ?? "", req.Icon ?? "", req.Tags ?? [], req.WorkflowJson);
            return doc is not null ? Results.Ok(doc) : Results.NotFound(new ApiError("TEMPLATE_NOT_FOUND", Params: new() { ["id"] = id }));
        });

        group.MapDelete("/{id}", async (string id, ITemplateStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var ok = await store.DeleteAsync(userId, id);
            return ok ? Results.NoContent() : Results.NotFound(new ApiError("TEMPLATE_NOT_FOUND", Params: new() { ["id"] = id }));
        });
    }
}

public record CreateTemplateRequest(string? Name, string? Description, string? Category, string? Icon, List<string>? Tags, string? WorkflowJson);
