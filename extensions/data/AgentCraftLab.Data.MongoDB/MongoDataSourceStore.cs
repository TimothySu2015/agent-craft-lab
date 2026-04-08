using AgentCraftLab.Data;
using MongoDB.Driver;

namespace AgentCraftLab.Data.MongoDB;

public class MongoDataSourceStore(MongoDbContext db) : IDataSourceStore
{
    private static string GenerateId() => $"ds-{Guid.NewGuid():N}"[..10];

    public async Task<DataSourceDocument> SaveAsync(DataSourceDocument doc)
    {
        doc.Id = GenerateId();
        doc.CreatedAt = DateTime.UtcNow;
        doc.UpdatedAt = DateTime.UtcNow;

        await db.DataSources.InsertOneAsync(doc);
        return doc;
    }

    public async Task<List<DataSourceDocument>> ListAsync(string userId)
    {
        return await db.DataSources
            .Find(d => d.UserId == userId)
            .SortByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<DataSourceDocument?> GetAsync(string id)
    {
        return await db.DataSources.Find(d => d.Id == id).FirstOrDefaultAsync();
    }

    public async Task<DataSourceDocument?> UpdateAsync(string userId, string id, string name, string description, string provider, string configJson)
    {
        return await db.DataSources.FindOneAndUpdateAsync(
            d => d.Id == id && d.UserId == userId,
            Builders<DataSourceDocument>.Update
                .Set(d => d.Name, name)
                .Set(d => d.Description, description)
                .Set(d => d.Provider, provider)
                .Set(d => d.ConfigJson, configJson)
                .Set(d => d.UpdatedAt, DateTime.UtcNow),
            new FindOneAndUpdateOptions<DataSourceDocument> { ReturnDocument = ReturnDocument.After });
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var result = await db.DataSources.DeleteOneAsync(d => d.Id == id && d.UserId == userId);
        return result.DeletedCount > 0;
    }

    public async Task<int> CountKbReferencesAsync(string id)
    {
        var filter = Builders<KnowledgeBaseDocument>.Filter.Eq(k => k.DataSourceId, id)
                   & Builders<KnowledgeBaseDocument>.Filter.Eq(k => k.IsDeleted, false);

        return (int)await db.KnowledgeBases.CountDocumentsAsync(filter);
    }
}
