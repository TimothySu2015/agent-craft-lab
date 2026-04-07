using AgentCraftLab.Engine.Data;
using MongoDB.Driver;

namespace AgentCraftLab.MongoDB;

/// <summary>
/// 請求日誌服務 — 記錄 A2A/MCP/API 端點呼叫，供 Analytics 統計。
/// </summary>
public class MongoRequestLogStore(MongoDbContext db) : IRequestLogStore
{
    public async Task LogAsync(RequestLogDocument log)
    {
        await db.RequestLogs.InsertOneAsync(log);
    }

    public async Task<List<RequestLogDocument>> QueryAsync(DateTime? from = null, DateTime? to = null,
        string? protocol = null, string? workflowKey = null, string? userId = null, int limit = 200)
    {
        var builder = Builders<RequestLogDocument>.Filter;
        var filter = builder.Empty;

        if (from.HasValue)
        {
            filter &= builder.Gte(l => l.CreatedAt, from.Value);
        }

        if (to.HasValue)
        {
            filter &= builder.Lte(l => l.CreatedAt, to.Value);
        }

        if (!string.IsNullOrEmpty(protocol))
        {
            filter &= builder.Eq(l => l.Protocol, protocol);
        }

        if (!string.IsNullOrEmpty(workflowKey))
        {
            filter &= builder.Eq(l => l.WorkflowKey, workflowKey);
        }

        if (!string.IsNullOrEmpty(userId))
        {
            filter &= builder.Eq(l => l.UserId, userId);
        }

        return await db.RequestLogs
            .Find(filter)
            .SortByDescending(l => l.CreatedAt)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<string?> GetTraceJsonAsync(string logId)
    {
        var doc = await db.RequestLogs
            .Find(Builders<RequestLogDocument>.Filter.Eq(l => l.Id, logId))
            .FirstOrDefaultAsync();
        return doc?.TraceJson;
    }

    public async Task<AnalyticsSummary> GetSummaryAsync(DateTime from, string? userId = null)
    {
        var builder = Builders<RequestLogDocument>.Filter;
        var filter = builder.Gte(l => l.CreatedAt, from);

        if (!string.IsNullOrEmpty(userId))
        {
            filter &= builder.Eq(l => l.UserId, userId);
        }

        var logs = await db.RequestLogs.Find(filter).ToListAsync();

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
