namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 通用的 Skill Prompt 載入器。從 Data/skill-prompts/{skillName}/ 讀取 common.md + 模型專屬 .md 檔案。
/// 支援動態掃描（新增模型只需放入對應 .md 檔案，零程式碼修改）。
/// 檔案不存在時 fallback 到內建預設。
/// </summary>
public class SkillPromptProvider
{
    private readonly string _baseDir;

    /// <summary>
    /// 建立 Skill Prompt 載入器。
    /// </summary>
    /// <param name="baseDir">skill-prompts 根目錄（預設 Data/skill-prompts）。</param>
    public SkillPromptProvider(string? baseDir = null)
    {
        _baseDir = baseDir ?? Path.Combine("Data", "skill-prompts");
    }

    /// <summary>
    /// 載入指定 skill 的 prompt（common + provider/模型專屬）。
    /// </summary>
    /// <param name="skillName">Skill 名稱（對應子目錄名，如 "prompt-refiner"）。</param>
    /// <param name="model">模型名稱（如 "gpt-4o"、"claude-sonnet-4-6"）。</param>
    /// <param name="provider">Provider 名稱（如 "openai"、"anthropic"、"google"）。優先用 provider 匹配。</param>
    public string LoadPrompt(string skillName, string model, string? provider = null)
    {
        var skillDir = Path.Combine(_baseDir, skillName);
        var common = LoadFile(skillDir, "common.md") ?? GetDefaultPrompt(skillName, "common");
        var specific = FindSpecificFile(skillDir, provider, model) ?? GetDefaultPrompt(skillName, model);
        return string.IsNullOrEmpty(specific) ? common : common + "\n\n---\n\n" + specific;
    }

    /// <summary>
    /// 優先用 provider 匹配檔名，fallback 用 model 名稱匹配。
    /// 例：provider="openai" → openai.md 或 gpt.md；provider="anthropic" → anthropic.md 或 claude.md。
    /// </summary>
    private static string? FindSpecificFile(string skillDir, string? provider, string model)
    {
        if (!Directory.Exists(skillDir))
        {
            return null;
        }

        var files = Directory.GetFiles(skillDir, "*.md");

        // 1. 優先：provider 完全匹配（如 openai.md、anthropic.md）
        if (!string.IsNullOrEmpty(provider))
        {
            var providerLower = provider.ToLowerInvariant();
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                if (name != "common" && providerLower.Contains(name))
                {
                    return File.ReadAllText(file);
                }
            }

            // Provider 別名對照（azure-openai → gpt）
            var alias = ResolveProviderAlias(providerLower);
            if (alias is not null)
            {
                foreach (var file in files)
                {
                    var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    if (name != "common" && name == alias)
                    {
                        return File.ReadAllText(file);
                    }
                }
            }
        }

        // 2. Fallback：model 名稱包含檔名
        var modelLower = model.ToLowerInvariant();
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            if (name != "common" && modelLower.Contains(name))
            {
                return File.ReadAllText(file);
            }
        }

        return null;
    }

    /// <summary>Provider 別名對照表。</summary>
    private static string? ResolveProviderAlias(string provider) => provider switch
    {
        "azure-openai" or "azure_openai" => "gpt",
        "openai" => "gpt",
        "anthropic" => "claude",
        "google" => "gemini",
        _ => null,
    };

    /// <summary>從指定目錄讀取檔案，不存在時回傳 null。</summary>
    private static string? LoadFile(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>內建預設 prompt（檔案不存在時的 fallback）。</summary>
    private static string GetDefaultPrompt(string skill, string key)
    {
        // prompt-refiner 的內建預設
        if (skill == "prompt-refiner" && key == "common")
        {
            return DefaultPromptRefinerCommon;
        }

        return "";
    }

    private const string DefaultPromptRefinerCommon = """
        # Prompt Engineering 通用指南

        ## 核心原則
        1. **清晰指令**：使用結構化標籤（XML/Markdown）分隔角色、任務、約束、範例
        2. **定義角色**：明確模型扮演的角色和專長
        3. **具體任務**：使用清晰的動詞（總結、翻譯、分析、審查）
        4. **輸出格式**：指定想要的格式（JSON、Markdown、表格）
        5. **參考文本**：提供參考資料減少幻覺
        6. **拆解任務**：複雜任務分步驟執行
        7. **思維鏈**：要求先分析再回答（Chain of Thought）
        8. **範例驅動**：提供 1-5 個 Few-shot 範例展示期望輸出

        ## 結構化模板
        ```xml
        <role>角色定義</role>
        <context>背景資訊</context>
        <task>具體任務描述</task>
        <constraints>限制條件</constraints>
        <examples>範例</examples>
        <output_format>輸出格式要求</output_format>
        ```
        """;
}
