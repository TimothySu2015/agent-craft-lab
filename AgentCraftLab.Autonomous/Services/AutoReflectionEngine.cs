using AgentCraftLab.Autonomous.Models;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 自動反思引擎 — 根據任務複雜度自動選擇 Single Auditor 或 Multi-Agent Panel。
/// 簡單任務用 Single（省成本），複雜任務用 Panel（高品質）。
/// </summary>
public sealed class AutoReflectionEngine(
    AuditorReflectionEngine singleEngine,
    MultiAgentReflectionEngine panelEngine) : IReflectionEngine
{
    /// <summary>判定任務需要 Panel 審查的步驟數門檻。</summary>
    private const int ComplexityStepThreshold = 5;

    /// <summary>判定任務需要 Panel 審查的答案長度門檻。</summary>
    private const int ComplexityAnswerLengthThreshold = 1000;

    /// <inheritdoc />
    public Task<AuditResult> AuditAsync(
        AutonomousRequest request,
        string finalAnswer,
        ReflectionConfig reflection,
        CancellationToken cancellationToken)
    {
        var engine = ShouldUsePanel(request, finalAnswer, reflection)
            ? (IReflectionEngine)panelEngine
            : singleEngine;

        return engine.AuditAsync(request, finalAnswer, reflection, cancellationToken);
    }

    /// <summary>
    /// 判斷是否應使用 Panel 模式。
    /// 複雜度信號：迭代次數多、有 sub-agent、答案長、目標包含多步驟關鍵字。
    /// </summary>
    private static bool ShouldUsePanel(AutonomousRequest request, string finalAnswer, ReflectionConfig reflection)
    {
        // 明確指定 Panel 或 Single → 直接遵從
        if (reflection.Mode == ReflectionMode.Panel)
        {
            return true;
        }

        if (reflection.Mode == ReflectionMode.Single)
        {
            return false;
        }

        // Auto 模式：根據複雜度信號判斷
        var complexityScore = 0;

        // 信號 1：答案長度
        if (finalAnswer.Length > ComplexityAnswerLengthThreshold)
        {
            complexityScore++;
        }

        // 信號 2：目標包含多步驟關鍵字
        if (SystemPromptBuilder.IsComplexGoal(request.Goal))
        {
            complexityScore++;
        }

        // 信號 3：高迭代上限（代表預期是複雜任務）
        if (request.MaxIterations > ComplexityStepThreshold)
        {
            complexityScore++;
        }

        // 2+ 信號 → Panel
        return complexityScore >= 2;
    }
}
