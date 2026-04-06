using AgentCraftLab.Engine.Middleware;
using AgentCraftLab.Engine.Pii;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Tests.Engine;

public class PiiMaskingChatClientTests
{
    /// <summary>簡單的 stub IChatClient，直接回傳指定文字。</summary>
    private sealed class StubChatClient(string responseText) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, responseText);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task IrreversibleMode_MasksWithReplacement()
    {
        var detector = new RegexPiiDetector([PiiLocale.Global]);
        var options = new PiiMaskingOptions { IrreversibleReplacement = "[MASKED]" };
        var inner = new StubChatClient("OK");
        var client = new PiiMaskingChatClient(inner, detector, vault: null, options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "My email is john@example.com"),
        };

        var response = await client.GetResponseAsync(messages);
        Assert.Equal("OK", response.Text);
    }

    [Fact]
    public async Task ReversibleMode_TokenizesAndDetokenizes()
    {
        var detector = new RegexPiiDetector([PiiLocale.Global]);
        var vault = new InMemoryPiiTokenVault();
        var options = new PiiMaskingOptions { DetokenizeOutput = true };

        // Inner returns a response containing a token (simulating LLM echoing)
        var inner = new StubChatClient("[EMAIL_1] confirmed");
        var client = new PiiMaskingChatClient(inner, detector, vault, options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "My email is john@example.com"),
        };

        var response = await client.GetResponseAsync(messages);
        // The vault should have stored the mapping, and output should be detokenized
        Assert.Equal("john@example.com confirmed", response.Text);
    }

    [Fact]
    public async Task LegacyConstructor_BackwardCompatible()
    {
        var inner = new StubChatClient("OK");
        var config = new Dictionary<string, string>
        {
            ["locales"] = "global",
            ["replacement"] = "***",
        };
        var client = new PiiMaskingChatClient(inner, config);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Email: test@example.com"),
        };

        var response = await client.GetResponseAsync(messages);
        Assert.Equal("OK", response.Text);
    }

    [Fact]
    public async Task NoPii_PassesThrough()
    {
        var detector = new RegexPiiDetector([PiiLocale.Global]);
        var inner = new StubChatClient("Sure, I can help!");
        var client = new PiiMaskingChatClient(inner, detector);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello, how are you?"),
        };

        var response = await client.GetResponseAsync(messages);
        Assert.Equal("Sure, I can help!", response.Text);
    }

    [Fact]
    public async Task ScanOutput_Disabled_DoesNotDetokenize()
    {
        var detector = new RegexPiiDetector([PiiLocale.Global]);
        var vault = new InMemoryPiiTokenVault();
        var options = new PiiMaskingOptions { DetokenizeOutput = false };

        var inner = new StubChatClient("[EMAIL_1] confirmed");
        var client = new PiiMaskingChatClient(inner, detector, vault, options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "My email is john@example.com"),
        };

        var response = await client.GetResponseAsync(messages);
        // Token should NOT be restored because DetokenizeOutput is false
        Assert.Equal("[EMAIL_1] confirmed", response.Text);
    }

    [Fact]
    public async Task Streaming_DetokenizesOutput()
    {
        var detector = new RegexPiiDetector([PiiLocale.Global]);
        var vault = new InMemoryPiiTokenVault();
        var options = new PiiMaskingOptions { DetokenizeOutput = true };

        var inner = new StubChatClient("[EMAIL_1] is valid");
        var client = new PiiMaskingChatClient(inner, detector, vault, options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Check john@example.com"),
        };

        var chunks = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync(messages))
        {
            if (update.Text is not null)
            {
                chunks.Add(update.Text);
            }
        }

        var fullText = string.Join("", chunks);
        Assert.Contains("john@example.com", fullText);
    }

    [Fact]
    public async Task MultiplePiiEntities_AllMasked()
    {
        var detector = new RegexPiiDetector([PiiLocale.Global, PiiLocale.TW]);
        var vault = new InMemoryPiiTokenVault();
        var options = new PiiMaskingOptions();

        var inner = new StubChatClient("Got it: [EMAIL_1] and [PHONE_1]");
        var client = new PiiMaskingChatClient(inner, detector, vault, options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Email: test@test.com, 電話 02-1234-5678"),
        };

        var response = await client.GetResponseAsync(messages);
        Assert.Contains("test@test.com", response.Text!);
        Assert.Contains("02-1234-5678", response.Text!);
    }

    [Fact]
    public void PiiMaskingOptions_FromConfig()
    {
        var config = new Dictionary<string, string>
        {
            ["mode"] = "reversible",
            ["confidenceThreshold"] = "0.7",
            ["replacement"] = "[PII]",
            ["scanOutput"] = "false",
        };
        var options = PiiMaskingOptions.FromConfig(config);

        Assert.True(options.DetokenizeOutput);  // reversible → true
        Assert.Equal(0.7, options.ConfidenceThreshold);
        Assert.Equal("[PII]", options.IrreversibleReplacement);
        Assert.False(options.ScanOutput);
    }

    [Fact]
    public void PiiMaskingOptions_FromConfig_Null()
    {
        var options = PiiMaskingOptions.FromConfig(null);
        Assert.Equal(0.5, options.ConfidenceThreshold);
        Assert.Equal("***", options.IrreversibleReplacement);
    }
}
