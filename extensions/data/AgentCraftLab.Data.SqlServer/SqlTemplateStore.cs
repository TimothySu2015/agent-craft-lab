using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Data.SqlServer;

public class SqlTemplateStore(IServiceScopeFactory scopeFactory) : ITemplateStore
{
    private static string GenerateId() => $"tpl-{Guid.NewGuid():N}"[..11];

    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task<TemplateDocument> SaveAsync(string userId, string name, string description, string category, string icon, List<string> tags, string workflowJson)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = new TemplateDocument
        {
            Id = GenerateId(),
            UserId = userId,
            Name = name,
            Description = description,
            Category = category,
            Icon = icon,
            WorkflowJson = workflowJson,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        doc.SetTags(tags);

        db.Templates.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<List<TemplateDocument>> ListAsync(string userId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Templates
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync();
    }

    public async Task<TemplateDocument?> GetAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Templates.FindAsync(id);
    }

    public async Task<TemplateDocument?> UpdateAsync(string userId, string id, string name, string description, string category, string icon, List<string> tags, string? workflowJson)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.Templates.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
        if (doc is null)
        {
            return null;
        }

        doc.Name = name;
        doc.Description = description;
        doc.Category = category;
        doc.Icon = icon;
        doc.SetTags(tags);
        if (workflowJson is not null)
        {
            doc.WorkflowJson = workflowJson;
        }

        doc.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.Templates.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
        if (doc is null)
        {
            return false;
        }

        db.Templates.Remove(doc);
        await db.SaveChangesAsync();
        return true;
    }
}
