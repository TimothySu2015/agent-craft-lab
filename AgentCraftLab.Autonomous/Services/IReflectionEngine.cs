using AgentCraftLab.Autonomous.Models;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 反思引擎介面 — Self-Reflection / Auditor 邏輯。
/// </summary>
public interface IReflectionEngine
{
    /// <summary>執行稽核，回傳審計結果。</summary>
    Task<AuditResult> AuditAsync(
        AutonomousRequest request,
        string finalAnswer,
        ReflectionConfig reflection,
        CancellationToken cancellationToken);
}
