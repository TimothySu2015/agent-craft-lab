using System.Text.RegularExpressions;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// {{node:step_name}} 跨節點引用解析器 — 在 instructions 或 input 中替換為對應節點的輸出。
/// 共用於 Flow（AI 規劃）和畫布 Workflow（人類設計）。
/// 支援可選的引用壓縮：超過門檻的 output 自動用 IContextCompactor 壓縮後再注入。
/// </summary>
public static partial class NodeReferenceResolver
{
    /// <summary>引用 output 超過此字元數時觸發壓縮（≈ 500 tokens）。</summary>
    internal const int CompressionThreshold = 2000;

    /// <summary>壓縮目標 token 數。</summary>
    internal const int CompressionBudget = 500;

    /// <summary>
    /// 解析文字中的 {{node:name}} 引用，直接用 name → output 對照表替換。
    /// 用於 Flow 模式（PlannedNode.Name 即為 key）。
    /// </summary>
    public static string Resolve(string? text, IReadOnlyDictionary<string, string>? nodeOutputs)
    {
        if (string.IsNullOrEmpty(text) || nodeOutputs is null or { Count: 0 })
            return text ?? "";

        if (!text.Contains("{{node:"))
            return text;

        return NodeRefPattern().Replace(text, match =>
        {
            var refName = match.Groups[1].Value.Trim();
            return nodeOutputs.TryGetValue(refName, out var output)
                ? output
                : match.Value;
        });
    }

