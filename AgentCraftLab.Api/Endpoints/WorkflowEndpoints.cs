using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/workflows");

        group.MapPost("/", async (CreateWorkflowRequest req, IWorkflowStore store, IUserContext userCtx) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new ApiError("WORKFLOW_NAME_REQUIRED", "Workflow name is required"));
            }

            var userId = await userCtx.GetUserIdAsync();
            var doc = await store.SaveAsync(userId, req.Name, req.Description ?? "", req.Type ?? "", req.WorkflowJson ?? "");
            return Results.Created($"/api/workflows/{doc.Id}", doc);
        });

        group.MapGet("/", async (IWorkflowStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            return Results.Ok(await store.ListAsync(userId));
        });

        group.MapGet("/{id}", async (string id, IWorkflowStore store) =>
        {
            var doc = await store.GetAsync(id);
            return doc is not null ? Results.Ok(doc) : WorkflowNotFound(id);
        });

        group.MapPut("/{id}", async (string id, UpdateWorkflowRequest req, IWorkflowStore store, IUserContext userCtx) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new ApiError("WORKFLOW_NAME_REQUIRED", "Workflow name is required"));
            }

            var userId = await userCtx.GetUserIdAsync();
            var doc = await store.UpdateAsync(userId, id, req.Name, req.Description ?? "", req.Type ?? "", req.WorkflowJson ?? "");
            return doc is not null ? Results.Ok(doc) : WorkflowNotFound(id);
        });

        group.MapDelete("/{id}", async (string id, IWorkflowStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var ok = await store.DeleteAsync(userId, id);
            return ok ? Results.NoContent() : WorkflowNotFound(id);
        });

        group.MapPatch("/{id}/publish", async (string id, PublishRequest req, IWorkflowStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var ok = await store.SetPublishedAsync(userId, id, req.IsPublished, req.InputModes);
            return ok ? Results.Ok() : WorkflowNotFound(id);
        });
    }

    private static IResult WorkflowNotFound(string id) =>
        Results.NotFound(new ApiError("WORKFLOW_NOT_FOUND", Params: new() { ["id"] = id }));
}

public record CreateWorkflowRequest(string Name, string? Description, string? Type, string? WorkflowJson);
public record UpdateWorkflowRequest(string Name, string? Description, string? Type, string? WorkflowJson);
public record PublishRequest(bool IsPublished, List<string>? InputModes);
