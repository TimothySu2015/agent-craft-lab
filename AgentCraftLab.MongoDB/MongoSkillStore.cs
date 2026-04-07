using AgentCraftLab.Engine.Data;
using MongoDB.Driver;

namespace AgentCraftLab.MongoDB;

/// <summary>
/// Skill CRUD 服務，使用 MongoDB 持久化。所有操作加 userId 參數做使用者隔離。
/// </summary>
public class MongoSkillStore(MongoDbContext db) : ISkillStore
{
    private static string GenerateId() => $"sk-{Guid.NewGuid():N}"[..10];

    public async Task<SkillDocument> SaveAsync(string userId, string name, string description, string category, string icon, string instructions, List<string> tools)
    {
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

        await db.Skills.InsertOneAsync(doc);
        return doc;
    }

    public async Task<List<SkillDocument>> ListAsync(string userId)
    {
        return await db.Skills
            .Find(s => s.UserId == userId)
            .SortByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<SkillDocument?> GetAsync(string id)
    {
        return await db.Skills.Find(s => s.Id == id).FirstOrDefaultAsync();
    }

    public async Task<SkillDocument?> UpdateAsync(string userId, string id, string name, string description, string category, string icon, string instructions, List<string> tools)
    {
        var update = Builders<SkillDocument>.Update
            .Set(s => s.Name, name)
            .Set(s => s.Description, description)
            .Set(s => s.Category, category)
            .Set(s => s.Icon, icon)
            .Set(s => s.Instructions, instructions)
            .Set(s => s.Tools, SkillDocument.SerializeTools(tools))
            .Set(s => s.UpdatedAt, DateTime.UtcNow);

        return await db.Skills.FindOneAndUpdateAsync(
            s => s.Id == id && s.UserId == userId,
            update,
            new FindOneAndUpdateOptions<SkillDocument> { ReturnDocument = ReturnDocument.After });
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var result = await db.Skills.DeleteOneAsync(s => s.Id == id && s.UserId == userId);
        return result.DeletedCount > 0;
    }
}
