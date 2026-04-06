namespace AgentCraftLab.Engine.Services.Compression;

/// <summary>
/// 壓縮狀態 — 在 ReAct / Flow 迭代間傳遞，讓壓縮積木和上層做更聰明的決策。
/// 記錄已執行的壓縮操作、節省的 token 數、API 快取資訊等。
/// 上層（ReactExecutor / FlowExecutor）維護此狀態，積木可讀取。
/// </summary>
public class CompressionState
{
    /// <summary>本次 session 已執行的壓縮次數。</summary>
    public int CompressionsApplied { get; set; }

    /// <summary>本次 session 總共節省的預估 token 數。</summary>
    public long TotalTokensSaved { get; set; }

    /// <summary>已被截斷的 tool call IDs（避免重複截斷同一個 tool result）。</summary>
    public HashSet<string> TruncatedToolCallIds { get; } = [];

    /// <summary>API 回傳的 cached token 數（用於未來 CacheAware 壓縮決策）。</summary>
    public int? ApiCachedTokenCount { get; set; }

    /// <summary>上次壓縮的時間戳。</summary>
    public DateTime? LastCompressionTime { get; set; }

    /// <summary>記錄一次壓縮操作。</summary>
    public void RecordCompression(long tokensSaved)
    {
        CompressionsApplied++;
        TotalTokensSaved += tokensSaved;
        LastCompressionTime = DateTime.UtcNow;
    }
}
