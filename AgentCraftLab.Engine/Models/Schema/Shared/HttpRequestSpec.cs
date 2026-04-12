using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCraftLab.Engine.Models.Schema;

// ─── HttpRequestSpec：Catalog 引用 vs Inline 定義 ───

/// <summary>
/// HTTP 請求規格 — discriminator union，分派 <see cref="CatalogHttpRef"/>（引用預定義 API）
/// 與 <see cref="InlineHttpRequest"/>（節點內就地定義）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CatalogHttpRef), "catalog")]
[JsonDerivedType(typeof(InlineHttpRequest), "inline")]
public abstract record HttpRequestSpec;

/// <summary>引用 <see cref="WorkflowResources.HttpApis"/> 中預定義的 HTTP API。</summary>
public sealed record CatalogHttpRef : HttpRequestSpec
{
    public string ApiId { get; init; } = "";

    /// <summary>
    /// 傳遞給 catalog API 的參數（JSON 物件）。內嵌的字串值可含 {{node:}}/{{var:}} 引用。
    /// </summary>
    public JsonNode? Args { get; init; }
}

/// <summary>節點內就地定義的完整 HTTP 請求。</summary>
public sealed record InlineHttpRequest : HttpRequestSpec
{
    public string Url { get; init; } = "";
    public HttpMethodKind Method { get; init; } = HttpMethodKind.Get;
    public IReadOnlyList<HttpHeader> Headers { get; init; } = [];
    public HttpBody? Body { get; init; }
    public string ContentType { get; init; } = "application/json";
    public HttpAuth Auth { get; init; } = new NoneAuth();
    public RetryConfig Retry { get; init; } = new();
    public int TimeoutSeconds { get; init; } = 15;
    public ResponseParser Response { get; init; } = new TextParser();

    /// <summary>回應最大字元數（0 = 不截斷）。</summary>
    public int ResponseMaxLength { get; init; } = 2000;
}

public enum HttpMethodKind
{
    Get,
    Post,
    Put,
    Delete,
    Patch,
    Head,
    Options
}

public sealed record HttpHeader(string Name, string Value);

/// <summary>
/// HTTP 請求 Body — Content 為 JSON 結構（字串/物件/陣列皆可）。
/// 內嵌字串可含 {{node:}}/{{var:}} 引用，由 <see cref="Services.Variables.IVariableResolver"/> 解析。
/// </summary>
public sealed record HttpBody
{
    public JsonNode? Content { get; init; }
}

// ─── HttpAuth：5 種驗證模式 ───

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(NoneAuth), "none")]
[JsonDerivedType(typeof(BearerAuth), "bearer")]
[JsonDerivedType(typeof(BasicAuth), "basic")]
[JsonDerivedType(typeof(ApiKeyHeaderAuth), "apikey-header")]
[JsonDerivedType(typeof(ApiKeyQueryAuth), "apikey-query")]
public abstract record HttpAuth;

public sealed record NoneAuth : HttpAuth;
public sealed record BearerAuth(string Token) : HttpAuth;
public sealed record BasicAuth(string UserPass) : HttpAuth;
public sealed record ApiKeyHeaderAuth(string KeyName, string Value) : HttpAuth;
public sealed record ApiKeyQueryAuth(string KeyName, string Value) : HttpAuth;

// ─── ResponseParser：文字 / JSON / JSONPath ───

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextParser), "text")]
[JsonDerivedType(typeof(JsonParser), "json")]
[JsonDerivedType(typeof(JsonPathParser), "jsonPath")]
public abstract record ResponseParser;

public sealed record TextParser : ResponseParser;
public sealed record JsonParser : ResponseParser;
public sealed record JsonPathParser(string Path) : ResponseParser;
