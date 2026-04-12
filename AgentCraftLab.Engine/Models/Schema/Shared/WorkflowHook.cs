using System.Text.Json.Serialization;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// Workflow 6 個 Hook 插入點的容器。
/// </summary>
public sealed record WorkflowHooks
{
    public WorkflowHook? OnInput { get; init; }
    public WorkflowHook? PreExecute { get; init; }
    public WorkflowHook? PreAgent { get; init; }
    public WorkflowHook? PostAgent { get; init; }
    public WorkflowHook? OnComplete { get; init; }
    public WorkflowHook? OnError { get; init; }
}

/// <summary>
/// Hook 抽象基底 — 目前支援 Code（本地轉換）和 Webhook（HTTP 通知）兩種。
/// BlockPattern/BlockMessage 為共用攔截機制（OnInput 可用來過濾訊息）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CodeHook), "code")]
[JsonDerivedType(typeof(WebhookHook), "webhook")]
public abstract record WorkflowHook
{
    public string? BlockPattern { get; init; }
    public string? BlockMessage { get; init; }
}

/// <summary>
/// Code Hook — 本地轉換（複用 CodeNode 的 TransformKind）。
/// </summary>
public sealed record CodeHook : WorkflowHook
{
    public TransformKind Kind { get; init; } = TransformKind.Template;
    public string Expression { get; init; } = "{{input}}";
    public string? Replacement { get; init; }
    public int MaxLength { get; init; }
    public string Delimiter { get; init; } = "\n";
    public int SplitIndex { get; init; }
}

/// <summary>
/// Webhook Hook — 對外 HTTP 通知。
/// </summary>
public sealed record WebhookHook : WorkflowHook
{
    public string Url { get; init; } = "";
    public HttpMethodKind Method { get; init; } = HttpMethodKind.Post;
    public IReadOnlyList<HttpHeader> Headers { get; init; } = [];
    public string? BodyTemplate { get; init; }
}
