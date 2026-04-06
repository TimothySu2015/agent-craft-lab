using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// IAutonomousNodeExecutor 實作 — 將 Engine 的 AutonomousNodeRequest 委派給 IGoalExecutor（FlowExecutor）。
/// </summary>
public sealed class FlowNodeAdapter : IAutonomousNodeExecutor
{
    private readonly IGoalExecutor _executor;

    public FlowNodeAdapter(IGoalExecutor executor)
    {
        _executor = executor;
    }

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        AutonomousNodeRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var goalRequest = GoalExecutionRequest.FromNodeRequest(request);

        await foreach (var evt in _executor.ExecuteAsync(goalRequest, cancellationToken))
        {
            yield return evt;
        }
    }
}
