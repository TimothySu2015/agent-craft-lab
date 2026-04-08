using System.Text.Json;
using AgentCraftLab.Data;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 情境模式聚合器 — 從歷史實體記憶與執行記憶中歸納使用者互動模式。
/// 機率觸發（不是每次都跑），避免 LLM 成本過高。
/// </summary>
public static class ContextualPatternAggregator
{
    private static readonly HashSet<string> ValidPatternTypes = ["preference", "behavior", "topic_interest"];

    /// <summary>
    /// 分析最近的執行和實體記憶，歸納使用者偏好與行為模式。
    /// </summary>
    public static async Task AggregateAsync(
        IChatClient client,
        IEntityMemoryStore entityStore,
        IContextualMemoryStore contextStore,
        IExecutionMemoryStore executionStore,
        string userId,
        CancellationToken ct)
    {
        // 收集近期數據
        var recentEntities = await entityStore.SearchAsync(userId, "", limit: 20);
        var recentExecutions = await executionStore.SearchAsync(userId, "", limit: 10);
        var existingPatterns = await contextStore.GetPatternsAsync(userId, limit: 10);

        if (recentExecutions.Count < 3)
        {
            // 數據太少，不值得聚合
            return;
        }

        // 建構 LLM 分析 prompt
        var entitySummary = string.Join("\n", recentEntities.Take(10).Select(e =>
            $"- {e.EntityName} ({e.EntityType}): {e.Facts[..Math.Min(e.Facts.Length, 100)]}"));

        var executionSummary = string.Join("\n", recentExecutions.Take(5).Select(e =>
            $"- [{(e.Succeeded ? "OK" : "FAIL")}] {e.GoalKeywords} ({e.StepCount} steps, tools: {e.ToolSequence})"));

        var existingPatternsSummary = existingPatterns.Count > 0
            ? "\nExisting patterns:\n" + string.Join("\n", existingPatterns.Select(p =>
                $"- [{p.PatternType}] {p.Description} (confidence: {p.Confidence:F1}, seen {p.OccurrenceCount}x)"))
            : "";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                """
                Analyze the user's recent AI agent executions and identify behavioral patterns.
                Consider: topics of interest, preferred tools, common task types, interaction style.

                Output JSON array of patterns:
                [
                  {
                    "patternType": "preference|behavior|topic_interest",
                    "description": "concise description of the pattern",
                    "confidence": 0.5 to 1.0
                  }
                ]

                Rules:
                - Max 5 patterns
                - Only include patterns with clear evidence (3+ occurrences or strong signal)
                - Don't repeat existing patterns unless confidence should increase
                - Focus on actionable insights that help the agent serve the user better
                - Respond in the same language as the input data
                """),
            new(ChatRole.User,
                $"Recent entities:\n{entitySummary}\n\nRecent executions:\n{executionSummary}{existingPatternsSummary}")
        };

        try
        {
            var response = await client.GetResponseAsync(messages, cancellationToken: ct);
            var text = response.Text ?? "[]";

            // 剝離 markdown fence
            if (text.Contains("```"))
            {
                var start = text.IndexOf('[');
                var end = text.LastIndexOf(']');
                if (start >= 0 && end > start)
                {
                    text = text[start..(end + 1)];
                }
            }

            var patterns = JsonSerializer.Deserialize<List<AggregatedPattern>>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (patterns is null)
            {
                return;
            }

            foreach (var pattern in patterns.Take(5))
            {
                var safeType = ValidPatternTypes.Contains(pattern.PatternType) ? pattern.PatternType : "behavior";
                var safeConfidence = Math.Clamp(pattern.Confidence, 0.1f, 1.0f);

                await contextStore.UpsertPatternAsync(
                    userId, safeType, pattern.Description, safeConfidence);
            }
        }
        catch
        {
            // 聚合失敗不影響主流程
        }
    }
}

/// <summary>LLM 聚合的模式結構。</summary>
public sealed record AggregatedPattern
{
    public string PatternType { get; init; } = "behavior";
    public string Description { get; init; } = "";
    public float Confidence { get; init; } = 0.5f;
}
