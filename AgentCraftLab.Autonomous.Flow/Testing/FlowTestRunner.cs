using AgentCraftLab.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Autonomous.Flow.Testing;

/// <summary>
/// Flow 自動化測試執行器 — 跑預定義情境、收集事件、驗證斷言、產生報告。
/// </summary>
public sealed class FlowTestRunner
{
    private readonly IGoalExecutor _executor;
    private readonly ILogger<FlowTestRunner> _logger;

    public FlowTestRunner(IGoalExecutor executor, ILogger<FlowTestRunner> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    /// <summary>
    /// 執行所有情境，逐一 yield 結果（讓 UI 即時顯示進度）。
    /// </summary>
    public async IAsyncEnumerable<(FlowTestScenario Scenario, FlowTestResult Result)> RunAllAsync(
        Dictionary<string, ProviderCredential> credentials,
        string provider,
        string model,
        IReadOnlyList<FlowTestScenario>? scenarios = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        Action<ExecutionEvent>? onEvent = null)
    {
        scenarios ??= BuiltInScenarios.All;

        foreach (var scenario in scenarios)
        {
            var result = await RunOneAsync(scenario, credentials, provider, model, cancellationToken, onEvent);
            yield return (scenario, result);
        }
    }

    /// <summary>
    /// 執行單一情境。
    /// </summary>
    public async Task<FlowTestResult> RunOneAsync(
        FlowTestScenario scenario,
        Dictionary<string, ProviderCredential> credentials,
        string provider,
        string model,
        CancellationToken cancellationToken = default,
        Action<ExecutionEvent>? onEvent = null)
    {
        var result = new FlowTestResult
        {
            ScenarioId = scenario.Id,
            ScenarioName = scenario.Name
        };

        var sw = Stopwatch.StartNew();

        var request = new GoalExecutionRequest
        {
            Goal = scenario.Goal,
            Credentials = credentials,
            Provider = provider,
            Model = model,
            AvailableTools = scenario.RequiredTools,
            MaxIterations = scenario.MaxIterations,
            MaxTotalTokens = scenario.MaxTotalTokens,
            MaxToolCalls = 50
        };

        try
        {
            await foreach (var evt in _executor.ExecuteAsync(request, cancellationToken))
            {
                CollectEvent(evt, result);
                onEvent?.Invoke(evt);
            }
        }
        catch (Exception ex)
        {
            result.HasError = true;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Test scenario '{Id}' threw exception", scenario.Id);
        }

        result.Duration = sw.Elapsed;

        // 驗證斷言
        Validate(scenario, result);

        return result;
    }

    private static void CollectEvent(ExecutionEvent evt, FlowTestResult result)
    {
        switch (evt.Type)
        {
            case EventTypes.PlanGenerated:
                result.HasPlan = true;
                var match = Regex.Match(evt.Text, @"(\d+) nodes?");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var count))
                    result.NodeCount = count;
                if (evt.Metadata?.TryGetValue(MetadataKeys.Tokens, out var planTokenStr) == true
                    && long.TryParse(planTokenStr, out var planTokens))
                {
                    result.TotalTokens += planTokens;
                }
                break;

            case EventTypes.NodeExecuting:
                var nodeType = evt.Metadata?.GetValueOrDefault("nodeType");
                if (nodeType is not null)
                    result.ObservedNodeTypes.Add(nodeType);
                break;

            case EventTypes.ToolCall:
                result.ToolCallCount++;
                break;

            case EventTypes.ReasoningStep:
                if (evt.Metadata?.TryGetValue(MetadataKeys.Tokens, out var tokenStr) == true
                    && long.TryParse(tokenStr, out var tokens))
                {
                    result.TotalTokens += tokens;
                }
                break;

            case EventTypes.AgentCompleted:
                if (!string.IsNullOrWhiteSpace(evt.Text))
                    result.FinalOutput = evt.Text.Length > 500 ? evt.Text[..500] + "..." : evt.Text;
                break;

            case EventTypes.WorkflowCompleted:
                result.HasWorkflowCompleted = true;
                break;

            case EventTypes.Error:
                result.HasError = true;
                result.ErrorMessage = evt.Text;
                break;
        }
    }

    private static void Validate(FlowTestScenario scenario, FlowTestResult result)
    {
        if (!result.HasPlan)
            result.Failures.Add("No plan generated");

        if (!result.HasWorkflowCompleted && !scenario.ExpectError)
            result.Failures.Add("WorkflowCompleted event missing");

        if (result.HasError && !scenario.ExpectError)
            result.Failures.Add($"Unexpected error: {result.ErrorMessage}");

        // 節點類型檢查（expected 必須是 observed 的子集）
        var missingTypes = scenario.ExpectedNodeTypes.Except(result.ObservedNodeTypes).ToList();
        if (missingTypes.Count > 0)
            result.Failures.Add($"Missing node types: {string.Join(", ", missingTypes)}");

        // ToolCall 範圍
        var (min, max) = scenario.ExpectedToolCallRange;
        if (result.ToolCallCount < min || result.ToolCallCount > max)
            result.Failures.Add($"ToolCall count {result.ToolCallCount} out of range [{min}, {max}]");

        result.Passed = result.Failures.Count == 0;
    }
}
