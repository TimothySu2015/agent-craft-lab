using System.Text;

namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// 增量 GuardRails 掃描器 — 逐 chunk 累積掃描，偵測到 Block 立即回報。
/// 用於串流模式的 output scanning，避免等全部 buffer 完才掃。
/// </summary>
public sealed class IncrementalGuardRailsScanner
{
    /// <summary>Overlap window：跨 chunk 邊界最長可能的關鍵字長度。</summary>
    private const int OverlapWindowSize = 100;

    private readonly IGuardRailsPolicy _policy;
    private readonly StringBuilder _buffer = new();
    private int _lastScannedLength;

    public IncrementalGuardRailsScanner(IGuardRailsPolicy policy)
    {
        _policy = policy;
    }

    /// <summary>累積的完整文字。</summary>
    public string AccumulatedText => _buffer.ToString();

    /// <summary>
    /// 加入新 chunk 並掃描。
    /// 關鍵字規則用 overlap window 掃描（只看新增部分 + 前方重疊），
    /// 整體 regex 在累積文字上執行。
    /// </summary>
    /// <returns>第一個 Block match，或 null 表示通過。</returns>
    public GuardRailsMatch? ScanChunk(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return null;
        }

        _buffer.Append(chunk);

        // 為避免每次都掃描完整 buffer（O(n^2)），
        // 只掃描從上次掃描位置前 100 字元開始的子字串（overlap window）。
        // 這確保跨 chunk 邊界的關鍵字也能被偵測到。
        var overlapStart = Math.Max(0, _lastScannedLength - OverlapWindowSize);
        var textToScan = _buffer.ToString(overlapStart, _buffer.Length - overlapStart);
        _lastScannedLength = _buffer.Length;

        var matches = _policy.Evaluate(textToScan, GuardRailsDirection.Output);
        foreach (var match in matches)
        {
            if (match.Rule.Action == GuardRailsAction.Block)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>重置掃描器，供複用。</summary>
    public void Reset()
    {
        _buffer.Clear();
        _lastScannedLength = 0;
    }
}
