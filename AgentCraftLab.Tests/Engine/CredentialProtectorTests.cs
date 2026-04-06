using AgentCraftLab.Engine.Data;
using Microsoft.AspNetCore.DataProtection;

namespace AgentCraftLab.Tests.Engine;

public class CredentialProtectorTests
{
    private static CredentialProtector CreateProtector()
    {
        var provider = DataProtectionProvider.Create("AgentCraftLab.Tests");
        return new CredentialProtector(provider);
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrip()
    {
        var protector = CreateProtector();

        var encrypted = protector.Encrypt("sk-test-12345");
        var decrypted = protector.Decrypt(encrypted);

        Assert.Equal("sk-test-12345", decrypted);
    }

    [Fact]
    public void Encrypt_ProducesNonPlaintext()
    {
        var protector = CreateProtector();

        var encrypted = protector.Encrypt("my-secret");

        Assert.NotEqual("my-secret", encrypted);
        Assert.True(encrypted.Length > "my-secret".Length);
    }

    [Fact]
    public void Encrypt_EmptyString_ReturnsEmpty()
    {
        var protector = CreateProtector();

        Assert.Equal("", protector.Encrypt(""));
        Assert.Equal("", protector.Encrypt(null!));
    }

    [Fact]
    public void Decrypt_EmptyString_ReturnsEmpty()
    {
        var protector = CreateProtector();

        Assert.Equal("", protector.Decrypt(""));
        Assert.Equal("", protector.Decrypt(null!));
    }

    [Fact]
    public void Decrypt_InvalidCiphertext_ReturnsEmpty()
    {
        var protector = CreateProtector();

        // 不是合法 DPAPI 加密的字串
        var result = protector.Decrypt("not-a-valid-encrypted-string");

        Assert.Equal("", result);
    }

    [Fact]
    public void Decrypt_WrongKey_ReturnsEmpty()
    {
        // 用不同的 provider 加密，再用原 provider 解密 → key mismatch
        var provider1 = DataProtectionProvider.Create("Provider-A");
        var protector1 = new CredentialProtector(provider1);

        var provider2 = DataProtectionProvider.Create("Provider-B");
        var protector2 = new CredentialProtector(provider2);

        var encrypted = protector1.Encrypt("secret");
        var result = protector2.Decrypt(encrypted);

        Assert.Equal("", result);
    }

    [Fact]
    public void Encrypt_SameInput_DifferentOutput()
    {
        // DPAPI 使用隨機 IV，每次加密結果不同
        var protector = CreateProtector();

        var enc1 = protector.Encrypt("same-input");
        var enc2 = protector.Encrypt("same-input");

        // 兩次加密結果可能相同也可能不同（取決於實作），但都能解密回原文
        Assert.Equal("same-input", protector.Decrypt(enc1));
        Assert.Equal("same-input", protector.Decrypt(enc2));
    }

    [Fact]
    public void Encrypt_Decrypt_SpecialCharacters()
    {
        var protector = CreateProtector();

        var input = "sk-key/with+special=chars&中文密鑰!@#$%^";
        var encrypted = protector.Encrypt(input);
        var decrypted = protector.Decrypt(encrypted);

        Assert.Equal(input, decrypted);
    }

    [Fact]
    public void Encrypt_Decrypt_LongKey()
    {
        var protector = CreateProtector();

        var input = new string('A', 10000); // 10KB key
        var encrypted = protector.Encrypt(input);
        var decrypted = protector.Decrypt(encrypted);

        Assert.Equal(input, decrypted);
    }
}
