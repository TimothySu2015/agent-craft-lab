using System.Text;
using System.Text.Json;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.SchemaMapper;

/// <summary>
/// LLM 驅動的 Schema Mapper — 將清洗後文件 + Schema 丟給 LLM，產出結構化 JSON。
/// </summary>
public sealed class LlmSchemaMapper : ISchemaMapper
{
    private readonly ILlmProvider _llm;
    private readonly Action<string>? _onProgress;

    /// <summary>單次 prompt 的最大內容長度（字元數），超過時自動截斷</summary>
    private const int MaxContentChars = 80_000;

    public LlmSchemaMapper(ILlmProvider llm, Action<string>? onProgress = null)
    {
        _llm = llm;
        _onProgress = onProgress;
    }

    public async Task<SchemaMapperResult> MapAsync(
        IReadOnlyList<CleanedDocument> documents,
        SchemaDefinition schema,
        SchemaMapperOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new SchemaMapperOptions();

        _onProgress?.Invoke($"Mapping {documents.Count} files to {schema.Name}...");

        var systemPrompt = BuildSystemPrompt(schema, options);
        var userPrompt = BuildUserPrompt(documents, options);

        var llmResult = await _llm.CompleteAsync(systemPrompt, userPrompt, ct);

        _onProgress?.Invoke($"Parsing LLM response ({llmResult.InputTokens} in + {llmResult.OutputTokens} out)...");

        return ParseResponse(llmResult, documents.Count);
    }

    private static string BuildSystemPrompt(SchemaDefinition schema, SchemaMapperOptions options)
    {
        var sb = new StringBuilder();

        sb.AppendLine("你是一位專業的文件分析師。你的任務是從提供的文件內容中，擷取並整理出符合指定 Schema 的結構化 JSON。");
        sb.AppendLine();
        sb.AppendLine("## 規則");
        sb.AppendLine();
        sb.AppendLine("1. **嚴格遵守 JSON Schema**：輸出必須符合下方提供的 Schema 結構");
        sb.AppendLine("2. **只從提供的文件內容中擷取**：不要編造資料，找不到的欄位填 null");
        sb.AppendLine("3. **保持原始語言**：文件用什麼語言，輸出就用什麼語言");

        if (options.IncludeSourceReferences)
        {
            sb.AppendLine("4. **標注來源**：在 source 欄位標注資料來自哪份文件");
        }

        sb.AppendLine("5. **待確認問題**：資料不足或有矛盾時，加入 open_questions 陣列");
        sb.AppendLine("6. **輸出格式**：只輸出 JSON，不要加任何說明文字或 markdown 標記");

        if (options.OutputLanguage is not null)
        {
            sb.AppendLine($"7. **輸出語言**：使用 {options.OutputLanguage}");
        }

        sb.AppendLine();
        sb.AppendLine("## 目標文件類型");
        sb.AppendLine();
        sb.AppendLine($"**{schema.Name}**：{schema.Description}");

        if (schema.ExtractionGuidance is not null)
        {
            sb.AppendLine();
            sb.AppendLine("## 擷取指引");
            sb.AppendLine();
            sb.AppendLine(schema.ExtractionGuidance);
        }

        sb.AppendLine();
        sb.AppendLine("## JSON Schema");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(schema.JsonSchema);
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static string BuildUserPrompt(IReadOnlyList<CleanedDocument> documents, SchemaMapperOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("以下是需要分析的文件內容：");
        sb.AppendLine();

        var totalChars = 0;

        foreach (var doc in documents)
        {
            sb.AppendLine($"=== 文件：{doc.FileName} ===");
            sb.AppendLine();

            // 依元素類型分組輸出，讓 LLM 更容易理解結構
            foreach (var element in doc.Elements)
            {
                var prefix = element.Type switch
                {
                    ElementType.Title => "## ",
                    ElementType.ListItem => "- ",
                    ElementType.Table => "[表格]\n",
                    ElementType.CodeSnippet => "[程式碼]\n",
                    ElementType.Image or ElementType.FigureCaption => "[圖片] ",
                    _ => "",
                };

                var line = $"{prefix}{element.Text}";

                // 加上頁碼/投影片引用
                if (element.PageNumber.HasValue && options.IncludeSourceReferences)
                {
                    line += $" (p.{element.PageNumber})";
                }

                totalChars += line.Length;
                if (totalChars > MaxContentChars)
                {
                    sb.AppendLine();
                    sb.AppendLine("[... 內容過長，已截斷 ...]");
                    break;
                }

                sb.AppendLine(line);
            }

            sb.AppendLine();

            if (totalChars > MaxContentChars)
            {
                break;
            }
        }

        sb.AppendLine("=== 文件結束 ===");
        sb.AppendLine();
        sb.AppendLine("請依照 Schema 擷取並整理上述文件的內容，輸出 JSON。");

        return sb.ToString();
    }

    private static SchemaMapperResult ParseResponse(LlmResponse llmResult, int sourceCount)
    {
        var json = ExtractJson(llmResult.Text);

        var openQuestions = new List<string>();
        var missingFields = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("open_questions", out var oq) && oq.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in oq.EnumerateArray())
                {
                    if (q.GetString() is { } qs)
                    {
                        openQuestions.Add(qs);
                    }
                }
            }

            FindNullFields(root, "", missingFields);
        }
        catch (JsonException)
        {
            // JSON 解析失敗，回傳原始回應
        }

        return new SchemaMapperResult
        {
            Json = json,
            MissingFields = missingFields,
            OpenQuestions = openQuestions,
            SourceCount = sourceCount,
            TotalInputTokens = llmResult.InputTokens,
            TotalOutputTokens = llmResult.OutputTokens,
        };
    }

    private static string ExtractJson(string response)
    {
        var trimmed = response.Trim();

        // 移除 ```json ... ``` 包裝
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence > 0)
            {
                trimmed = trimmed[..lastFence];
            }
        }

        // 嘗試找到第一個 { 和最後一個 }
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return trimmed;
    }

    /// <summary>遞迴掃描 JSON 中值為 null 的欄位</summary>
    private static void FindNullFields(JsonElement element, string path, List<string> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var fieldPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
                if (prop.Value.ValueKind == JsonValueKind.Null)
                {
                    result.Add(fieldPath);
                }
                else
                {
                    FindNullFields(prop.Value, fieldPath, result);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in element.EnumerateArray())
            {
                FindNullFields(item, $"{path}[{i}]", result);
                i++;
            }
        }
    }
}
