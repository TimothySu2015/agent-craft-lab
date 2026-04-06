using System.Text.Json;
using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Engine.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 包裝高風險工具的 AIFunction。執行前檢查 ApprovedTools 白名單，
/// 未核准時設旗標回傳 [BLOCKED]，由 ReactExecutor 暫停等人類審批。
/// </summary>
public sealed class RiskGateFunction : AIFunction
{
    private readonly AIFunction _inner;
    private readonly RiskApprovalContext _context;
    private readonly RiskRule _rule;

    public RiskGateFunction(AIFunction inner, RiskApprovalContext context, RiskRule rule)
    {
        _inner = inner;
        _context = context;
        _rule = rule;
    }

    public override string Name => _inner.Name;
    public override string Description => _inner.Description;
    public override JsonElement JsonSchema => _inner.JsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // 已核准 → 直接執行原工具
        if (_context.IsApproved(_inner.Name))
        {
            return await _inner.InvokeAsync(arguments, cancellationToken);
        }

        // Block 行為 → 永遠阻擋
        if (_rule.Action == RiskAction.Block)
        {
            return $"[BLOCKED] Tool '{_inner.Name}' is blocked by risk policy (level: {_rule.RiskLevel}). This tool cannot be used. Find an alternative approach.";
        }

        // RequireApproval → 設旗標，由 ReactExecutor 暫停
        var argsText = arguments.Count > 0
            ? JsonSerializer.Serialize(arguments, JsonDefaults.A2AOptions)
            : "{}";
        _context.RequestApproval(_inner.Name, argsText, _rule.RiskLevel);

        return $"[BLOCKED] Tool '{_inner.Name}' requires human approval (risk level: {_rule.RiskLevel}). Waiting for approval before execution.";
    }
}
