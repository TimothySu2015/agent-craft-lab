using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/analytics/summary", async (
            IRequestLogStore logStore, IUserContext userCtx,
            DateTime? from, string? userId) =>
        {
            var effectiveFrom = from ?? DateTime.UtcNow.AddDays(-1);
            var currentUserId = await userCtx.GetUserIdAsync();
            var summary = await logStore.GetSummaryAsync(effectiveFrom, userId ?? currentUserId);
            return Results.Ok(summary);
        });

        app.MapGet("/api/analytics/logs", async (
            IRequestLogStore logStore, IUserContext userCtx,
            DateTime? from, DateTime? to, string? protocol, int? limit) =>
        {
            var currentUserId = await userCtx.GetUserIdAsync();
            var logs = await logStore.QueryAsync(from, to, protocol, userId: currentUserId, limit: limit ?? 100);
            return Results.Ok(logs);
        });
    }
}
