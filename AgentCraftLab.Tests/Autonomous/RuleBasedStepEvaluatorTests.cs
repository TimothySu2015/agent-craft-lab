using AgentCraftLab.Autonomous.Services;

namespace AgentCraftLab.Tests.Autonomous;

public class RuleBasedStepEvaluatorTests
{
    private readonly RuleBasedStepEvaluator _evaluator = new();

    private static StepContext MakeContext(
        string toolName = "WebSearch",
        string toolArgs = "{\"query\":\"test\"}",
        string toolResult = "Some result",
        bool hasTextResponse = false,
        string? prevToolName = null,
        string? prevToolArgs = null,
        int consecutiveNoTextSteps = 0,
        int consecutiveToolFailures = 0)
    {
        return new StepContext
        {
            Iteration = 1,
            MaxIterations = 20,
            ToolName = toolName,
            ToolArgs = toolArgs,
            ToolResult = toolResult,
            HasTextResponse = hasTextResponse,
            PreviousToolName = prevToolName,
            PreviousToolArgs = prevToolArgs,
            ConsecutiveNoTextSteps = consecutiveNoTextSteps,
            ConsecutiveToolFailures = consecutiveToolFailures
        };
    }

    // ─── 正常情況：無問題 ───

    [Fact]
    public void Evaluate_NormalStep_ReturnsNull()
    {
        var result = _evaluator.Evaluate(MakeContext());
        Assert.Null(result);
    }

    // ─── 規則 1：空結果 ───

    [Fact]
    public void Evaluate_EmptyResult_ReturnsWarning()
    {
        var result = _evaluator.Evaluate(MakeContext(toolResult: ""));
        Assert.NotNull(result);
        Assert.Equal(StepEvalLevel.Warning, result.Level);
        Assert.Equal("empty_or_error_result", result.RuleName);
    }

    [Fact]
    public void Evaluate_ErrorResult_ReturnsWarning()
    {
        var result = _evaluator.Evaluate(MakeContext(toolResult: "[Error] Connection timeout"));
        Assert.NotNull(result);
        Assert.Equal("empty_or_error_result", result.RuleName);
    }

    [Fact]
    public void Evaluate_WhitespaceResult_ReturnsWarning()
    {
        var result = _evaluator.Evaluate(MakeContext(toolResult: "   "));
        Assert.NotNull(result);
        Assert.Equal("empty_or_error_result", result.RuleName);
    }

    // ─── 規則 2：重複呼叫 ───

    [Fact]
    public void Evaluate_DuplicateCall_ReturnsWarning()
    {
        var result = _evaluator.Evaluate(MakeContext(
            toolName: "WebSearch",
            toolArgs: "{\"query\":\"AI\"}",
            prevToolName: "WebSearch",
            prevToolArgs: "{\"query\":\"AI\"}"));

        Assert.NotNull(result);
        Assert.Equal(StepEvalLevel.Warning, result.Level);
        Assert.Equal("duplicate_call", result.RuleName);
    }

    [Fact]
    public void Evaluate_SameToolDifferentArgs_ReturnsNull()
    {
        var result = _evaluator.Evaluate(MakeContext(
            toolName: "WebSearch",
            toolArgs: "{\"query\":\"AI\"}",
            prevToolName: "WebSearch",
            prevToolArgs: "{\"query\":\"ML\"}"));

        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_DifferentTool_ReturnsNull()
    {
        var result = _evaluator.Evaluate(MakeContext(
            toolName: "Calculator",
            prevToolName: "WebSearch",
            prevToolArgs: "{\"query\":\"AI\"}"));

        Assert.Null(result);
    }

    // ─── 規則 3：結果膨脹 ───

    [Fact]
    public void Evaluate_LargeResult_ReturnsHint()
    {
        var largeResult = new string('x', 5001);
        var result = _evaluator.Evaluate(MakeContext(toolResult: largeResult));

        Assert.NotNull(result);
        Assert.Equal(StepEvalLevel.Hint, result.Level);
        Assert.Equal("result_bloat", result.RuleName);
    }

    [Fact]
    public void Evaluate_ExactThreshold_ReturnsNull()
    {
        var exactResult = new string('x', 5000);
        var result = _evaluator.Evaluate(MakeContext(toolResult: exactResult));
        Assert.Null(result);
    }

    // ─── 規則 4：連續失敗 ───

    [Fact]
    public void Evaluate_ConsecutiveFailures_ReturnsBlock()
    {
        var result = _evaluator.Evaluate(MakeContext(
            consecutiveToolFailures: 2));

        Assert.NotNull(result);
        Assert.Equal(StepEvalLevel.Block, result.Level);
        Assert.Equal("consecutive_failures", result.RuleName);
    }

    [Fact]
    public void Evaluate_SingleFailure_ReturnsNull()
    {
        var result = _evaluator.Evaluate(MakeContext(
            consecutiveToolFailures: 1));

        // 單次失敗不觸發（門檻 2），但空結果規則可能觸發
        // 這裡用正常結果測試
        Assert.Null(result);
    }

    // ─── 規則 5：偏離目標 ───

    [Fact]
    public void Evaluate_GoalDrift_ReturnsWarning()
    {
        var result = _evaluator.Evaluate(MakeContext(
            consecutiveNoTextSteps: 3));

        Assert.NotNull(result);
        Assert.Equal(StepEvalLevel.Warning, result.Level);
        Assert.Equal("goal_drift", result.RuleName);
    }

    [Fact]
    public void Evaluate_TwoStepsNoText_ReturnsNull()
    {
        var result = _evaluator.Evaluate(MakeContext(
            consecutiveNoTextSteps: 2));

        Assert.Null(result);
    }

    // ─── 優先順序 ───

    [Fact]
    public void Evaluate_EmptyResultTakesPriority_OverDuplicate()
    {
        // 空結果 + 重複呼叫 → 空結果優先
        var result = _evaluator.Evaluate(MakeContext(
            toolResult: "",
            prevToolName: "WebSearch",
            prevToolArgs: "{\"query\":\"test\"}"));

        Assert.NotNull(result);
        Assert.Equal("empty_or_error_result", result.RuleName);
    }

    // ─── Hint 訊息格式 ───

    [Fact]
    public void Evaluate_HintContainsToolName()
    {
        var result = _evaluator.Evaluate(MakeContext(
            toolName: "AzureWebSearch",
            toolResult: ""));

        Assert.NotNull(result);
        Assert.Contains("AzureWebSearch", result.Hint);
    }
}
