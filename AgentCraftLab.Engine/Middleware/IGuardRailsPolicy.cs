namespace AgentCraftLab.Engine.Middleware;

/// <summary>
/// GuardRails 規則引擎介面。實作可替換為 ML 分類器、Azure Content Safety、NeMo Guardrails 等。
/// </summary>
public interface IGuardRailsPolicy
{
    /// <summary>
    /// 評估文字是否違反規則。
    /// </summary>
    /// <param name="text">待評估的文字。</param>
    /// <param name="direction">掃描方向（Input 或 Output）。</param>
    /// <returns>所有匹配的規則結果。</returns>
    IReadOnlyList<GuardRailsMatch> Evaluate(string text, GuardRailsDirection direction);
}
