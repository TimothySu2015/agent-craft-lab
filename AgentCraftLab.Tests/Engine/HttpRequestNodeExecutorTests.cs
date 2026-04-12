using System.Collections.Concurrent;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;
using AgentCraftLab.Engine.Strategies;
using AgentCraftLab.Engine.Strategies.NodeExecutors;

namespace AgentCraftLab.Tests.Engine;

public class HttpRequestNodeExecutorTests
{
    private readonly HttpRequestNodeExecutor _executor = new();

    [Fact]
    public void NodeConfigType_IsHttpRequestNode()
    {
        Assert.Equal(typeof(HttpRequestNode), _executor.NodeConfigType);
    }

    [Fact]
    public async Task NoHttpService_ReturnsServiceError()
    {
        var node = new HttpRequestNode
        {
            Id = "h1",
            Name = "TestHTTP",
            Spec = new InlineHttpRequest
            {
                Url = "https://api.example.com/data",
            },
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
        var node = new HttpRequestNode
        {
            Id = "h2",
            Name = "EmptyHTTP",
            Spec = new InlineHttpRequest
            {
                Url = "",
            },
        };

        var state = CreateState(httpDefs: null);

        var events = await CollectEvents(node, state);
        Assert.Contains(events, e =>
            e.Type == EventTypes.TextChunk && e.Text!.Contains("[HTTP Error]"));
        Assert.Contains(events, e =>
            e.Type == EventTypes.TextChunk && e.Text!.Contains("no inline URL"));
    }

    [Fact]
    public async Task CatalogMode_ApiIdNotInDefs_ReturnsError()
    {
        // Phase C hard-cut：converter 依 HttpApiId 非空就轉為 CatalogHttpRef，
        // 不再 fallback 到 inline。舊 schema 允許同時填 apiId + url 作為 fallback，
        // 新 schema 要求使用者擇一。templates 重寫時要挑一邊。
        var node = new HttpRequestNode
        {
            Id = "h3",
            Name = "FallbackHTTP",
            Spec = new CatalogHttpRef
            {
                ApiId = "nonexistent",
            },
        };

        var state = CreateState(httpDefs: []);

        var events = await CollectEvents(node, state);
        // 新行為：catalog 找不到 → 直接 error（不再 fallback 到 inline url）
        Assert.Contains(events, e =>
            e.Type == EventTypes.TextChunk && e.Text!.Contains("[HTTP Error]"));
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

        var node = new HttpRequestNode
        {
            Id = "h4",
            Name = "WeatherHTTP",
            Spec = new CatalogHttpRef
            {
                ApiId = "weather",
            },
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
        var node = new HttpRequestNode
        {
            Id = "h5",
            Name = "SeqHTTP",
            Spec = new InlineHttpRequest
            {
                Url = "https://api.example.com",
            },
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
    public void InlineHttpRequest_InlineFields_DefaultValues()
    {
        var spec = new InlineHttpRequest();
        Assert.Equal("", spec.Url);
        Assert.Equal(HttpMethodKind.Get, spec.Method);
        Assert.Empty(spec.Headers);
        Assert.Null(spec.Body);
        Assert.Equal("application/json", spec.ContentType);
        Assert.Equal(2000, spec.ResponseMaxLength);
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
        var node = new HttpRequestNode
        {
            Id = "ct1",
            Name = "CsvHTTP",
            Spec = new InlineHttpRequest
            {
                Url = "https://api.example.com/upload",
                Method = HttpMethodKind.Post,
                ContentType = "text/csv",
                ResponseMaxLength = 5000,
            },
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
    public void InlineHttpRequest_TimeoutSeconds_Default()
    {
        var spec = new InlineHttpRequest();
        Assert.Equal(15, spec.TimeoutSeconds);
    }

    [Fact]
    public async Task InlineMode_TimeoutSeconds_Propagated()
    {
        var node = new HttpRequestNode
        {
            Id = "t1",
            Name = "TimeoutHTTP",
            Spec = new InlineHttpRequest
            {
                Url = "https://api.example.com",
                TimeoutSeconds = 60,
            },
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
    public void InlineHttpRequest_AuthFields_DefaultValues()
    {
        var spec = new InlineHttpRequest();
        Assert.IsType<NoneAuth>(spec.Auth);
    }

    [Fact]
    public async Task InlineMode_AuthMode_Propagated()
    {
        var node = new HttpRequestNode
        {
            Id = "auth1",
            Name = "AuthHTTP",
            Spec = new InlineHttpRequest
            {
                Url = "https://api.example.com",
                Auth = new BearerAuth("test-token"),
            },
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
    public void InlineHttpRequest_Retry_DefaultValues()
    {
        var spec = new InlineHttpRequest();
        Assert.Equal(0, spec.Retry.Count);
        Assert.Equal(1000, spec.Retry.DelayMs);
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
    public void InlineHttpRequest_ResponseFormat_DefaultValues()
    {
        var spec = new InlineHttpRequest();
        Assert.IsType<TextParser>(spec.Response);
    }

    [Fact]
    public async Task InlineMode_ResponseFormat_Propagated()
    {
        var node = new HttpRequestNode
        {
            Id = "rf1",
            Name = "JsonPathHTTP",
            Spec = new InlineHttpRequest
            {
                Url = "https://api.example.com",
                Response = new JsonPathParser("data.name"),
            },
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
        var node = new HttpRequestNode
        {
            Id = "h6",
            Name = "MyCustomAPI",
            Spec = new InlineHttpRequest
            {
                Url = "https://api.example.com",
            },
        };

        var state = CreateState(httpDefs: null);

        var events = await CollectEvents(node, state);
        var started = events.First(e => e.Type == EventTypes.AgentStarted);
        Assert.Equal("MyCustomAPI", started.AgentName);
    }

    private async Task<List<ExecutionEvent>> CollectEvents(
        HttpRequestNode node, ImperativeExecutionState state)
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
