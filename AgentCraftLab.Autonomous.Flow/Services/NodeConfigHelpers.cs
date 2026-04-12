using AgentCraftLab.Engine.Models;
using Schema = AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Autonomous.Flow.Services;

/// <summary>
/// Schema.NodeConfig 小工具 — 型別 → 字串識別、Flow-specific 分支 metadata 讀寫。
/// Flow 內部很多地方需要把 <see cref="Schema.NodeConfig"/> 子型別轉回字串（對齊
/// <see cref="NodeTypes"/> 常數），統一由這裡提供避免散落 switch。
/// </summary>
public static class NodeConfigHelpers
{
    /// <summary>
    /// 取得 NodeConfig 的字串型別識別（對應 <see cref="NodeTypes"/> 常數）。
    /// Pattern match 失敗時 throw — 不該靜默 fallback 到 "unknown"。
    /// </summary>
    public static string GetNodeTypeString(Schema.NodeConfig node) => node switch
    {
        Schema.AgentNode => NodeTypes.Agent,
        Schema.CodeNode => NodeTypes.Code,
        Schema.ConditionNode => NodeTypes.Condition,
        Schema.LoopNode => NodeTypes.Loop,
        Schema.IterationNode => NodeTypes.Iteration,
        Schema.ParallelNode => NodeTypes.Parallel,
        Schema.HttpRequestNode => NodeTypes.HttpRequest,
        Schema.RouterNode => NodeTypes.Router,
        Schema.A2AAgentNode => NodeTypes.A2AAgent,
        Schema.AutonomousNode => NodeTypes.Autonomous,
        Schema.HumanNode => NodeTypes.Human,
        Schema.RagNode => NodeTypes.Rag,
        Schema.StartNode => NodeTypes.Start,
        Schema.EndNode => NodeTypes.End,
        _ => throw new NotSupportedException($"Unknown NodeConfig subtype: {node.GetType().Name}")
    };

    // ─── Flow-specific condition branch indices（stash 到 Meta） ───
    //
    // Flow 的 condition 節點支援明確指定 true/false 跳轉目標 index（相對於
    // Engine 的 graph 連線驅動）。Schema.ConditionNode 沒有這兩個欄位，
    // 透過 <see cref="Schema.NodeConfig.Meta"/> 字典 stash。

    public const string MetaTrueBranchIndex = "flow:trueBranchIndex";
    public const string MetaFalseBranchIndex = "flow:falseBranchIndex";

    /// <summary>從 Meta 讀取 Flow-specific 分支 index，失敗回 null。</summary>
    public static int? GetBranchIndex(Schema.NodeConfig node, string key)
    {
        if (node.Meta is null) return null;
        if (!node.Meta.TryGetValue(key, out var raw)) return null;
        return int.TryParse(raw, out var v) ? v : null;
    }

    /// <summary>寫入/更新 Flow-specific 分支 index 到 Meta，回傳新的 meta 字典。</summary>
    public static IReadOnlyDictionary<string, string>? WithBranchIndices(
        IReadOnlyDictionary<string, string>? existing,
        int? trueBranchIndex,
        int? falseBranchIndex)
    {
        if (trueBranchIndex is null && falseBranchIndex is null)
        {
            return existing;
        }

        var merged = existing is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(existing);

        if (trueBranchIndex is not null)
            merged[MetaTrueBranchIndex] = trueBranchIndex.Value.ToString();
        if (falseBranchIndex is not null)
            merged[MetaFalseBranchIndex] = falseBranchIndex.Value.ToString();

        return merged;
    }

    // ─── 扁平存取擴充方法（給 Flow Tuning scenario 檢查用） ───
    //
    // Flow Tuning harness 的 semantic check 原本讀 PlannedNode 的 flat 欄位，
    // 改成 pattern match 每個 subtype 很冗長。這組擴充方法提供「忽略型別的
    // 扁平存取」讓 check 閉包維持簡潔：型別不匹配時回 null / 空字串 /
    // 預設值，反映「這個欄位在這種節點上不存在」的語意。

    public static string? InstructionsOrNull(this Schema.NodeConfig node) => node switch
    {
        Schema.AgentNode a => a.Instructions,
        Schema.LoopNode l => l.BodyAgent.Instructions,
        Schema.IterationNode i => i.BodyAgent.Instructions,
        _ => null
    };

    public static IReadOnlyList<string> ToolsOrEmpty(this Schema.NodeConfig node) => node switch
    {
        Schema.AgentNode a => a.Tools,
        Schema.LoopNode l => l.BodyAgent.Tools,
        Schema.IterationNode i => i.BodyAgent.Tools,
        _ => []
    };

    public static string NodeTypeKey(this Schema.NodeConfig node) => GetNodeTypeString(node);

    public static Schema.TransformKind? CodeKindOrNull(this Schema.NodeConfig node) =>
        node is Schema.CodeNode c ? c.Kind : null;

    public static Schema.ConditionKind? ConditionKindOrNull(this Schema.NodeConfig node) => node switch
    {
        Schema.ConditionNode c => c.Condition.Kind,
        Schema.LoopNode l => l.Condition.Kind,
        _ => null
    };

    public static string? ConditionValueOrNull(this Schema.NodeConfig node) => node switch
    {
        Schema.ConditionNode c => c.Condition.Value,
        Schema.LoopNode l => l.Condition.Value,
        _ => null
    };

    public static int? MaxIterationsOrNull(this Schema.NodeConfig node) =>
        node is Schema.LoopNode l ? l.MaxIterations : null;

    public static IReadOnlyList<Schema.BranchConfig>? BranchesOrNull(this Schema.NodeConfig node) =>
        node is Schema.ParallelNode p ? p.Branches : null;

    public static Schema.MergeStrategyKind? MergeStrategyOrNull(this Schema.NodeConfig node) =>
        node is Schema.ParallelNode p ? p.Merge : null;

    public static Schema.SplitModeKind? SplitModeOrNull(this Schema.NodeConfig node) =>
        node is Schema.IterationNode i ? i.Split : null;

    public static int? MaxConcurrencyOrNull(this Schema.NodeConfig node) =>
        node is Schema.IterationNode i ? i.MaxConcurrency : null;

    public static Schema.OutputFormat? OutputFormatOrNull(this Schema.NodeConfig node) =>
        node is Schema.AgentNode a ? a.Output.Kind : null;

    public static string? OutputSchemaOrNull(this Schema.NodeConfig node) =>
        node is Schema.AgentNode a ? a.Output.SchemaJson : null;

    public static IReadOnlyList<Schema.RouteConfig>? RoutesOrNull(this Schema.NodeConfig node) =>
        node is Schema.RouterNode r ? r.Routes : null;

    public static int? TrueBranchIndexOrNull(this Schema.NodeConfig node) =>
        GetBranchIndex(node, MetaTrueBranchIndex);

    public static int? FalseBranchIndexOrNull(this Schema.NodeConfig node) =>
        GetBranchIndex(node, MetaFalseBranchIndex);
}
