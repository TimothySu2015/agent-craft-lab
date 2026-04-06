using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>人機互動處理結果。</summary>
public record HumanInteractionResult(
    List<ExecutionEvent> Events,
    int IterationAdjustment,
    int AskUserCountIncrement = 0);

/// <summary>
/// 人機互動處理器 — Risk Approval + AskUser 暫停/恢復 + Risk 工具包裝。
/// </summary>
public interface IHumanInteractionHandler
{
    /// <summary>包裝高風險工具。</summary>
    void WrapRiskTools(List<AITool> tools, RiskApprovalContext riskCtx, List<Models.RiskRule> rules);

    /// <summary>處理待審批的高風險工具呼叫。</summary>
    Task<HumanInteractionResult> HandlePendingRiskApprovalsAsync(
        RiskApprovalContext riskCtx, List<ChatMessage> messages, CancellationToken ct);

    /// <summary>處理 ask_user 使用者提問。</summary>
    Task<HumanInteractionResult> HandlePendingUserInputAsync(
        AskUserContext askUserCtx, List<ChatMessage> messages,
        int currentAskUserCount, int maxAskUserCalls, CancellationToken ct);
}
