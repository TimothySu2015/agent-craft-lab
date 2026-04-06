using System.Net;
using System.Runtime.CompilerServices;
using AgentCraftLab.Engine.Middleware;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class RecoveryChatClientTests
{
    /// <summary>可設定回應行為的 stub ChatClient。</summary>
    private sealed class ConfigurableStubChatClient : IChatClient
    {
        public ChatFinishReason? FinishReason { get; set; }
        public int? MaxOutputTokensSeen { get; private set; }
        public int CallCount { get; private set; }
        public Exception? ExceptionToThrow { get; set; }
        public string ResponseText { get; set; } = "OK";

        /// <summary>第 N 次呼叫後改為正常回應（用於測試重試成功情境）。</summary>
        public int? SucceedAfterCall { get; set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            MaxOutputTokensSeen = options?.MaxOutputTokens;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            var finishReason = (SucceedAfterCall.HasValue && CallCount > SucceedAfterCall.Value)
                ? ChatFinishReason.Stop
                : FinishReason;

            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, ResponseText))
            {
                FinishReason = finishReason
            };
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, ResponseText)
            {
                FinishReason = FinishReason
            };
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task NonTruncated_PassesThrough()
    {
        var inner = new ConfigurableStubChatClient { FinishReason = ChatFinishReason.Stop };
        var client = new RecoveryChatClient(inner);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("OK", response.Text);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Truncated_DoublesMaxOutputTokens()
    {
        var inner = new ConfigurableStubChatClient
        {
            FinishReason = ChatFinishReason.Length,
            SucceedAfterCall = 1 // 第二次呼叫回傳 Stop
        };
        var client = new RecoveryChatClient(inner);
        var options = new ChatOptions { MaxOutputTokens = 4096 };

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        Assert.Equal(2, inner.CallCount);
        Assert.Equal(8192, inner.MaxOutputTokensSeen); // 4096 * 2
    }

    [Fact]
    public async Task Truncated_RespectsMaxRetries()
    {
        var inner = new ConfigurableStubChatClient { FinishReason = ChatFinishReason.Length };
        var recoveryOptions = new RecoveryOptions { MaxTruncationRetries = 2 };
        var client = new RecoveryChatClient(inner, recoveryOptions);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        // 初始呼叫 1 + 重試 2 = 總共 3
        Assert.Equal(3, inner.CallCount);
        Assert.Equal(ChatFinishReason.Length, response.FinishReason);
    }

    [Fact]
    public async Task Truncated_RespectsTokensCeiling()
    {
        var inner = new ConfigurableStubChatClient { FinishReason = ChatFinishReason.Length };
        var recoveryOptions = new RecoveryOptions
        {
            MaxTruncationRetries = 5,
            MaxOutputTokensCeiling = 8000
        };
        var client = new RecoveryChatClient(inner, recoveryOptions);
        var options = new ChatOptions { MaxOutputTokens = 4096 };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        // 4096 * 2 = 8192 > 8000 → 只用 8000，且之後不再加倍
        Assert.Equal(8000, inner.MaxOutputTokensSeen);
        // 初始呼叫 + 1 次重試（第二次加倍後 == ceiling，不再繼續）
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task Truncated_DisabledByOption()
    {
        var inner = new ConfigurableStubChatClient { FinishReason = ChatFinishReason.Length };
        var recoveryOptions = new RecoveryOptions { EnableTruncationRecovery = false };
        var client = new RecoveryChatClient(inner, recoveryOptions);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(1, inner.CallCount); // 不重試
        Assert.Equal(ChatFinishReason.Length, response.FinishReason);
    }

    [Fact]
    public async Task ContextOverflow_LogsAndRethrows()
    {
        var inner = new ConfigurableStubChatClient
        {
            ExceptionToThrow = new HttpRequestException("context_length_exceeded", null, HttpStatusCode.BadRequest)
        };
        var client = new RecoveryChatClient(inner);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));
    }

    [Fact]
    public async Task ContextOverflow_InvokesCallback()
    {
        var callbackInvoked = false;
        var inner = new ConfigurableStubChatClient
        {
            ExceptionToThrow = new HttpRequestException("maximum context length", null, HttpStatusCode.BadRequest)
        };
        var recoveryOptions = new RecoveryOptions
        {
            OnContextOverflow = (_, _) => { callbackInvoked = true; return Task.CompletedTask; }
        };
        var client = new RecoveryChatClient(inner, recoveryOptions);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.True(callbackInvoked);
    }

    [Fact]
    public async Task ModelUnavailable_LogsAndRethrows()
    {
        var inner = new ConfigurableStubChatClient
        {
            ExceptionToThrow = new HttpRequestException("model_not_found", null, HttpStatusCode.NotFound)
        };
        var client = new RecoveryChatClient(inner);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));
    }

    [Fact]
    public async Task ModelUnavailable_InvokesCallback()
    {
        string? receivedModel = null;
        var inner = new ConfigurableStubChatClient
        {
            ExceptionToThrow = new HttpRequestException("model does not exist", null, HttpStatusCode.NotFound)
        };
        var recoveryOptions = new RecoveryOptions
        {
            OnModelUnavailable = (_, model, _) => { receivedModel = model; return Task.CompletedTask; }
        };
        var client = new RecoveryChatClient(inner, recoveryOptions);
        var options = new ChatOptions { ModelId = "gpt-5-turbo" };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options));

        Assert.Equal("gpt-5-turbo", receivedModel);
    }

    [Fact]
    public async Task Streaming_TruncatedLogsOnly()
    {
        var inner = new ConfigurableStubChatClient
        {
            FinishReason = ChatFinishReason.Length,
            ResponseText = "partial"
        };
        var client = new RecoveryChatClient(inner);

        var chunks = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            if (update.Text is not null)
            {
                chunks.Add(update.Text);
            }
        }

        // 串流模式不重試，但 chunks 正常 yield
        Assert.Single(chunks);
        Assert.Equal("partial", chunks[0]);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Streaming_ContextOverflow_Rethrows()
    {
        var inner = new ConfigurableStubChatClient
        {
            ExceptionToThrow = new HttpRequestException("context_length_exceeded", null, HttpStatusCode.BadRequest)
        };
        var client = new RecoveryChatClient(inner);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
            {
                // should throw before yielding
            }
        });
    }

    [Fact]
    public async Task NullOptions_CreatesDefault()
    {
        var inner = new ConfigurableStubChatClient
        {
            FinishReason = ChatFinishReason.Length,
            SucceedAfterCall = 1
        };
        var client = new RecoveryChatClient(inner);

        // null options → 預設 MaxOutputTokens 4096，加倍到 8192
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options: null);

        Assert.Equal(2, inner.CallCount);
        Assert.Equal(8192, inner.MaxOutputTokensSeen);
    }

    [Fact]
    public void FromConfig_ParsesSettings()
    {
        var config = new Dictionary<string, string>
        {
            ["maxTruncationRetries"] = "5",
            ["maxOutputTokensCeiling"] = "65536",
            ["enableTruncation"] = "false",
            ["enableContextOverflow"] = "false",
            ["enableModelUnavailable"] = "false"
        };

        var options = RecoveryOptions.FromConfig(config);

        Assert.Equal(5, options.MaxTruncationRetries);
        Assert.Equal(65536, options.MaxOutputTokensCeiling);
        Assert.False(options.EnableTruncationRecovery);
        Assert.False(options.EnableContextOverflowDetection);
        Assert.False(options.EnableModelUnavailableDetection);
    }

    [Fact]
    public void FromConfig_NullReturnsDefaults()
    {
        var options = RecoveryOptions.FromConfig(null);

        Assert.True(options.EnableTruncationRecovery);
        Assert.Equal(2, options.MaxTruncationRetries);
        Assert.Equal(32_768, options.MaxOutputTokensCeiling);
    }

    [Fact]
    public void CloneWithMaxTokens_PreservesProperties()
    {
        var source = new ChatOptions
        {
            Temperature = 0.7f,
            TopP = 0.9f,
            MaxOutputTokens = 1000,
            ModelId = "gpt-4o",
            FrequencyPenalty = 0.5f,
            PresencePenalty = 0.3f,
            Seed = 42,
        };

        var clone = RecoveryChatClient.CloneWithMaxTokens(source, 2000);

        Assert.Equal(2000, clone.MaxOutputTokens);
        Assert.Equal(0.7f, clone.Temperature);
        Assert.Equal(0.9f, clone.TopP);
        Assert.Equal("gpt-4o", clone.ModelId);
        Assert.Equal(0.5f, clone.FrequencyPenalty);
        Assert.Equal(0.3f, clone.PresencePenalty);
        Assert.Equal(42, clone.Seed);
    }

    // ─── L4 Context Overflow + Compactor 測試 ───

    /// <summary>簡單的 stub IContextCompactor。</summary>
    private sealed class StubCompactor(string? result) : IContextCompactor
    {
        public int CallCount { get; private set; }

        public Task<string?> CompressAsync(string content, string context, int tokenBudget, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    /// <summary>第一次 throw context overflow，第二次正常回應。</summary>
    private sealed class OverflowThenSuccessStubClient : IChatClient
    {
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1)
            {
                throw new HttpRequestException("context_length_exceeded", null, HttpStatusCode.BadRequest);
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "OK after compression"))
            {
                FinishReason = ChatFinishReason.Stop
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "OK");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task ContextOverflow_WithCompactor_CompressesAndRetries()
    {
        var inner = new OverflowThenSuccessStubClient();
        var compactor = new StubCompactor("compressed history");
        var recoveryOptions = new RecoveryOptions { ContextCompactor = compactor };
        var client = new RecoveryChatClient(inner, recoveryOptions);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system prompt"),
            new(ChatRole.User, "old message"),
            new(ChatRole.Assistant, "old response"),
            new(ChatRole.User, "latest question")
        };

        var response = await client.GetResponseAsync(messages);

        Assert.Equal("OK after compression", response.Text);
        Assert.Equal(1, compactor.CallCount); // 壓縮被呼叫
        Assert.Equal(2, inner.CallCount); // 第一次 overflow + 第二次重試成功
    }

    [Fact]
    public async Task ContextOverflow_CompactorReturnsNull_FallsBackToRethrow()
    {
        var inner = new ConfigurableStubChatClient
        {
            ExceptionToThrow = new HttpRequestException("context_length_exceeded", null, HttpStatusCode.BadRequest)
        };
        var compactor = new StubCompactor(null); // 壓縮失敗
        var recoveryOptions = new RecoveryOptions { ContextCompactor = compactor };
        var client = new RecoveryChatClient(inner, recoveryOptions);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.User, "old"),
            new(ChatRole.User, "latest")
        };

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync(messages));
        Assert.Equal(1, compactor.CallCount); // 嘗試壓縮
    }

    [Fact]
    public async Task ContextOverflow_NoCompactor_ExistingBehavior()
    {
        var inner = new ConfigurableStubChatClient
        {
            ExceptionToThrow = new HttpRequestException("context_length_exceeded", null, HttpStatusCode.BadRequest)
        };
        // 不設定 ContextCompactor → 走原有 callback 路徑
        var client = new RecoveryChatClient(inner);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));
    }

    // ─── 全鏈路測試：RecoveryChatClient → MessageSerializer → LlmContextCompactor → retry ───

    /// <summary>
    /// LLM stub：
    ///   - 當被 RecoveryChatClient 呼叫時（第 1 次）→ throw context overflow
    ///   - 當被 RecoveryChatClient 重試時（第 2 次）→ 回傳成功
    ///   - 當被 LlmContextCompactor 呼叫壓縮時 → 回傳壓縮摘要
    /// 透過訊息內容區分呼叫者。
    /// </summary>
    private sealed class FullChainStubClient : IChatClient
    {
        public int MainCallCount { get; private set; }
        public int CompressorCallCount { get; private set; }
        public List<ChatMessage>? RetryMessages { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var msgList = messages.ToList();

            // LlmContextCompactor 的壓縮呼叫：system prompt 含 "compression assistant"
            if (msgList.Any(m => m.Text?.Contains("compression assistant", StringComparison.OrdinalIgnoreCase) == true))
            {
                CompressorCallCount++;
                return Task.FromResult(new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, "- Key finding: TCP vs UDP differences\n- Tool called: web_search\n- Result: comparison data"))
                {
                    FinishReason = ChatFinishReason.Stop
                });
            }

            // 主要 LLM 呼叫
            MainCallCount++;
            if (MainCallCount == 1)
            {
                // 第一次：模擬 context overflow
                throw new HttpRequestException("context_length_exceeded: maximum context length is 128000 tokens", null, HttpStatusCode.BadRequest);
            }

            // 第二次（重試）：記錄收到的壓縮後 messages 並回傳成功
            RetryMessages = msgList;
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "Final answer based on compressed history"))
            {
                FinishReason = ChatFinishReason.Stop
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "OK");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task FullChain_ContextOverflow_SerializeCompressRebuildRetry()
    {
        // 全鏈路：RecoveryChatClient → MessageSerializer → LlmContextCompactor → retry
        var stubClient = new FullChainStubClient();

        // 用真正的 LlmContextCompactor（但背後是 stub LLM）
        var realCompactor = new LlmContextCompactor(stubClient);
        var recoveryOptions = new RecoveryOptions { ContextCompactor = realCompactor };
        var client = new RecoveryChatClient(stubClient, recoveryOptions);

        // 建構一個有足夠歷史的對話（中間歷史需 > 200 tokens 才能觸發壓縮）
        var longToolResult = string.Join("\n", Enumerable.Range(1, 50).Select(i =>
            $"Finding {i}: TCP uses three-way handshake for connection establishment, while UDP sends datagrams without prior setup."));
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful research assistant."),
            new(ChatRole.User, "Search for TCP vs UDP"),
            new(ChatRole.Assistant, "I'll search for TCP and UDP differences using web search tool."),
            new(ChatRole.Tool, [new FunctionCallContent("c1", "web_search", new Dictionary<string, object?> { ["q"] = "TCP vs UDP differences" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", longToolResult)]),
            new(ChatRole.Assistant, "Based on my extensive research, here are the comprehensive key differences between TCP and UDP protocols covering reliability, speed, and connection handling."),
            new(ChatRole.User, "Now compare their performance in gaming scenarios"),
        };

        // 執行：第一次 overflow → 壓縮中間歷史 → 重試成功
        var response = await client.GetResponseAsync(messages);

        // 驗證 1：最終回應成功
        Assert.Equal("Final answer based on compressed history", response.Text);

        // 驗證 2：主 LLM 被呼叫 2 次（第一次 overflow + 第二次重試）
        Assert.Equal(2, stubClient.MainCallCount);

        // 驗證 3：壓縮 LLM 被呼叫 1 次（LlmContextCompactor 壓縮中間歷史）
        Assert.Equal(1, stubClient.CompressorCallCount);

        // 驗證 4：重試時的 messages 結構正確（3 條：system + compressed history + last user）
        Assert.NotNull(stubClient.RetryMessages);
        Assert.Equal(3, stubClient.RetryMessages!.Count);

        // 第一條：原始 system prompt 保留
        Assert.Equal(ChatRole.System, stubClient.RetryMessages[0].Role);
        Assert.Contains("helpful research assistant", stubClient.RetryMessages[0].Text!);

        // 第二條：壓縮後的歷史摘要（System role，含 "[Compressed history" 標記）
        Assert.Equal(ChatRole.System, stubClient.RetryMessages[1].Role);
        Assert.Contains("[Compressed history of previous", stubClient.RetryMessages[1].Text!);
        Assert.Contains("Key finding", stubClient.RetryMessages[1].Text!);

        // 第三條：最後的 user message 保留
        Assert.Equal(ChatRole.User, stubClient.RetryMessages[2].Role);
        Assert.Contains("gaming scenarios", stubClient.RetryMessages[2].Text!);
    }

    [Fact]
    public async Task FullChain_MiddleHistoryIncludesToolCalls()
    {
        // 驗證 MessageSerializer 正確序列化了 FunctionCallContent / FunctionResultContent
        // 中間歷史需夠長才能觸發壓縮（> 200 tokens）
        var stubClient = new FullChainStubClient();
        var realCompactor = new LlmContextCompactor(stubClient);
        var recoveryOptions = new RecoveryOptions { ContextCompactor = realCompactor };
        var client = new RecoveryChatClient(stubClient, recoveryOptions);

        // 需要足夠多的中間 messages，序列化後 > 200 tokens（每條截斷 200 chars，需 ~5+ 條）
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.User, "Calculate step by step: (1+2)*(3+4)*(5+6)"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "calculator", new Dictionary<string, object?> { ["expr"] = "1+2" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "Result of 1+2 = 3. This is the first intermediate calculation in the series.")]),
            new(ChatRole.Assistant, [new FunctionCallContent("c2", "calculator", new Dictionary<string, object?> { ["expr"] = "3+4" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c2", "Result of 3+4 = 7. This is the second intermediate calculation in the series.")]),
            new(ChatRole.Assistant, [new FunctionCallContent("c3", "calculator", new Dictionary<string, object?> { ["expr"] = "5+6" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c3", "Result of 5+6 = 11. This is the third intermediate calculation in the series.")]),
            new(ChatRole.Assistant, [new FunctionCallContent("c4", "calculator", new Dictionary<string, object?> { ["expr"] = "3*7*11" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c4", "Result of 3*7*11 = 231. This is the final result of the compound expression.")]),
            new(ChatRole.Assistant, "The step-by-step calculation shows: (1+2)=3, (3+4)=7, (5+6)=11, and 3*7*11=231. The final answer is 231."),
            new(ChatRole.User, "What is 3+3?"),
        };

        var response = await client.GetResponseAsync(messages);

        Assert.Equal("Final answer based on compressed history", response.Text);
        Assert.Equal(1, stubClient.CompressorCallCount);
    }

    [Fact]
    public async Task FullChain_TooFewMessages_SkipsCompression()
    {
        // 只有 2 條 messages（system + user）→ 沒有中間歷史可壓縮 → 直接 rethrow
        var inner = new ConfigurableStubChatClient
        {
            ExceptionToThrow = new HttpRequestException("context_length_exceeded", null, HttpStatusCode.BadRequest)
        };
        var realCompactor = new LlmContextCompactor(inner);
        var recoveryOptions = new RecoveryOptions { ContextCompactor = realCompactor };
        var client = new RecoveryChatClient(inner, recoveryOptions);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.User, "question"),
        };

        // 只有 2 條 → TryCompressAndRetryAsync 回傳 null → fallback rethrow
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync(messages));
    }

    // ─── 主動壓縮（Proactive Compaction）測試 ───

    [Fact]
    public async Task ProactiveCompaction_UnderThreshold_NoCompression()
    {
        var inner = new ConfigurableStubChatClient { FinishReason = ChatFinishReason.Stop };
        var recoveryOptions = new RecoveryOptions { ProactiveCompressionThreshold = 100_000 }; // 很高的門檻
        var client = new RecoveryChatClient(inner, recoveryOptions);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "short message")]);

        Assert.Equal("OK", response.Text);
        Assert.Equal(1, inner.CallCount); // 直接通過，無壓縮
    }

    [Fact]
    public async Task ProactiveCompaction_OverThreshold_RunsTruncation()
    {
        var inner = new ConfigurableStubChatClient { FinishReason = ChatFinishReason.Stop };
        // 極低門檻 → 觸發壓縮
        var recoveryOptions = new RecoveryOptions { ProactiveCompressionThreshold = 10 };
        var client = new RecoveryChatClient(inner, recoveryOptions);

        // 建構超長 tool result
        var longResult = string.Join(" ", Enumerable.Range(1, 500).Select(i => $"result_{i}"));
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.Tool, [new FunctionResultContent("c1", longResult)]),
            new(ChatRole.User, "question")
        };

        var response = await client.GetResponseAsync(messages);

        Assert.Equal("OK", response.Text);
        Assert.Equal(1, inner.CallCount); // 壓縮後正常呼叫
    }

    [Fact]
    public async Task ProactiveCompaction_Disabled_WhenThresholdNull()
    {
        var inner = new ConfigurableStubChatClient { FinishReason = ChatFinishReason.Stop };
        // ProactiveCompressionThreshold 不設定（null）
        var recoveryOptions = new RecoveryOptions();
        var client = new RecoveryChatClient(inner, recoveryOptions);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("OK", response.Text);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task ProactiveCompaction_WithCompactor_RunsLlmCompressionWhenNeeded()
    {
        var inner = new ConfigurableStubChatClient { FinishReason = ChatFinishReason.Stop };
        var compactor = new StubCompactor("compressed summary");
        var recoveryOptions = new RecoveryOptions
        {
            ProactiveCompressionThreshold = 10, // 極低門檻
            ContextCompactor = compactor
        };
        var client = new RecoveryChatClient(inner, recoveryOptions);

        // 建構超長歷史（3+ messages 才會跑 LLM 壓縮）
        var longHistory = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"detailed_analysis_{i}"));
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.Assistant, longHistory),
            new(ChatRole.User, "question")
        };

        var response = await client.GetResponseAsync(messages);

        Assert.Equal("OK", response.Text);
        Assert.Equal(1, compactor.CallCount); // LLM 壓縮被呼叫
    }
}
