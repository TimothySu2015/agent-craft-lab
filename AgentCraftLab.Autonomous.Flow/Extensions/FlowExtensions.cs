using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Autonomous.Flow.Testing;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCraftLab.Autonomous.Flow.Extensions;

/// <summary>
/// DI 註冊擴展 — 一行切換到 Flow 結構化模式。
/// </summary>
public static class FlowExtensions
{
    /// <summary>
    /// 註冊 Flow 結構化執行器（IGoalExecutor → FlowExecutor）。
    /// 取代 ReactExecutor 的 IGoalExecutor 註冊。
    /// 需要先呼叫 AddAgentCraftEngine() 註冊 Engine 核心服務。
    /// </summary>
    public static IServiceCollection AddAutonomousFlowAgent(this IServiceCollection services)
    {
        services.AddSingleton<WorkflowCrystallizer>();
        services.AddScoped<FlowAgentFactory>();
        services.AddScoped<FlowNodeRunner>();
        services.AddScoped<FlowExecutor>();
        services.AddScoped<IGoalExecutor>(sp => sp.GetRequiredService<FlowExecutor>());
        services.AddScoped<IAutonomousNodeExecutor, FlowNodeAdapter>();
        services.AddScoped<FlowTestRunner>();
        return services;
    }
}
