using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 實體抽取器 — 從執行結果中萃取具名實體及事實，供跨 Session 實體記憶使用。
/// </summary>
public static class EntityExtractor
{
    /// <summary>最大抽取實體數。</summary>
    private const int MaxEntities = 10;

    /// <summary>
    /// 用 LLM 從目標與結果中抽取具名實體及事實。
    /// </summary>
    public static async Task<List<ExtractedEntity>> ExtractAsync(
        IChatClient client, string goal, string resultSummary, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(resultSummary))
        {
            return [];
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                """
                Extract named entities and their key facts from the execution results.
                Focus on: people, organizations, products, locations, and important concepts.
                Only extract entities with concrete, factual information — skip vague references.

                Output JSON array:
                [
                  {
                    "name": "entity name",
                    "type": "person|organization|product|concept|location",
                    "facts": ["fact 1", "fact 2"]
                  }
                ]

                Max 10 entities. Max 5 facts per entity. Keep facts concise (1 sentence each).
                Respond in the same language as the input.
                If no entities are found, return [].
                """),
            new(ChatRole.User,
                $"Goal: {goal}\n\nResult:\n{resultSummary[..Math.Min(resultSummary.Length, 1500)]}")
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

            var entities = JsonSerializer.Deserialize<List<ExtractedEntity>>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return entities?.Take(MaxEntities).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }
}

/// <summary>LLM 抽取的實體結構。</summary>
public sealed record ExtractedEntity
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "concept";
    public List<string> Facts { get; init; } = [];
}