    /// <summary>
    /// 非同步解析 {{node:name}} 引用，超過門檻時用 compactor 壓縮後注入。
    /// context 參數提供壓縮方向（通常是當前節點的 instructions 去除 {{node:}} 標記後的文字）。
    /// </summary>
    public static async Task<string> ResolveAsync(
        string? text,
        IReadOnlyDictionary<string, string>? nodeOutputs,
        IContextCompactor compactor,
        string compressContext,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text) || nodeOutputs is null or { Count: 0 })
            return text ?? "";

        if (!text.Contains("{{node:"))
            return text;

        return await ReplaceAsync(text, nodeOutputs.TryGetValue, compactor, compressContext, ct);
    }

    /// <summary>
    /// 解析文字中的 {{node:name}} 引用，用 name → ID 反向查找 + ID → output 替換。
    /// 用於畫布 Workflow（NodeResults key 是 node ID，使用者用 node name 引用）。
    /// </summary>
    public static string Resolve(
        string? text,
        IReadOnlyDictionary<string, string> nodeResults,
        IReadOnlyDictionary<string, Models.WorkflowNode> nodeMap)
    {
        if (string.IsNullOrEmpty(text) || nodeResults.Count == 0)
            return text ?? "";

        if (!text.Contains("{{node:"))
            return text;

        // 建立 name → ID 反向索引（lazy，只在有引用時建立）
        Dictionary<string, string>? nameToId = null;

        return NodeRefPattern().Replace(text, match =>
        {
            var refName = match.Groups[1].Value.Trim();

            // 先嘗試直接用 name 當 ID 查（以防使用者用 ID 引用）
            if (nodeResults.TryGetValue(refName, out var output))
                return output;

            // 建立反向索引（重複 Name 時取第一個，避免 ArgumentException）
            nameToId ??= nodeMap
                .GroupBy(kv => kv.Value.Name ?? kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

            // 用 name 查 ID，再用 ID 查 output
            if (nameToId.TryGetValue(refName, out var nodeId) && nodeResults.TryGetValue(nodeId, out var result))
                return result;

            return match.Value;
        });
    }

    /// <summary>
    /// 非同步解析畫布 {{node:name}} 引用，超過門檻時壓縮。
    /// </summary>
    public static async Task<string> ResolveAsync(
        string? text,
        IReadOnlyDictionary<string, string> nodeResults,
        IReadOnlyDictionary<string, Models.WorkflowNode> nodeMap,
        IContextCompactor compactor,
        string compressContext,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text) || nodeResults.Count == 0)
            return text ?? "";

        if (!text.Contains("{{node:"))
            return text;

        Dictionary<string, string>? nameToId = null;

        bool TryLookup(string refName, out string? value)
        {
            if (nodeResults.TryGetValue(refName, out var v))
            {
                value = v;
                return true;
            }

            nameToId ??= nodeMap
                .GroupBy(kv => kv.Value.Name ?? kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

            if (nameToId.TryGetValue(refName, out var nodeId) && nodeResults.TryGetValue(nodeId, out var result))
            {
                value = result;
                return true;
            }

            value = null;
            return false;
        }

        return await ReplaceAsync(text, TryLookup, compactor, compressContext, ct);
    }

    /// <summary>delegate 型別，用於查找 node output。</summary>
    private delegate bool TryGetOutput(string refName, out string? value);

    /// <summary>非同步替換核心 — 逐一解析 {{node:}} 標記，超過門檻時壓縮。</summary>
    private static async Task<string> ReplaceAsync(
        string text,
        TryGetOutput lookup,
        IContextCompactor compactor,
        string compressContext,
        CancellationToken ct)
    {
        var matches = NodeRefPattern().Matches(text);
        if (matches.Count == 0)
            return text;

        // 從後往前替換，避免 offset 偏移
        var result = text;
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var refName = match.Groups[1].Value.Trim();

            if (!lookup(refName, out var output) || output is null)
                continue;

            // 超過門檻 → 壓縮
            if (output.Length > CompressionThreshold)
            {
                var compressed = await compactor.CompressAsync(output, compressContext, CompressionBudget, ct);
                if (compressed is not null)
                {
                    output = compressed;
                }
            }

            result = string.Concat(result.AsSpan(0, match.Index), output, result.AsSpan(match.Index + match.Length));
        }

        return result;
    }

    /// <summary>檢查文字中是否含有 {{node:}} 引用。</summary>
    public static bool HasReferences(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains("{{node:");

    /// <summary>提取文字中所有 {{node:name}} 引用的名稱。</summary>
    public static IReadOnlyList<string> ExtractNames(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        return NodeRefPattern().Matches(text)
            .Select(m => m.Groups[1].Value.Trim())
            .Distinct()
            .ToList();
    }

    // ─── Variable Resolution（{{sys:}} / {{var:}} / {{env:}}）───

    /// <summary>
    /// 解析文字中的 {{sys:name}}、{{var:name}}、{{env:name}} 變數引用。
    /// 不處理 {{node:}} — 那由現有的 Resolve 方法獨立處理。
    /// </summary>
    public static string ResolveVariables(
        string? text,
        IReadOnlyDictionary<string, string>? systemVars,
        IReadOnlyDictionary<string, string>? workflowVars,
        IReadOnlyDictionary<string, string>? envVars = null)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";

        if (!text.Contains("{{sys:") && !text.Contains("{{var:") && !text.Contains("{{env:"))
            return text;

        return VariableRefPattern().Replace(text, match =>
        {
            var prefix = match.Groups[1].Value;
            var name = match.Groups[2].Value.Trim();
            var source = prefix switch
            {
                "sys" => systemVars,
                "var" => workflowVars,
                "env" => envVars,
                _ => null
            };
            return source is not null && source.TryGetValue(name, out var value)
                ? value
                : match.Value;
        });
    }

    /// <summary>檢查文字中是否含有 {{sys:}} / {{var:}} / {{env:}} 變數引用。</summary>
    public static bool HasVariableReferences(string? text) =>
        !string.IsNullOrEmpty(text) &&
        (text.Contains("{{sys:") || text.Contains("{{var:") || text.Contains("{{env:"));

    /// <summary>提取文字中所有變數引用的 (prefix, name) 組合。</summary>
    public static IReadOnlyList<(string Prefix, string Name)> ExtractVariableNames(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        return VariableRefPattern().Matches(text)
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value.Trim()))
            .Distinct()
            .ToList();
    }

    [GeneratedRegex(@"\{\{node:([^}]+)\}\}")]
    private static partial Regex NodeRefPattern();

    [GeneratedRegex(@"\{\{(sys|var|env):([^}]+)\}\}")]
    private static partial Regex VariableRefPattern();
}
