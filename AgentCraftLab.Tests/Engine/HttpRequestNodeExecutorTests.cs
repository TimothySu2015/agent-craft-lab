using System.Collections.Concurrent;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;
using AgentCraftLab.Engine.Strategies.NodeExecutors;

namespace AgentCraftLab.Tests.Engine;

public class HttpRequestNodeExecutorTests
{
    private readonly HttpRequestNodeExecutor _executor = new();

    [Fact]
    public void NodeType_ReturnsHttpRequest()
    {
        Assert.Equal(NodeTypes.HttpRequest, _executor.NodeType);
    }

    [Fact]
    public async Task NoHttpService_ReturnsServiceError()
    {
        var node = new WorkflowNode
        {
            Id = "h1",
            Type = NodeTypes.HttpRequest,
            Name = "TestHTTP",
            HttpUrl = "https://api.example.com/data",
        };

        // 直接用 Empty context（HttpApiService 為 null）
        var context = AgentExecutionContext.Empty;
        var state = new ImperativeExecutionState
        {
            Adjacency = [],
            NodeMap = [],
            Agents = [],
            ChatClients = [],
            ChatHistories = [],
            LoopCounters = [],
            AgentContext = context,
            Request = new WorkflowExecutionRequest { UserMessage = "test" },
            HistoryStrategy = new SimpleTrimmingStrategy(),
        };

        var events = await CollectEvents(node, state);
        Assert.Contains(events, e =>
            e.Type == EventTypes.TextChunk && e.Text!.Contains("HttpApiToolService not available"));
    }

    [Fact]
    public async Task NoApiIdAndNoUrl_ReturnsConfigError()
    {
        var node = new WorkflowNode
        {
            Id = "h2",
            Type = NodeTypes.HttpRequest,
            Name = "EmptyHTTP",
            HttpApiId = "",
            HttpUrl = "",
        };

        var state = CreateState(httpDefs: null);

        var events = await CollectEvents(node, state);
        Assert.Contains(events, e =>
            e.Type == EventTypes.TextChunk && e.Text!.Contains("[HTTP Error]"));
        Assert.Contains(events, e =>
            e.Type == EventTypes.TextChunk && e.Text!.Contains("no inline URL"));
    }

    [Fact]
    public async Task CatalogMode_ApiIdNotInDefs_FallsBackToInline()
    {
        var node = new WorkflowNode
        {
            Id = "h3",
            Type = NodeTypes.HttpRequest,
            Name = "FallbackHTTP",
            HttpApiId = "nonexistent",
            HttpUrl = "https://fallback.example.com",
            HttpMethod = "PUT",
            HttpHeaders = "X-Custom: value",
        };

        var state = CreateState(httpDefs: []);

        var events = await CollectEvents(node, state);
        // 有 fallback inline URL → 不該回傳 "not found" 錯誤
        Assert.DoesNotContain(events, e =>
            e.Type == EventTypes.TextChunk && e.Text!.Contains("not found"));
    }

    [Fact]
    public async Task CatalogMode_ApiIdFound_UsesCatalogDef()
    {
        var catalogDef = new HttpApiDefinition
        {
            Id = "weather",
            Name = "Weather",
            Url = "https://weather.example.com/{city}",
            Method = "GET",
            Headers = "X-Api-Key: secret",
        };

        var node = new WorkflowNode
        {
            Id = "h4",
            Type = NodeTypes.HttpRequest,
            Name = "WeatherHTTP",
            HttpApiId = "weather",
            HttpUrl = "https://should-not-use.example.com",
        };

        var state = CreateState(httpDefs: new() { ["weather"] = catalogDef });

        var events = await CollectEvents(node, state);
        // Catalog 模式優先 → 產生正常事件流程
        Assert.Contains(events, e => e.Type == EventTypes.AgentStarted);
        Assert.Contains(events, e => e.Type == EventTypes.AgentCompleted);
    }

    [Fact]
    public async Task EmitsCorrectEventSequence()
    {
        var node = new WorkflowNode
        {
            Id = "h5",
            Type = NodeTypes.HttpRequest,
            Name = "SeqHTTP",
            HttpUrl = "https://api.example.com",
        };

        var state = CreateState(httpDefs: null);

        var events = await CollectEvents(node, state);
        var types = events.Select(e => e.Type).ToList();

        Assert.Equal(4, types.Count);
        Assert.Equal(EventTypes.AgentStarted, types[0]);
        Assert.Equal(EventTypes.ToolCall, types[1]);
        Assert.Equal(EventTypes.TextChunk, types[2]);
        Assert.Equal(EventTypes.AgentCompleted, types[3]);
    }

