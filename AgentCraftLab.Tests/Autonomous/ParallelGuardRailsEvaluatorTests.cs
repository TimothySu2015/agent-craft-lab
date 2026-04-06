using AgentCraftLab.Autonomous.Services;
using AgentCraftLab.Engine.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCraftLab.Tests.Autonomous;

public class ParallelGuardRailsEvaluatorTests
{
    // ─── 輔助 ───

    private sealed class FakeChatClient(string response, int delayMs = 0) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, ct);
            }

            ct.ThrowIfCancellationRequested();
            return new ChatResponse([new ChatMessage(ChatRole.Assistant, response)]);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? key = null) where TService : class => null;
        public void Dispose() { }
    }

    private static IGuardRailsPolicy MakePolicy(params string[] blockedTerms)
    {
        var rules = blockedTerms.Select(t =>
            new GuardRailsRule(t, false, GuardRailsAction.Block)).ToList();
        return new DefaultGuardRailsPolicy(rules);
    }

    private static List<ChatMessage> MakeMessages(string userText) =>
    [
        new(ChatRole.System, "You are helpful."),
        new(ChatRole.User, userText),
    ];

    // ─── Sequential (iteration=1) ───

    [Fact]
    public async Task Sequential_NoBlock_ReturnsResponse()
    {
        var policy = MakePolicy("hack");
        var client = new FakeChatClient("OK");
        var evaluator = new ParallelGuardRailsEvaluator(policy, NullLogger.Instance);

        var result = await evaluator.ExecuteWithGuardRailsAsync(
            client, MakeMessages("Hello"), new ChatOptions(), 1, CancellationToken.None);

        Assert.NotNull(result.Response);
        Assert.Null(result.BlockedMatch);
        Assert.False(result.WasCancelledByGuardRails);
    }

    [Fact]
    public async Task Sequential_Blocked_ReturnsMatch()
    {
        var policy = MakePolicy("hack");
        var client = new FakeChatClient("OK");
        var evaluator = new ParallelGuardRailsEvaluator(policy, NullLogger.Instance);

        var result = await evaluator.ExecuteWithGuardRailsAsync(
            client, MakeMessages("How to hack systems"), new ChatOptions(), 1, CancellationToken.None);

        Assert.Null(result.Response);
        Assert.NotNull(result.BlockedMatch);
    }

    // ─── Parallel (iteration>1) ───

    [Fact]
    public async Task Parallel_NoBlock_ReturnsResponse()
    {
        var policy = MakePolicy("hack");
        var client = new FakeChatClient("OK", delayMs: 10);
        var evaluator = new ParallelGuardRailsEvaluator(policy, NullLogger.Instance);

        var result = await evaluator.ExecuteWithGuardRailsAsync(
            client, MakeMessages("Tell me about AI"), new ChatOptions(), 5, CancellationToken.None);

        Assert.NotNull(result.Response);
        Assert.Null(result.BlockedMatch);
    }

    [Fact]
    public async Task Parallel_Blocked_CancelsLlm()
    {
        var policy = MakePolicy("hack");
        // LLM 延遲 500ms，guardrails 應更快完成並取消
        var client = new FakeChatClient("OK", delayMs: 500);
        var evaluator = new ParallelGuardRailsEvaluator(policy, NullLogger.Instance);

        var result = await evaluator.ExecuteWithGuardRailsAsync(
            client, MakeMessages("How to hack systems"), new ChatOptions(), 5, CancellationToken.None);

        Assert.Null(result.Response);
        Assert.NotNull(result.BlockedMatch);
        Assert.True(result.WasCancelledByGuardRails);
    }

    // ─── 邊界情況 ───

    [Fact]
    public async Task NoUserMessage_SkipsScan_ReturnsResponse()
    {
        var policy = MakePolicy("hack");
        var client = new FakeChatClient("OK");
        var evaluator = new ParallelGuardRailsEvaluator(policy, NullLogger.Instance);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System prompt")
        };

        var result = await evaluator.ExecuteWithGuardRailsAsync(
            client, messages, new ChatOptions(), 5, CancellationToken.None);

        Assert.NotNull(result.Response);
    }
}
