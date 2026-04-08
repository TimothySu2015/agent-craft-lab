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
    /// 註冊多語言腳本引擎（Jint JS + Roslyn C#）+ IScriptEngineFactory。
    /// 取代 AddScript()，同時支援 JavaScript 和 C# 腳本執行。
    /// </summary>
    public static IServiceCollection AddMultiLanguageScript(
        this IServiceCollection services,
        ScriptOptions? defaultOptions = null)
    {
        var jintEngine = new JintScriptEngine(defaultOptions);
        var roslynEngine = new RoslynScriptEngine(defaultOptions);

        var factory = new ScriptEngineFactory()
            .Register("javascript", jintEngine)
            .Register("csharp", roslynEngine);

        services.AddSingleton<IScriptEngineFactory>(factory);

        // 向後相容：IScriptEngine 預設仍回傳 Jint（既有程式碼不受影響）
        services.AddSingleton<IScriptEngine>(jintEngine);

        // 多語言 TransformHelper 整合
        TransformHelper.MultiLanguageScriptExecutor = (language, code, input) =>
        {
            var engine = factory.GetEngine(language);
            var result = engine.ExecuteAsync(code, input).GetAwaiter().GetResult();
            return result.Success ? result.Output : $"[Script error: {result.Error}]";
        };

        // 向後相容 ScriptExecutor（fallback）
        TransformHelper.ScriptExecutor = (code, input) =>
        {
            var result = jintEngine.ExecuteAsync(code, input).GetAwaiter().GetResult();
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

        // Roslyn 預熱（背景執行，避免阻塞啟動）
        var factory = provider.GetService<IScriptEngineFactory>();
        if (factory is not null)
        {
            Task.Run(RoslynScriptEngine.Warmup);
        }

        var logger = provider.GetService<ILogger<JintScriptEngine>>();
        var languages = factory?.SupportedLanguages is { Count: > 0 } langs
            ? string.Join(", ", langs)
            : "JavaScript";
        logger?.LogInformation("Script engine registered ({Languages})", languages);
    }
}
