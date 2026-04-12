using System.Text.Json;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// Skill 註冊表服務：管理所有可供 Agent / Flow 使用的內建 Skill。
/// 內建 Skill 從 Data/built-in-skills/ 載入（結構 + 三語版本）。
/// </summary>
public class SkillRegistryService
{
    private readonly Dictionary<string, SkillDefinition> _registry = new();

    /// <summary>每個 locale 的 Skill 語言包（displayName / description / instructions）。</summary>
    private readonly Dictionary<string, Dictionary<string, SkillLocaleEntry>> _locales = new();

    private static readonly string[] SupportedLocales = Locales.Supported;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public SkillRegistryService()
    {
        LoadFromFiles();
    }

    /// <summary>
    /// 從 Data/built-in-skills/ 載入結構定義 + 三語版本。
    /// 預設用 zh-TW 作為 _registry 的 fallback 語言。
    /// </summary>
    private void LoadFromFiles()
    {
        var basePath = Path.Combine("Data", "built-in-skills");
        var structurePath = Path.Combine(basePath, "skills.json");

        if (!File.Exists(structurePath))
        {
            // 開發環境可能在不同路徑
            return;
        }

        // 1. 載入結構定義
        var structureJson = File.ReadAllText(structurePath);
        var structures = JsonSerializer.Deserialize<List<SkillStructure>>(structureJson, JsonOptions) ?? [];

        // 2. 載入每個 locale 的語言包
        foreach (var locale in SupportedLocales)
        {
            var localePath = Path.Combine(basePath, "locales", locale, "skills.json");
            if (!File.Exists(localePath)) continue;

            var localeJson = File.ReadAllText(localePath);
            var entries = JsonSerializer.Deserialize<Dictionary<string, SkillLocaleEntry>>(localeJson, JsonOptions);
            if (entries is not null)
            {
                _locales[locale] = entries;
            }
        }

        // 3. 用 zh-TW 作為 _registry 的預設語言（向下相容）
        var defaultLocale = _locales.GetValueOrDefault("zh-TW")
                            ?? _locales.GetValueOrDefault("en")
                            ?? new Dictionary<string, SkillLocaleEntry>();

        foreach (var s in structures)
        {
            var locale = defaultLocale.GetValueOrDefault(s.Id);
            var category = Enum.TryParse<SkillCategory>(s.Category, true, out var cat)
                ? cat
                : SkillCategory.DomainKnowledge;

            Register(new SkillDefinition(
                s.Id,
                locale?.DisplayName ?? s.Id,
                locale?.Description ?? "",
                locale?.Instructions ?? "",
                category,
                s.Icon ?? "&#x1F3AF;",
                s.Tools));
        }
    }

    /// <summary>
    /// 取得指定 locale 版本的 Skill 定義。
    /// 若該 locale 無對應翻譯，fallback 到 registry 預設版本。
    /// </summary>
    public SkillDefinition? GetLocalized(string id, string locale)
    {
        if (!_registry.TryGetValue(id, out var baseSkill))
            return null;

        if (_locales.TryGetValue(locale, out var localeEntries)
            && localeEntries.TryGetValue(id, out var entry))
        {
            return baseSkill with
            {
                DisplayName = entry.DisplayName ?? baseSkill.DisplayName,
                Description = entry.Description ?? baseSkill.Description,
                Instructions = entry.Instructions ?? baseSkill.Instructions,
            };
        }

        return baseSkill;
    }

    /// <summary>
    /// 根據 Skill ID 清單取得指定 locale 的多個定義（內建 + 自訂）。
    /// </summary>
    public List<SkillDefinition> Resolve(IEnumerable<string> skillIds, List<Data.SkillDocument>? customSkills, string locale = "zh-TW")
    {
        var results = new List<SkillDefinition>();
        foreach (var id in skillIds)
        {
            if (_registry.ContainsKey(id))
            {
                var localized = GetLocalized(id, locale);
                if (localized is not null) results.Add(localized);
            }
            else
            {
                var custom = customSkills?.FirstOrDefault(s => s.Id == id);
                if (custom is not null)
                {
                    results.Add(ToDefinition(custom));
                }
            }
        }

        return results;
    }

    public void Register(SkillDefinition skill)
    {
        _registry[skill.Id] = skill;
    }

    // ─── 內建 Skill 查詢（不含自訂）───

    /// <summary>
    /// 根據 Skill ID 取得內建定義。
    /// </summary>
    public SkillDefinition? GetById(string id) =>
        _registry.TryGetValue(id, out var skill) ? skill : null;

    /// <summary>
    /// 根據 Skill ID 清單取得多個內建定義。
    /// </summary>
    public List<SkillDefinition> Resolve(IEnumerable<string> skillIds) =>
        skillIds
            .Where(_registry.ContainsKey)
            .Select(id => _registry[id])
            .ToList();

    /// <summary>
    /// 取得所有內建 Skill。
    /// </summary>
    public IReadOnlyList<SkillDefinition> GetAvailableSkills() =>
        _registry.Values.ToList();

    /// <summary>
    /// 按分類取得所有內建 Skill（供 UI 分組顯示）。
    /// </summary>
    public IReadOnlyDictionary<SkillCategory, List<SkillDefinition>> GetByCategory() =>
        _registry.Values
            .GroupBy(s => s.Category)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.DisplayName).ToList());

    // ─── 合併內建 + 自訂 Skill ───

    /// <summary>
    /// 將 SkillDocument（自訂）轉換為 SkillDefinition。
    /// </summary>
    public static SkillDefinition ToDefinition(Data.SkillDocument doc) =>
        new(doc.Id, doc.Name, doc.Description, doc.Instructions,
            Enum.TryParse<SkillCategory>(doc.Category, out var cat) ? cat : SkillCategory.DomainKnowledge,
            doc.Icon,
            doc.GetTools() is { Count: > 0 } tools ? tools : null);

    /// <summary>
    /// 根據 Skill ID 取得定義（內建 + 自訂）。
    /// </summary>
    public SkillDefinition? GetById(string id, List<Data.SkillDocument>? customSkills)
    {
        if (_registry.TryGetValue(id, out var builtin))
        {
            return builtin;
        }

        var custom = customSkills?.FirstOrDefault(s => s.Id == id);
        return custom is not null ? ToDefinition(custom) : null;
    }

    /// <summary>
    /// 根據 Skill ID 清單取得多個定義（內建 + 自訂）。使用預設 locale。
    /// </summary>
    public List<SkillDefinition> Resolve(IEnumerable<string> skillIds, List<Data.SkillDocument>? customSkills)
        => Resolve(skillIds, customSkills, "zh-TW");

    /// <summary>
    /// 取得所有 Skill（內建 + 自訂），按分類分組。
    /// </summary>
    public IReadOnlyDictionary<SkillCategory, List<SkillDefinition>> GetByCategory(List<Data.SkillDocument>? customSkills)
    {
        var all = _registry.Values.AsEnumerable();
        if (customSkills is { Count: > 0 })
        {
            all = all.Concat(customSkills.Select(ToDefinition));
        }

        return all
            .GroupBy(s => s.Category)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.DisplayName).ToList());
    }

    // ─── 內部 DTO ───

    private record SkillStructure
    {
        public string Id { get; init; } = "";
        public string Category { get; init; } = "domainKnowledge";
        public string? Icon { get; init; }
        public List<string>? Tools { get; init; }
    }

    internal record SkillLocaleEntry
    {
        public string? DisplayName { get; init; }
        public string? Description { get; init; }
        public string? Instructions { get; init; }
    }
}
