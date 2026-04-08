using AgentCraftLab.Data;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

public class MongoScheduleStore : IScheduleStore
{
    private readonly MongoDbContext _db;

    public MongoScheduleStore(MongoDbContext db) => _db = db;

    private IMongoCollection<ScheduleDocument> Schedules => _db.Schedules;
    private IMongoCollection<ScheduleLogDocument> ScheduleLogs => _db.ScheduleLogs;

    public async Task<List<ScheduleDocument>> GetActiveSchedulesAsync()
    {
        return await Schedules
            .Find(s => s.Enabled)
            .ToListAsync();
    }

    public async Task<List<ScheduleDocument>> ListAsync(string userId)
    {
        return await Schedules
            .Find(s => s.UserId == userId)
            .SortByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<ScheduleDocument?> GetAsync(string id)
    {
        return await Schedules
            .Find(s => s.Id == id)
            .FirstOrDefaultAsync();
    }

    public async Task<ScheduleDocument> UpsertAsync(ScheduleDocument schedule)
    {
        if (string.IsNullOrEmpty(schedule.Id))
        {
            schedule.Id = $"sch-{Guid.NewGuid():N}"[..12];
            schedule.CreatedAt = DateTime.UtcNow;
        }

        schedule.UpdatedAt = DateTime.UtcNow;

        await Schedules.ReplaceOneAsync(
            s => s.Id == schedule.Id,
            schedule,
            new ReplaceOptions { IsUpsert = true });

        return schedule;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var result = await Schedules.DeleteOneAsync(s => s.Id == id && s.UserId == userId);
        return result.DeletedCount > 0;
    }

    public async Task<List<ScheduleLogDocument>> GetLogsAsync(string scheduleId, int limit = 20)
    {
        return await ScheduleLogs
            .Find(l => l.ScheduleId == scheduleId)
            .SortByDescending(l => l.CreatedAt)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task AddLogAsync(ScheduleLogDocument log)
    {
        if (string.IsNullOrEmpty(log.Id))
        {
            log.Id = $"slog-{Guid.NewGuid():N}"[..14];
        }

        log.CreatedAt = DateTime.UtcNow;
        await ScheduleLogs.InsertOneAsync(log);
    }
}
