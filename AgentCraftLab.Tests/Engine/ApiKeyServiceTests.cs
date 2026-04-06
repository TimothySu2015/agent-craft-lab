using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Engine;

public class ApiKeyServiceTests
{
    // ─── In-Memory Store ───

    private class InMemoryApiKeyStore : IApiKeyStore
    {
        private readonly List<ApiKeyDocument> _docs = [];

        public Task<ApiKeyDocument> SaveAsync(string userId, string name, string keyHash, string keyPrefix,
            string? scopedWorkflowIds = null, DateTime? expiresAt = null)
        {
            var doc = new ApiKeyDocument
            {
                Id = $"ak-{Guid.NewGuid():N}"[..11],
                UserId = userId,
                Name = name,
                KeyHash = keyHash,
                KeyPrefix = keyPrefix,
                ScopedWorkflowIds = scopedWorkflowIds ?? "",
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            _docs.Add(doc);
            return Task.FromResult(doc);
        }

        public Task<List<ApiKeyDocument>> ListAsync(string userId)
            => Task.FromResult(_docs.Where(d => d.UserId == userId).ToList());

        public Task<ApiKeyDocument?> GetAsync(string id)
            => Task.FromResult(_docs.FirstOrDefault(d => d.Id == id));

        public Task<ApiKeyDocument?> FindByHashAsync(string keyHash)
            => Task.FromResult(_docs.FirstOrDefault(d => d.KeyHash == keyHash && !d.IsRevoked
                && (d.ExpiresAt == null || d.ExpiresAt > DateTime.UtcNow)));

        public Task<bool> RevokeAsync(string userId, string id)
        {
            var doc = _docs.FirstOrDefault(d => d.Id == id && d.UserId == userId);
            if (doc is null) return Task.FromResult(false);
            doc.IsRevoked = true;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(string userId, string id)
        {
            var count = _docs.RemoveAll(d => d.Id == id && d.UserId == userId);
            return Task.FromResult(count > 0);
        }

        public Task UpdateLastUsedAsync(string id)
        {
            var doc = _docs.FirstOrDefault(d => d.Id == id);
            if (doc is not null) doc.LastUsedAt = DateTime.UtcNow;
            return Task.CompletedTask;
        }
    }

    // ─── CreateAsync ───

    [Fact]
    public async Task Create_GeneratesKeyWithPrefix()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);

        var (doc, rawKey) = await svc.CreateAsync("user-1", "Test Key");

        Assert.StartsWith("ack_", rawKey);
        Assert.Equal(36, rawKey.Length); // "ack_" (4) + 32 random
    }

    [Fact]
    public async Task Create_StoresHashNotRawKey()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);

        var (doc, rawKey) = await svc.CreateAsync("user-1", "Test Key");

        Assert.NotEqual(rawKey, doc.KeyHash);
        Assert.Equal(64, doc.KeyHash.Length); // SHA-256 hex = 64 chars
    }

    [Fact]
    public async Task Create_StoresKeyPrefix()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);

        var (doc, rawKey) = await svc.CreateAsync("user-1", "Test Key");

        Assert.Equal(rawKey[..12], doc.KeyPrefix);
    }

    [Fact]
    public async Task Create_WithScope_StoresScopedWorkflowIds()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);

        var (doc, _) = await svc.CreateAsync("user-1", "Scoped Key", "wf-1,wf-2");

        Assert.Equal("wf-1,wf-2", doc.ScopedWorkflowIds);
    }

    [Fact]
    public async Task Create_WithExpiry_StoresExpiresAt()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);
        var expiry = DateTime.UtcNow.AddDays(30);

        var (doc, _) = await svc.CreateAsync("user-1", "Expiring Key", expiresAt: expiry);

        Assert.NotNull(doc.ExpiresAt);
        Assert.Equal(expiry, doc.ExpiresAt.Value);
    }

    // ─── ValidateAsync ───

    [Fact]
    public async Task Validate_ValidKey_ReturnsResult()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);
        var (_, rawKey) = await svc.CreateAsync("user-1", "Valid Key");

        var result = await svc.ValidateAsync(rawKey);

        Assert.NotNull(result);
        Assert.Equal("user-1", result.UserId);
        Assert.Equal("Valid Key", result.ApiKeyName);
    }

    [Fact]
    public async Task Validate_InvalidKey_ReturnsNull()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);

        var result = await svc.ValidateAsync("ack_nonexistent12345678901234567890");

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_EmptyKey_ReturnsNull()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);

        Assert.Null(await svc.ValidateAsync(""));
        Assert.Null(await svc.ValidateAsync(null!));
    }

    [Fact]
    public async Task Validate_WrongPrefix_ReturnsNull()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);

        var result = await svc.ValidateAsync("wrong_prefix12345678901234567890");

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_RevokedKey_ReturnsNull()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);
        var (doc, rawKey) = await svc.CreateAsync("user-1", "To Revoke");
        await store.RevokeAsync("user-1", doc.Id);

        var result = await svc.ValidateAsync(rawKey);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_ExpiredKey_ReturnsNull()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);
        var (_, rawKey) = await svc.CreateAsync("user-1", "Expired", expiresAt: DateTime.UtcNow.AddSeconds(-1));

        var result = await svc.ValidateAsync(rawKey);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_ScopedKey_MatchingWorkflow_ReturnsResult()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);
        var (_, rawKey) = await svc.CreateAsync("user-1", "Scoped", "wf-1,wf-2");

        var result = await svc.ValidateAsync(rawKey, "wf-1");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Validate_ScopedKey_NonMatchingWorkflow_ReturnsNull()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);
        var (_, rawKey) = await svc.CreateAsync("user-1", "Scoped", "wf-1,wf-2");

        var result = await svc.ValidateAsync(rawKey, "wf-999");

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_UnscopedKey_AnyWorkflow_ReturnsResult()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);
        var (_, rawKey) = await svc.CreateAsync("user-1", "Unscoped");

        var result = await svc.ValidateAsync(rawKey, "any-workflow");

        Assert.NotNull(result);
    }

    // ─── Key Uniqueness ───

    [Fact]
    public async Task Create_TwoKeys_DifferentRawKeys()
    {
        var store = new InMemoryApiKeyStore();
        var svc = new ApiKeyService(store);

        var (_, key1) = await svc.CreateAsync("user-1", "Key 1");
        var (_, key2) = await svc.CreateAsync("user-1", "Key 2");

        Assert.NotEqual(key1, key2);
    }
}
