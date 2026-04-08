using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Data.Sqlite;

public class SqliteRefineryStore(IServiceScopeFactory scopeFactory) : IRefineryStore
{
    private static string GenerateProjectId() => $"ref-{Guid.NewGuid():N}"[..10];
    private static string GenerateFileId() => $"reff-{Guid.NewGuid():N}"[..11];
    private static string GenerateOutputId() => $"refo-{Guid.NewGuid():N}"[..11];

    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    // ── Project CRUD ──

    public async Task<RefineryProject> SaveAsync(string userId, string name, string description,
        string? schemaTemplateId, string? customSchemaJson,
        string provider, string model, string? outputLanguage,
        string extractionMode = "fast", bool enableChallenge = false,
        string imageProcessingMode = "skip")
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var id = GenerateProjectId();
        var doc = new RefineryProject
        {
            Id = id,
            UserId = userId,
            Name = name,
            Description = description,
            SchemaTemplateId = schemaTemplateId,
            CustomSchemaJson = customSchemaJson,
            Provider = provider,
            Model = model,
            OutputLanguage = outputLanguage,
            ExtractionMode = extractionMode,
            EnableChallenge = enableChallenge,
            ImageProcessingMode = imageProcessingMode,
            IndexName = $"{userId}_refinery_{id}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.RefineryProjects.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<List<RefineryProject>> ListAsync(string userId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.RefineryProjects
            .Where(p => p.UserId == userId && !p.IsDeleted)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync();
    }

    public async Task<RefineryProject?> GetAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.RefineryProjects.FindAsync(id);
    }

    public async Task<RefineryProject?> UpdateAsync(string userId, string id, string name, string description,
        string? schemaTemplateId, string? customSchemaJson,
        string provider, string model, string? outputLanguage,
        string extractionMode = "fast", bool enableChallenge = false,
        string imageProcessingMode = "skip")
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.RefineryProjects.FindAsync(id);
        if (doc is null || doc.UserId != userId)
        {
            return null;
        }

        doc.Name = name;
        doc.Description = description;
        doc.SchemaTemplateId = schemaTemplateId;
        doc.CustomSchemaJson = customSchemaJson;
        doc.Provider = provider;
        doc.Model = model;
        doc.OutputLanguage = outputLanguage;
        doc.ExtractionMode = extractionMode;
        doc.EnableChallenge = enableChallenge;
        doc.ImageProcessingMode = imageProcessingMode;
        doc.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.RefineryProjects.FindAsync(id);
        if (doc is null || doc.UserId != userId)
        {
            return false;
        }

        doc.IsDeleted = true;
        doc.DeletedAt = DateTime.UtcNow;
        doc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    // ── File management ──

    public async Task<RefineryFile> AddFileAsync(string projectId, string fileName, string mimeType,
        long fileSize, string cleanedJson, int elementCount, string indexStatus = "Pending")
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = new RefineryFile
        {
            Id = GenerateFileId(),
            RefineryProjectId = projectId,
            FileName = fileName,
            MimeType = mimeType,
            FileSize = fileSize,
            CleanedJson = cleanedJson,
            ElementCount = elementCount,
            IndexStatus = indexStatus,
            CreatedAt = DateTime.UtcNow,
        };

        db.RefineryFiles.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<List<RefineryFile>> ListFilesAsync(string projectId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.RefineryFiles
            .Where(f => f.RefineryProjectId == projectId)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync();
    }

    public async Task<RefineryFile?> GetFileAsync(string projectId, string fileId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.RefineryFiles
            .FirstOrDefaultAsync(f => f.Id == fileId && f.RefineryProjectId == projectId);
    }

    public async Task<bool> RemoveFileAsync(string projectId, string fileId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.RefineryFiles
            .FirstOrDefaultAsync(f => f.Id == fileId && f.RefineryProjectId == projectId);
        if (doc is null)
        {
            return false;
        }

        db.RefineryFiles.Remove(doc);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task UpdateStatsAsync(string id, int fileCount)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.RefineryProjects.FindAsync(id);
        if (doc is null)
        {
            return;
        }

        doc.FileCount = fileCount;
        doc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task UpdateFileIndexStatusAsync(string fileId, string status, string? chunkIds = null, int? chunkCount = null)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.RefineryFiles.FindAsync(fileId);
        if (doc is null)
        {
            return;
        }

        doc.IndexStatus = status;
        if (chunkIds is not null)
        {
            doc.ChunkIds = chunkIds;
        }

        if (chunkCount.HasValue)
        {
            doc.ChunkCount = chunkCount.Value;
        }

        await db.SaveChangesAsync();
    }

    public async Task ToggleFileIncludedAsync(string fileId, bool isIncluded)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.RefineryFiles.FindAsync(fileId);
        if (doc is not null)
        {
            doc.IsIncluded = isIncluded;
            await db.SaveChangesAsync();
        }
    }

    // ── Output versioning ──

    public async Task<RefineryOutput> AddOutputAsync(string projectId, int version,
        string? schemaTemplateId, string schemaName,
        string outputJson, string outputMarkdown,
        string missingFields, string openQuestions,
        string challenges, float overallConfidence,
        string sourceFiles, int sourceFileCount)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = new RefineryOutput
        {
            Id = GenerateOutputId(),
            RefineryProjectId = projectId,
            Version = version,
            SchemaTemplateId = schemaTemplateId,
            SchemaName = schemaName,
            OutputJson = outputJson,
            OutputMarkdown = outputMarkdown,
            MissingFields = missingFields,
            OpenQuestions = openQuestions,
            Challenges = challenges,
            OverallConfidence = overallConfidence,
            SourceFiles = sourceFiles,
            SourceFileCount = sourceFileCount,
            CreatedAt = DateTime.UtcNow,
        };

        db.RefineryOutputs.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<List<RefineryOutput>> ListOutputsAsync(string projectId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.RefineryOutputs
            .Where(o => o.RefineryProjectId == projectId)
            .OrderByDescending(o => o.Version)
            .ToListAsync();
    }

    public async Task<RefineryOutput?> GetOutputAsync(string projectId, int version)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.RefineryOutputs
            .FirstOrDefaultAsync(o => o.RefineryProjectId == projectId && o.Version == version);
    }

    public async Task<RefineryOutput?> GetLatestOutputAsync(string projectId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.RefineryOutputs
            .Where(o => o.RefineryProjectId == projectId)
            .OrderByDescending(o => o.Version)
            .FirstOrDefaultAsync();
    }

    // ── Cleanup ──

    public async Task<List<RefineryProject>> GetPendingDeletionsAsync(TimeSpan delay)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var cutoff = DateTime.UtcNow - delay;
        return await db.RefineryProjects
            .Where(p => p.IsDeleted && p.DeletedAt < cutoff)
            .ToListAsync();
    }

    public async Task HardDeleteAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var files = await db.RefineryFiles.Where(f => f.RefineryProjectId == id).ToListAsync();
        db.RefineryFiles.RemoveRange(files);

        var outputs = await db.RefineryOutputs.Where(o => o.RefineryProjectId == id).ToListAsync();
        db.RefineryOutputs.RemoveRange(outputs);

        var project = await db.RefineryProjects.FindAsync(id);
        if (project is not null)
        {
            db.RefineryProjects.Remove(project);
        }

        await db.SaveChangesAsync();
    }
}
