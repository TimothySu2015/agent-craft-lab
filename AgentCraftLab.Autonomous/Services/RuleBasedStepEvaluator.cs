namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 確定性步驟評估器（Route A）— 基於規則的每步品質檢查，零 LLM 成本。
/// 五條規則：空結果、重複呼叫、結果膨脹、連續失敗、偏離目標。
/// </summary>
public sealed class RuleBasedStepEvaluator : IStepEvaluator
{
    /// <summary>結果膨脹門檻（字元數）。</summary>
    private const int ResultBloatThreshold = 5000;

    /// <summary>連續失敗觸發門檻。</summary>
    private const int ConsecutiveFailureThreshold = 2;

    /// <summary>偏離目標觸發門檻（連續無文字回應步數）。</summary>
    private const int DriftThreshold = 3;

    /// <inheritdoc />
    public StepEvaluation? Evaluate(StepContext context)
    {
        // 規則 1：空結果或錯誤
        var emptyResult = CheckEmptyOrError(context);
        if (emptyResult is not null)
        {
            return emptyResult;
        }

        // 規則 2：重複呼叫（相同工具 + 相同參數）
        var duplicateResult = CheckDuplicateCall(context);
        if (duplicateResult is not null)
        {
            return duplicateResult;
        }

        // 規則 3：結果膨脹
        var bloatResult = CheckResultBloat(context);
        if (bloatResult is not null)
        {
            return bloatResult;
        }

        // 規則 4：連續失敗
        var failureResult = CheckConsecutiveFailures(context);
        if (failureResult is not null)
        {
            return failureResult;
        }

        // 規則 5：偏離目標
        var driftResult = CheckGoalDrift(context);
        if (driftResult is not null)
        {
            return driftResult;
        }

        return null;
    }

    /// <summary>常見的「無結果」模式（工具回傳有內容但實質上是空結果）。</summary>
    private static readonly string[] NoResultPatterns =
    [
        "[Error]",
        "No results found",
        "No Wikipedia results",
        "no relevant results",
        "could not find",
        "returned no results",
        "0 results",
        "Error:",
        "failed to"
    ];

    private static StepEvaluation? CheckEmptyOrError(StepContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.ToolResult))
        {
            return MakeEmptyResultEvaluation(ctx);
        }

        foreach (var pattern in NoResultPatterns)
        {
            if (ctx.ToolResult.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return MakeEmptyResultEvaluation(ctx);
            }
        }

        return null;
    }

    private static StepEvaluation MakeEmptyResultEvaluation(StepContext ctx)
    {
        return new StepEvaluation
        {
            Level = StepEvalLevel.Warning,
            RuleName = "empty_or_error_result",
            Hint = $"[Step Hint] Tool '{ctx.ToolName}' returned an empty or error result. " +
                   "Consider: (1) using a different tool, (2) adjusting your query parameters, " +
                   "or (3) trying an alternative approach."
        };
    }

    private static StepEvaluation? CheckDuplicateCall(StepContext ctx)
    {
        if (ctx.PreviousToolName is not null &&
            ctx.ToolName == ctx.PreviousToolName &&
            ctx.ToolArgs == ctx.PreviousToolArgs)
        {
            return new StepEvaluation
            {
                Level = StepEvalLevel.Warning,
                RuleName = "duplicate_call",
                Hint = $"[Step Hint] You just called '{ctx.ToolName}' with the exact same parameters. " +
                       "This is likely redundant. Try a different query, a different tool, or proceed to answer."
            };
        }

        return null;
    }

    private static StepEvaluation? CheckResultBloat(StepContext ctx)
    {
        if (ctx.ToolResult.Length > ResultBloatThreshold)
        {
            return new StepEvaluation
            {
                Level = StepEvalLevel.Hint,
                RuleName = "result_bloat",
                Hint = $"[Step Hint] Tool '{ctx.ToolName}' returned a very large result ({ctx.ToolResult.Length} chars). " +
                       "Use more specific queries (e.g., read_file with offset/limit, search with narrower regex) " +
                       "to reduce context usage."
            };
        }

        return null;
    }

    private static StepEvaluation? CheckConsecutiveFailures(StepContext ctx)
    {
        if (ctx.ConsecutiveToolFailures >= ConsecutiveFailureThreshold)
        {
            return new StepEvaluation
            {
                Level = StepEvalLevel.Block,
                RuleName = "consecutive_failures",
                Hint = $"[Step Hint] Tool '{ctx.ToolName}' has failed {ctx.ConsecutiveToolFailures} times in a row. " +
                       "STOP using this tool and switch to an alternative approach. " +
                       "If no alternative exists, explain what you need and move on."
            };
        }

        return null;
    }

    private static StepEvaluation? CheckGoalDrift(StepContext ctx)
    {
        if (ctx.ConsecutiveNoTextSteps >= DriftThreshold)
        {
            return new StepEvaluation
            {
                Level = StepEvalLevel.Warning,
                RuleName = "goal_drift",
                Hint = "[Step Hint] You have been calling tools for several steps without producing any text output. " +
                       "You may be drifting from the original goal. Pause and re-read the user's request, " +
                       "then decide if your current approach is making progress."
            };
        }

        return null;
    }
}
