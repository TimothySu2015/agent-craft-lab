using System.Security.Cryptography;
using System.Text;
using AgentCraftLab.Data;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// API Key 管理與驗證服務。Key 格式：ack_ + 32 隨機字元，存儲為 SHA-256 hash。
/// </summary>
public class ApiKeyService(IApiKeyStore store)
{
    private const string KeyPrefix = "ack_";

    /// <summary>
    /// 建立 API Key。回傳文件 + 明文 rawKey（僅此一次）。
    /// </summary>
    public async Task<(ApiKeyDocument Document, string RawKey)> CreateAsync(
        string userId, string name, string? scopedWorkflowIds = null, DateTime? expiresAt = null)
    {
        var rawKey = KeyPrefix + GenerateRandomString(32);
        var keyHash = ComputeHash(rawKey);
        var keyPrefixDisplay = rawKey[..12];

        var doc = await store.SaveAsync(userId, name, keyHash, keyPrefixDisplay, scopedWorkflowIds, expiresAt);
        return (doc, rawKey);
    }

    /// <summary>
    /// 驗證 API Key。成功時回傳驗證結果，失敗回傳 null。
    /// </summary>
    public async Task<ApiKeyValidationResult?> ValidateAsync(string rawKey, string? workflowKey = null)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || !rawKey.StartsWith(KeyPrefix))
        {
            return null;
        }

        var keyHash = ComputeHash(rawKey);
        var doc = await store.FindByHashAsync(keyHash);
        if (doc is null)
        {
            return null;
        }

        // 檢查 workflow scope
        if (workflowKey is not null)
        {
            var scoped = doc.GetScopedWorkflowIds();
            if (scoped.Count > 0 && !scoped.Contains(workflowKey))
            {
                return null;
            }
        }

        // fire-and-forget 更新最後使用時間
        _ = store.UpdateLastUsedAsync(doc.Id);

        return new ApiKeyValidationResult(doc.UserId, doc.Id, doc.Name);
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static string GenerateRandomString(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return RandomNumberGenerator.GetString(chars, length);
    }
}

public record ApiKeyValidationResult(string UserId, string ApiKeyId, string ApiKeyName);
