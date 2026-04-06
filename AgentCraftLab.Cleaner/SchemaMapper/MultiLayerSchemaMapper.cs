using System.Text;
using System.Text.Json;
using AgentCraftLab.Cleaner.Abstractions;
using AgentCraftLab.Cleaner.Elements;

namespace AgentCraftLab.Cleaner.SchemaMapper;

/// <summary>
/// 多層 Schema Mapper — 分層 Agent + Search 擷取結構化文件。
///
/// Layer 2（大綱規劃）：判斷 Schema 哪些區塊有資料 + 規劃搜尋關鍵字
/// Layer 3（逐項擷取）：每個區塊用 Search 找相關 chunks → LLM 擷取該區塊 JSON（並行）
/// Merge：純程式合併所有區塊 → 完整 JSON
///
/// 相比 LlmSchemaMapper（一次性），精準度更高、支援大文件。
/// </summary>
public sealed class MultiLayerSchemaMapper : ISchemaMapper
{
    private readonly ILlmProvider _llm;
    private readonly SearchCallback? _search;
    private readonly Action<string>? _onProgress;

    /// <summary>每份文件取前 N 字作為摘要（Layer 2 用）</summary>
    private const int SummaryCharLimit = 5000;

    /// <summary>每個區塊的搜尋結果上限</summary>
    private const int SearchTopK = 20;

    /// <summary>並行擷取的最大併發數（避免 LLM rate limit）</summary>
    private const int MaxConcurrency = 5;

    /// <summary>LLM 回傳的待確認問題欄位名稱</summary>
    private const string OpenQuestionsField = "_open_questions";

    // Token 累計（thread-safe）
    private int _totalInputTokens;
    private int _totalOutputTokens;

    public MultiLayerSchemaMapper(ILlmProvider llm, SearchCallback? search = null, Action<string>? onProgress = null)
    {
        _llm = llm;
        _search = search;
        _onProgress = onProgress;
    }

    public async Task<SchemaMapperResult> MapAsync(
        IReadOnlyList<CleanedDocument> documents,
        SchemaDefinition schema,
        SchemaMapperOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new SchemaMapperOptions();
        _totalInputTokens = 0;
        _totalOutputTokens = 0;

        var sections = ExtractSchemaSections(schema.JsonSchema);
        if (sections.Count == 0)
        {
            var fallback = new LlmSchemaMapper(_llm, _onProgress);
            return await fallback.MapAsync(documents, schema, options, ct);
        }

        // Layer 2: 大綱規劃
        _onProgress?.Invoke($"Layer 2: Planning extraction for {sections.Count} sections...");
        var plans = await PlanSectionsAsync(documents, sections, schema, options, ct);
        var activeSections = plans.Count(p => p.HasData);
        _onProgress?.Invoke($"Layer 2: Found {activeSections}/{sections.Count} sections with data");

        // Layer 3: 逐項擷取（並行）
        _onProgress?.Invoke($"Layer 3: Extracting {activeSections} sections (parallel)...");
        var results = await ExtractSectionsAsync(plans, sections, schema, options, ct);
        _onProgress?.Invoke($"Layer 3: Completed {results.Count} sections");

        // Layer 4: LLM Challenge（可選）
        List<FieldChallenge> challenges = [];
        if (options.EnableChallenge && _search is not null)
        {
            _onProgress?.Invoke("Layer 4: Challenging extraction results...");
            challenges = await ChallengeResultsAsync(results, plans, schema, options, ct);
            _onProgress?.Invoke($"Layer 4: {challenges.Count} challenges found");
        }

        // Merge
        _onProgress?.Invoke("Merging results...");
        return MergeResults(results, plans, documents.Count, challenges);
    }

    // ═══════════════════════════════════════
    // Layer 2: 大綱規劃
    // ═══════════════════════════════════════