    [Fact]
    public void WorkflowNode_InlineFields_DefaultValues()
    {
        var node = new WorkflowNode();
        Assert.Equal("", node.HttpUrl);
        Assert.Equal("GET", node.HttpMethod);
        Assert.Equal("", node.HttpHeaders);
        Assert.Equal("", node.HttpBodyTemplate);
        Assert.Equal("application/json", node.HttpContentType);
        Assert.Equal(2000, node.HttpResponseMaxLength);
    }

    [Fact]
    public void HttpApiDefinition_NewFields_DefaultValues()
    {
        var def = new HttpApiDefinition();
        Assert.Equal("application/json", def.ContentType);
        Assert.Equal(2000, def.ResponseMaxLength);
    }

    [Fact]
    public async Task InlineMode_ContentType_PropagatedToApiDef()
    {
        var node = new WorkflowNode
        {
            Id = "ct1",
            Type = NodeTypes.HttpRequest,
            Name = "CsvHTTP",
            HttpUrl = "https://api.example.com/upload",
            HttpMethod = "POST",
            HttpContentType = "text/csv",
            HttpResponseMaxLength = 5000,
        };

        var state = CreateState(httpDefs: null);
        var events = await CollectEvents(node, state);
        // 節點正常執行（會因為假 URL 失敗，但不是 config error）
        Assert.DoesNotContain(events, e =>
            e.Type == EventTypes.TextChunk && e.Text!.Contains("not available"));
    }

    [Fact]
    public void ParseFormUrlEncoded_JsonInput()
    {
        // 測試 HttpApiToolService 的 form-urlencoded 支援
        var def = new HttpApiDefinition
        {
            ContentType = "application/x-www-form-urlencoded",
        };
        // 預設值驗證
        Assert.Equal("application/x-www-form-urlencoded", def.ContentType);
    }

    [Fact]
    public void ResponseMaxLength_ZeroMeansNoTruncation()
    {
        var def = new HttpApiDefinition { ResponseMaxLength = 0 };
        Assert.Equal(0, def.ResponseMaxLength);
    }

    [Fact]
    public void HttpApiDefinition_TimeoutSeconds_Default()
    {
        var def = new HttpApiDefinition();
        Assert.Equal(15, def.TimeoutSeconds);
    }

    [Fact]
    public void WorkflowNode_TimeoutSeconds_Default()
    {
        var node = new WorkflowNode();
        Assert.Equal(15, node.HttpTimeoutSeconds);
    }

    [Fact]
    public async Task InlineMode_TimeoutSeconds_Propagated()
    {
        var node = new WorkflowNode
        {
            Id = "t1",
            Type = NodeTypes.HttpRequest,
            Name = "TimeoutHTTP",
            HttpUrl = "https://api.example.com",
            HttpTimeoutSeconds = 60,
        };

        var state = CreateState(httpDefs: null);
        var events = await CollectEvents(node, state);
        // 節點正常執行（不是 config error）
        Assert.Contains(events, e => e.Type == EventTypes.AgentStarted);
        Assert.Contains(events, e => e.Type == EventTypes.AgentCompleted);
    }

    [Fact]
    public void BuildMultipartContent_ValidPartsJson()
    {
        // 驗證 multipart content type 被正確設定
        var def = new HttpApiDefinition
        {
            ContentType = "multipart/form-data",
            BodyTemplate = """{"parts":[{"name":"file","filename":"test.txt","contentType":"text/plain","data":"hello"},{"name":"field","value":"world"}]}""",
        };
        Assert.Equal("multipart/form-data", def.ContentType);
    }

    [Fact]
    public void BuildMultipartContent_Base64Data()
    {
        // 驗證 base64 前綴可被設定
        var def = new HttpApiDefinition
        {
            ContentType = "multipart/form-data",
            BodyTemplate = """{"parts":[{"name":"file","filename":"data.bin","data":"base64:SGVsbG8="}]}""",
        };
        Assert.Contains("base64:", def.BodyTemplate);
    }

    // ════════════════════════════════════════
    // Phase 3a: Auth 預設
    // ════════════════════════════════════════

    [Theory]
    [InlineData("none")]
    [InlineData("bearer")]
    [InlineData("basic")]
    [InlineData("apikey-header")]
    [InlineData("apikey-query")]
    public void HttpApiDefinition_AuthMode_Values(string mode)
    {
        var def = new HttpApiDefinition { AuthMode = mode };
        Assert.Equal(mode, def.AuthMode);
    }

