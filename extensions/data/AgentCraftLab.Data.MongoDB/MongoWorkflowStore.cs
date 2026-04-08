using AgentCraftLab.Data;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

/// <summary>
/// Workflow CRUD 服務，使用 MongoDB 持久化。所有操作加 userId 參數做使用者隔離。
/// </summary>
public class MongoWorkflowStore(MongoDbContext db) : IWorkflowStore
{
    private static string GenerateId() => $"wf-{Guid.NewGuid():N}"[..10];

    public async Task<WorkflowDocument> SaveAsync(string userId, string name, string description, string type, string workflowJson)
    {
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

        await db.Workflows.InsertOneAsync(doc);
        return doc;
    }

    public async Task<WorkflowDocument?> GetAsync(string id)
    {
        return await db.Workflows.Find(w => w.Id == id).FirstOrDefaultAsync();
    }

    public async Task<List<WorkflowDocument>> ListAsync(string userId)
    {
        return await db.Workflows
            .Find(w => w.UserId == userId)
            .SortByDescending(w => w.UpdatedAt)
            .ToListAsync();
    }

    public async Task<WorkflowDocument?> UpdateAsync(string userId, string id, string name, string description, string type, string workflowJson)
    {
        var result = await db.Workflows.FindOneAndUpdateAsync(
            w => w.Id == id && w.UserId == userId,
            Builders<WorkflowDocument>.Update
                .Set(w => w.Name, name)
                .Set(w => w.Description, description)
                .Set(w => w.Type, type)
                .Set(w => w.WorkflowJson, workflowJson)
                .Set(w => w.UpdatedAt, DateTime.UtcNow),
            new FindOneAndUpdateOptions<WorkflowDocument> { ReturnDocument = ReturnDocument.After });

        return result;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var result = await db.Workflows.DeleteOneAsync(w => w.Id == id && w.UserId == userId);
        return result.DeletedCount > 0;
    }

    public async Task<bool> SetPublishedAsync(string userId, string id, bool isPublished, List<string>? inputModes = null)
    {
        var update = Builders<WorkflowDocument>.Update
            .Set(w => w.IsPublished, isPublished)
            .Set(w => w.UpdatedAt, DateTime.UtcNow);

        if (inputModes is not null)
        {
            update = update.Set(w => w.AcceptedInputModes, string.Join(",", inputModes));
        }

        var result = await db.Workflows.UpdateOneAsync(
            w => w.Id == id && w.UserId == userId, update);

        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdateTypeAsync(string userId, string id, List<string> types)
    {
        var result = await db.Workflows.UpdateOneAsync(
            w => w.Id == id && w.UserId == userId,
            Builders<WorkflowDocument>.Update
                .Set(w => w.Type, string.Join(",", types))
                .Set(w => w.UpdatedAt, DateTime.UtcNow));

        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 列出所有已發布的 workflow（不需 userId，供公開端點使用）。
    /// </summary>
    public async Task<List<WorkflowDocument>> ListPublishedAsync()
    {
        return await db.Workflows
            .Find(w => w.IsPublished)
            .SortByDescending(w => w.UpdatedAt)
            .ToListAsync();
    }
}
