using AgentCraftLab.Data;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

public class MongoKnowledgeBaseStore(MongoDbContext db) : IKnowledgeBaseStore
{
    private static string GenerateKbId() => $"kb-{Guid.NewGuid():N}"[..10];
    private static string GenerateFileId() => $"kbf-{Guid.NewGuid():N}"[..11];

    public async Task<KnowledgeBaseDocument> SaveAsync(string userId, string name, string description,
        string embeddingModel, int chunkSize, int chunkOverlap, string? dataSourceId = null,
        string chunkStrategy = "fixed")
    {
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

        await db.KnowledgeBases.InsertOneAsync(doc);
        return doc;
    }

    public async Task<List<KnowledgeBaseDocument>> ListAsync(string userId)
    {
        return await db.KnowledgeBases
            .Find(k => k.UserId == userId && !k.IsDeleted)
            .SortByDescending(k => k.UpdatedAt)
            .ToListAsync();
    }

    public async Task<KnowledgeBaseDocument?> GetAsync(string id)
    {
        return await db.KnowledgeBases.Find(k => k.Id == id).FirstOrDefaultAsync();
    }

    public async Task<KnowledgeBaseDocument?> UpdateAsync(string userId, string id, string name, string description)
    {
        var result = await db.KnowledgeBases.FindOneAndUpdateAsync(
            k => k.Id == id && k.UserId == userId && !k.IsDeleted,
            Builders<KnowledgeBaseDocument>.Update
                .Set(k => k.Name, name)
                .Set(k => k.Description, description)
                .Set(k => k.UpdatedAt, DateTime.UtcNow),
            new FindOneAndUpdateOptions<KnowledgeBaseDocument> { ReturnDocument = ReturnDocument.After });

        return result;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var result = await db.KnowledgeBases.UpdateOneAsync(
            k => k.Id == id && k.UserId == userId && !k.IsDeleted,
            Builders<KnowledgeBaseDocument>.Update
                .Set(k => k.IsDeleted, true)
                .Set(k => k.DeletedAt, DateTime.UtcNow)
                .Set(k => k.UpdatedAt, DateTime.UtcNow));

        return result.ModifiedCount > 0;
    }

    public async Task<KbFileDocument> AddFileAsync(string knowledgeBaseId, string fileName, string mimeType,
        long fileSize, List<string> chunkIds)
    {
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

        await db.KbFiles.InsertOneAsync(doc);
        return doc;
    }

    public async Task<KbFileDocument?> GetFileAsync(string knowledgeBaseId, string fileId)
    {
        return await db.KbFiles.Find(f => f.Id == fileId && f.KnowledgeBaseId == knowledgeBaseId).FirstOrDefaultAsync();
    }

    public async Task<List<KbFileDocument>> ListFilesAsync(string knowledgeBaseId)
    {
        return await db.KbFiles
            .Find(f => f.KnowledgeBaseId == knowledgeBaseId)
            .SortByDescending(f => f.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> RemoveFileAsync(string knowledgeBaseId, string fileId)
    {
        var result = await db.KbFiles.DeleteOneAsync(f => f.Id == fileId && f.KnowledgeBaseId == knowledgeBaseId);
        return result.DeletedCount > 0;
    }

    public async Task UpdateStatsAsync(string id, int fileCount, long totalChunks)
    {
        await db.KnowledgeBases.UpdateOneAsync(
            k => k.Id == id,
            Builders<KnowledgeBaseDocument>.Update
                .Set(k => k.FileCount, fileCount)
                .Set(k => k.TotalChunks, totalChunks)
                .Set(k => k.UpdatedAt, DateTime.UtcNow));
    }

    public async Task<List<KnowledgeBaseDocument>> GetPendingDeletionsAsync(TimeSpan delay)
    {
        var cutoff = DateTime.UtcNow - delay;
        return await db.KnowledgeBases
            .Find(k => k.IsDeleted && k.DeletedAt != null && k.DeletedAt < cutoff)
            .ToListAsync();
    }

    public async Task HardDeleteAsync(string id)
    {
        await db.KbFiles.DeleteManyAsync(f => f.KnowledgeBaseId == id);
        await db.KnowledgeBases.DeleteOneAsync(k => k.Id == id);
    }
}
