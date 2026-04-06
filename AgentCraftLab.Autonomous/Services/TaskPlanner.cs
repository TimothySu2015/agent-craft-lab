using AgentCraftLab.Autonomous.Models;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 任務規劃器 — 在 ReAct 迴圈前產生輕量執行計劃。
/// 只在複雜任務時啟用，簡單任務跳過。
/// </summary>
public sealed class TaskPlanner
{
    /// <summary>計劃文字的最短有效長度。</summary>
    private const int MinPlanLength = 20;

    private readonly ReactExecutorConfig _config;

    public TaskPlanner(ReactExecutorConfig? config = null)
    {
        _config = config ?? new ReactExecutorConfig();
    }
    /// <summary>
    /// 產生執行計劃。回傳計劃文字（直接注入 system prompt），或 null 表示不需要計劃。
    /// </summary>
    public async Task<string?> GeneratePlanAsync(
        IChatClient client,
        string goal,
        IList<AITool> tools,
        CancellationToken cancellationToken)
    {
        // 只有複雜目標才需要規劃
        if (!SystemPromptBuilder.IsComplexGoal(goal))
        {
            return null;
        }

        // 最多列 20 個工具名，提供給規劃器參考
        var toolNames = string.Join(", ", tools
            .OfType<AIFunction>()
            .Select(f => f.Name)
            .Take(_config.PlanMaxToolsInPrompt));

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                """
                You are a task planner. Given a goal and available tools, produce a concise execution plan.
                Output a short numbered list (3-6 steps max). Each step should be one sentence.
                Focus on the logical order of actions and which tools to use.
                Do NOT execute anything — only plan.
                Respond in the same language as the goal.

                IMPORTANT — Parallel execution strategy:
                When the goal involves researching/comparing 2+ independent entities (companies, countries, platforms, etc.),
                the plan MUST use spawn_sub_agent for parallel research. Example:
                  "1. Use spawn_sub_agent to research Entity A, B, C in parallel (each with AzureWebSearch tool)."
                  "2. Use collect_results to gather all research data."
                  "3. Synthesize and compare the results."
                Do NOT plan sequential searches when parallel spawn is possible.
                """),
            new(ChatRole.User,
                $"Goal: {goal}\n\nAvailable tools: {toolNames}\n\nProduce a concise plan.")
        };

        try
        {
            var options = new ChatOptions { MaxOutputTokens = _config.PlanMaxOutputTokens };
            var response = await client.GetResponseAsync(messages, options, cancellationToken);
            var plan = response.Text;
            if (string.IsNullOrWhiteSpace(plan) || plan.Length < MinPlanLength)
            {
                return null;
            }

            // 截斷過長的計劃（防止 LLM 生成過多內容）
            if (plan.Length > _config.PlanMaxLength)
            {
                plan = plan[.._config.PlanMaxLength] + "...";
            }

            return plan;
        }
        catch
        {
            // 規劃失敗不應阻斷執行，靜默跳過
            return null;
        }
    }

    /// <summary>
    /// 動態重規劃：根據目前進度，評估是否需要調整策略。
    /// 只在進度達 50% 以上時才考慮重規劃，避免過早干預。
    /// 回傳新計劃（如果需要調整），或 null（維持現有計劃）。
    /// </summary>
    public async Task<string?> ReplanAsync(
        IChatClient client,
        string originalGoal,
        string currentProgress,
        int currentStep,
        int maxSteps,
        CancellationToken cancellationToken)
    {
        // 只在進度達 50% 以上時才考慮重規劃
        if (currentStep < maxSteps / 2)
        {
            return null;
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                """
                You are a task replanner. Given the original goal and current progress,
                assess if the current approach is working. If not, suggest a revised plan.
                If the current approach is fine, respond with exactly: "PLAN_OK"
                Otherwise, provide a revised numbered list (3-5 steps) for the remaining work.
                Respond in the same language as the goal.
                """),
            new(ChatRole.User,
                $"Original goal: {originalGoal}\n\n" +
                $"Progress so far (step {currentStep}/{maxSteps}):\n{currentProgress}\n\n" +
                "Should the plan be revised?")
        };

        try
        {
            var options = new ChatOptions { MaxOutputTokens = _config.PlanMaxOutputTokens };
            var response = await client.GetResponseAsync(messages, options, cancellationToken);
            var plan = response.Text;
            if (string.IsNullOrWhiteSpace(plan) || plan.Contains("PLAN_OK", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // 截斷過長的重規劃結果
            if (plan.Length > _config.PlanMaxLength)
            {
                plan = plan[.._config.PlanMaxLength] + "...";
            }

            return plan;
        }
        catch
        {
            // 重規劃失敗不阻斷執行，靜默跳過
            return null;
        }
    }
}
