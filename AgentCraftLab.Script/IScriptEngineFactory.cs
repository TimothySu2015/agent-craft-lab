namespace AgentCraftLab.Script;

/// <summary>
/// 多語言腳本引擎工廠 — 根據語言名稱分派到對應的 IScriptEngine 實作。
/// </summary>
public interface IScriptEngineFactory
{
    /// <summary>
    /// 取得指定語言的腳本引擎。
    /// </summary>
    /// <param name="language">語言名稱："javascript" 或 "csharp"。</param>
    IScriptEngine GetEngine(string language);

    /// <summary>支援的語言清單。</summary>
    IReadOnlyList<string> SupportedLanguages { get; }
}

/// <summary>
/// 預設多語言腳本引擎工廠。
/// </summary>
public sealed class ScriptEngineFactory : IScriptEngineFactory
{
    private readonly Dictionary<string, IScriptEngine> _engines = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> SupportedLanguages => _engines.Keys.ToList();

    public ScriptEngineFactory Register(string language, IScriptEngine engine)
    {
        _engines[language] = engine;
        return this;
    }

    public IScriptEngine GetEngine(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            language = "javascript";
        }

        if (_engines.TryGetValue(language, out var engine))
        {
            return engine;
        }

        // 別名支援
        var normalized = language.ToLowerInvariant() switch
        {
            "js" => "javascript",
            "c#" or "cs" or "dotnet" => "csharp",
            _ => language,
        };

        if (_engines.TryGetValue(normalized, out engine))
        {
            return engine;
        }

        throw new NotSupportedException(
            $"Script language '{language}' is not supported. Available: {string.Join(", ", _engines.Keys)}");
    }
}
