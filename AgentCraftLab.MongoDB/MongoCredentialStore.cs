using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Models;
using MongoDB.Driver;

namespace AgentCraftLab.MongoDB;

/// <summary>
/// Credential CRUD 服務，API Key 使用 Data Protection API 加密存儲。
/// 所有操作加 userId 參數做使用者隔離。
/// </summary>
public class MongoCredentialStore(MongoDbContext db, CredentialProtector protector) : ICredentialStore
{
    private static string GenerateId() => $"cred-{Guid.NewGuid():N}"[..12];

    public async Task<CredentialDocument> SaveAsync(string userId, string provider, string name, string apiKey, string endpoint = "", string model = "")
    {
        var doc = new CredentialDocument
        {
            Id = GenerateId(),
            UserId = userId,
            Provider = provider,
            Name = name,
            EncryptedApiKey = protector.Encrypt(apiKey),
            Endpoint = endpoint,
            Model = model,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await db.Credentials.InsertOneAsync(doc);
        return doc;
    }

    public async Task<List<CredentialDocument>> ListAsync(string userId)
    {
        return await db.Credentials
            .Find(c => c.UserId == userId)
            .SortBy(c => c.Provider).ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<CredentialDocument?> GetAsync(string id)
    {
        return await db.Credentials.Find(c => c.Id == id).FirstOrDefaultAsync();
    }

    public async Task<CredentialDocument?> UpdateAsync(string userId, string id, string name, string apiKey, string endpoint = "", string model = "")
    {
        var update = Builders<CredentialDocument>.Update
            .Set(c => c.Name, name)
            .Set(c => c.Endpoint, endpoint)
            .Set(c => c.Model, model)
            .Set(c => c.UpdatedAt, DateTime.UtcNow);

        if (!string.IsNullOrEmpty(apiKey))
        {
            update = update.Set(c => c.EncryptedApiKey, protector.Encrypt(apiKey));
        }

        return await db.Credentials.FindOneAndUpdateAsync(
            c => c.Id == id && c.UserId == userId,
            update,
            new FindOneAndUpdateOptions<CredentialDocument> { ReturnDocument = ReturnDocument.After });
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var result = await db.Credentials.DeleteOneAsync(c => c.Id == id && c.UserId == userId);
        return result.DeletedCount > 0;
    }

    /// <summary>
    /// 取得指定使用者的解密後 ProviderCredential，供 WorkflowExecutionService 使用。
    /// </summary>
    public async Task<Dictionary<string, ProviderCredential>> GetDecryptedCredentialsAsync(string userId)
    {
        var credentials = await ListAsync(userId);
        var result = new Dictionary<string, ProviderCredential>();

        foreach (var cred in credentials)
        {
            result[cred.Provider] = new ProviderCredential
            {
                ApiKey = protector.Decrypt(cred.EncryptedApiKey),
                Endpoint = cred.Endpoint,
                Model = cred.Model
            };
        }

        return result;
    }
}
