using System.Text.Json;
using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;

namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// GoalExecutionRequest ↔ AutonomousRequest 轉換器。
/// 共用欄位直接映射；ReactExecutor 特有設定透過 Options 字典傳遞。
/// </summary>
public static class GoalRequestConverter
{
    private const string KeyRisk = "react:risk";
    private const string KeyReflection = "react:reflection";
    private const string KeySharedState = "react:sharedStateInit";
    private const string KeyCraftMd = "react:craftMd";
    private const string KeyBudgetMaxInput = "react:maxInputTokens";
    private const string KeyBudgetMaxOutput = "react:maxOutputTokens";
    private const string KeyBudgetOnExceed = "react:budgetOnExceed";
    private const string KeyPerToolLimits = "react:perToolLimits";
    private const string KeyDefaultPerToolLimit = "react:defaultPerToolLimit";

    /// <summary>
    /// GoalExecutionRequest → AutonomousRequest（給 ReactExecutor 內部使用）。
    /// </summary>
    public static AutonomousRequest ToAutonomousRequest(GoalExecutionRequest source)
    {
        var result = new AutonomousRequest
        {
            ExecutionId = source.ExecutionId,
            UserId = source.UserId,
            Goal = source.Goal,
            Credentials = source.Credentials,
            Provider = source.Provider,
            Model = source.Model,
            AvailableTools = source.AvailableTools,
            AvailableSkills = source.AvailableSkills,
            McpServers = source.McpServers,
            A2AAgents = source.A2AAgents,
            HttpApis = source.HttpApis,
            MaxIterations = source.MaxIterations,
            Attachment = source.Attachment,
            Budget = new TokenBudget
            {
                MaxTotalTokens = source.MaxTotalTokens,
                MaxInputTokens = GetLong(source.Options, KeyBudgetMaxInput),
                MaxOutputTokens = GetLong(source.Options, KeyBudgetMaxOutput),
                OnExceed = GetEnum(source.Options, KeyBudgetOnExceed, BudgetExceededAction.Stop)
            },
            ToolLimits = new ToolCallLimits
            {
                MaxTotalCalls = source.MaxToolCalls,
                PerToolLimits = GetJson<Dictionary<string, int>>(source.Options, KeyPerToolLimits) ?? [],
                DefaultPerToolLimit = (int)GetLong(source.Options, KeyDefaultPerToolLimit, 10)
            },
            Risk = GetJson<RiskConfig>(source.Options, KeyRisk),
            Reflection = GetJson<ReflectionConfig>(source.Options, KeyReflection),
            SharedStateInit = GetJson<Dictionary<string, string>>(source.Options, KeySharedState),
            CraftMd = GetString(source.Options, KeyCraftMd)
        };

        return result;
    }

    /// <summary>
    /// AutonomousRequest → GoalExecutionRequest（給外部統一消費）。
    /// </summary>
    public static GoalExecutionRequest ToGoalRequest(AutonomousRequest source)
    {
        var options = new Dictionary<string, object>();

        if (source.Risk is not null)
            options[KeyRisk] = source.Risk;
        if (source.Reflection is not null)
            options[KeyReflection] = source.Reflection;
        if (source.SharedStateInit is not null)
            options[KeySharedState] = source.SharedStateInit;
        if (source.Budget.MaxInputTokens > 0)
            options[KeyBudgetMaxInput] = source.Budget.MaxInputTokens;
        if (source.Budget.MaxOutputTokens > 0)
            options[KeyBudgetMaxOutput] = source.Budget.MaxOutputTokens;
        if (source.Budget.OnExceed != BudgetExceededAction.Stop)
            options[KeyBudgetOnExceed] = source.Budget.OnExceed.ToString();
        if (source.ToolLimits.PerToolLimits.Count > 0)
            options[KeyPerToolLimits] = source.ToolLimits.PerToolLimits;
        if (source.ToolLimits.DefaultPerToolLimit != 10)
            options[KeyDefaultPerToolLimit] = source.ToolLimits.DefaultPerToolLimit;
        if (source.CraftMd is not null)
            options[KeyCraftMd] = source.CraftMd;

        return new GoalExecutionRequest
        {
            ExecutionId = source.ExecutionId,
            UserId = source.UserId,
            Goal = source.Goal,
            Credentials = source.Credentials,
            Provider = source.Provider,
            Model = source.Model,
            AvailableTools = source.AvailableTools,
            AvailableSkills = source.AvailableSkills,
            McpServers = source.McpServers,
            A2AAgents = source.A2AAgents,
            HttpApis = source.HttpApis,
            MaxIterations = source.MaxIterations,
            MaxTotalTokens = source.Budget.MaxTotalTokens,
            MaxToolCalls = source.ToolLimits.MaxTotalCalls,
            Attachment = source.Attachment,
            Options = options.Count > 0 ? options : null
        };
    }

    private static string? GetString(Dictionary<string, object>? options, string key)
    {
        if (options is null || !options.TryGetValue(key, out var val)) return null;
        return val switch
        {
            string s => s,
            JsonElement je => je.GetString(),
            _ => val.ToString()
        };
    }

    private static long GetLong(Dictionary<string, object>? options, string key, long defaultValue = 0)
    {
        if (options is null || !options.TryGetValue(key, out var val)) return defaultValue;
        return val switch
        {
            long l => l,
            int i => i,
            JsonElement je when je.TryGetInt64(out var jl) => jl,
            string s when long.TryParse(s, out var sl) => sl,
            _ => defaultValue
        };
    }

    private static T GetEnum<T>(Dictionary<string, object>? options, string key, T defaultValue) where T : struct, Enum
    {
        if (options is null || !options.TryGetValue(key, out var val)) return defaultValue;
        return val switch
        {
            T t => t,
            string s when Enum.TryParse<T>(s, true, out var e) => e,
            JsonElement je => Enum.TryParse<T>(je.GetString(), true, out var e2) ? e2 : defaultValue,
            _ => defaultValue
        };
    }

    private static T? GetJson<T>(Dictionary<string, object>? options, string key) where T : class
    {
        if (options is null || !options.TryGetValue(key, out var val)) return null;
        return val switch
        {
            T t => t,
            JsonElement je => je.Deserialize<T>(),
            _ => null
        };
    }
}
