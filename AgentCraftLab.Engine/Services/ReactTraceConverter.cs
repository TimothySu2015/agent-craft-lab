using System.Text.Json;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// ReAct 軌跡 → FlowPlan JSON 轉換器。
/// 純規則映射，零 LLM，從 ExecutionEvent 串流中提取 spawn/collect/ask 結構。
/// </summary>
public static class ReactTraceConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Meta-tool 名稱（與 MetaToolFactory 常數對齊，但不引用 Autonomous 專案）
    private const string SpawnSubAgent = "spawn_sub_agent";
    private const string CollectResults = "collect_results";
    private const string CreateSubAgent = "create_sub_agent";
    private const string AskSubAgent = "ask_sub_agent";
    private const string ListSubAgents = "list_sub_agents";
    private const string SetSharedState = "set_shared_state";
    private const string GetSharedState = "get_shared_state";
    private const string AskUser = "ask_user";
    private const string RequestPeerReview = "request_peer_review";
    private const string ChallengeAssertion = "challenge_assertion";

    private static readonly HashSet<string> MetaToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        SpawnSubAgent, CollectResults, CreateSubAgent, AskSubAgent,
        ListSubAgents, SetSharedState, GetSharedState, AskUser,
        RequestPeerReview, ChallengeAssertion
    };

    /// <summary>
    /// 將 ReAct 執行事件轉換為 FlowPlan JSON。
    /// 回傳 null 表示軌跡太簡單（無 spawn/ask），不值得轉換。
    /// </summary>
    public static string? ConvertToFlowPlanJson(List<ExecutionEvent> events, string originalGoal)
    {
        if (events.Count == 0)
        {
            return null;
        }

        // 確認有 WorkflowCompleted（只處理成功的軌跡）
        if (!events.Any(e => e.Type == EventTypes.WorkflowCompleted))
        {
            return null;
        }

        // 提取 ToolCall 事件
        var toolCalls = events
            .Where(e => e.Type == EventTypes.ToolCall)
            .Select(e => ParseToolCallText(e.Text))
            .Where(tc => tc.ToolName.Length > 0)
            .ToList();

        if (toolCalls.Count <= 1)
        {
            return null;
        }

        // 檢查是否有結構性事件
        var hasStructural = toolCalls.Any(tc =>
            tc.ToolName.Equals(SpawnSubAgent, StringComparison.OrdinalIgnoreCase) ||
            tc.ToolName.Equals(CreateSubAgent, StringComparison.OrdinalIgnoreCase));

        if (!hasStructural)
        {
            return null;
        }

        // 依序掃描，建立節點清單
        var nodes = new List<Dictionary<string, object>>();
        var spawnGroup = new List<(string ToolName, string ArgsJson)>();
        // name → (instructions, tools) — create 時一次存完整資訊，ask 時直接查
        var agentRegistry = new Dictionary<string, (string Instructions, List<string>? Tools)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (toolName, argsJson) in toolCalls)
        {
            if (toolName.Equals(SpawnSubAgent, StringComparison.OrdinalIgnoreCase))
            {
                spawnGroup.Add((toolName, argsJson));
            }
            else if (toolName.Equals(CollectResults, StringComparison.OrdinalIgnoreCase))
            {
                if (spawnGroup.Count > 0)
                {
                    nodes.Add(BuildParallelNode(spawnGroup));
                    spawnGroup.Clear();
                }
            }
            else if (toolName.Equals(CreateSubAgent, StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractJsonField(argsJson, "name") ?? "";
                var instructions = ExtractJsonField(argsJson, "instructions") ?? "";
                var tools = ExtractJsonArrayField(argsJson, "tools");
                if (name.Length > 0)
                {
                    agentRegistry[name] = (instructions, tools);
                }
            }
            else if (toolName.Equals(AskSubAgent, StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractJsonField(argsJson, "name") ?? "agent";
                var (instructions, tools) = agentRegistry.GetValueOrDefault(name, ("", null));

                var agentNode = new Dictionary<string, object>
                {
                    ["type"] = "agent",
                    ["name"] = name,
                    ["instructions"] = instructions
                };
                if (tools is { Count: > 0 })
                {
                    agentNode["tools"] = NormalizeToolIds(tools);
                }

                nodes.Add(agentNode);
            }
            else if (!MetaToolNames.Contains(toolName))
            {
                // 直接工具呼叫 → agent 節點
                var query = ExtractJsonField(argsJson, "query")
                    ?? ExtractJsonField(argsJson, "expression")
                    ?? ExtractJsonField(argsJson, "url")
                    ?? "";
                var normalizedTool = NormalizeToolId(toolName);

                nodes.Add(new Dictionary<string, object>
                {
                    ["type"] = "agent",
                    ["name"] = $"Search",
                    ["instructions"] = query.Length > 0 ? $"Search for: {query}" : "Execute tool call",
                    ["tools"] = new List<string> { normalizedTool }
                });
            }
        }

        // flush 殘留的 spawn group（spawn 後沒有 collect 的邊界情況）
        if (spawnGroup.Count > 0)
        {
            nodes.Add(BuildParallelNode(spawnGroup));
            spawnGroup.Clear();
        }

        if (nodes.Count == 0)
        {
            return null;
        }

        // 追加 Summarizer
        nodes.Add(new Dictionary<string, object>
        {
            ["type"] = "agent",
            ["name"] = "Summarizer",
            ["instructions"] = $"根據上游收集的資料，回答使用者的問題：{originalGoal}"
        });

        var plan = new Dictionary<string, object> { ["nodes"] = nodes };
        return JsonSerializer.Serialize(plan, JsonOptions);
    }

    /// <summary>從 ToolCall Text 提取工具名和 JSON 參數。</summary>
    internal static (string ToolName, string ArgsJson) ParseToolCallText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ("", "");
        }

        var parenIndex = text.IndexOf('(');
        if (parenIndex <= 0)
        {
            return (text.Trim(), "{}");
        }

        var toolName = text[..parenIndex].Trim();
        var argsStart = parenIndex + 1;
        var argsEnd = text.LastIndexOf(')');
        var argsJson = argsEnd > argsStart
            ? text[argsStart..argsEnd]
            : "{}";

        return (toolName, argsJson);
    }

    /// <summary>從 JSON 字串提取指定欄位的字串值。</summary>
    internal static string? ExtractJsonField(string json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        catch
        {
            // JSON 解析失敗，靜默回傳 null
        }

        return null;
    }

    /// <summary>從 JSON 字串提取字串陣列欄位。</summary>
    internal static List<string>? ExtractJsonArrayField(string json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.Array)
            {
                return prop.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToList();
            }
        }
        catch
        {
            // JSON 解析失敗
        }

        return null;
    }

    private static Dictionary<string, object> BuildParallelNode(List<(string ToolName, string ArgsJson)> spawnGroup)
    {
        var branches = new List<Dictionary<string, object>>();
        foreach (var (_, argsJson) in spawnGroup)
        {
            var task = ExtractJsonField(argsJson, "task") ?? "unknown task";
            var tools = ExtractJsonArrayField(argsJson, "tools");

            var branch = new Dictionary<string, object>
            {
                ["name"] = task,
                ["goal"] = task
            };
            if (tools is { Count: > 0 })
            {
                branch["tools"] = NormalizeToolIds(tools);
            }

            branches.Add(branch);
        }

        return new Dictionary<string, object>
        {
            ["type"] = "parallel",
            ["name"] = "Parallel Research",
            ["branches"] = branches,
            ["merge"] = "labeled"
        };
    }

    private static string NormalizeToolId(string toolId)
    {
        if (toolId.StartsWith("functions.", StringComparison.OrdinalIgnoreCase))
        {
            return toolId["functions.".Length..];
        }

        return toolId;
    }

    private static List<string> NormalizeToolIds(List<string> toolIds)
    {
        return toolIds.Select(NormalizeToolId).ToList();
    }
}
