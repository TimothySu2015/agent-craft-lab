using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Engine.Data;

public class SqliteRequestLogStore(IServiceScopeFactory scopeFactory) : IRequestLogStore
{
    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task LogAsync(RequestLogDocument log)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        db.RequestLogs.Add(log);
        await db.SaveChangesAsync();
    }

    public async Task<List<RequestLogDocument>> QueryAsync(DateTime? from = null, DateTime? to = null,
        string? protocol = null, string? workflowKey = null, string? userId = null, int limit = 200)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        IQueryable<RequestLogDocument> query = db.RequestLogs;

        if (from.HasValue)
        {
            query = query.Where(l => l.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(l => l.CreatedAt <= to.Value);
        }

        if (!string.IsNullOrEmpty(protocol))
        {
            query = query.Where(l => l.Protocol == protocol);
        }

        if (!string.IsNullOrEmpty(workflowKey))
        {
            query = query.Where(l => l.WorkflowKey == workflowKey);
        }

        if (!string.IsNullOrEmpty(userId))
        {
            query = query.Where(l => l.UserId == userId);
        }

        return await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<string?> GetTraceJsonAsync(string logId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.RequestLogs
            .Where(l => l.Id == logId)
            .Select(l => l.TraceJson)
            .FirstOrDefaultAsync();
    }

    public async Task<AnalyticsSummary> GetSummaryAsync(DateTime from, string? userId = null)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        IQueryable<RequestLogDocument> query = db.RequestLogs.Where(l => l.CreatedAt >= from);

        if (!string.IsNullOrEmpty(userId))
        {
            query = query.Where(l => l.UserId == userId);
        }

        var logs = await query.ToListAsync();

        return new AnalyticsSummary
        {
            TotalCalls = logs.Count,
            SuccessCount = logs.Count(l => l.Success),
            FailCount = logs.Count(l => !l.Success),
            AvgElapsedMs = logs.Count > 0 ? (long)logs.Average(l => l.ElapsedMs) : 0,
            ByProtocol = logs.GroupBy(l => l.Protocol)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByWorkflow = logs.GroupBy(l => l.WorkflowName)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }
}
