using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Script;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 註冊 Jint JavaScript 沙箱引擎 + 設定 TransformHelper 的 script 支援。
    /// 呼叫後需在 app build 完成後呼叫 <see cref="UseScriptTools"/> 完成工具掛載。
    /// </summary>
    public static IServiceCollection AddScript(
        this IServiceCollection services,
        ScriptOptions? defaultOptions = null)
    {
        var engine = new JintScriptEngine(defaultOptions);
        services.AddSingleton<IScriptEngine>(engine);

        // TransformHelper 整合 — 讓 Code 節點的 "script" transformType 可用
        TransformHelper.ScriptExecutor = (code, input) =>
        {
            var result = engine.ExecuteAsync(code, input).GetAwaiter().GetResult();
            return result.Success ? result.Output : $"[Script error: {result.Error}]";
        };

        return services;
    }

    /// <summary>
    /// 將腳本工具掛載到 ToolRegistryService。在 app build 完成後呼叫。
    /// </summary>
    public static void UseScriptTools(this IServiceProvider provider)
    {
        var scriptEngine = provider.GetService<IScriptEngine>();
        if (scriptEngine is null)
        {
            return;
        }

        var registry = provider.GetRequiredService<ToolRegistryService>();
        registry.RegisterScriptTools(scriptEngine);

        var logger = provider.GetService<ILogger<JintScriptEngine>>();
        logger?.LogInformation("Script engine registered (Jint JavaScript sandbox)");
    }
}
