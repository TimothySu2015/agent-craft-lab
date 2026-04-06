using AgentCraftLab.Autonomous.Services;
using AgentCraftLab.Script;

namespace AgentCraftLab.Api.Services;

/// <summary>
/// IScriptEngine → IToolCodeRunner 橋接器。
/// 讓 Autonomous 層的 create_tool meta-tool 可使用 Jint 沙箱執行自製工具。
/// </summary>
public sealed class ScriptEngineToolCodeRunner(IScriptEngine engine) : IToolCodeRunner
{
    public async Task<ToolCodeResult> ExecuteAsync(
        string code, string input, int timeoutSeconds = 3, CancellationToken ct = default)
    {
        var options = new ScriptOptions
        {
            TimeoutSeconds = timeoutSeconds,
            MemoryLimitMB = 20,    // 自製工具用更緊的限制
            MaxRecursion = 50,
            MaxStatements = 50_000
        };

        var result = await engine.ExecuteAsync(code, input, options, ct);
        return new ToolCodeResult(result.Success, result.Output, result.Error);
    }
}
