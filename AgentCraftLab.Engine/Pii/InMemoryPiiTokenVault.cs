using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace AgentCraftLab.Engine.Pii;

/// <summary>
/// 記憶體型 PII Token 保管庫。將 PII 值映射為型別化 token（如 [EMAIL_1]），
/// 支援可逆還原、Session 隔離、TTL 自動過期。Thread-safe。
/// </summary>
public sealed class InMemoryPiiTokenVault : IPiiTokenVault
{
    private readonly ConcurrentDictionary<string, SessionData> _sessions = new();
    private readonly TimeSpan _ttl;
    private DateTimeOffset _lastCleanup = DateTimeOffset.UtcNow;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);

    /// <summary>用於匹配 token 的 Regex（如 [EMAIL_1]、[CREDITCARD_3]）。</summary>
    private static readonly Regex TokenPattern = new(@"\[([A-Z_]+)_(\d+)\]", RegexOptions.Compiled);

    /// <summary>
    /// 建立 Token 保管庫。
    /// </summary>
    /// <param name="ttl">Token 存活時間（預設 1 小時）。</param>
    public InMemoryPiiTokenVault(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromHours(1);
    }

    /// <inheritdoc/>
    public string Tokenize(string sessionKey, string originalValue, PiiEntityType type)
    {
        CleanExpiredSessions();

        var session = _sessions.GetOrAdd(sessionKey, _ => new SessionData());
        session.LastAccess = DateTimeOffset.UtcNow;

        // 相同值回傳相同 token
        if (session.ValueToToken.TryGetValue(originalValue, out var existingToken))
        {
            return existingToken;
        }

        var typeKey = type.ToString().ToUpperInvariant();
        var counter = session.TypeCounters.AddOrUpdate(typeKey, 1, (_, c) => c + 1);
        var token = $"[{typeKey}_{counter}]";

        session.ValueToToken[originalValue] = token;
        session.TokenToValue[token] = originalValue;

        return token;
    }

    /// <inheritdoc/>
    public string Detokenize(string sessionKey, string text)
    {
        if (!_sessions.TryGetValue(sessionKey, out var session))
        {
            return text;
        }

        session.LastAccess = DateTimeOffset.UtcNow;

        return TokenPattern.Replace(text, match =>
        {
            var token = match.Value;
            return session.TokenToValue.TryGetValue(token, out var original) ? original : token;
        });
    }

    /// <inheritdoc/>
    public void ClearSession(string sessionKey)
    {
        _sessions.TryRemove(sessionKey, out _);
    }

    /// <summary>清除過期的 session（節流：每分鐘最多清理一次）。</summary>
    private void CleanExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastCleanup < CleanupInterval)
        {
            return;
        }

        _lastCleanup = now;
        foreach (var kvp in _sessions)
        {
            if (now - kvp.Value.LastAccess > _ttl)
            {
                _sessions.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>Session 內部資料。</summary>
    private sealed class SessionData
    {
        public ConcurrentDictionary<string, string> ValueToToken { get; } = new();
        public ConcurrentDictionary<string, string> TokenToValue { get; } = new();
        public ConcurrentDictionary<string, int> TypeCounters { get; } = new();
        public DateTimeOffset LastAccess { get; set; } = DateTimeOffset.UtcNow;
    }
}
