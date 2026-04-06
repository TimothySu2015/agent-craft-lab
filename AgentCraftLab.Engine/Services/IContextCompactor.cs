namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 通用文字壓縮介面 — 將 content 壓縮至 tokenBudget 內。
/// context 提供壓縮方向（例如 user query、goal、downstream node instructions）。
/// 回傳壓縮後的文字，或 null 表示不需要壓縮（已在 budget 內）或壓縮失敗。
/// <para>
/// 所有壓縮場景統一使用此介面：
/// <list type="bullet">
///   <item>RAG：content = 串接的 chunks，context = user query</item>
///   <item>對話歷史：content = 序列化的舊 messages，context = current goal</item>
///   <item>節點輸出：content = node output，context = downstream node instructions</item>
/// </list>
/// </para>
/// </summary>
public interface IContextCompactor
{
    /// <summary>
    /// 將 content 壓縮至 tokenBudget 內。
    /// </summary>
    /// <param name="content">要壓縮的原始文字。</param>
    /// <param name="context">壓縮方向指引（讓 LLM 知道保留什麼資訊最重要）。</param>
    /// <param name="tokenBudget">目標 token 預算。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>壓縮後的文字，或 null 表示不需要壓縮或壓縮失敗。</returns>
    Task<string?> CompressAsync(string content, string context, int tokenBudget, CancellationToken ct = default);
}
