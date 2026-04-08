using AgentCraftLab.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Data.PostgreSQL;

public class PgCredentialStore(IServiceScopeFactory scopeFactory, CredentialProtector protector) : ICredentialStore
{
    private static string GenerateId() => $"cred-{Guid.NewGuid():N}"[..12];

    private (AppDbContext Db, IServiceScope Scope) CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    public async Task<CredentialDocument> SaveAsync(string userId, string provider, string name, string apiKey, string endpoint = "", string model = "")
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

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

        db.Credentials.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<List<CredentialDocument>> ListAsync(string userId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Credentials
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();
    }

    public async Task<CredentialDocument?> GetAsync(string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        return await db.Credentials.FindAsync(id);
    }

    public async Task<CredentialDocument?> UpdateAsync(string userId, string id, string name, string apiKey, string endpoint = "", string model = "")
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.Credentials.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (doc is null)
        {
            return null;
        }

        doc.Name = name;
        if (!string.IsNullOrEmpty(apiKey))
        {
            doc.EncryptedApiKey = protector.Encrypt(apiKey);
        }

        doc.Endpoint = endpoint;
        doc.Model = model;
        doc.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var doc = await db.Credentials.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (doc is null)
        {
            return false;
        }

        db.Credentials.Remove(doc);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<Dictionary<string, ProviderCredential>> GetDecryptedCredentialsAsync(string userId)
    {
        var (db, scope) = CreateScope();
        await using var _ = scope as IAsyncDisposable;

        var docs = await db.Credentials.Where(c => c.UserId == userId).ToListAsync();
        var result = new Dictionary<string, ProviderCredential>();

        foreach (var doc in docs)
        {
            result[doc.Provider] = new ProviderCredential
            {
                ApiKey = protector.Decrypt(doc.EncryptedApiKey),
                Endpoint = doc.Endpoint,
                Model = doc.Model
            };
        }

        return result;
    }
}
