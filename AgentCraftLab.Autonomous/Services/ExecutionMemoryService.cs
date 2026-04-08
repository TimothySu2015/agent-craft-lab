using System.Text;
using System.Text.Json;
using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Data;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 執行記憶服務介面 — 跨 Session 學習。
/// </summary>
public interface IExecutionMemoryService
{
    /// <summary>查詢相似的歷史經驗，產生注入 system prompt 的文字。</summary>
    Task<string?> GetRelevantExperienceAsync(string userId, string goal, CancellationToken ct);

    /// <summary>執行結束後，萃取經驗並儲存。</summary>
    Task RecordExecutionAsync(
        string userId, string goal, bool succeeded,
        List<string> toolsUsed, int stepCount, long tokensUsed, long elapsedMs,
        IChatClient? llmClient, CancellationToken ct,
        string? auditIssuesJson = null,
        string? resultSummary = null,
        string? planJson = null);
}

/// <summary>
/// 執行記憶服務 — 跨 Session 學習的核心。
/// 負責：萃取執行經驗 → 儲存 → 查詢相似經驗 → 注入 prompt。
/// </summary>
public sealed class ExecutionMemoryService : IExecutionMemoryService
{
    private readonly IExecutionMemoryStore _store;
    private readonly IEntityMemoryStore? _entityStore;
    private readonly IContextualMemoryStore? _contextualStore;
    private readonly ILogger<ExecutionMemoryService> _logger;
    private readonly ReactExecutorConfig _config;

    /// <summary>反思文字的最大長度</summary>
    private const int MaxReflectionLength = 300;

