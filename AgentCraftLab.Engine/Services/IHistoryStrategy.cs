using Microsoft.Extensions.AI;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// History 管理策略介面 — 控制對話歷史的裁剪/壓縮方式。
/// 不同策略適用於不同場景（短對話、長迴圈、需要上下文保留等）。
/// 合約：history[0] 永遠是 system message，策略必須保留。
/// </summary>
public interface IHistoryStrategy
{
    /// <summary>策略名稱（用於配置和日誌）</summary>
    string Name { get; }

    /// <summary>
    /// 裁剪/壓縮對話歷史。直接修改傳入的 list。
    /// 合約：history[0] 是 system message，必須保留。
    /// </summary>
    /// <param name="history">可變的對話歷史（直接修改）</param>
    /// <param name="maxMessages">節點設定的最大訊息數</param>
    void TrimHistory(List<ChatMessage> history, int maxMessages);
}
