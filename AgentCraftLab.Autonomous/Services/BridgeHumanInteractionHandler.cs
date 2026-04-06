using System.Text.RegularExpressions;
using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 人機互動處理器實作 — 透過 HumanInputBridge 與人類溝通。
/// 負責 Risk Approval 審批、AskUser 暫停/恢復、高風險工具包裝。
/// </summary>
public sealed class BridgeHumanInteractionHandler(
    HumanInputBridge? humanBridge,
    ILogger<BridgeHumanInteractionHandler> logger) : IHumanInteractionHandler
{
    private const string AgentName = "Autonomous Agent";

    /// <summary>Risk 審批等待超時（秒）。超時自動拒絕。</summary>
    private const int ApprovalTimeoutSeconds = 300; // 5 分鐘

    /// <summary>使用者輸入等待超時（秒）。超時自動跳過。</summary>
    private const int UserInputTimeoutSeconds = 600; // 10 分鐘

    /// <inheritdoc />
    public void WrapRiskTools(
        List<AITool> tools, RiskApprovalContext riskCtx, List<RiskRule> rules)
    {
        for (var i = 0; i < tools.Count; i++)
        {
            if (tools[i] is not AIFunction func)
            {
                continue;
            }

            foreach (var rule in rules)
            {
                if (string.IsNullOrWhiteSpace(rule.ToolPattern))
                {
                    continue;
                }

                try
                {
                    if (Regex.IsMatch(func.Name, rule.ToolPattern, RegexOptions.IgnoreCase,
                            TimeSpan.FromSeconds(2)))
                    {
                        tools[i] = new RiskGateFunction(func, riskCtx, rule);
                        break; // 一個工具只匹配第一條規則
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    // regex 超時，保守地視為匹配 — 套用風險閘道保護
                    logger.LogWarning("Tool '{ToolName}' risk pattern timeout, applying risk gate conservatively", func.Name);
                    tools[i] = new RiskGateFunction(func, riskCtx, rule);
                    break;
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<HumanInteractionResult> HandlePendingRiskApprovalsAsync(
        RiskApprovalContext riskCtx, List<ChatMessage> messages, CancellationToken ct)
    {
        var events = new List<ExecutionEvent>();

        while (riskCtx.Dequeue() is { } pending)
        {
            events.Add(ExecutionEvent.WaitingForRiskApproval(
                AgentName, pending.ToolName, pending.Arguments, pending.RiskLevel));

            string approval;
            if (humanBridge is null)
            {
                // 無 HumanInputBridge → 自動拒絕
                logger.LogWarning("No HumanInputBridge available, auto-rejecting tool '{Tool}'", pending.ToolName);
                approval = "reject";
            }
            else try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(ApprovalTimeoutSeconds));
                approval = await humanBridge.WaitForInputAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 超時（非使用者取消）→ 自動拒絕
                logger.LogWarning("Risk approval timed out after {Seconds}s, auto-rejecting tool '{Tool}'",
                    ApprovalTimeoutSeconds, pending.ToolName);
                approval = "reject";
            }

            var approved = approval.Equals("approve", StringComparison.OrdinalIgnoreCase);
            events.Add(ExecutionEvent.RiskApprovalResult(AgentName, approved, pending.ToolName));

            if (approved)
            {
                riskCtx.Approve(pending.ToolName);
                messages.Add(new ChatMessage(ChatRole.User,
                    $"[Human approved tool '{pending.ToolName}'. Please retry calling it now.]"));
            }
            else
            {
                messages.Add(new ChatMessage(ChatRole.User,
                    $"[Human rejected tool '{pending.ToolName}'. Find an alternative approach or explain why this tool is necessary.]"));
            }
        }

        return new HumanInteractionResult(events, IterationAdjustment: -1);
    }

    /// <inheritdoc />
    public async Task<HumanInteractionResult> HandlePendingUserInputAsync(
        AskUserContext askUserCtx, List<ChatMessage> messages,
        int currentAskUserCount, int maxAskUserCalls, CancellationToken ct)
    {
        var events = new List<ExecutionEvent>();
        var countIncrement = 0;

        if (currentAskUserCount >= maxAskUserCalls)
        {
            // 超過上限，自動拒絕並要求 Agent 自行判斷
            messages.Add(new ChatMessage(ChatRole.User,
                "[System: Clarification limit reached. Proceed with your best judgment — no more questions allowed.]"));
            askUserCtx.Reset();
        }
        else
        {
            countIncrement = 1;
            events.Add(ExecutionEvent.WaitingForInput(
                AgentName, askUserCtx.Question, askUserCtx.InputType, askUserCtx.Choices ?? ""));

            string userResponse;
            if (humanBridge is null)
            {
                logger.LogWarning("No HumanInputBridge available, skipping user input");
                userResponse = "[No response — no input bridge available. Proceed with your best judgment.]";
            }
            else try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(UserInputTimeoutSeconds));
                userResponse = await humanBridge.WaitForInputAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 超時（非使用者取消）→ 自動跳過，讓 Agent 自行判斷
                logger.LogWarning("User input timed out after {Seconds}s, proceeding with best judgment",
                    UserInputTimeoutSeconds);
                userResponse = "[No response — timed out. Proceed with your best judgment.]";
            }

            events.Add(ExecutionEvent.UserInputReceived(AgentName, userResponse));

            messages.Add(new ChatMessage(ChatRole.User, $"[User response to your question]: {userResponse}"));
            askUserCtx.Reset();
        }

        return new HumanInteractionResult(events, IterationAdjustment: -1, AskUserCountIncrement: countIncrement);
    }
}
