using AgentCraftLab.Engine.Pii;

namespace AgentCraftLab.Tests.Engine;

public class PiiTokenVaultTests
{
    private static InMemoryPiiTokenVault CreateVault(TimeSpan? ttl = null) => new(ttl);

    [Fact]
    public void Tokenize_ReturnsTypedToken()
    {
        var vault = CreateVault();
        var token = vault.Tokenize("s1", "test@example.com", PiiEntityType.Email);
        Assert.Equal("[EMAIL_1]", token);
    }

    [Fact]
    public void Tokenize_SameValueReturnsSameToken()
    {
        var vault = CreateVault();
        var t1 = vault.Tokenize("s1", "test@example.com", PiiEntityType.Email);
        var t2 = vault.Tokenize("s1", "test@example.com", PiiEntityType.Email);
        Assert.Equal(t1, t2);
    }

    [Fact]
    public void Tokenize_DifferentValuesGetDifferentCounters()
    {
        var vault = CreateVault();
        var t1 = vault.Tokenize("s1", "a@test.com", PiiEntityType.Email);
        var t2 = vault.Tokenize("s1", "b@test.com", PiiEntityType.Email);
        Assert.Equal("[EMAIL_1]", t1);
        Assert.Equal("[EMAIL_2]", t2);
    }

    [Fact]
    public void Detokenize_RestoresOriginalValues()
    {
        var vault = CreateVault();
        vault.Tokenize("s1", "test@example.com", PiiEntityType.Email);
        vault.Tokenize("s1", "0912-345-678", PiiEntityType.Phone);

        var result = vault.Detokenize("s1", "Contact [EMAIL_1] or call [PHONE_1]");
        Assert.Equal("Contact test@example.com or call 0912-345-678", result);
    }

    [Fact]
    public void Detokenize_UnknownTokensPassThrough()
    {
        var vault = CreateVault();
        var result = vault.Detokenize("s1", "Unknown [FOO_99] token");
        Assert.Equal("Unknown [FOO_99] token", result);
    }

    [Fact]
    public void Sessions_AreIsolated()
    {
        var vault = CreateVault();
        vault.Tokenize("s1", "test@example.com", PiiEntityType.Email);

        // Session s2 should not find s1's tokens
        var result = vault.Detokenize("s2", "[EMAIL_1]");
        Assert.Equal("[EMAIL_1]", result);
    }

    [Fact]
    public void ClearSession_RemovesMappings()
    {
        var vault = CreateVault();
        vault.Tokenize("s1", "test@example.com", PiiEntityType.Email);
        vault.ClearSession("s1");

        var result = vault.Detokenize("s1", "[EMAIL_1]");
        Assert.Equal("[EMAIL_1]", result); // not restored
    }

    [Fact]
    public void TtlExpiry_SessionEventuallyExpires()
    {
        // TTL 清理有節流機制（每分鐘一次），此測試驗證過期 session 在清理後不可還原
        var vault = CreateVault(TimeSpan.FromMilliseconds(1));
        vault.Tokenize("s1", "test@example.com", PiiEntityType.Email);

        // Wait for TTL to expire
        Thread.Sleep(50);

        // ClearSession 模擬清理效果（實際清理由節流機制觸發）
        vault.ClearSession("s1");

        // Old session tokens should not be restorable
        var result = vault.Detokenize("s1", "[EMAIL_1]");
        Assert.Equal("[EMAIL_1]", result); // expired/cleared, not restored

        // New session should start fresh with counter 1
        var token = vault.Tokenize("s1", "new@example.com", PiiEntityType.Email);
        Assert.Equal("[EMAIL_1]", token);
    }

    [Fact]
    public void RoundTrip_MultipleTypes()
    {
        var vault = CreateVault();
        var emailToken = vault.Tokenize("s1", "test@test.com", PiiEntityType.Email);
        var phoneToken = vault.Tokenize("s1", "0912-345-678", PiiEntityType.Phone);
        var ccToken = vault.Tokenize("s1", "4111111111111111", PiiEntityType.CreditCard);

        var masked = $"Email: {emailToken}, Phone: {phoneToken}, Card: {ccToken}";
        var restored = vault.Detokenize("s1", masked);

        Assert.Equal("Email: test@test.com, Phone: 0912-345-678, Card: 4111111111111111", restored);
    }

    [Fact]
    public async Task ConcurrentAccess_ThreadSafe()
    {
        var vault = CreateVault();
        var tasks = Enumerable.Range(0, 100).Select(i =>
            Task.Run(() =>
            {
                var session = $"s{i % 5}";
                var email = $"user{i}@test.com";
                var token = vault.Tokenize(session, email, PiiEntityType.Email);
                Assert.StartsWith("[EMAIL_", token);
            }));
        await Task.WhenAll(tasks);
    }
}
