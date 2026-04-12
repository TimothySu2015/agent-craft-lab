using System.ComponentModel;

namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// 多路由分類節點 — 根據上游輸出匹配 keyword 分派到 N 條輸出之一。
/// </summary>
public sealed record RouterNode : NodeConfig
{
    [Description("路由規則清單 — 每條規則含 name / keywords / isDefault")]
    public IReadOnlyList<RouteConfig> Routes { get; init; } = [];
}
