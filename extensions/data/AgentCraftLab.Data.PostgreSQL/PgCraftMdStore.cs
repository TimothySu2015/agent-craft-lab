using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Data.PostgreSQL;

/// <summary>
/// craft.md 的 PostgreSQL 實作 — 儲存使用者自訂的 Agent 行為規範。
/// 查詢邏輯：先找 workflow 專屬 → 找不到用使用者預設 → 都沒有回傳 null。
/// </summary>
public class PgCraftMdStore(IServiceScopeFactory scopeFactory) : ICraftMdStore
{
    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task<CraftMdDocument> SaveAsync(string userId, string? workflowId, string content)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var existing = await db.CraftMds
            .FirstOrDefaultAsync(c => c.UserId == userId && c.WorkflowId == workflowId);

        if (existing is not null)
        {
            existing.Content = content;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new CraftMdDocument
            {
                Id = $"craft-{Guid.NewGuid():N}"[..16],
                UserId = userId,
                WorkflowId = workflowId,
                Content = content,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.CraftMds.Add(existing);
        }

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<string?> GetContentAsync(string userId, string? workflowId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        // 先找 workflow 專屬
        if (workflowId is not null)
        {
            var workflowDoc = await db.CraftMds
                .FirstOrDefaultAsync(c => c.UserId == userId && c.WorkflowId == workflowId);
            if (workflowDoc is not null)
            {
                return workflowDoc.Content;
            }
        }

        // 找不到 → 使用者預設
        var defaultDoc = await db.CraftMds
            .FirstOrDefaultAsync(c => c.UserId == userId && c.WorkflowId == null);

        return defaultDoc?.Content;
    }

    public async Task<CraftMdDocument?> GetAsync(string userId, string? workflowId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.CraftMds
            .FirstOrDefaultAsync(c => c.UserId == userId && c.WorkflowId == workflowId);
    }

    public async Task<bool> DeleteAsync(string userId, string? workflowId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.CraftMds
            .FirstOrDefaultAsync(c => c.UserId == userId && c.WorkflowId == workflowId);

        if (doc is null)
        {
            return false;
        }

        db.CraftMds.Remove(doc);
        await db.SaveChangesAsync();
        return true;
    }
}
