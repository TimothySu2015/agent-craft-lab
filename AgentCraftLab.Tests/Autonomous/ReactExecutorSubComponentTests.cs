using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Autonomous.Services;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Tests.Autonomous;

/// <summary>
/// ReactExecutor 子元件測試 — TokenTracker / ToolCallTracker / ConvergenceDetector / GoalRequestConverter
/// </summary>
public class ReactExecutorSubComponentTests
{
    // ════════════════════════════════════════
    // TokenTracker
    // ════════════════════════════════════════

    [Fact]
    public void TokenTracker_InitialState_Zero()
    {
        var tracker = new TokenTracker(new TokenBudget { MaxTotalTokens = 1000 });

        Assert.Equal(0, tracker.TotalTokensUsed);
        Assert.False(tracker.IsExceeded);
    }

    [Fact]
    public void TokenTracker_Record_AccumulatesTokens()
    {
        var tracker = new TokenTracker(new TokenBudget { MaxTotalTokens = 1000 });

        tracker.Record(100, 50);
        tracker.Record(200, 80);

        Assert.Equal(300, tracker.InputTokensUsed);
        Assert.Equal(130, tracker.OutputTokensUsed);
        Assert.Equal(430, tracker.TotalTokensUsed);
    }

    [Fact]
    public void TokenTracker_ExceedsBudget_IsExceeded()
    {
        var tracker = new TokenTracker(new TokenBudget { MaxTotalTokens = 100 });

        var exceeded = tracker.Record(80, 30);

        Assert.True(exceeded);
        Assert.True(tracker.IsExceeded);
    }

    [Fact]
    public void TokenTracker_WithinBudget_NotExceeded()
    {
        var tracker = new TokenTracker(new TokenBudget { MaxTotalTokens = 1000 });

        var exceeded = tracker.Record(100, 50);

        Assert.False(exceeded);
        Assert.False(tracker.IsExceeded);
    }

    [Fact]
    public void TokenTracker_UsagePercent()
    {
        var tracker = new TokenTracker(new TokenBudget { MaxTotalTokens = 200 });

        tracker.Record(50, 50);

        Assert.Equal(50, tracker.UsagePercent);
    }

    [Fact]
    public void TokenTracker_Remaining()
    {
        var tracker = new TokenTracker(new TokenBudget { MaxTotalTokens = 1000 });

        tracker.Record(300, 200);

        Assert.Equal(500, tracker.Remaining);
    }

    [Fact]
    public void TokenTracker_Unlimited_RemainingNegativeOne()
    {
        var tracker = new TokenTracker(new TokenBudget { MaxTotalTokens = 0 });

        tracker.Record(9999, 9999);

        Assert.Equal(-1, tracker.Remaining);
        Assert.False(tracker.IsExceeded);
    }

    [Fact]
    public void TokenTracker_ShouldStop_WhenExceededAndStopAction()
    {
        var tracker = new TokenTracker(new TokenBudget { MaxTotalTokens = 100, OnExceed = BudgetExceededAction.Stop });

        tracker.Record(80, 30);

        Assert.True(tracker.ShouldStop);
    }

    [Fact]
    public void TokenTracker_ShouldNotStop_WhenExceededButWarnAction()
    {
        var tracker = new TokenTracker(new TokenBudget { MaxTotalTokens = 100, OnExceed = BudgetExceededAction.Warn });

        tracker.Record(80, 30);

        Assert.True(tracker.IsExceeded);
        Assert.False(tracker.ShouldStop);
    }

    // ════════════════════════════════════════
    // ToolCallTracker
    // ════════════════════════════════════════

    [Fact]
    public void ToolCallTracker_InitialState()
    {
        var tracker = new ToolCallTracker(new ToolCallLimits { MaxTotalCalls = 50 });

        Assert.Equal(0, tracker.TotalCalls);
        Assert.Equal(50, tracker.TotalRemaining);
    }

