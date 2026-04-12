using System.ComponentModel;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// 確定性 HTTP 呼叫節點 — 用 <see cref="HttpRequestSpec"/> 分派 Catalog 引用或 Inline 定義。
/// 取代舊 schema 的 HttpApiId + HttpUrl + HttpMethod + ... 20 個散落欄位。
/// </summary>
public sealed record HttpRequestNode : NodeConfig
{
    [Description("HTTP 請求規格 — discriminator union：CatalogHttpRef（引用 WorkflowResources.HttpApis）或 InlineHttpRequest（就地定義）")]
    public HttpRequestSpec Spec { get; init; } = new InlineHttpRequest();
}
