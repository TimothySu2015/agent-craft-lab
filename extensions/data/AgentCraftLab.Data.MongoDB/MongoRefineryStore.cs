using AgentCraftLab.Data;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

public class MongoRefineryStore(MongoDbContext db) : IRefineryStore
{
    private static string GenerateProjectId() => $"ref-{Guid.NewGuid():N}"[..10];
    private static string GenerateFileId() => $"reff-{Guid.NewGuid():N}"[..11];
    private static string GenerateOutputId() => $"refo-{Guid.NewGuid():N}"[..11];

    // ── Project CRUD ──

    public async Task<RefineryProject> SaveAsync(string userId, string name, string description,
        string? schemaTemplateId, string? customSchemaJson,
        string provider, string model, string? outputLanguage,
        string extractionMode = "fast", bool enableChallenge = false,
        string imageProcessingMode = "skip")
    {
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
            UpdatedAt = DateTime.UtcNow
        };

        await db.RefineryProjects.InsertOneAsync(doc);
        return doc;
    }

    public async Task<List<RefineryProject>> ListAsync(string userId)
    {
        return await db.RefineryProjects
            .Find(p => p.UserId == userId && !p.IsDeleted)
            .SortByDescending(p => p.UpdatedAt)
            .ToListAsync();
    }

    public async Task<RefineryProject?> GetAsync(string id)
    {
        return await db.RefineryProjects.Find(p => p.Id == id).FirstOrDefaultAsync();
    }

    public async Task<RefineryProject?> UpdateAsync(string userId, string id, string name, string description,
        string? schemaTemplateId, string? customSchemaJson,
        string provider, string model, string? outputLanguage,
        string extractionMode = "fast", bool enableChallenge = false,
        string imageProcessingMode = "skip")
    {
        return await db.RefineryProjects.FindOneAndUpdateAsync(
            p => p.Id == id && p.UserId == userId,
            Builders<RefineryProject>.Update
                .Set(p => p.Name, name)
                .Set(p => p.Description, description)
                .Set(p => p.SchemaTemplateId, schemaTemplateId)
                .Set(p => p.CustomSchemaJson, customSchemaJson)
                .Set(p => p.Provider, provider)
                .Set(p => p.Model, model)
                .Set(p => p.OutputLanguage, outputLanguage)
                .Set(p => p.ExtractionMode, extractionMode)
                .Set(p => p.EnableChallenge, enableChallenge)
                .Set(p => p.ImageProcessingMode, imageProcessingMode)
                .Set(p => p.UpdatedAt, DateTime.UtcNow),
            new FindOneAndUpdateOptions<RefineryProject> { ReturnDocument = ReturnDocument.After });
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var result = await db.RefineryProjects.UpdateOneAsync(
            p => p.Id == id && p.UserId == userId && !p.IsDeleted,
            Builders<RefineryProject>.Update
                .Set(p => p.IsDeleted, true)
                .Set(p => p.DeletedAt, DateTime.UtcNow)
                .Set(p => p.UpdatedAt, DateTime.UtcNow));

        return result.ModifiedCount > 0;
    }

    public async Task<List<RefineryProject>> GetPendingDeletionsAsync(TimeSpan delay)
    {
        var cutoff = DateTime.UtcNow - delay;
        return await db.RefineryProjects
            .Find(p => p.IsDeleted && p.DeletedAt != null && p.DeletedAt < cutoff)
            .ToListAsync();
    }

    public async Task HardDeleteAsync(string id)
    {
        await db.RefineryFiles.DeleteManyAsync(f => f.RefineryProjectId == id);
        await db.RefineryOutputs.DeleteManyAsync(o => o.RefineryProjectId == id);
        await db.RefineryProjects.DeleteOneAsync(p => p.Id == id);
    }

    // ── File management ──

    public async Task<RefineryFile> AddFileAsync(string projectId, string fileName, string mimeType,
        long fileSize, string cleanedJson, int elementCount, string indexStatus = "Pending")
    {
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
            CreatedAt = DateTime.UtcNow
        };

        await db.RefineryFiles.InsertOneAsync(doc);
        return doc;
    }

    public async Task<List<RefineryFile>> ListFilesAsync(string projectId)
    {
        return await db.RefineryFiles
            .Find(f => f.RefineryProjectId == projectId)
            .SortBy(f => f.CreatedAt)
            .ToListAsync();
    }

    public async Task<RefineryFile?> GetFileAsync(string projectId, string fileId)
    {
        return await db.RefineryFiles
            .Find(f => f.Id == fileId && f.RefineryProjectId == projectId)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> RemoveFileAsync(string projectId, string fileId)
    {
        var result = await db.RefineryFiles.DeleteOneAsync(f => f.Id == fileId && f.RefineryProjectId == projectId);
        return result.DeletedCount > 0;
    }

    public async Task UpdateStatsAsync(string id, int fileCount)
    {
        await db.RefineryProjects.UpdateOneAsync(
            p => p.Id == id,
            Builders<RefineryProject>.Update
                .Set(p => p.FileCount, fileCount)
                .Set(p => p.UpdatedAt, DateTime.UtcNow));
    }

    public async Task UpdateFileIndexStatusAsync(string fileId, string status, string? chunkIds = null, int? chunkCount = null)
    {
        var update = Builders<RefineryFile>.Update.Set(f => f.IndexStatus, status);

        if (chunkIds is not null)
        {
            update = update.Set(f => f.ChunkIds, chunkIds);
        }

        if (chunkCount.HasValue)
        {
            update = update.Set(f => f.ChunkCount, chunkCount.Value);
        }

        await db.RefineryFiles.UpdateOneAsync(f => f.Id == fileId, update);
    }

    public async Task ToggleFileIncludedAsync(string fileId, bool isIncluded)
    {
        await db.RefineryFiles.UpdateOneAsync(
            f => f.Id == fileId,
            Builders<RefineryFile>.Update.Set(f => f.IsIncluded, isIncluded));
    }

    // ── Output versioning ──

    public async Task<RefineryOutput> AddOutputAsync(string projectId, int version,
        string? schemaTemplateId, string schemaName,
        string outputJson, string outputMarkdown,
        string missingFields, string openQuestions,
        string challenges, float overallConfidence,
        string sourceFiles, int sourceFileCount)
    {
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
            CreatedAt = DateTime.UtcNow
        };

        await db.RefineryOutputs.InsertOneAsync(doc);
        return doc;
    }

    public async Task<List<RefineryOutput>> ListOutputsAsync(string projectId)
    {
        return await db.RefineryOutputs
            .Find(o => o.RefineryProjectId == projectId)
            .SortByDescending(o => o.Version)
            .ToListAsync();
    }

    public async Task<RefineryOutput?> GetOutputAsync(string projectId, int version)
    {
        return await db.RefineryOutputs
            .Find(o => o.RefineryProjectId == projectId && o.Version == version)
            .FirstOrDefaultAsync();
    }

    public async Task<RefineryOutput?> GetLatestOutputAsync(string projectId)
    {
        return await db.RefineryOutputs
            .Find(o => o.RefineryProjectId == projectId)
            .SortByDescending(o => o.Version)
            .FirstOrDefaultAsync();
    }
}