    // 英文停用詞
    private static readonly HashSet<string> StopWordsEn = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "are", "was", "were", "be", "been",
        "have", "has", "had", "do", "does", "did", "will", "would",
        "can", "could", "should", "may", "might", "shall",
        "to", "of", "in", "for", "on", "with", "at", "by", "from",
        "it", "this", "that", "these", "those", "i", "you", "he", "she", "we", "they",
        "and", "or", "but", "not", "no", "if", "then", "so", "very",
        "my", "your", "his", "her", "our", "their", "its",
        "what", "which", "who", "whom", "how", "when", "where", "why",
        "please", "help", "me", "about", "just", "also"
    };

    // 中文停用詞
    private static readonly HashSet<string> StopWordsCn = new()
    {
        "的", "了", "是", "在", "我", "有", "和", "就", "不", "人", "都", "一",
        "上", "也", "很", "到", "說", "要", "去", "你", "會", "著", "沒有", "看",
        "好", "自己", "這", "他", "她", "它", "們", "嗎", "吧", "呢", "啊",
        "請", "幫", "幫我", "可以", "能", "讓", "把", "用", "使用"
    };

    public ExecutionMemoryService(
        IExecutionMemoryStore store,
        ILogger<ExecutionMemoryService> logger,
        ReactExecutorConfig? config = null,
        IEntityMemoryStore? entityStore = null,
        IContextualMemoryStore? contextualStore = null)
    {
        _store = store;
        _entityStore = entityStore;
        _contextualStore = contextualStore;
        _logger = logger;
        _config = config ?? new ReactExecutorConfig();
    }

    /// <summary>
    /// 查詢相似的歷史經驗，產生注入 system prompt 的文字。
    /// </summary>
    public async Task<string?> GetRelevantExperienceAsync(
        string userId, string goal, CancellationToken ct)
    {
        var keywords = ExtractKeywords(goal, _config.MemoryMaxKeywords);
        if (string.IsNullOrWhiteSpace(keywords))
        {
            return null;
        }

        List<ExecutionMemoryDocument> memories;
        try
        {
            memories = await _store.SemanticSearchAsync(userId, keywords, limit: _config.MemoryMaxExperiences);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查詢執行記憶失敗，跳過經驗注入");
            return null;
        }

        if (memories.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Past Experience (from previous sessions)");
        sb.AppendLine("Use these insights to improve your approach:\n");

        foreach (var m in memories)
        {
            var status = m.Succeeded ? "SUCCESS" : "FAILED";

            // 記憶衰減標籤：越新的經驗排序越前，幫助 LLM 優先參考近期經驗
            var age = DateTime.UtcNow - m.CreatedAt;
            var recencyLabel = age.TotalDays switch
            {
                < 7 => "[Recent]",
                < 30 => "[This month]",
                _ => "[Older]"
            };
            sb.AppendLine($"### {recencyLabel} [{status}] Similar task ({m.StepCount} steps, {m.TokensUsed:N0} tokens)");

            if (!string.IsNullOrWhiteSpace(m.Reflection))
            {
                sb.AppendLine($"**Reflection:** {m.Reflection}");
            }

            if (!string.IsNullOrWhiteSpace(m.KeyInsights) && m.KeyInsights != "[]")
            {
                try
                {
                    var insights = JsonSerializer.Deserialize<List<string>>(m.KeyInsights);
                    if (insights is { Count: > 0 })
                    {
                        sb.AppendLine("**Key insights:**");
                        foreach (var insight in insights)
                        {
                            sb.AppendLine($"- {insight}");
                        }
                    }
                }
                catch
                {
                    // 解析失敗就跳過 insights
                }
            }

            // 注入 Auditor 審查反饋 — 避免重蹈覆轍
            if (m.AuditIssues != "[]" && !string.IsNullOrWhiteSpace(m.AuditIssues))
            {
                try
                {
                    var issues = JsonSerializer.Deserialize<List<string>>(m.AuditIssues);
                    if (issues is { Count: > 0 })
                    {
                        sb.AppendLine("**Past audit issues (avoid repeating):**");
                        foreach (var issue in issues)
                        {
                            sb.AppendLine($"- {issue}");
                        }
                    }
                }
                catch
                {
                    // 解析失敗跳過
                }
            }

            if (!string.IsNullOrWhiteSpace(m.ToolSequence))
            {
                sb.AppendLine($"**Tools used:** {m.ToolSequence}");
            }

            // 注入結果摘要 — 讓跨 Session 也能接續上次的實際內容
            if (!string.IsNullOrWhiteSpace(m.ResultSummary))
            {
                sb.AppendLine($"**Result summary:** {m.ResultSummary}");
            }

            // 注入歷史 plan 結構 — 供 Flow 規劃或 ReAct 規劃參考
            if (!string.IsNullOrWhiteSpace(m.PlanJson))
            {
                sb.AppendLine($"**Suggested plan structure:** ```json\n{m.PlanJson}\n```");
            }

            sb.AppendLine();
        }

        var result = sb.ToString();

        // 限制總長度，避免 prompt 膨脹
        if (result.Length > _config.MemoryMaxPromptLength)
        {
            result = result[.._config.MemoryMaxPromptLength] + "\n...(truncated)";
        }

        // 實體記憶注入
        if (_config.EntityMemoryEnabled && _entityStore is not null)
        {
            try
            {
                var entities = await _entityStore.SearchAsync(userId, keywords, limit: 5);
                if (entities.Count > 0)
                {
                    var entitySection = FormatEntityMemory(entities);
                    if (entitySection.Length + result.Length <= _config.MemoryMaxPromptLength + _config.EntityMaxPromptLength)
                    {
                        result += "\n\n" + entitySection;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "查詢實體記憶失敗");
            }
        }

        // 情境記憶注入
        if (_config.ContextualMemoryEnabled && _contextualStore is not null)
        {
            try
            {
                var patterns = await _contextualStore.GetPatternsAsync(userId, limit: 5);
                if (patterns.Count > 0)
                {
                    var contextSection = FormatContextualMemory(patterns);
                    if (contextSection.Length <= _config.ContextualMaxPromptLength)
                    {
                        result += "\n\n" + contextSection;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "查詢情境記憶失敗");
            }
        }

        _logger.LogInformation(
            "注入 {Count} 筆歷史經驗，目標關鍵字: {Keywords}",
            memories.Count, keywords);

        return result;
    }

    /// <summary>
    /// 執行結束後，萃取經驗並儲存。
    /// </summary>
    public async Task RecordExecutionAsync(
        string userId,
        string goal,
        bool succeeded,
        List<string> toolsUsed,
        int stepCount,
        long tokensUsed,
        long elapsedMs,
        IChatClient? llmClient,
        CancellationToken ct,
        string? auditIssuesJson = null,
        string? resultSummary = null,
        string? planJson = null)
    {
        var keywords = ExtractKeywords(goal, _config.MemoryMaxKeywords);
        var toolSequence = string.Join(", ", toolsUsed.Distinct());

        // 用 LLM 產生反思（如果有 client）
        var reflection = "";
        var keyInsights = "[]";

        if (llmClient is not null)
        {
            try
            {
                (reflection, keyInsights) = await GenerateReflectionAsync(
                    llmClient, goal, succeeded, toolSequence, stepCount, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "產生反思失敗，改為不含反思儲存");
            }
        }

        // 截斷結果摘要（保留前 500 字，平衡資訊保留與 DB 儲存成本）
        var trimmedSummary = resultSummary is { Length: > 500 }
            ? resultSummary[..500]
            : resultSummary ?? "";

        var memory = new ExecutionMemoryDocument
        {
            Id = $"mem-{Guid.NewGuid():N}"[..16],
            UserId = userId,
            GoalKeywords = keywords,
            Succeeded = succeeded,
            ToolSequence = toolSequence,
            StepCount = stepCount,
            TokensUsed = tokensUsed,
            ElapsedMs = elapsedMs,
            Reflection = reflection,
            KeyInsights = keyInsights,
            AuditIssues = auditIssuesJson ?? "[]",
            ResultSummary = trimmedSummary,
            PlanJson = planJson,
            CreatedAt = DateTime.UtcNow
        };

        await _store.SaveAsync(memory);
        _logger.LogInformation(
            "已儲存執行記憶: {Id} ({Status}, {Steps} 步)",
            memory.Id, succeeded ? "成功" : "失敗", stepCount);

        await ExtractEntitiesIfEnabledAsync(userId, goal, trimmedSummary, memory.Id, succeeded, llmClient, ct);
        await CleanupIfDueAsync(userId);
        await AggregateContextualPatternsIfDueAsync(userId, llmClient, ct);
    }

    /// <summary>實體記憶抽取（與 reflection 分開呼叫，避免 prompt 過長影響品質）。</summary>
    private async Task ExtractEntitiesIfEnabledAsync(
        string userId, string goal, string resultSummary, string executionId,
        bool succeeded, IChatClient? llmClient, CancellationToken ct)
    {
        if (!_config.EntityMemoryEnabled || _entityStore is null || llmClient is null || !succeeded)
        {
            return;
        }

        try
        {
            var entities = await EntityExtractor.ExtractAsync(llmClient, goal, resultSummary, ct);
            foreach (var entity in entities.Take(_config.EntityMaxPerExecution))
            {
                await _entityStore.MergeFactsAsync(
                    userId, entity.Name, entity.Facts, entity.Type, executionId);
            }

            if (entities.Count > 0)
            {
                _logger.LogInformation("已抽取 {Count} 個實體到記憶", entities.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "實體記憶抽取失敗");
        }
    }

    /// <summary>機率式清理（每 N 次儲存觸發一次），避免每次都執行 I/O。</summary>
    private async Task CleanupIfDueAsync(string userId)
    {
        if (Random.Shared.Next(_config.MemoryCleanupProbability) != 0)
        {
            return;
        }

        try
        {
            var cleaned = await _store.CleanupAsync(userId);
            if (cleaned > 0)
            {
                _logger.LogInformation("已清理 {Count} 筆過期執行記憶", cleaned);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理執行記憶失敗");
        }
    }

    /// <summary>情境模式聚合（機率觸發，避免每次都跑 LLM）。</summary>
    private async Task AggregateContextualPatternsIfDueAsync(
        string userId, IChatClient? llmClient, CancellationToken ct)
    {
        if (!_config.ContextualMemoryEnabled || _contextualStore is null ||
            _entityStore is null || llmClient is null ||
            Random.Shared.Next(_config.ContextualAggregationProbability) != 0)
        {
            return;
        }

        try
        {
            await ContextualPatternAggregator.AggregateAsync(
                llmClient, _entityStore, _contextualStore, _store, userId, ct);
            _logger.LogInformation("已完成情境模式聚合");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "情境模式聚合失敗");
        }
    }

    /// <summary>
    /// 從目標文字萃取關鍵字（簡單版：移除停用詞 + 取重要詞）。
    /// </summary>
    internal static string ExtractKeywords(string goal, int maxKeywords = 15)
    {
        if (string.IsNullOrWhiteSpace(goal))
        {
            return "";
        }

        var words = goal
            .Split(
                [' ', ',', '.', '，', '。', '、', '：', '？', '！', '\n', '\r', '(', ')', '[', ']'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && !StopWordsEn.Contains(w) && !StopWordsCn.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxKeywords);

        return string.Join(" ", words);
    }

    /// <summary>格式化實體記憶為 prompt 注入文字。</summary>
    internal static string FormatEntityMemory(List<EntityMemoryDocument> entities)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Known Entities (from previous sessions)");
        foreach (var e in entities)
        {
            sb.Append($"- **{e.EntityName}** ({e.EntityType}): ");
            try
            {
                var facts = JsonSerializer.Deserialize<List<string>>(e.Facts);
                if (facts is { Count: > 0 })
                {
                    sb.AppendLine(string.Join("; ", facts.Take(3)));
                }
                else
                {
                    sb.AppendLine("(no facts)");
                }
            }
            catch
            {
                sb.AppendLine(e.Facts[..Math.Min(e.Facts.Length, 100)]);
            }
        }

        return sb.ToString();
    }

    /// <summary>格式化情境記憶為 prompt 注入文字。</summary>
    internal static string FormatContextualMemory(List<ContextualMemoryDocument> patterns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## User Patterns (observed from history)");
        foreach (var p in patterns)
        {
            sb.AppendLine($"- [{p.PatternType}] {p.Description} (confidence: {p.Confidence:F1})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 用 LLM 產生 Reflexion 風格的反思。
    /// </summary>
    private static async Task<(string Reflection, string KeyInsights)> GenerateReflectionAsync(
        IChatClient client, string goal, bool succeeded,
        string toolSequence, int stepCount, CancellationToken ct)
    {
        var status = succeeded ? "successfully completed" : "failed to complete";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                """
                You are an experience analyst. Given an AI agent's execution summary,
                produce a brief reflection and key insights for future similar tasks.

                Output JSON:
                {
                  "reflection": "1-2 sentence summary of what happened and what to do differently next time",
                  "insights": ["insight 1", "insight 2", "insight 3"]
                }

                Keep insights actionable and specific. Max 3 insights.
                Respond in the same language as the goal.
                """),
            new(ChatRole.User,
                $"Goal: {goal}\nStatus: {status}\nTools used: {toolSequence}\nSteps taken: {stepCount}")
        };

        var response = await client.GetResponseAsync(messages, cancellationToken: ct);
        var text = response.Text ?? "";

        // 嘗試解析 JSON
        try
        {
            // 剝離 markdown fence
            if (text.Contains("```"))
            {
                var start = text.IndexOf('{');
                var end = text.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    text = text[start..(end + 1)];
                }
            }

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var reflection = root.TryGetProperty("reflection", out var r) ? r.GetString() ?? "" : "";
            var insights = "[]";
            if (root.TryGetProperty("insights", out var ins))
            {
                insights = ins.GetRawText();
            }

            return (reflection, insights);
        }
        catch
        {
            // JSON 解析失敗，用原始文字作為 reflection
            var fallback = text.Length > MaxReflectionLength ? text[..MaxReflectionLength] : text;
            return (fallback, "[]");
        }
    }
}
