using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics;
using AgentCraftLab.Engine.Diagnostics;
using AgentCraftLab.Engine.Models;
using Microsoft.Agents.AI.Workflows;

namespace AgentCraftLab.Engine.Strategies;

/// <summary>
/// Concurrent workflow 策略：所有 agents 同時執行。
/// </summary>
public class ConcurrentWorkflowStrategy : IWorkflowStrategy
{
    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowStrategyContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sessionId = context.SessionId;

        using var workflowActivity = EngineActivitySource.Source.StartActivity(
            "workflow_execute", ActivityKind.Server);
        workflowActivity?.SetTag("workflow.strategy", "concurrent");
        workflowActivity?.SetTag("workflow.agent_count", context.AgentContext.Agents.Count);
        if (sessionId is not null)
            workflowActivity?.SetTag(EngineActivitySource.SessionIdTag, sessionId);

        var agentList = context.AgentContext.Agents.Values.ToArray();
        Workflow workflow = AgentWorkflowBuilder.BuildConcurrent(agentList);

        await using StreamingRun run = await InProcessExecution.Default
            .OpenStreamingAsync(workflow);
        await run.TrySendMessageAsync(context.Request.UserMessage);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        var agentTexts = new Dictionary<string, StringBuilder>();
        var knownAgents = new HashSet<string>();

        await foreach (WorkflowEvent evt in run.WatchStreamAsync()
                           .WithCancellation(cancellationToken))
        {
            if (evt is AgentResponseUpdateEvent e)
            {
                var name = e.ExecutorId ?? "Agent";
                var text = e.Update?.Text ?? "";

                if (knownAgents.Add(name))
                {
                    agentTexts[name] = new StringBuilder();
                    yield return ExecutionEvent.AgentStarted(name);
                }

                if (!string.IsNullOrEmpty(text))
                {
                    agentTexts[name].Append(text);
                    yield return ExecutionEvent.TextChunk(name, text);
                }
            }
            else if (evt is WorkflowErrorEvent errorEvt)
            {
                var ex = errorEvt.Exception;
                var errorDetail = ex is not null
                    ? $"{ex.Message}{(ex.InnerException is not null ? $" → {ex.InnerException.Message}" : "")}\n{ex.StackTrace}"
                    : "Unknown workflow error";
                yield return ExecutionEvent.Error(errorDetail);
                yield break;
            }
            else if (evt is WorkflowOutputEvent)
            {
                foreach (var (name, sb) in agentTexts)
                {
                    var agentText = sb.ToString();
                    var est = ModelPricing.EstimateTokens(agentText);
                    yield return ExecutionEvent.AgentCompleted(name, agentText, est, est);
                }
                break;
            }
        }
    }
}
