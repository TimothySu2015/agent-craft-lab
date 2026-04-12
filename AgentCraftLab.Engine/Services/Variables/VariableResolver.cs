using System.Text.RegularExpressions;
using AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Engine.Services.Variables;

/// <summary>
/// <see cref="IVariableResolver"/> 的預設實作 — 使用 regex 掃描 {{scope:name}} 並查表替換。
/// 單一入口，統一 Flow / 畫布 Workflow / ReAct 的變數解析。
/// </summary>
public sealed partial class VariableResolver : IVariableResolver
{
    /// <summary>引用 output 超過此字元數時觸發壓縮（≈ 500 tokens）。</summary>
    internal const int CompressionThreshold = 2000;

    /// <summary>壓縮目標 token 數。</summary>
    internal const int CompressionBudget = 500;

    public string Resolve(string? text, VariableContext context)
    {
        if (string.IsNullOrEmpty(text) || !HasReferences(text))
        {
            return text ?? "";
        }

        return ReferencePattern().Replace(text, match =>
        {
            var scope = ParseScope(match.Groups[1].Value);
            var name = match.Groups[2].Value.Trim();
            return Lookup(scope, name, context, out var value)
                ? value
                : match.Value;
        });
    }

    public async Task<string> ResolveAsync(
        string? text,
        VariableContext context,
        IContextCompactor compactor,
        string compressContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text) || !HasReferences(text))
        {
            return text ?? "";
        }

        var matches = ReferencePattern().Matches(text);
        if (matches.Count == 0)
        {
            return text;
        }

        // 從後往前替換，避免 offset 偏移
        var result = text;
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var scope = ParseScope(match.Groups[1].Value);
            var name = match.Groups[2].Value.Trim();

            if (!Lookup(scope, name, context, out var value))
            {
                continue;
            }

            // 僅 NodeOutput 需要壓縮（其他 scope 的值通常很短）
            if (scope == VariableScope.NodeOutput && value.Length > CompressionThreshold)
            {
                var compressed = await compactor
                    .CompressAsync(value, compressContext, CompressionBudget, cancellationToken)
                    .ConfigureAwait(false);
                if (compressed is not null)
                {
                    value = compressed;
                }
            }

            result = string.Concat(
                result.AsSpan(0, match.Index),
                value,
                result.AsSpan(match.Index + match.Length));
        }

        return result;
    }

    public bool HasReferences(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.Contains("{{sys:", StringComparison.Ordinal)
            || text.Contains("{{var:", StringComparison.Ordinal)
            || text.Contains("{{env:", StringComparison.Ordinal)
            || text.Contains("{{node:", StringComparison.Ordinal)
            || text.Contains("{{runtime:", StringComparison.Ordinal);
    }

    public IReadOnlyList<VariableReference> ExtractReferences(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return ReferencePattern().Matches(text)
            .Select(m => new VariableReference(ParseScope(m.Groups[1].Value), m.Groups[2].Value.Trim()))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// 靜態提取文字中所有 <c>{{node:name}}</c> 引用的名稱（去重）。
    /// 供 FlowPlanValidator 等靜態 context 使用，不需建立 VariableResolver 實例。
    /// 其他 scope（sys / var / env / runtime）不會被捕獲。
    /// </summary>
    public static IReadOnlyList<string> ExtractNodeReferenceNames(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return ReferencePattern().Matches(text)
            .Where(m => m.Groups[1].Value == "node")
            .Select(m => m.Groups[2].Value.Trim())
            .Distinct()
            .ToList();
    }

    private static VariableScope ParseScope(string prefix) => prefix switch
    {
        "sys" => VariableScope.System,
        "var" => VariableScope.Workflow,
        "runtime" => VariableScope.Runtime,
        "env" => VariableScope.Environment,
        "node" => VariableScope.NodeOutput,
        // Regex 已限制只捕獲上述五種 prefix，理論上不可能走到這裡。
        // 若未來擴充 regex 卻忘了對應，立刻 throw 失敗，避免誤判為 Workflow 的隱性 bug。
        _ => throw new ArgumentOutOfRangeException(nameof(prefix), prefix, "Unknown variable scope prefix")
    };

    private static bool Lookup(
        VariableScope scope,
        string name,
        VariableContext context,
        out string value)
    {
        var source = scope switch
        {
            VariableScope.System => context.System,
            VariableScope.Workflow => context.Workflow,
            VariableScope.Runtime => context.Runtime,
            VariableScope.Environment => context.Environment,
            VariableScope.NodeOutput => context.NodeOutputs,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unhandled variable scope")
        };

        if (source.TryGetValue(name, out var direct))
        {
            value = direct;
            return true;
        }

        // NodeOutput 特殊：若直接查 name 失敗，嘗試透過 NodeNameMap 反查 ID
        if (scope == VariableScope.NodeOutput && context.NodeNameMap is { Count: > 0 })
        {
            if (context.NodeNameMap.TryGetValue(name, out var nodeId)
                && context.NodeOutputs.TryGetValue(nodeId, out var byId))
            {
                value = byId;
                return true;
            }
        }

        value = "";
        return false;
    }

    [GeneratedRegex(@"\{\{(sys|var|runtime|env|node):([^}]+)\}\}")]
    private static partial Regex ReferencePattern();
}
