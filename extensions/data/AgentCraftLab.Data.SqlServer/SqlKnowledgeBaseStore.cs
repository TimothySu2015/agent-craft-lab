using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Data.SqlServer;

public class SqlKnowledgeBaseStore(IServiceScopeFactory scopeFactory) : IKnowledgeBaseStore
{
    private static string GenerateKbId() => $"kb-{Guid.NewGuid():N}"[..10];
    private static string GenerateFileId() => $"kbf-{Guid.NewGuid():N}"[..11];

    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task<KnowledgeBaseDocument> SaveAsync(string userId, string name, string description,
        string embeddingModel, int chunkSize, int chunkOverlap, string? dataSourceId = null,
        string chunkStrategy = "fixed")
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var id = GenerateKbId();
        var doc = new KnowledgeBaseDocument
        {
            Id = id,
            UserId = userId,
            Name = name,
            Description = description,
            IndexName = $"{userId}_kb_{id}",
            EmbeddingModel = embeddingModel,
            ChunkSize = chunkSize,
            ChunkOverlap = chunkOverlap,
            ChunkStrategy = chunkStrategy,
            DataSourceId = dataSourceId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.KnowledgeBases.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<List<KnowledgeBaseDocument>> ListAsync(string userId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.KnowledgeBases
            .Where(k => k.UserId == userId && !k.IsDeleted)
            .OrderByDescending(k => k.UpdatedAt)
            .ToListAsync();
    }

    public async Task<KnowledgeBaseDocument?> GetAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.KnowledgeBases.FindAsync(id);
    }

    public async Task<KnowledgeBaseDocument?> UpdateAsync(string userId, string id, string name, string description)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.KnowledgeBases.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId && !k.IsDeleted);
        if (doc is null)
        {
            return null;
        }

        doc.Name = name;
        doc.Description = description;
        doc.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.KnowledgeBases.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId && !k.IsDeleted);
        if (doc is null)
        {
            return false;
        }

        doc.IsDeleted = true;
        doc.DeletedAt = DateTime.UtcNow;
        doc.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<KbFileDocument> AddFileAsync(string knowledgeBaseId, string fileName, string mimeType,
        long fileSize, List<string> chunkIds)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = new KbFileDocument
        {
            Id = GenerateFileId(),
            KnowledgeBaseId = knowledgeBaseId,
            FileName = fileName,
            MimeType = mimeType,
            FileSize = fileSize,
            ChunkCount = chunkIds.Count,
            CreatedAt = DateTime.UtcNow
        };
        doc.SetChunkIds(chunkIds);

        db.KbFiles.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<KbFileDocument?> GetFileAsync(string knowledgeBaseId, string fileId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.KbFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.KnowledgeBaseId == knowledgeBaseId);
    }

    public async Task<List<KbFileDocument>> ListFilesAsync(string knowledgeBaseId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.KbFiles
            .Where(f => f.KnowledgeBaseId == knowledgeBaseId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> RemoveFileAsync(string knowledgeBaseId, string fileId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.KbFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.KnowledgeBaseId == knowledgeBaseId);
        if (doc is null)
        {
            return false;
        }

        db.KbFiles.Remove(doc);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task UpdateStatsAsync(string id, int fileCount, long totalChunks)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.KnowledgeBases.FindAsync(id);
        if (doc is null)
        {
            return;
        }

        doc.FileCount = fileCount;
        doc.TotalChunks = totalChunks;
        doc.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task<List<KnowledgeBaseDocument>> GetPendingDeletionsAsync(TimeSpan delay)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var cutoff = DateTime.UtcNow - delay;
        return await db.KnowledgeBases
            .Where(k => k.IsDeleted && k.DeletedAt != null && k.DeletedAt < cutoff)
            .ToListAsync();
    }

    public async Task HardDeleteAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        // 刪除所有關聯檔案
        var files = await db.KbFiles.Where(f => f.KnowledgeBaseId == id).ToListAsync();
        db.KbFiles.RemoveRange(files);

        var kb = await db.KnowledgeBases.FindAsync(id);
        if (kb is not null)
        {
            db.KnowledgeBases.Remove(kb);
        }

        await db.SaveChangesAsync();
    }
}