    [Fact]
    public void ToolCallTracker_Record_Increments()
    {
        var tracker = new ToolCallTracker(new ToolCallLimits { MaxTotalCalls = 50 });

        tracker.Record("web_search");
        tracker.Record("web_search");
        tracker.Record("read_url");

        Assert.Equal(3, tracker.TotalCalls);
        Assert.Equal(2, tracker.CallCounts["web_search"]);
        Assert.Equal(1, tracker.CallCounts["read_url"]);
    }

    [Fact]
    public void ToolCallTracker_CanCall_RespectsPerToolLimit()
    {
        var tracker = new ToolCallTracker(new ToolCallLimits
        {
            MaxTotalCalls = 100,
            DefaultPerToolLimit = 2
        });

        Assert.True(tracker.Record("tool-a"));
        Assert.True(tracker.Record("tool-a"));
        Assert.False(tracker.CanCall("tool-a")); // 已達 per-tool limit
        Assert.True(tracker.CanCall("tool-b"));  // 其他工具不受影響
    }

    [Fact]
    public void ToolCallTracker_CanCall_RespectsMaxTotal()
    {
        var tracker = new ToolCallTracker(new ToolCallLimits { MaxTotalCalls = 2, DefaultPerToolLimit = 100 });

        tracker.Record("a");
        tracker.Record("b");

        Assert.False(tracker.CanCall("c")); // 總數已達上限
    }

    [Fact]
    public void ToolCallTracker_Remaining_PerTool()
    {
        var tracker = new ToolCallTracker(new ToolCallLimits { MaxTotalCalls = 100, DefaultPerToolLimit = 5 });

        tracker.Record("search");
        tracker.Record("search");

        Assert.Equal(3, tracker.Remaining("search"));
        Assert.Equal(5, tracker.Remaining("other"));
    }

    [Fact]
    public void ToolCallTracker_CustomPerToolLimit()
    {
        var tracker = new ToolCallTracker(new ToolCallLimits
        {
            MaxTotalCalls = 100,
            DefaultPerToolLimit = 10,
            PerToolLimits = new Dictionary<string, int> { ["expensive_tool"] = 1 }
        });

        Assert.True(tracker.Record("expensive_tool"));
        Assert.False(tracker.CanCall("expensive_tool"));
        Assert.True(tracker.CanCall("normal_tool")); // 用 default limit
    }

    [Fact]
    public void ToolCallTracker_GetUsageSummary_Empty()
    {
        var tracker = new ToolCallTracker(new ToolCallLimits { MaxTotalCalls = 50 });

        var summary = tracker.GetUsageSummary();

        Assert.Contains("50", summary);
    }

    [Fact]
    public void ToolCallTracker_GetUsageSummary_WithCalls()
    {
        var tracker = new ToolCallTracker(new ToolCallLimits { MaxTotalCalls = 50, DefaultPerToolLimit = 10 });

        tracker.Record("search");
        tracker.Record("search");

        var summary = tracker.GetUsageSummary();

        Assert.Contains("2/50", summary);
        Assert.Contains("search", summary);
    }

    // ════════════════════════════════════════
    // ConvergenceDetector
    // ════════════════════════════════════════

    [Fact]
    public void Convergence_NotEnoughHistory_ReturnsFalse()
    {
        var detector = new ConvergenceDetector();

        detector.RecordToolCall("search", "result A");

        Assert.False(detector.ShouldTerminateEarly());
    }

    [Fact]
    public void Convergence_SameToolSameResult_ReturnsTrue()
    {
        var detector = new ConvergenceDetector();

        detector.RecordToolCall("search", "NVIDIA stock price is $120");
        detector.RecordToolCall("search", "NVIDIA stock price is $120");
        detector.RecordToolCall("search", "NVIDIA stock price is $120");

        Assert.True(detector.ShouldTerminateEarly());
    }

