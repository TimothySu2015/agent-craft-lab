using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Code 節點執行器 — 確定性資料轉換，零 LLM 成本。
/// Script 模式支援讀寫 Workflow 變數：
/// - 讀取：腳本中 JSON.parse($variables) 取得當前變數
/// - 寫回：腳本回傳 JSON 含 "__variables__" key，值會寫回 state.Variables
/// </summary>
public sealed class CodeNodeExecutor : NodeExecutorBase<CodeNode>
{
    protected override async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, CodeNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = string.IsNullOrWhiteSpace(node.Name) ? $"Code_{node.Id}" : node.Name;
        yield return ExecutionEvent.AgentStarted(nodeName);

        // 解析 input 中的變數引用
        var input = state.PreviousResult;
        if (state.VariableResolver.HasReferences(input))
        {
            input = state.VariableResolver.Resolve(input, state.ToVariableContext());
        }

        var isScript = node.Kind == TransformKind.Script;

        // Script 模式：注入 $variables 讓腳本可讀取
        if (isScript && state.Variables.Count > 0)
        {
            TransformHelper.CurrentVariablesJson = JsonSerializer.Serialize(state.Variables);
        }

        var result = TransformHelper.ApplyTransform(
            FormatTransformType(node.Kind, node.Replacement),
            input,
            template: node.Expression,
            pattern: node.Expression,
            replacement: node.Replacement,
            maxLength: node.MaxLength,
            delimiter: node.Delimiter,
            splitIndex: node.SplitIndex,
            scriptLanguage: node.Language is { } lang ? FormatScriptLanguage(lang) : null);

        // Script 模式：提取 __variables__ 寫回 state
        if (isScript)
        {
            result = ExtractVariableMutations(result, state.Variables);
            TransformHelper.CurrentVariablesJson = null;
        }

        yield return ExecutionEvent.TextChunk(nodeName, result);
        yield return ExecutionEvent.AgentCompleted(nodeName, result);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 將新 <see cref="TransformKind"/> enum 轉為 TransformHelper 期望的舊字串常數。
    /// 舊 schema 的 "regex-extract" / "regex-replace" 在新 enum 合併為 Regex — 透過
    /// <paramref name="replacement"/> 有無區分（有 replacement → replace；無 → extract）。
    /// </summary>
    private static string FormatTransformType(TransformKind kind, string? replacement) => kind switch
    {
        TransformKind.Template => "template",
        TransformKind.Regex => string.IsNullOrEmpty(replacement) ? "regex-extract" : "regex-replace",
        TransformKind.JsonPath => "json-path",
        TransformKind.Trim => "trim",
        TransformKind.Truncate => "trim", // 舊 "trim" 實際做的是 max-length 截斷
        TransformKind.Split => "split-take",
        TransformKind.Upper => "upper",
        TransformKind.Lower => "lower",
        TransformKind.Script => "script",
        _ => "template"
    };

    private static string FormatScriptLanguage(ScriptLanguage lang) => lang switch
    {
        ScriptLanguage.CSharp => "csharp",
        _ => "javascript"
    };

    /// <summary>
    /// 從 script output 提取 __variables__ mutations 寫回 state.Variables。
    /// 格式：{"__variables__": {"counter": "5", "name": "alice"}, "__output__": "actual output"}
    /// 如果沒有 __variables__，output 原樣回傳。
    /// </summary>
    private static string ExtractVariableMutations(string output, Dictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(output) || !output.TrimStart().StartsWith('{'))
            return output;

        try
        {
            using var doc = JsonDocument.Parse(output);
            if (!doc.RootElement.TryGetProperty("__variables__", out var varsElem))
                return output;

            foreach (var prop in varsElem.EnumerateObject())
            {
                variables[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.GetRawText();
            }

            if (doc.RootElement.TryGetProperty("__output__", out var outputElem))
            {
                return outputElem.ValueKind == JsonValueKind.String
                    ? outputElem.GetString() ?? ""
                    : outputElem.GetRawText();
            }

            return output;
        }
        catch (JsonException)
        {
            return output;
        }
    }
}
