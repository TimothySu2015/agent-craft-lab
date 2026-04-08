using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Data;

/// <summary>
/// Credential 加解密。使用 ASP.NET Core Data Protection API（跨平台、Azure 相容）。
/// </summary>
public class CredentialProtector
{
    private readonly IDataProtector _protector;
    private readonly ILogger<CredentialProtector>? _logger;

    public CredentialProtector(IDataProtectionProvider provider, ILogger<CredentialProtector>? logger = null)
    {
        _protector = provider.CreateProtector("AgentCraftLab.Credentials");
        _logger = logger;
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return "";
        }

        return _protector.Protect(plainText);
    }

    public string Decrypt(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return "";
        }

        try
        {
            return _protector.Unprotect(protectedText);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Data Protection key 不匹配（DB 來自其他機器或 key 已輪換），回傳空字串讓使用者重新設定
            _logger?.LogWarning("Credential decryption failed — Data Protection key mismatch. User needs to re-enter credentials.");
            return "";
        }
    }
}
