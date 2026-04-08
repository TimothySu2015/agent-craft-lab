using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Data.Sqlite;

public class SqliteScheduleStore(IServiceScopeFactory scopeFactory) : IScheduleStore
{
    private static string GenerateId() => $"sch-{Guid.NewGuid():N}"[..12];
    private static string GenerateLogId() => $"slog-{Guid.NewGuid():N}"[..14];

    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task<List<ScheduleDocument>> GetActiveSchedulesAsync()
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;
        return await db.Schedules.Where(s => s.Enabled).ToListAsync();
    }

    public async Task<List<ScheduleDocument>> ListAsync(string userId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;
        return await db.Schedules
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<ScheduleDocument?> GetAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;
        return await db.Schedules.FindAsync(id);
    }

    public async Task<ScheduleDocument> UpsertAsync(ScheduleDocument schedule)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        if (string.IsNullOrEmpty(schedule.Id))
        {
            schedule.Id = GenerateId();
            schedule.CreatedAt = DateTime.UtcNow;
        }

        schedule.UpdatedAt = DateTime.UtcNow;

        var existing = await db.Schedules.FindAsync(schedule.Id);
        if (existing is null)
        {
            db.Schedules.Add(schedule);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(schedule);
        }

        await db.SaveChangesAsync();
        return schedule;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var entity = await db.Schedules.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (entity is null) return false;

        db.Schedules.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<ScheduleLogDocument>> GetLogsAsync(string scheduleId, int limit = 20)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;
        return await db.ScheduleLogs
            .Where(l => l.ScheduleId == scheduleId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task AddLogAsync(ScheduleLogDocument log)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        if (string.IsNullOrEmpty(log.Id))
        {
            log.Id = GenerateLogId();
        }

        log.CreatedAt = DateTime.UtcNow;
        db.ScheduleLogs.Add(log);
        await db.SaveChangesAsync();
    }
}