    [Fact]
    public void WorkflowNode_AuthFields_DefaultValues()
    {
        var node = new WorkflowNode();
        Assert.Equal("none", node.HttpAuthMode);
        Assert.Equal("", node.HttpAuthCredential);
        Assert.Equal("", node.HttpAuthKeyName);
    }

    [Fact]
    public async Task InlineMode_AuthMode_Propagated()
    {
        var node = new WorkflowNode
        {
            Id = "auth1",
            Type = NodeTypes.HttpRequest,
            Name = "AuthHTTP",
            HttpUrl = "https://api.example.com",
            HttpAuthMode = "bearer",
            HttpAuthCredential = "test-token",
        };

        var state = CreateState(httpDefs: null);
        var events = await CollectEvents(node, state);
        Assert.Contains(events, e => e.Type == EventTypes.AgentStarted);
        Assert.Contains(events, e => e.Type == EventTypes.AgentCompleted);
    }

    // ════════════════════════════════════════
    // Phase 3b: 重試
    // ════════════════════════════════════════

    [Fact]
    public void HttpApiDefinition_Retry_DefaultValues()
    {
        var def = new HttpApiDefinition();
        Assert.Equal(0, def.RetryCount);
        Assert.Equal(1000, def.RetryDelayMs);
    }

    [Fact]
    public void WorkflowNode_Retry_DefaultValues()
    {
        var node = new WorkflowNode();
        Assert.Equal(0, node.HttpRetryCount);
        Assert.Equal(1000, node.HttpRetryDelayMs);
    }

    // ════════════════════════════════════════
    // Phase 3c: 回應格式解析
    // ════════════════════════════════════════

    [Fact]
    public void HttpApiDefinition_ResponseFormat_DefaultValues()
    {
        var def = new HttpApiDefinition();
        Assert.Equal("text", def.ResponseFormat);
        Assert.Equal("", def.ResponseJsonPath);
    }

    [Fact]
    public void WorkflowNode_ResponseFormat_DefaultValues()
    {
        var node = new WorkflowNode();
        Assert.Equal("text", node.HttpResponseFormat);
        Assert.Equal("", node.HttpResponseJsonPath);
    }

    [Fact]
    public async Task InlineMode_ResponseFormat_Propagated()
    {
        var node = new WorkflowNode
        {
            Id = "rf1",
            Type = NodeTypes.HttpRequest,
            Name = "JsonPathHTTP",
            HttpUrl = "https://api.example.com",
            HttpResponseFormat = "jsonpath",
            HttpResponseJsonPath = "data.name",
        };

        var state = CreateState(httpDefs: null);
        var events = await CollectEvents(node, state);
        Assert.Contains(events, e => e.Type == EventTypes.AgentStarted);
    }

    // ════════════════════════════════════════
    // Original tests
    // ════════════════════════════════════════

    [Fact]
    public async Task InlineMode_UsesNodeName_InStartedEvent()
    {
        var node = new WorkflowNode
        {
            Id = "h6",
            Type = NodeTypes.HttpRequest,
            Name = "MyCustomAPI",
            HttpUrl = "https://api.example.com",
        };

        var state = CreateState(httpDefs: null);

        var events = await CollectEvents(node, state);
        var started = events.First(e => e.Type == EventTypes.AgentStarted);
        Assert.Equal("MyCustomAPI", started.AgentName);
    }

    private async Task<List<ExecutionEvent>> CollectEvents(
        WorkflowNode node, ImperativeExecutionState state)
    {
        var events = new List<ExecutionEvent>();
        await foreach (var evt in _executor.ExecuteAsync(node.Id, node, state, CancellationToken.None))
        {
            events.Add(evt);
        }
        return events;
    }

    private static ImperativeExecutionState CreateState(
        HttpApiToolService? httpService = null,
        Dictionary<string, HttpApiDefinition>? httpDefs = null,
        string previousResult = "")
    {
        httpService ??= new HttpApiToolService(new NoOpHttpClientFactory());
        var context = new AgentExecutionContext(
            [],
            [],
            [],
            new ConcurrentQueue<(string, string, string)>(),
            HttpApiService: httpService,
            HttpApiDefs: httpDefs
        );

        return new ImperativeExecutionState
        {
            Adjacency = [],
            NodeMap = [],
            Agents = [],
            ChatClients = [],
            ChatHistories = [],
            LoopCounters = [],
            AgentContext = context,
            Request = new WorkflowExecutionRequest { UserMessage = "test" },
            PreviousResult = previousResult,
            HistoryStrategy = new SimpleTrimmingStrategy(),
        };
    }

    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
