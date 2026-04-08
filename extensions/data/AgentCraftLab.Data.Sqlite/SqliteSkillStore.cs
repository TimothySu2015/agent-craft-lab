using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Data.Sqlite;

public class SqliteSkillStore(IServiceScopeFactory scopeFactory) : ISkillStore
{
    private static string GenerateId() => $"sk-{Guid.NewGuid():N}"[..10];

    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task<SkillDocument> SaveAsync(string userId, string name, string description, string category, string icon, string instructions, List<string> tools)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = new SkillDocument
        {
            Id = GenerateId(),
            UserId = userId,
            Name = name,
            Description = description,
            Category = category,
            Icon = icon,
            Instructions = instructions,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        doc.SetTools(tools);

        db.Skills.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<List<SkillDocument>> ListAsync(string userId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Skills
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<SkillDocument?> GetAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Skills.FindAsync(id);
    }

    public async Task<SkillDocument?> UpdateAsync(string userId, string id, string name, string description, string category, string icon, string instructions, List<string> tools)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.Skills.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (doc is null)
        {
            return null;
        }

        doc.Name = name;
        doc.Description = description;
        doc.Category = category;
        doc.Icon = icon;
        doc.Instructions = instructions;
        doc.SetTools(tools);
        doc.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.Skills.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (doc is null)
        {
            return false;
        }

        db.Skills.Remove(doc);
        await db.SaveChangesAsync();
        return true;
    }
}
