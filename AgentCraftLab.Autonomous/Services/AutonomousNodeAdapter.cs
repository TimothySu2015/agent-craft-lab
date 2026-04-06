using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// IAutonomousNodeExecutor 實作 — 將 Engine 的 AutonomousNodeRequest 轉換為 GoalExecutionRequest，
/// 委派給 IGoalExecutor 執行。不再直接依賴 ReactExecutor，透過介面解耦。
/// </summary>
public sealed class AutonomousNodeAdapter : IAutonomousNodeExecutor
{
    private readonly IGoalExecutor _executor;

    public AutonomousNodeAdapter(IGoalExecutor executor)
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
