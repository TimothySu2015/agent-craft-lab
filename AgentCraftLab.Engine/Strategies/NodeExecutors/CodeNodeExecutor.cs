using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>
/// Code 節點執行器 — 確定性資料轉換，零 LLM 成本。
/// Script 模式支援讀寫 Workflow 變數：
/// - 讀取：腳本中 JSON.parse($variables) 取得當前變數
/// - 寫回：腳本回傳 JSON 含 "__variables__" key，值會寫回 state.Variables
/// </summary>
public sealed class CodeNodeExecutor : INodeExecutor
{
    public string NodeType => NodeTypes.Code;

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId, WorkflowNode node, ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodeName = string.IsNullOrWhiteSpace(node.Name) ? $"Code_{node.Id}" : node.Name;
        yield return ExecutionEvent.AgentStarted(nodeName);

        // 解析 input 中的變數引用
        var input = state.PreviousResult;
        if (NodeReferenceResolver.HasVariableReferences(input))
        {
            input = NodeReferenceResolver.ResolveVariables(input, state.SystemVariables, state.Variables, state.EnvironmentVariables);
        }

        // Script 模式：注入 $variables 讓腳本可讀取
        var isScript = string.Equals(node.TransformType, "script", StringComparison.OrdinalIgnoreCase);
        if (isScript && state.Variables.Count > 0)
        {
            TransformHelper.CurrentVariablesJson = JsonSerializer.Serialize(state.Variables);
        }

        var result = TransformHelper.ApplyTransform(node, input);

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

            // 寫回變數
            foreach (var prop in varsElem.EnumerateObject())
            {
                variables[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.GetRawText();
            }

            // 回傳 __output__ 或移除 __variables__ 後的原始值
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
