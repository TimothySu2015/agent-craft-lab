using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Autonomous.Services;
using AgentCraftLab.Data;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Autonomous.Extensions;

/// <summary>
/// DI 註冊擴展 — 一行加入 Autonomous Agent 服務。
/// </summary>
public static class AutonomousExtensions
{
    /// <summary>
    /// 最小化註冊：僅核心執行引擎（ReactExecutor + AgentFactory），
    /// 不含 Engine 橋接層（IAutonomousNodeExecutor）。
    /// 適用於單元測試或獨立 CLI 應用。
    /// </summary>
    public static IServiceCollection AddAutonomousAgentCore(this IServiceCollection services)
    {
        services.AddSingleton<ReactExecutorConfig>();
        services.AddSingleton<IToolDelegationStrategy, SafeWhitelistToolDelegation>();
        // 反思引擎：Single + Panel + Auto 三種模式
        services.AddSingleton<AuditorReflectionEngine>();
        services.AddSingleton<MultiAgentReflectionEngine>();
        services.AddSingleton<AutoReflectionEngine>();
        services.AddSingleton<IReflectionEngine>(sp => sp.GetRequiredService<AutoReflectionEngine>());
        services.AddSingleton<IStepEvaluator, RuleBasedStepEvaluator>();
        services.AddSingleton<IBudgetPolicy, DefaultBudgetPolicy>();
        services.AddSingleton<IHistoryManager, HybridHistoryManager>();
        services.AddSingleton<SystemPromptBuilder>();
        services.AddScoped<AgentFactory>();
        services.AddScoped<IHumanInteractionHandler>(sp =>
        {
            var bridge = sp.GetService<HumanInputBridge>();
            var logger = sp.GetRequiredService<ILogger<BridgeHumanInteractionHandler>>();
            return new BridgeHumanInteractionHandler(bridge, logger);
        });
        services.AddScoped<ReactExecutor>();
        services.AddScoped<IGoalExecutor>(sp => sp.GetRequiredService<ReactExecutor>());
        services.AddSingleton<IExecutionCheckpoint, InMemoryExecutionCheckpoint>();

        // 檢查點管理器（依賴 ICheckpointStore，由 Engine 層提供；config 控制是否啟用）
        services.AddSingleton<CheckpointManager>(sp =>
        {
            var store = sp.GetRequiredService<ICheckpointStore>();
            var config = sp.GetRequiredService<ReactExecutorConfig>();
            var logger = sp.GetRequiredService<ILogger<CheckpointManager>>();
            return new CheckpointManager(store, config, logger);
        });

        // 跨 Session 記憶服務（依賴 IExecutionMemoryStore，由 Engine/Commercial 層提供）
        // 使用 TryAdd 避免重複註冊；若 IExecutionMemoryStore 未註冊，ReactExecutor 會收到 null
        services.AddSingleton<IExecutionMemoryService, ExecutionMemoryService>();

        // 集中監控指標收集器 — Singleton，跨所有 ReactExecutor 實例共享統計
        services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();

        return services;
    }

    /// <summary>
    /// 完整註冊：核心引擎 + Engine 橋接層（IAutonomousNodeExecutor）。
    /// 需要先呼叫 AddAgentCraftEngine() 註冊 Engine 核心服務。
    /// </summary>
    public static IServiceCollection AddAutonomousAgent(this IServiceCollection services)
    {
        services.AddAutonomousAgentCore();
        services.AddScoped<IAutonomousNodeExecutor, AutonomousNodeAdapter>();
        return services;
    }
}
