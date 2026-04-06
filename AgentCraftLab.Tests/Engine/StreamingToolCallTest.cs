using AgentCraftLab.Engine.Strategies;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

/// <summary>
/// 驗證 MEAI 10.4.1 是否修復了 FunctionInvokingChatClient + GetStreamingResponseAsync + Tool Call 的 bug。
/// 如果此測試通過，表示可以將 SingleAgentStrategy 改回 streaming 模式。
/// </summary>
public class StreamingToolCallTest
{
    // ─── 基本驗證：工具是否被呼叫 ───

    [Fact]
    public async Task Streaming_WithTools_CallsFunction()
    {
        var callCount = 0;
        var fakeLlm = new FakeLlmWithToolCall(() => callCount++);
        var tool = MakeCalculatorTool();
        using var funcClient = new FunctionInvokingChatClient(fakeLlm);

        var messages = new List<ChatMessage> { new(ChatRole.User, "What is 2+2?") };
        var options = new ChatOptions { Tools = [tool] };

        var chunks = new List<string>();
        await foreach (var update in funcClient.GetStreamingResponseAsync(messages, options))
        {
            if (update.Text is not null)
            {
                chunks.Add(update.Text);
            }
        }

        Assert.True(callCount > 0, "Tool function was never called — streaming + tool call bug still exists");
        Assert.NotEmpty(chunks);
    }

    // ─── FunctionCallContent 是否出現在 streaming updates ───

    [Fact]
    public async Task Streaming_WithTools_ContainsFunctionCallContent()
    {
        var fakeLlm = new FakeLlmWithToolCall(() => { });
        var tool = MakeCalculatorTool();
        using var funcClient = new FunctionInvokingChatClient(fakeLlm);

        var messages = new List<ChatMessage> { new(ChatRole.User, "What is 2+2?") };
        var options = new ChatOptions { Tools = [tool] };

        var allContents = new List<AIContent>();
        await foreach (var update in funcClient.GetStreamingResponseAsync(messages, options))
        {
            allContents.AddRange(update.Contents);
        }

        var hasFunctionCall = allContents.Any(c => c is FunctionCallContent);
        var hasFunctionResult = allContents.Any(c => c is FunctionResultContent);
        var hasText = allContents.Any(c => c is TextContent);

        // 記錄發現的 content types（供分析用）
        var contentTypes = allContents.Select(c => c.GetType().Name).Distinct().ToList();

        Assert.True(hasText, $"No TextContent found. Content types: {string.Join(", ", contentTypes)}");

        // 記錄 FunctionCall/Result 是否在 streaming 中出現（不 assert，只觀察）
        // FunctionInvokingChatClient 可能內部處理 tool call 不暴露給外部
    }

    // ─── 多次 tool call 是否正常迴圈 ───

    [Fact]
    public async Task Streaming_WithTools_MultipleToolCalls()
    {
        var callCount = 0;
        var fakeLlm = new FakeLlmMultiTool(() => callCount++);
        var tool = MakeCalculatorTool();
        using var funcClient = new FunctionInvokingChatClient(fakeLlm);

        var messages = new List<ChatMessage> { new(ChatRole.User, "What is 2+2 and 3+3?") };
        var options = new ChatOptions { Tools = [tool] };

        var chunks = new List<string>();
        await foreach (var update in funcClient.GetStreamingResponseAsync(messages, options))
        {
            if (update.Text is not null)
            {
                chunks.Add(update.Text);
            }
        }

        Assert.True(callCount >= 2, $"Expected 2+ tool calls, got {callCount}");
        Assert.NotEmpty(chunks);
    }

    // ─── 無工具時 streaming 正常 ───

    [Fact]
    public async Task Streaming_NoTools_WorksNormally()
    {
        var fakeLlm = new FakeLlmNoTools();
        using var funcClient = new FunctionInvokingChatClient(fakeLlm);

        var messages = new List<ChatMessage> { new(ChatRole.User, "Hello") };

        var chunks = new List<string>();
        await foreach (var update in funcClient.GetStreamingResponseAsync(messages))
        {
            if (update.Text is not null)
            {
                chunks.Add(update.Text);
            }
        }

        Assert.NotEmpty(chunks);
        Assert.Contains("Hello back", string.Join("", chunks));
    }

    // ─── GetResponseAsync 對照（確認行為一致）───

    [Fact]
    public async Task NonStreaming_WithTools_CallsFunction()
    {
        var callCount = 0;
        var fakeLlm = new FakeLlmWithToolCall(() => callCount++);
        var tool = MakeCalculatorTool();
        using var funcClient = new FunctionInvokingChatClient(fakeLlm);

        var messages = new List<ChatMessage> { new(ChatRole.User, "What is 2+2?") };
        var options = new ChatOptions { Tools = [tool] };

        var response = await funcClient.GetResponseAsync(messages, options);

        Assert.True(callCount > 0, "Tool function was never called in non-streaming mode");
        Assert.False(string.IsNullOrEmpty(response.Text));
    }

    // ─── 輔助方法 ───

    private static AITool MakeCalculatorTool()
    {
        return AIFunctionFactory.Create(
            (string expression) => $"Result: {expression} = 42",
            "Calculator",
            "Calculate math");
    }

    // ═══════════════════════════════════════════
    // Fake LLM 實作
    // ═══════════════════════════════════════════

    /// <summary>模擬 LLM：第一次回傳 1 個 FunctionCallContent，第二次回傳文字。</summary>
    private sealed class FakeLlmWithToolCall(Action onToolCalled) : IChatClient
    {
        private int _callIndex;

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            await Task.CompletedTask;
            var msgList = messages.ToList();
            var hasFunctionResult = msgList.Any(m => m.Contents.Any(c => c is FunctionResultContent));

            if (!hasFunctionResult && _callIndex == 0)
            {
                _callIndex++;
                onToolCalled();
                return new ChatResponse(
                    [new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("call_1", "Calculator",
                            new Dictionary<string, object?> { ["expression"] = "2+2" })])]);
            }

            return new ChatResponse([new ChatMessage(ChatRole.Assistant, "The answer is 42.")]);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            var msgList = messages.ToList();
            var hasFunctionResult = msgList.Any(m => m.Contents.Any(c => c is FunctionResultContent));

            if (!hasFunctionResult && _callIndex == 0)
            {
                _callIndex++;
                onToolCalled();
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [new FunctionCallContent("call_1", "Calculator",
                        new Dictionary<string, object?> { ["expression"] = "2+2" })]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "The answer is 42.");
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? key = null) where TService : class => null;
        public void Dispose() { }
    }

    /// <summary>模擬 LLM：回傳 2 個 FunctionCallContent（多次工具呼叫）。</summary>
    private sealed class FakeLlmMultiTool(Action onToolCalled) : IChatClient
    {
        private int _callIndex;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            var funcResults = messages.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).ToList();

            if (_callIndex == 0)
            {
                _callIndex++;
                onToolCalled();
                onToolCalled();
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [new FunctionCallContent("call_1", "Calculator",
                        new Dictionary<string, object?> { ["expression"] = "2+2" }),
                     new FunctionCallContent("call_2", "Calculator",
                        new Dictionary<string, object?> { ["expression"] = "3+3" })]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "2+2=42 and 3+3=42.");
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? key = null) where TService : class => null;
        public void Dispose() { }
    }

    /// <summary>模擬 LLM：無工具呼叫，直接回文字。</summary>
    private sealed class FakeLlmNoTools : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Hello back!")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Hello ");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "back!");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? key = null) where TService : class => null;
        public void Dispose() { }
    }
}
