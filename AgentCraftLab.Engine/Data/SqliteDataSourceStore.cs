using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Engine.Data;

public class SqliteDataSourceStore(IServiceScopeFactory scopeFactory) : IDataSourceStore
{
    private static string GenerateId() => $"ds-{Guid.NewGuid():N}"[..10];

    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task<DataSourceDocument> SaveAsync(DataSourceDocument doc)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        doc.Id = GenerateId();
        doc.CreatedAt = DateTime.UtcNow;
        doc.UpdatedAt = DateTime.UtcNow;

        db.DataSources.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<List<DataSourceDocument>> ListAsync(string userId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.DataSources
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<DataSourceDocument?> GetAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.DataSources.FindAsync(id);
    }

    public async Task<DataSourceDocument?> UpdateAsync(string userId, string id, string name, string description, string provider, string configJson)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.DataSources.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);
        if (doc is null)
        {
            return null;
        }

        doc.Name = name;
        doc.Description = description;
        doc.Provider = provider;
        doc.ConfigJson = configJson;
        doc.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.DataSources.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);
        if (doc is null)
        {
            return false;
        }

        db.DataSources.Remove(doc);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<int> CountKbReferencesAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.KnowledgeBases.CountAsync(k => k.DataSourceId == id && !k.IsDeleted);
    }
}
