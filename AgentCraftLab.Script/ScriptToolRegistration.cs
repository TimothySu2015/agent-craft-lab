using System.ComponentModel;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Script;

/// <summary>
/// 腳本工具註冊 — 將 script_execute 工具掛到 ToolRegistryService。
/// </summary>
public static class ScriptToolRegistration
{
    public static void RegisterScriptTools(this ToolRegistryService registry, IScriptEngine scriptEngine)
    {
        registry.Register("script_execute", "Script - Execute JavaScript",
            "在沙箱中執行 JavaScript 腳本，處理資料轉換、格式化、計算等任務。input 變數包含前一步的輸出，用 result 變數設定輸出結果。",
            () => AIFunctionFactory.Create(
                ([Description("JavaScript 程式碼。可用 `input` 變數取得輸入文字，設定 `result` 變數作為輸出。")] string code,
                 [Description("輸入文字（如前一步的輸出）")] string input) =>
                    ExecuteScriptAsync(code, input, scriptEngine),
                name: "ScriptExecute",
                description: "在安全沙箱中執行 JavaScript 腳本"),
            ToolCategory.Data, "\U0001F4DC");
    }

    internal static async Task<string> ExecuteScriptAsync(string code, string input, IScriptEngine engine)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "Error: code is required.";
        }

        var result = await engine.ExecuteAsync(code, input);

        if (!result.Success)
        {
            return $"Script error: {result.Error}";
        }

        return result.Output;
    }
}
