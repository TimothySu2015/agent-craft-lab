using AgentCraftLab.Data;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

public class MongoTemplateStore(MongoDbContext db) : ITemplateStore
{
    private static string GenerateId() => $"tpl-{Guid.NewGuid():N}"[..11];

    public async Task<TemplateDocument> SaveAsync(string userId, string name, string description, string category, string icon, List<string> tags, string workflowJson)
    {
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

        await db.Templates.InsertOneAsync(doc);
        return doc;
    }

    public async Task<List<TemplateDocument>> ListAsync(string userId)
    {
        return await db.Templates
            .Find(t => t.UserId == userId)
            .SortByDescending(t => t.UpdatedAt)
            .ToListAsync();
    }

    public async Task<TemplateDocument?> GetAsync(string id)
    {
        return await db.Templates.Find(t => t.Id == id).FirstOrDefaultAsync();
    }

    public async Task<TemplateDocument?> UpdateAsync(string userId, string id, string name, string description, string category, string icon, List<string> tags, string? workflowJson)
    {
        var updateDef = Builders<TemplateDocument>.Update
            .Set(t => t.Name, name)
            .Set(t => t.Description, description)
            .Set(t => t.Category, category)
            .Set(t => t.Icon, icon)
            .Set(t => t.Tags, string.Join(",", tags))
            .Set(t => t.UpdatedAt, DateTime.UtcNow);

        if (workflowJson is not null)
        {
            updateDef = updateDef.Set(t => t.WorkflowJson, workflowJson);
        }

        return await db.Templates.FindOneAndUpdateAsync(
            t => t.Id == id && t.UserId == userId,
            updateDef,
            new FindOneAndUpdateOptions<TemplateDocument> { ReturnDocument = ReturnDocument.After });
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var result = await db.Templates.DeleteOneAsync(t => t.Id == id && t.UserId == userId);
        return result.DeletedCount > 0;
    }
}
