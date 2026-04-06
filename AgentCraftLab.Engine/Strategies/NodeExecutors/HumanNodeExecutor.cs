using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Human 節點執行器 — 暫停等待使用者輸入，支援 text/choice/approval 模式。
/// </summary>
public sealed class HumanNodeExecutor : INodeExecutor
{
    public string NodeType => NodeTypes.Human;

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, WorkflowNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = string.IsNullOrWhiteSpace(node.Name) ? $"Human_{node.Id}" : node.Name;

        if (state.HumanBridge is null)
        {
            yield return ExecutionEvent.Error($"Human Node '{nodeName}' requires HumanInputBridge.");
            yield break;
        }

        var prompt = string.IsNullOrWhiteSpace(node.Prompt) ? "Please provide your input:" : node.Prompt;
        var inputType = string.IsNullOrWhiteSpace(node.InputType) ? "text" : node.InputType;
        var choices = node.Choices ?? "";

        yield return ExecutionEvent.WaitingForInput(nodeName, prompt, inputType, choices);

        string userInput;
        var timedOut = false;
        if (node.TimeoutSeconds > 0)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(node.TimeoutSeconds));
            try
            {
                userInput = await state.HumanBridge.WaitForInputAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                userInput = "timeout";
                timedOut = true;
            }
        }
        else
        {
            userInput = await state.HumanBridge.WaitForInputAsync(cancellationToken);
        }

        if (timedOut)
        {
            yield return ExecutionEvent.TextChunk(nodeName, $"Human input timed out after {node.TimeoutSeconds}s");
        }

        yield return ExecutionEvent.UserInputReceived(nodeName, userInput);
    }

    public Task<NodeExecutionResult> BuildResultAsync(
        string nodeId, WorkflowNode node,
        ImperativeExecutionState state, List<ExecutionEvent> collectedEvents,
        CancellationToken cancellationToken = default)
    {
        var userInput = collectedEvents
            .LastOrDefault(e => e.Type == EventTypes.UserInputReceived)?.Text ?? "";

        var outputPort = userInput == "reject" ? OutputPorts.Output2 : OutputPorts.Output1;

        return Task.FromResult(new NodeExecutionResult
        {
            Output = userInput,
            OutputPort = outputPort
        });
    }
}
