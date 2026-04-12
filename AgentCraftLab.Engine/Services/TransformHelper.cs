using System.Text.Json;
using System.Text.RegularExpressions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Models.Schema;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 共用的文字轉換工具。供 Code 節點和 Workflow Hook 複用。
/// </summary>
public static class TransformHelper
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 腳本執行 delegate — 由 AgentCraftLab.Script 的 AddScript() 設定。
    /// 未設定時 "script" transformType 回傳錯誤提示。
    /// </summary>
    internal static Func<string, string, string>? ScriptExecutor { get; set; }

    /// <summary>
    /// 當前 Workflow 變數 JSON（Code 節點 script 模式用）。
    /// Script 引擎可透過此值注入 $variables 到腳本環境。
    /// 由 CodeNodeExecutor 在執行前設定，執行後清除。
    /// </summary>
    [ThreadStatic]
    internal static string? CurrentVariablesJson;

    /// <summary>
    /// 多語言腳本執行 delegate — (language, code, input) → output。
    /// 由 AddMultiLanguageScript() 設定。優先使用此 delegate，fallback 到 ScriptExecutor。
    /// </summary>
    internal static Func<string, string, string, string>? MultiLanguageScriptExecutor { get; set; }

    private static string ExecuteScript(string code, string input, string language = "javascript")
    {
        // 注入 $variables 到腳本環境
        if (CurrentVariablesJson is not null)
        {
            if (language is "javascript" or "js")
            {
                code = $"const $variables = {CurrentVariablesJson};\n{code}";
            }
            else if (language is "csharp" or "cs")
            {
                var escaped = CurrentVariablesJson.Replace("\\", "\\\\").Replace("\"", "\\\"");
                code = $"var _variables = JsonSerializer.Deserialize<Dictionary<string, string>>(\"{escaped}\") ?? new();\n{code}";
            }
        }

        if (MultiLanguageScriptExecutor is not null)
        {
            return MultiLanguageScriptExecutor(language, code, input);
        }

        if (ScriptExecutor is null)
        {
            return "[Script engine not configured. Call AddScript() to enable script execution.]";
        }
        return ScriptExecutor(code, input);
    }

    /// <summary>
    /// 強型別入口 — 接受 <see cref="TransformKind"/> enum + <see cref="ScriptLanguage"/>。
    /// </summary>
    public static string ApplyTransform(
        TransformKind kind, string input, string? expression = null,
        string? replacement = null, int maxLength = 0, string? delimiter = null, int splitIndex = 0,
        ScriptLanguage? language = null)
    {
        var typeString = kind switch
        {
            TransformKind.Template => "template",
            TransformKind.Regex => "regex-replace",
            TransformKind.JsonPath => "json-path",
            TransformKind.Trim or TransformKind.Truncate => "trim",
            TransformKind.Split => "split-take",
            TransformKind.Upper => "upper",
            TransformKind.Lower => "lower",
            TransformKind.Script => "script",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown TransformKind")
        };
        var langString = language switch
        {
            ScriptLanguage.JavaScript => "javascript",
            ScriptLanguage.CSharp => "csharp",
            _ => null
        };
        return ApplyTransform(typeString, input, expression, expression, replacement, maxLength, delimiter, splitIndex, langString);
    }

    /// <summary>
    /// 舊字串入口（向下相容）：9 種模式 — template/regex-extract/regex-replace/json-path/trim/split-take/upper/lower/script。
    /// </summary>
    public static string ApplyTransform(
        string transformType, string input, string? template = null, string? pattern = null,
        string? replacement = null, int maxLength = 0, string? delimiter = null, int splitIndex = 0,
        string? scriptLanguage = null)
    {
        try
        {
            return transformType switch
            {
                "template" => ApplyTemplate(template ?? "{{input}}", input),
                "regex-extract" => ExtractRegex(input, pattern),
                "regex-replace" => string.IsNullOrEmpty(pattern)
                    ? input
                    : Regex.Replace(input, pattern, replacement ?? "", RegexOptions.None, RegexTimeout),
                "json-path" => ExtractJsonPath(input, pattern),
                "trim" => maxLength > 0 && input.Length > maxLength
                    ? input[..maxLength]
                    : input,
                "split-take" => SplitAndTake(input, delimiter, splitIndex),
                "upper" => input.ToUpperInvariant(),
                "lower" => input.ToLowerInvariant(),
                "script" => ExecuteScript(template ?? "", input, scriptLanguage ?? "javascript"),
                _ => input
            };
        }
        catch (Exception ex)
        {
            return $"[Code transform error: {ex.Message}]";
        }
    }

    /// <summary>
    /// Template 引擎 — 支援 {{input}} 簡單替換 + {{#each input}}...{{/each}} 迭代 JSON array。
    /// </summary>
    private static string ApplyTemplate(string template, string input)
    {
        // 簡單替換（無 {{#each}}）
        if (!template.Contains("{{#each"))
        {
            return template.Replace("{{input}}", input);
        }

        // {{#each input}}...{{/each}} 迭代
        var eachStart = template.IndexOf("{{#each", StringComparison.Ordinal);
        var bodyStart = template.IndexOf("}}", eachStart, StringComparison.Ordinal) + 2;
        var eachEnd = template.IndexOf("{{/each}}", bodyStart, StringComparison.Ordinal);

        if (eachEnd < 0) return EachFallback(template, input);

        var before = template[..eachStart].Replace("{{input}}", input);
        var body = template[bodyStart..eachEnd];
        var after = template[(eachEnd + 9)..].Replace("{{input}}", input);

        // 解析 JSON array（先嘗試整段 input，失敗則從文字中提取 [...] 區段）
        var jsonInput = ExtractJsonArray(input);
        if (jsonInput is null)
            return EachFallback(template, input);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonInput);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                return EachFallback(template, input);

            var sb = new System.Text.StringBuilder();
            sb.Append(before);

            // 從 body 提取所有 {{this.xxx}} 佔位符名稱，用於 fuzzy match
            var placeholders = ExtractPlaceholderNames(body);

            var index = 0;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var line = body;

                // {{this.propertyName}} 替換（exact match 優先，fallback fuzzy match）
                foreach (var prop in element.EnumerateObject())
                {
                    var exact = $"{{{{this.{prop.Name}}}}}";
                    if (line.Contains(exact))
                    {
                        line = line.Replace(exact, prop.Value.ToString());
                    }
                    else
                    {
                        // fuzzy match：忽略大小寫和底線/連字號差異
                        var normalizedProp = NormalizeName(prop.Name);
                        foreach (var ph in placeholders)
                        {
                            if (NormalizeName(ph) == normalizedProp)
                            {
                                line = line.Replace($"{{{{this.{ph}}}}}", prop.Value.ToString());
                                break;
                            }
                        }
                    }
                }

                // {{this}} 替換（整個元素）
                line = line.Replace("{{this}}", element.ToString());

                // {{@index}} 替換
                line = line.Replace("{{@index}}", index.ToString());

                sb.Append(line);
                index++;
            }

            sb.Append(after);
            return sb.ToString();
        }
        catch
        {
            return EachFallback(template, input);
        }
    }

    /// <summary>
    /// 從文字中提取 JSON array。先嘗試整段 parse，失敗則找第一個 '[' 到最後一個 ']' 的區段。
    /// 處理 LLM 常見的「說明文字 + ```json [...] ``` + 說明文字」格式。
    /// </summary>
    private static string? ExtractJsonArray(string input)
    {
        // 嘗試直接解析
        var trimmed = input.Trim();
        if (trimmed.StartsWith('['))
            return trimmed;

        // 從文字中提取 [...] 區段
        var start = input.IndexOf('[');
        var end = input.LastIndexOf(']');
        if (start >= 0 && end > start)
            return input[start..(end + 1)];

        return null;
    }

    /// <summary>
    /// {{#each}} fallback — 若 template 有 {{input}} 則替換，否則直接回傳 input 資料。
    /// 避免回傳含未解析 Handlebars 語法的原始 template。
    /// </summary>
    private static string EachFallback(string template, string input)
    {
        if (template.Contains("{{input}}"))
            return template.Replace("{{input}}", input);

        return input;
    }

    /// <summary>
    /// 從 template body 提取所有 {{this.xxx}} 的 xxx 名稱。
    /// </summary>
    private static List<string> ExtractPlaceholderNames(string body)
    {
        var names = new List<string>();
        var searchFrom = 0;
        while (true)
        {
            var start = body.IndexOf("{{this.", searchFrom, StringComparison.Ordinal);
            if (start < 0) break;
            var nameStart = start + 7; // skip "{{this."
            var end = body.IndexOf("}}", nameStart, StringComparison.Ordinal);
            if (end < 0) break;
            names.Add(body[nameStart..end]);
            searchFrom = end + 2;
        }
        return names;
    }

    /// <summary>
    /// 正規化屬性名稱：轉小寫、移除底線和連字號。
    /// 使 english_name / englishName / English_Name 都匹配。
    /// </summary>
    private static string NormalizeName(string name)
        => name.ToLowerInvariant().Replace("_", "").Replace("-", "");

    private static string ExtractRegex(string input, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return input;
        }

        var match = Regex.Match(input, pattern, RegexOptions.None, RegexTimeout);
        if (!match.Success)
        {
            return "";
        }

        return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
    }

    private static string ExtractJsonPath(string input, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return input;
        }

        try
        {
            using var doc = JsonDocument.Parse(input);
            var segments = path.TrimStart('$', '.').Split('.');
            JsonElement current = doc.RootElement;
            foreach (var seg in segments)
            {
                if (int.TryParse(seg, out var index))
                {
                    current = current[index];
                }
                else
                {
                    current = current.GetProperty(seg);
                }
            }

            return current.ValueKind == JsonValueKind.String ? current.GetString()! : current.GetRawText();
        }
        catch
        {
            return $"[JSON path '{path}' not found]";
        }
    }

    private static string SplitAndTake(string input, string? delimiter, int index)
    {
        var delim = string.IsNullOrEmpty(delimiter) ? "\n" : delimiter;
        var parts = input.Split(delim);
        return index >= 0 && index < parts.Length ? parts[index] : input;
    }
}
