using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Data.Sqlite;

public class SqliteWorkflowStore(IServiceScopeFactory scopeFactory) : IWorkflowStore
{
    private static string GenerateId() => $"wf-{Guid.NewGuid():N}"[..10];

    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task<WorkflowDocument> SaveAsync(string userId, string name, string description, string type, string workflowJson)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = new WorkflowDocument
        {
            Id = GenerateId(),
            UserId = userId,
            Name = name,
            Description = description,
            Type = type,
            WorkflowJson = workflowJson,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Workflows.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<WorkflowDocument?> GetAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Workflows.FindAsync(id);
    }

    public async Task<List<WorkflowDocument>> ListAsync(string userId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Workflows
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync();
    }

    public async Task<WorkflowDocument?> UpdateAsync(string userId, string id, string name, string description, string type, string workflowJson)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.Workflows.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);
        if (doc is null)
        {
            return null;
        }

        doc.Name = name;
        doc.Description = description;
        doc.Type = type;
        doc.WorkflowJson = workflowJson;
        doc.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.Workflows.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);
        if (doc is null)
        {
            return false;
        }

        db.Workflows.Remove(doc);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetPublishedAsync(string userId, string id, bool isPublished, List<string>? inputModes = null)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.Workflows.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);
        if (doc is null)
        {
            return false;
        }

        doc.IsPublished = isPublished;
        if (inputModes is not null)
        {
            doc.SetInputModes(inputModes);
        }

        doc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateTypeAsync(string userId, string id, List<string> types)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.Workflows.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);
        if (doc is null)
        {
            return false;
        }

        doc.SetTypes(types);
        doc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<WorkflowDocument>> ListPublishedAsync()
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Workflows
            .Where(w => w.IsPublished)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync();
    }
}