    private async Task<List<SectionPlan>> PlanSectionsAsync(
        IReadOnlyList<CleanedDocument> documents,
        Dictionary<string, string> sections,
        SchemaDefinition schema,
        SchemaMapperOptions options,
        CancellationToken ct)
    {
        // 建立文件摘要（每份取前 N 字）
        var summaries = new StringBuilder();
        foreach (var doc in documents)
        {
            summaries.AppendLine($"=== {doc.FileName} ===");
            var text = doc.GetFullText();
            summaries.AppendLine(text.Length > SummaryCharLimit ? text[..SummaryCharLimit] + "..." : text);
            summaries.AppendLine();
        }

        var sectionList = string.Join(", ", sections.Keys);

        var systemPrompt = "你是文件分析專家。根據以下文件摘要，判斷哪些 Schema 區塊有對應的資料。\n\n" +
            $"Schema 區塊：{sectionList}\n\n" +
            "請為每個區塊判斷：\n" +
            "1. hasData：文件中是否有該區塊的相關資料（true/false）\n" +
            "2. searchQueries：用於搜尋該區塊資料的關鍵字清單（2-5 個，用來源文件的語言）\n\n" +
            "只輸出 JSON 陣列，不要加說明文字。範例格式：\n" +
            """[{"section": "name", "hasData": true, "searchQueries": ["keyword1", "keyword2"]}]""";

        var llmResult = await _llm.CompleteAsync(systemPrompt, summaries.ToString(), ct);
        Interlocked.Add(ref _totalInputTokens, llmResult.InputTokens);
        Interlocked.Add(ref _totalOutputTokens, llmResult.OutputTokens);
        return ParseSectionPlans(llmResult.Text, sections.Keys);
    }

    // ═══════════════════════════════════════
    // Layer 3: 逐項擷取（並行）
    // ═══════════════════════════════════════

    private async Task<List<SectionResult>> ExtractSectionsAsync(
        List<SectionPlan> plans,
        Dictionary<string, string> sections,
        SchemaDefinition schema,
        SchemaMapperOptions options,
        CancellationToken ct)
    {
        var activePlans = plans.Where(p => p.HasData).ToList();
        var results = new List<SectionResult>();

        // 用 SemaphoreSlim 控制並行數
        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        var tasks = activePlans.Select(async plan =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await ExtractSingleSectionAsync(plan, sections, schema, options, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var completed = await Task.WhenAll(tasks);
        results.AddRange(completed);
        return results;
    }

    private async Task<SectionResult> ExtractSingleSectionAsync(
        SectionPlan plan,
        Dictionary<string, string> sections,
        SchemaDefinition schema,
        SchemaMapperOptions options,
        CancellationToken ct)
    {
        // Step 1: 搜尋相關 chunks
        var relevantContent = await SearchForSectionAsync(plan, ct);

        // Step 2: 取得該區塊的 sub-schema
        var subSchema = sections.TryGetValue(plan.Section, out var ss) ? ss : "{}";

        // Step 3: LLM 擷取
        var systemPrompt = BuildSectionExtractionPrompt(plan.Section, subSchema, schema, options);
        var userPrompt = BuildSectionUserPrompt(plan.Section, relevantContent);

        var llmResult = await _llm.CompleteAsync(systemPrompt, userPrompt, ct);
        Interlocked.Add(ref _totalInputTokens, llmResult.InputTokens);
        Interlocked.Add(ref _totalOutputTokens, llmResult.OutputTokens);
        _onProgress?.Invoke($"Layer 3: {plan.Section} done ({llmResult.InputTokens + llmResult.OutputTokens} tokens)");

        // LLM 可能回傳 Object（如 budget）或 Array（如 functional_requirements）
        var json = llmResult.Text.Trim();
        var firstBracket = json.IndexOf('[');
        var firstBrace = json.IndexOf('{');
        if (firstBracket >= 0 && (firstBrace < 0 || firstBracket < firstBrace))
        {
            json = ExtractJsonArray(json);
        }
        else
        {
            json = ExtractJson(json);
        }

        // 擷取該區塊的 open_questions（LLM 可能回傳 Object 或 Array）
        var questions = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(OpenQuestionsField, out var oq) &&
                oq.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in oq.EnumerateArray())
                {
                    if (q.GetString() is { } qs)
                    {
                        questions.Add(qs);
                    }
                }
            }
        }
        catch (JsonException) { /* ignore */ }

