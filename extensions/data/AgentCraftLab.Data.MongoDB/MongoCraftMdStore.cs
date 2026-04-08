using AgentCraftLab.Data;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

public class MongoCraftMdStore(MongoDbContext db) : ICraftMdStore
{
    private static string GenerateId() => $"cmd-{Guid.NewGuid():N}"[..12];

    public async Task<CraftMdDocument> SaveAsync(string userId, string? workflowId, string content)
    {
        var filter = Builders<CraftMdDocument>.Filter.Eq(x => x.UserId, userId)
                   & Builders<CraftMdDocument>.Filter.Eq(x => x.WorkflowId, workflowId);

        var existing = await db.CraftMds.Find(filter).FirstOrDefaultAsync();

        if (existing is not null)
        {
            await db.CraftMds.UpdateOneAsync(filter,
                Builders<CraftMdDocument>.Update
                    .Set(x => x.Content, content)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow));

            existing.Content = content;
            existing.UpdatedAt = DateTime.UtcNow;
            return existing;
        }

        var doc = new CraftMdDocument
        {
            Id = GenerateId(),
            UserId = userId,
            WorkflowId = workflowId,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await db.CraftMds.InsertOneAsync(doc);
        return doc;
    }

    public async Task<string?> GetContentAsync(string userId, string? workflowId)
    {
        if (workflowId is not null)
        {
            var workflowDoc = await db.CraftMds
                .Find(x => x.UserId == userId && x.WorkflowId == workflowId)
                .FirstOrDefaultAsync();

            if (workflowDoc is not null)
            {
                return workflowDoc.Content;
            }
        }

        var defaultDoc = await db.CraftMds
            .Find(x => x.UserId == userId && x.WorkflowId == null)
            .FirstOrDefaultAsync();

        return defaultDoc?.Content;
    }

    public async Task<CraftMdDocument?> GetAsync(string userId, string? workflowId)
    {
        var filter = Builders<CraftMdDocument>.Filter.Eq(x => x.UserId, userId)
                   & Builders<CraftMdDocument>.Filter.Eq(x => x.WorkflowId, workflowId);

        return await db.CraftMds.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<bool> DeleteAsync(string userId, string? workflowId)
    {
        var filter = Builders<CraftMdDocument>.Filter.Eq(x => x.UserId, userId)
                   & Builders<CraftMdDocument>.Filter.Eq(x => x.WorkflowId, workflowId);

        var result = await db.CraftMds.DeleteOneAsync(filter);
        return result.DeletedCount > 0;
    }
}
