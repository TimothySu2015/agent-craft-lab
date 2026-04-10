using AgentCraftLab.Data;
using AgentCraftLab.Engine.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentCraftLab.Api.Endpoints;

public static class ScheduleEndpoints
{
    public static void MapScheduleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/schedules");

        group.MapGet("/", async ([FromServices] IScheduleStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            return Results.Ok(await store.ListAsync(userId));
        });

        group.MapGet("/{id}", async (string id, [FromServices] IScheduleStore store) =>
        {
            var doc = await store.GetAsync(id);
            return doc is not null ? Results.Ok(doc) : Results.NotFound();
        });

        group.MapPost("/", async (UpsertScheduleRequest req,
            [FromServices] IScheduleStore store,
            [FromServices] IWorkflowStore wfStore,
            IUserContext userCtx) =>
        {
            if (string.IsNullOrWhiteSpace(req.WorkflowId))
            {
                return Results.BadRequest(new ApiError("SCHED_WORKFLOW_REQUIRED", "WorkflowId is required"));
            }

            if (string.IsNullOrWhiteSpace(req.CronExpression))
            {
                return Results.BadRequest(new ApiError("SCHED_CRON_REQUIRED", "CronExpression is required"));
            }

            var userId = await userCtx.GetUserIdAsync();
            var workflow = await wfStore.GetAsync(req.WorkflowId);

            var doc = new ScheduleDocument
            {
                Id = req.Id ?? $"sched-{Guid.NewGuid():N}"[..15],
                UserId = userId,
                WorkflowId = req.WorkflowId,
                WorkflowName = workflow?.Name ?? req.WorkflowId,
                CronExpression = req.CronExpression,
                TimeZone = req.TimeZone ?? "UTC",
                Enabled = req.Enabled ?? true,
                DefaultInput = req.DefaultInput ?? "",
            };

            var result = await store.UpsertAsync(doc);
            return Results.Created($"/api/schedules/{result.Id}", result);
        });

        group.MapPatch("/{id}/toggle", async (string id,
            [FromServices] IScheduleStore store, IUserContext userCtx) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();

            doc.Enabled = !doc.Enabled;
            doc.UpdatedAt = DateTime.UtcNow;
            var result = await store.UpsertAsync(doc);
            return Results.Ok(result);
        });

        group.MapDelete("/{id}", async (string id,
            [FromServices] IScheduleStore store, IUserContext userCtx) =>
        {
            var userId = await userCtx.GetUserIdAsync();
            var ok = await store.DeleteAsync(userId, id);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/{id}/logs", async (string id, int? limit,
            [FromServices] IScheduleStore store) =>
        {
            var logs = await store.GetLogsAsync(id, limit ?? 20);
            return Results.Ok(logs);
        });
    }
}

public record UpsertScheduleRequest(
    string? Id,
    string WorkflowId,
    string CronExpression,
    string? TimeZone,
    bool? Enabled,
    string? DefaultInput
);