        return new SectionResult(plan.Section, json, questions);
    }

    private async Task<string> SearchForSectionAsync(SectionPlan plan, CancellationToken ct)
    {
        if (_search is null || plan.SearchQueries.Count == 0)
        {
            return "(No search results available)";
        }

        var allChunks = new List<string>();
        foreach (var query in plan.SearchQueries)
        {
            var chunks = await _search(query, SearchTopK, ct);
            allChunks.AddRange(chunks);
        }

        // 去重
        var unique = allChunks.Distinct().ToList();
        return string.Join("\n\n---\n\n", unique);
    }

    private static string BuildSectionExtractionPrompt(
        string section, string subSchema, SchemaDefinition schema, SchemaMapperOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"你是文件分析師，正在擷取「{schema.Name}」的「{section}」區塊。");
        sb.AppendLine();
        sb.AppendLine("規則：");
        sb.AppendLine("1. 嚴格遵守以下 JSON Schema 結構");
        sb.AppendLine("2. 只從提供的文件內容中擷取，不要編造");
        sb.AppendLine("3. 找不到的欄位填 null");
        sb.AppendLine("4. 保持原始語言");
        sb.AppendLine("5. 如有待確認問題，加入 _open_questions 陣列");
        sb.AppendLine("6. 只輸出 JSON，不要加說明文字");

        if (options.OutputLanguage is not null)
        {
            sb.AppendLine($"7. 輸出語言：{options.OutputLanguage}");
        }

        if (schema.ExtractionGuidance is not null)
        {
            sb.AppendLine();
            sb.AppendLine("擷取指引：");
            sb.AppendLine(schema.ExtractionGuidance);
        }

        sb.AppendLine();
        sb.AppendLine($"「{section}」的 JSON Schema：");
        sb.AppendLine("```json");
        sb.AppendLine(subSchema);
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static string BuildSectionUserPrompt(string section, string relevantContent)
    {
        return $"""
            以下是與「{section}」相關的文件內容（由搜尋引擎擷取的相關段落）：

            {relevantContent}

            請從上述內容中擷取「{section}」的結構化資料，輸出 JSON。
            """;
    }

    // ═══════════════════════════════════════
    // ═══════════════════════════════════════
    // Layer 4: LLM Challenge 驗證
    // ═══════════════════════════════════════

    private const float ConfidenceAcceptThreshold = 0.8f;
    private const float ConfidenceFlagThreshold = 0.5f;

    private async Task<List<FieldChallenge>> ChallengeResultsAsync(
        List<SectionResult> results,
        List<SectionPlan> plans,
        SchemaDefinition schema,
        SchemaMapperOptions options,
        CancellationToken ct)
    {
        var allChallenges = new List<FieldChallenge>();

        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        var tasks = results.Select(async result =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await ChallengeSingleSectionAsync(result, plans, schema, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var completed = await Task.WhenAll(tasks);
        foreach (var challenges in completed)
        {
            allChallenges.AddRange(challenges);
        }

        return allChallenges;
    }

    private async Task<List<FieldChallenge>> ChallengeSingleSectionAsync(
        SectionResult result,
        List<SectionPlan> plans,
        SchemaDefinition schema,
        CancellationToken ct)
    {
        // 取得該區塊的搜尋關鍵字，搜尋原始內容
        var plan = plans.FirstOrDefault(p => p.Section == result.Section);
        var originalContent = await SearchForSectionAsync(plan ?? new SectionPlan(result.Section, true, [result.Section.Replace('_', ' ')]), ct);

        var systemPrompt =
            $"你是品質審核專家。你的任務是驗證以下「{result.Section}」區塊的擷取結果是否正確。\n\n" +
            "規則：\n" +
            "1. 逐一檢查每個欄位值是否與原始文件一致\n" +
            "2. 找出不一致、矛盾、或可疑的欄位\n" +
            "3. 對每個問題欄位給出信心度（0.0-1.0）\n" +
            "4. 只輸出 JSON，不要加說明文字\n\n" +
            "輸出格式：\n" +
            """{"challenges": [{"field": "欄位路徑", "original": "原始值", "reason": "質疑原因", "suggested": "建議值或null", "confidence": 0.5}], "verified": ["已驗證正確的欄位"]}""";

        var userPrompt =
            $"=== 擷取結果（{result.Section}）===\n{result.Json}\n\n" +
            $"=== 原始文件內容 ===\n{originalContent}\n\n" +
            "請驗證擷取結果是否與原始文件一致。";

        var llmResult = await _llm.CompleteAsync(systemPrompt, userPrompt, ct);
        Interlocked.Add(ref _totalInputTokens, llmResult.InputTokens);
        Interlocked.Add(ref _totalOutputTokens, llmResult.OutputTokens);

        _onProgress?.Invoke($"Layer 4: {result.Section} challenged ({llmResult.InputTokens + llmResult.OutputTokens} tokens)");

        return ParseChallengeResponse(llmResult.Text, result.Section);
    }

    private static List<FieldChallenge> ParseChallengeResponse(string response, string section)
    {
        var challenges = new List<FieldChallenge>();
        try
        {
            var json = ExtractJson(response);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("challenges", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var field = item.TryGetProperty("field", out var f) ? f.GetString() ?? "" : "";
                    var original = item.TryGetProperty("original", out var o) ? o.ToString() : "";
                    var reason = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
                    var suggested = item.TryGetProperty("suggested", out var s) && s.ValueKind != JsonValueKind.Null
                        ? s.ToString() : null;
                    var confidence = item.TryGetProperty("confidence", out var c) ? c.GetSingle() : ConfidenceFlagThreshold;

                    var action = confidence >= ConfidenceAcceptThreshold ? ChallengeAction.Accept
                        : confidence >= ConfidenceFlagThreshold ? ChallengeAction.Flag
                        : ChallengeAction.Reject;

                    challenges.Add(new FieldChallenge
                    {
                        Field = $"{section}.{field}",
                        OriginalValue = original,
                        ChallengeReason = reason,
                        SuggestedValue = suggested,
                        Confidence = confidence,
                        Action = action,
                    });
                }
            }
        }
        catch (JsonException) { /* Challenge 解析失敗，不影響主流程 */ }

        return challenges;
    }

    // ═══════════════════════════════════════
    // Merge: 合併所有區塊
    // ═══════════════════════════════════════

    private SchemaMapperResult MergeResults(
        List<SectionResult> results, List<SectionPlan> plans, int sourceCount,
        List<FieldChallenge>? challenges = null)
    {
        var merged = new Dictionary<string, JsonElement>();
        var allQuestions = new List<string>();
        var allMissing = new List<string>();

        foreach (var result in results)
        {
            try
            {
                using var doc = JsonDocument.Parse(result.Json);
                // 複製 JSON element（因為 doc 會被 dispose）
                merged[result.Section] = doc.RootElement.Clone();

                // 掃描 null 欄位
                FindNullFields(doc.RootElement, result.Section, allMissing);
            }
            catch (JsonException)
            {
                // 解析失敗的區塊標記為 missing
                allMissing.Add(result.Section);
            }

            allQuestions.AddRange(result.OpenQuestions);
        }

        // hasData=false 的區塊標記為 missing
        foreach (var plan in plans.Where(p => !p.HasData))
        {
            allMissing.Add(plan.Section);
        }

        // 組合完整 JSON
        var finalJson = BuildFinalJson(merged);

        var allChallenges = challenges ?? [];
        var overallConfidence = allChallenges.Count > 0
            ? allChallenges.Average(c => c.Confidence)
            : 1.0f;

        return new SchemaMapperResult
        {
            Json = finalJson,
            MissingFields = allMissing.Distinct().ToList(),
            OpenQuestions = allQuestions.Distinct().ToList(),
            SourceCount = sourceCount,
            TotalInputTokens = _totalInputTokens,
            TotalOutputTokens = _totalOutputTokens,
            Challenges = allChallenges,
            OverallConfidence = (float)overallConfidence,
        };
    }

    private static string BuildFinalJson(Dictionary<string, JsonElement> sections)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in sections)
            {
                writer.WritePropertyName(key);
                // 移除 _open_questions（已收集到外層）
                if (value.ValueKind == JsonValueKind.Object)
                {
                    writer.WriteStartObject();
                    foreach (var prop in value.EnumerateObject())
                    {
                        if (prop.Name != OpenQuestionsField)
                        {
                            prop.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                }
                else
                {
                    value.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // ═══════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════

    /// <summary>從 JSON Schema 解析 top-level properties → 每個區塊的 sub-schema</summary>
    internal static Dictionary<string, string> ExtractSchemaSections(string jsonSchema)
    {
        var result = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(jsonSchema);
            if (doc.RootElement.TryGetProperty("properties", out var props) &&
                props.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in props.EnumerateObject())
                {
                    result[prop.Name] = prop.Value.GetRawText();
                }
            }
        }
        catch (JsonException) { /* Schema 無法解析 */ }

        return result;
    }

    private static List<SectionPlan> ParseSectionPlans(string response, IEnumerable<string> knownSections)
    {
        var plans = new List<SectionPlan>();
        var knownSet = new HashSet<string>(knownSections, StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = ExtractJsonArray(response);
            using var doc = JsonDocument.Parse(json);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var section = item.GetProperty("section").GetString() ?? "";
                if (!knownSet.Contains(section))
                {
                    continue;
                }

                var hasData = item.TryGetProperty("hasData", out var hd) && hd.GetBoolean();
                var queries = new List<string>();
                if (item.TryGetProperty("searchQueries", out var sq) && sq.ValueKind == JsonValueKind.Array)
                {
                    foreach (var q in sq.EnumerateArray())
                    {
                        if (q.GetString() is { } qs)
                        {
                            queries.Add(qs);
                        }
                    }
                }

                plans.Add(new SectionPlan(section, hasData, queries));
            }
        }
        catch (JsonException)
        {
            // LLM 回應無法解析 → 所有區塊都當有資料，用區塊名作搜尋詞
            foreach (var section in knownSet)
            {
                plans.Add(new SectionPlan(section, true, [section.Replace('_', ' ')]));
            }
        }

        // 確保所有已知區塊都有 plan
        foreach (var section in knownSet)
        {
            if (plans.All(p => !p.Section.Equals(section, StringComparison.OrdinalIgnoreCase)))
            {
                plans.Add(new SectionPlan(section, false, []));
            }
        }

        return plans;
    }

    private static string ExtractJson(string response) =>
        ExtractJsonContent(response, '{', '}');

    private static string ExtractJsonArray(string response) =>
        ExtractJsonContent(response, '[', ']');

    private static string ExtractJsonContent(string response, char startChar, char endChar)
    {
        var trimmed = response.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0) trimmed = trimmed[(firstNewline + 1)..];
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence > 0) trimmed = trimmed[..lastFence];
        }

        var start = trimmed.IndexOf(startChar);
        var end = trimmed.LastIndexOf(endChar);
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }

    private static void FindNullFields(JsonElement element, string path, List<string> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Name == OpenQuestionsField) continue;
                var fieldPath = $"{path}.{prop.Name}";
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
    }
}

internal record SectionPlan(string Section, bool HasData, List<string> SearchQueries);
internal record SectionResult(string Section, string Json, List<string> OpenQuestions);