    [Fact]
    public void Convergence_DifferentTools_ReturnsFalse()
    {
        var detector = new ConvergenceDetector();

        detector.RecordToolCall("search", "result A");
        detector.RecordToolCall("read_url", "result B");
        detector.RecordToolCall("analyze", "result C");

        Assert.False(detector.ShouldTerminateEarly());
    }

    [Fact]
    public void Convergence_SameToolDifferentResults_ReturnsFalse()
    {
        var detector = new ConvergenceDetector();

        detector.RecordToolCall("search", "Apple revenue is $400B");
        detector.RecordToolCall("search", "Google revenue is $300B");
        detector.RecordToolCall("search", "Microsoft revenue is $200B");

        Assert.False(detector.ShouldTerminateEarly());
    }

    [Fact]
    public void Convergence_InformationDepleted_ReturnsTrue()
    {
        var detector = new ConvergenceDetector();

        // 5 步中有 3 步回應極短 → 資訊枯竭
        detector.RecordResponseLength(10);
        detector.RecordResponseLength(10);
        detector.RecordResponseLength(10);
        detector.RecordResponseLength(10);
        detector.RecordResponseLength(10);

        // 也需要至少 3 個 tool call history（不同工具名）
        detector.RecordToolCall("a", "x");
        detector.RecordToolCall("b", "y");
        detector.RecordToolCall("c", "z");

        Assert.True(detector.ShouldTerminateEarly());
    }

    // ════════════════════════════════════════
    // GoalRequestConverter
    // ════════════════════════════════════════

    [Fact]
    public void GoalRequestConverter_RoundTrip_PreservesFields()
    {
        var original = new GoalExecutionRequest
        {
            Goal = "Analyze NVIDIA stock",
            Provider = "openai",
            Model = "gpt-4o",
            MaxIterations = 10,
            MaxTotalTokens = 50000,
            MaxToolCalls = 30,
            AvailableTools = ["web_search", "read_url"],
            Credentials = new Dictionary<string, ProviderCredential>
            {
                ["openai"] = new() { ApiKey = "sk-test" }
            }
        };

        var autonomous = GoalRequestConverter.ToAutonomousRequest(original);
        var converted = GoalRequestConverter.ToGoalRequest(autonomous);

        Assert.Equal(original.Goal, converted.Goal);
        Assert.Equal(original.Provider, converted.Provider);
        Assert.Equal(original.Model, converted.Model);
        Assert.Equal(original.MaxIterations, converted.MaxIterations);
        Assert.Equal(original.MaxTotalTokens, converted.MaxTotalTokens);
        Assert.Equal(original.MaxToolCalls, converted.MaxToolCalls);
        Assert.Equal(original.AvailableTools, converted.AvailableTools);
    }

    [Fact]
    public void GoalRequestConverter_BudgetMapping()
    {
        var goal = new GoalExecutionRequest
        {
            Goal = "test",
            MaxTotalTokens = 100000,
            Credentials = [],
        };

        var autonomous = GoalRequestConverter.ToAutonomousRequest(goal);

        Assert.Equal(100000, autonomous.Budget.MaxTotalTokens);
    }

    [Fact]
    public void GoalRequestConverter_ToolLimitsMapping()
    {
        var goal = new GoalExecutionRequest
        {
            Goal = "test",
            MaxToolCalls = 25,
            Credentials = [],
        };

        var autonomous = GoalRequestConverter.ToAutonomousRequest(goal);

        Assert.Equal(25, autonomous.ToolLimits.MaxTotalCalls);
    }

    [Fact]
    public void GoalRequestConverter_NullOptions_DefaultValues()
    {
        var goal = new GoalExecutionRequest { Goal = "test", Credentials = [], Options = null };

        var autonomous = GoalRequestConverter.ToAutonomousRequest(goal);

        Assert.Equal(0, autonomous.Budget.MaxInputTokens);
        Assert.Equal(BudgetExceededAction.Stop, autonomous.Budget.OnExceed);
        Assert.Null(autonomous.Risk);
        Assert.Null(autonomous.Reflection);
    }
}
