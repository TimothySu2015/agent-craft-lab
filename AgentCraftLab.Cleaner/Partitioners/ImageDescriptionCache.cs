using System.Collections.Concurrent;
using AgentCraftLab.Cleaner.Abstractions;

namespace AgentCraftLab.Cleaner.Partitioners;

/// <summary>
/// 圖片描述快取 — 以 SHA-256 hash 為 key，同一次 partition 內去重。
/// PPT 中每頁 logo、浮水印等重複圖片只描述一次，大幅節省 API 成本。
/// </summary>
internal sealed class ImageDescriptionCache
{
    private readonly ConcurrentDictionary<string, ImageDescriptionResult> _cache = new();

    /// <summary>嘗試從快取取得描述結果</summary>
    public bool TryGet(string hash, out ImageDescriptionResult? result) =>
        _cache.TryGetValue(hash, out result);

    /// <summary>將描述結果存入快取</summary>
    public void Set(string hash, ImageDescriptionResult result) =>
        _cache.TryAdd(hash, result);
}
