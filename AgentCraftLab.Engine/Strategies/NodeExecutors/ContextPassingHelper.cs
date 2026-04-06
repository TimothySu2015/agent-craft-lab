using System.Text;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

/// <summary>Context Passing 模式常數。</summary>
public static class ContextPassingModes
{
    public const string PreviousOnly = "previous-only";
    public const string WithOriginal = "with-original";
    public const string Accumulate = "accumulate";
}

/// <summary>
/// Context Passing 輔助工具 — 根據 WorkflowSettings.ContextPassing 模式，
/// 建構注入到 agent system prompt 的上下文前綴。
/// </summary>
public static class ContextPassingHelper
{
    private const int MaxPerNodeChars = 500;
    private const int MaxTotalChars = 2000;
    internal const string ContextMarker = "[Workflow Context]";

    /// <summary>
    /// 根據 ContextPassing 模式建構 system prompt 前綴。
    /// previous-only 回傳空字串（不注入）。
    /// </summary>
    public static string BuildContextPrefix(ImperativeExecutionState state, string nodeId)
    {
        if (state.ContextPassing is ContextPassingModes.PreviousOnly or "")
            return "";

        var sb = new StringBuilder();
        sb.AppendLine(ContextMarker);
        sb.AppendLine($"The user's original request to the workflow: {state.OriginalUserMessage}");
        sb.AppendLine("Your input below is the output from the previous step.");
        sb.AppendLine("Base your response ONLY on the input provided, not on the original request.");
        sb.AppendLine("The original request is provided for context only — do not fabricate information to fulfill it.");

        if (state.ContextPassing == ContextPassingModes.Accumulate && state.NodeResults.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Previous Step Results]");
            var total = 0;
            var i = 1;
            foreach (var (nid, output) in state.NodeResults)
            {
                if (nid == nodeId)
                {
                    continue;
                }

                var name = state.NodeMap.TryGetValue(nid, out var n) ? n.Name : nid;
                var truncated = output.Length > MaxPerNodeChars
                    ? output[..MaxPerNodeChars] + "..."
                    : output;
                var line = $"{i}. {name}: {truncated}";
                if (total + line.Length > MaxTotalChars)
                {
                    break;
                }

                sb.AppendLine(line);
                total += line.Length;
                i++;
            }
        }

        return sb.ToString();
    }
}
