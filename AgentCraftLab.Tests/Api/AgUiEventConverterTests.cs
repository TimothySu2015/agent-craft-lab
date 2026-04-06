using AgentCraftLab.Api;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Tests.Api;

public class AgUiEventConverterTests
{
    private const string ThreadId = "thread-1";
    private const string RunId = "run-1";
    private readonly AgUiEventConverter _converter = new(RunId);

    private List<AgUiEvent> Convert(ExecutionEvent evt)
        => _converter.Convert(evt, ThreadId, RunId).ToList();

    // ─── 1. AgentStarted ───

    [Fact]
    public void AgentStarted_EmitsStepStarted_TextMessageStart_ContentWithName_StateSnapshot()
    {
        var events = Convert(ExecutionEvent.AgentStarted("Planner"));

        Assert.Contains(events, e => e.Type == AgUiEventTypes.StepStarted && e.StepName == "Planner");
        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageStart && e.Role == "assistant");
        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageContent && e.Delta!.Contains("[Planner]"));
        Assert.Contains(events, e => e.Type == AgUiEventTypes.StateSnapshot);
    }

    // ─── 2. TextChunk ───

    [Fact]
    public void TextChunk_EmitsTextMessageContent_WithDelta()
    {
        // 先啟動 agent 以建立 active message
        Convert(ExecutionEvent.AgentStarted("Agent1"));

        var events = Convert(ExecutionEvent.TextChunk("Agent1", "Hello world"));

        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageContent && e.Delta == "Hello world");
    }

    [Fact]
    public void TextChunk_EmptyText_NoEvents()
    {
        Convert(ExecutionEvent.AgentStarted("Agent1"));

        var events = Convert(ExecutionEvent.TextChunk("Agent1", ""));

        Assert.Empty(events);
    }

    // ─── 3. AgentCompleted ───

    [Fact]
    public void AgentCompleted_EmitsStepFinished_StateSnapshot()
    {
        Convert(ExecutionEvent.AgentStarted("Agent1"));

        var events = Convert(ExecutionEvent.AgentCompleted("Agent1", "done"));

        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageEnd);
        Assert.Contains(events, e => e.Type == AgUiEventTypes.StepFinished && e.StepName == "Agent1");
        Assert.Contains(events, e => e.Type == AgUiEventTypes.StateSnapshot);
    }

    // ─── 4. ToolCall ───

    [Fact]
    public void ToolCall_EmitsToolCallStart_ToolCallArgs()
    {
        Convert(ExecutionEvent.AgentStarted("Agent1"));

        var events = Convert(ExecutionEvent.ToolCall("Agent1", "web_search", "query=hello"));

        Assert.Contains(events, e => e.Type == AgUiEventTypes.ToolCallStart && e.ToolCallName == "web_search");
        Assert.Contains(events, e => e.Type == AgUiEventTypes.ToolCallArgs && e.Delta == "query=hello");
    }

    [Fact]
    public void ToolCall_IncrementsToolCount()
    {
        Convert(ExecutionEvent.AgentStarted("Agent1"));
        Convert(ExecutionEvent.ToolCall("Agent1", "tool1", "a"));
        Convert(ExecutionEvent.ToolCall("Agent1", "tool2", "b"));
        Convert(ExecutionEvent.AgentCompleted("Agent1", "done"));

        var completed = Convert(ExecutionEvent.WorkflowCompleted());
        var statsEvent = completed.FirstOrDefault(e =>
            e.Type == AgUiEventTypes.TextMessageContent && e.Delta!.Contains("Tools 2"));

        Assert.NotNull(statsEvent);
    }

    // ─── 5. ToolResult ───

    [Fact]
    public void ToolResult_EmitsToolCallEnd_PopsFromStack()
    {
        Convert(ExecutionEvent.AgentStarted("Agent1"));
        Convert(ExecutionEvent.ToolCall("Agent1", "web_search", "q"));

        var events = Convert(ExecutionEvent.ToolResult("Agent1", "web_search", "result"));

        var endEvent = Assert.Single(events, e => e.Type == AgUiEventTypes.ToolCallEnd);
        Assert.Equal($"{RunId}-tc-1", endEvent.ToolCallId);
    }

    // ─── 6. Error ───

    [Fact]
    public void Error_EmitsErrorTextMessage_RunError()
    {
        var events = Convert(ExecutionEvent.Error("something broke"));

        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageStart);
        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageContent && e.Delta!.Contains("something broke"));
        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageEnd);
        Assert.Contains(events, e => e.Type == AgUiEventTypes.RunError && e.Message == "something broke");
    }

    // ─── 7. ReasoningStep ───

    [Fact]
    public void ReasoningStep_EmitsReasoningEvents_WithReasoningRole()
    {
        var events = Convert(ExecutionEvent.ReasoningStep("Agent1", 1, 5, 100, 500));

        Assert.Contains(events, e => e.Type == AgUiEventTypes.ReasoningStart);
        Assert.Contains(events, e => e.Type == AgUiEventTypes.ReasoningMessageStart && e.Role == "reasoning");
        Assert.Contains(events, e => e.Type == AgUiEventTypes.ReasoningMessageContent && e.Delta == "Step 1/5");
        Assert.Contains(events, e => e.Type == AgUiEventTypes.ReasoningMessageEnd);
        Assert.Contains(events, e => e.Type == AgUiEventTypes.ReasoningEnd);
    }

    [Fact]
    public void ReasoningStep_AccumulatesTokens()
    {
        Convert(ExecutionEvent.ReasoningStep("Agent1", 1, 3, 200, 100));
        Convert(ExecutionEvent.ReasoningStep("Agent1", 2, 3, 300, 100));

        var completed = Convert(ExecutionEvent.WorkflowCompleted());
        var statsEvent = completed.FirstOrDefault(e =>
            e.Type == AgUiEventTypes.TextMessageContent && e.Delta!.Contains("500"));

        Assert.NotNull(statsEvent);
    }

    // ─── 8. WorkflowCompleted ───

    [Fact]
    public void WorkflowCompleted_EmitsStatsMessage_StateSnapshot()
    {
        Convert(ExecutionEvent.AgentStarted("Agent1"));
        Convert(ExecutionEvent.AgentCompleted("Agent1", "done", 100, 50, "gpt-4o"));

        var events = Convert(ExecutionEvent.WorkflowCompleted());

        // Stats message: TextMessageStart → Content → End
        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageStart);
        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageContent);
        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageEnd);
        Assert.Contains(events, e => e.Type == AgUiEventTypes.StateSnapshot);
    }

    // ─── 9. Token Accumulation ───

    [Fact]
    public void AgentCompleted_WithTokenMetadata_AccumulatesTotalTokens()
    {
        Convert(ExecutionEvent.AgentStarted("Agent1"));
        Convert(ExecutionEvent.AgentCompleted("Agent1", "done", 100, 50));
        Convert(ExecutionEvent.AgentStarted("Agent2"));
        Convert(ExecutionEvent.AgentCompleted("Agent2", "done", 200, 100));

        var events = Convert(ExecutionEvent.WorkflowCompleted());
        var statsEvent = events.First(e =>
            e.Type == AgUiEventTypes.TextMessageContent && e.Delta!.Contains("tokens"));

        // 150 + 300 = 450
        Assert.Contains("450", statsEvent.Delta!);
    }

    // ─── 10. Per-Agent Cost via ModelPricing ───

    [Fact]
    public void AgentCompleted_WithModel_CalculatesCost()
    {
        Convert(ExecutionEvent.AgentStarted("Agent1"));
        Convert(ExecutionEvent.AgentCompleted("Agent1", "done", 500, 500, "gpt-4o-mini"));

        var events = Convert(ExecutionEvent.WorkflowCompleted());
        var statsEvent = events.First(e =>
            e.Type == AgUiEventTypes.TextMessageContent && e.Delta!.Contains("$"));

        Assert.NotNull(statsEvent);
    }

    // ─── 11. Stats Message Format ───

    [Fact]
    public void StatsMessage_ContainsStepsToolsTokensDuration()
    {
        Convert(ExecutionEvent.AgentStarted("Agent1"));
        Convert(ExecutionEvent.ToolCall("Agent1", "tool1", "a"));
        Convert(ExecutionEvent.ToolResult("Agent1", "tool1", "r"));
        Convert(ExecutionEvent.AgentCompleted("Agent1", "done", 100, 50, "gpt-4o"));

        var events = Convert(ExecutionEvent.WorkflowCompleted());
        var statsEvent = events.First(e =>
            e.Type == AgUiEventTypes.TextMessageContent && e.Delta!.Contains("Steps"));

        Assert.Contains("Steps 1", statsEvent.Delta!);
        Assert.Contains("Tools 1", statsEvent.Delta!);
        Assert.Contains("150", statsEvent.Delta!);
        Assert.Contains("s", statsEvent.Delta!);
    }

    // ─── 12. Message Lifecycle ───

    [Fact]
    public void MessageLifecycle_EnsureStartEnd_Pairs()
    {
        // AgentStarted creates a message, AgentCompleted ends it
        var startEvents = Convert(ExecutionEvent.AgentStarted("Agent1"));
        var endEvents = Convert(ExecutionEvent.AgentCompleted("Agent1", "done"));

        Assert.Contains(startEvents, e => e.Type == AgUiEventTypes.TextMessageStart);
        Assert.Contains(endEvents, e => e.Type == AgUiEventTypes.TextMessageEnd);
    }

    [Fact]
    public void SecondAgentStarted_EndsFirstMessage_StartsNew()
    {
        Convert(ExecutionEvent.AgentStarted("Agent1"));
        var events = Convert(ExecutionEvent.AgentStarted("Agent2"));

        // Should end previous message then start new one
        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageEnd);
        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageStart);
        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageContent && e.Delta!.Contains("[Agent2]"));
    }

    [Fact]
    public void TextChunk_WithoutActiveMessage_StartsNewMessage()
    {
        // No AgentStarted first — TextChunk should auto-start a message
        var events = Convert(ExecutionEvent.TextChunk("Agent1", "hello"));

        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageStart);
        Assert.Contains(events, e => e.Type == AgUiEventTypes.TextMessageContent && e.Delta == "hello");
    }

    // ─── 13. NodeCompleted → nodeOutputs 同步 ───

    [Fact]
    public void NodeCompleted_RecordsNodeOutput_InStateSnapshot()
    {
        var events = Convert(ExecutionEvent.NodeCompleted("agent", "Summarizer", "This is the summary output"));

        var snapshot = events.FirstOrDefault(e => e.Type == AgUiEventTypes.StateSnapshot);
        Assert.NotNull(snapshot);

        // Snapshot 包含 nodeOutputs
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot.Snapshot);
        Assert.Contains("nodeOutputs", json);
        Assert.Contains("Summarizer", json);
    }

    [Fact]
    public void NodeCompleted_SetsNodeStateToCompleted()
    {
        Convert(ExecutionEvent.NodeCompleted("agent", "Agent1", "done"));

        var events = Convert(ExecutionEvent.NodeCompleted("agent", "Agent2", "also done"));
        var snapshot = events.First(e => e.Type == AgUiEventTypes.StateSnapshot);

        var json = System.Text.Json.JsonSerializer.Serialize(snapshot.Snapshot);
        // 兩個節點都應該是 completed
        Assert.Contains("Agent1", json);
        Assert.Contains("Agent2", json);
        Assert.Contains("completed", json);
    }

    [Fact]
    public void NodeCompleted_TruncatesLongOutput()
    {
        var longOutput = new string('x', 600);
        var events = Convert(ExecutionEvent.NodeCompleted("agent", "LongAgent", longOutput));

        var snapshot = events.First(e => e.Type == AgUiEventTypes.StateSnapshot);
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot.Snapshot);

        // Output 應被截斷（不超過 500 + "..."）
        Assert.DoesNotContain(longOutput, json);
        Assert.Contains("...", json);
    }

    [Fact]
    public void WorkflowCompleted_PreservesNodeOutputs()
    {
        // 先完成一個節點
        Convert(ExecutionEvent.NodeCompleted("agent", "Agent1", "some output"));

        // 完成 workflow — nodeOutputs 保留（供 Rerun/Debug 使用）
        var events = Convert(ExecutionEvent.WorkflowCompleted());
        var snapshot = events.First(e => e.Type == AgUiEventTypes.StateSnapshot);

        var json = System.Text.Json.JsonSerializer.Serialize(snapshot.Snapshot);
        Assert.Contains("some output", json);
    }

    // ─── 14. DebugPaused / DebugResumed ───

    [Fact]
    public void DebugPaused_SetsNodeStateAndPendingAction()
    {
        var events = Convert(ExecutionEvent.DebugPaused("agent", "Summarizer", "output text"));

        var snapshot = events.First(e => e.Type == AgUiEventTypes.StateSnapshot);
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot.Snapshot);

        Assert.Contains("debug-paused", json);
        Assert.Contains("Summarizer", json);
        Assert.Contains("pendingDebugAction", json);
    }

    [Fact]
    public void DebugResumed_ClearsPendingAction()
    {
        // First pause
        Convert(ExecutionEvent.DebugPaused("agent", "Agent1", "output"));
        // Then resume
        var events = Convert(ExecutionEvent.DebugResumed("Agent1", "Continue"));

        var snapshot = events.First(e => e.Type == AgUiEventTypes.StateSnapshot);
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot.Snapshot);

        // pendingDebugAction should be null after resume
        Assert.Contains("pendingDebugAction", json);
        Assert.Contains("null", json);
    }

    [Fact]
    public void WorkflowCompleted_ClearsPendingDebugAction()
    {
        Convert(ExecutionEvent.DebugPaused("agent", "Agent1", "output"));
        Convert(ExecutionEvent.DebugResumed("Agent1", "Continue"));

        var events = Convert(ExecutionEvent.WorkflowCompleted());
        var snapshot = events.First(e => e.Type == AgUiEventTypes.StateSnapshot);
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot.Snapshot);

        Assert.Contains("null", json);
    }
}
