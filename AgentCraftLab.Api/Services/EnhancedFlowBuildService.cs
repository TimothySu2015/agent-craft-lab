using AgentCraftLab.Data;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentCraftLab.Autonomous.Flow.Models;
using AgentCraftLab.Autonomous.Flow.Services;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.AI;

namespace AgentCraftLab.Api.Services;

/// <summary>
/// 強化版 AI Build — 使用 Flow Planner 的調校 prompt + 驗證器，產出品質更好的 Workflow。
/// 架構：FlowPlannerPrompt（規劃規則）→ SSE 串流 → FlowPlanValidator（驗證）→ FlowPlanConverter（格式轉換）。
/// </summary>
public sealed class EnhancedFlowBuildService
{
    private readonly ILlmClientFactory _clientFactory;
    private readonly ILogger<EnhancedFlowBuildService> _logger;
    private readonly Dictionary<string, string> _toolDescriptions;
    private readonly List<string> _availableToolIds;

    public EnhancedFlowBuildService(
        ILlmClientFactory clientFactory,
        ToolRegistryService toolRegistry,
        ILogger<EnhancedFlowBuildService> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;

        var tools = toolRegistry.GetAvailableTools();
        _availableToolIds = tools.Select(t => t.Id).ToList();
        _toolDescriptions = tools.ToDictionary(t => t.Id, t => t.Description);
    }

    /// <summary>
    /// SSE 串流生成 workflow。Phase 1 串流思考文字，Phase 2 後處理 JSON（validate + convert）。
    /// </summary>
    public async IAsyncEnumerable<string> GenerateAsync(
        FlowBuildRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Credential.ApiKey) && !Providers.IsKeyOptional(request.Provider))
        {
            yield return $"\n\n❌ 尚未設定 API Key，請先在設定中填入 {request.Provider} 的金鑰。";
            yield break;
        }

        // 建立 LLM client
        var credentials = new Dictionary<string, ProviderCredential>
        {
            [request.Provider] = request.Credential
        };
        var (client, error) = _clientFactory.CreateClient(credentials, request.Provider, request.Model);
        if (client is null)
        {
            yield return $"\n\n❌ 無法建立 LLM 連線：{error}";
            yield break;
        }

        // 組合 prompt
        var messages = BuildMessages(request);

        _logger.LogInformation("[EnhancedFlowBuild] 開始生成，model={Model}", request.Model);

        // 串流 LLM 回覆，分離思考文字和 JSON
        var fullText = new StringBuilder();
        var inJsonBlock = false;
        var jsonBuffer = new StringBuilder();

        var chatOptions = new ChatOptions { Temperature = 0f };
        await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions, ct))
        {
            if (update.Text is not { Length: > 0 } text) continue;

            fullText.Append(text);

            if (!inJsonBlock)
            {
                // 只在 chunk 含反引號時才檢查全文（避免每 chunk 都 ToString O(n^2)）
                if (text.Contains('`'))
                {
                    var accumulated = fullText.ToString();
                    var jsonStart = accumulated.IndexOf("```json", StringComparison.Ordinal);
                    if (jsonStart >= 0)
                    {
                        inJsonBlock = true;
                        // 輸出 ```json 之前的思考文字
                        var textJsonStart = text.IndexOf("```json", StringComparison.Ordinal);
                        if (textJsonStart > 0) yield return text[..textJsonStart];

                        // 開始緩衝 JSON
                        jsonBuffer.Append(accumulated[(jsonStart + 7)..]);
                        continue;
                    }
                }

                // 正常串流思考文字
                yield return text;
            }
            else
            {
                // 緩衝 JSON 區塊
                jsonBuffer.Append(text);
            }
        }

        // 後處理：從緩衝的 JSON 擷取、驗證、轉換
        var jsonContent = jsonBuffer.ToString();
        var endMarker = jsonContent.IndexOf("```", StringComparison.Ordinal);
        if (endMarker >= 0)
        {
            jsonContent = jsonContent[..endMarker];
        }
        jsonContent = jsonContent.Trim();

        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            // fallback：從完整文字中提取
            var full = fullText.ToString();
            var match = JsonBlockRegex.Match(full);
            if (match.Success)
            {
                jsonContent = match.Groups[1].Value.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(jsonContent))
        {
            var convertedJson = ValidateAndConvert(jsonContent);
            if (convertedJson is not null)
            {
                yield return $"\n\n```json\n{convertedJson}\n```";
                _logger.LogInformation("[EnhancedFlowBuild] 生成完成，已驗證並轉換");
            }
            else
            {
                // 驗證失敗，回傳原始 JSON
                yield return $"\n\n```json\n{jsonContent}\n```";
                _logger.LogWarning("[EnhancedFlowBuild] FlowPlan 解析或轉換失敗，回傳原始 JSON");
            }
        }
        else
        {
            _logger.LogWarning("[EnhancedFlowBuild] 未找到 JSON block");
        }
    }

    private string? ValidateAndConvert(string jsonContent)
    {
        try
        {
            // 先修復字面換行符（必須在移除註解之前，否則 URL 中的 // 會被誤判為註解）
            var cleaned = FixUnescapedNewlinesInJsonStrings(jsonContent);
            // 移除 JSON string 外部的 // 和 /* */ 註解（不動 string 內的 //）
            cleaned = RemoveJsonComments(cleaned);
            cleaned = TrailingCommaRegex.Replace(cleaned, "$1");

            // 修復 LLM 截斷造成的未閉合 JSON（unclosed string / brackets）
            cleaned = RepairTruncatedJson(cleaned);

            var plan = JsonSerializer.Deserialize<FlowPlan>(cleaned, AgentCraftLab.Engine.Models.Schema.SchemaJsonOptions.Default);
            if (plan?.Nodes is null || plan.Nodes.Count == 0) return null;

            // 驗證 + 自動修正
            var validationRequest = new GoalExecutionRequest
            {
                Goal = "",
                Credentials = new Dictionary<string, ProviderCredential>(),
                AvailableTools = _availableToolIds
            };
            var (validatedPlan, warnings) = FlowPlanValidator.ValidateAndFix(plan, validationRequest);

            foreach (var w in warnings)
            {
                _logger.LogInformation("[EnhancedFlowBuild] Validation: {Warning}", w);
            }

            if (validatedPlan.Nodes.Count == 0) return null;

            // 轉換為 AI Build 格式
            return FlowPlanConverter.ConvertToAiBuildJson(validatedPlan);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EnhancedFlowBuild] JSON 解析失敗");
            return null;
        }
    }

    private List<ChatMessage> BuildMessages(FlowBuildRequest request)
    {
        // 用 FlowPlannerPrompt 的規劃規則 + 額外的串流 UX 指示
        var goalRequest = new GoalExecutionRequest
        {
            Goal = request.UserMessage,
            Credentials = new Dictionary<string, ProviderCredential>
            {
                [request.Provider] = request.Credential
            },
            AvailableTools = _availableToolIds
        };

        var basePrompt = FlowPlannerPrompt.Build(goalRequest, _toolDescriptions);

        // 加入串流 UX 指示 + locale 語言指令
        var enhancedPrompt = basePrompt + GetStreamingUxInstructions(request.Locale);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, enhancedPrompt)
        };

        // 對話歷史
        foreach (var entry in request.History)
        {
            var role = entry.Role == "assistant" ? ChatRole.Assistant : ChatRole.User;
            messages.Add(new ChatMessage(role, entry.Text));
        }

        // 使用者訊息（含畫布 context）
        var userContent = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.CurrentWorkflowJson))
        {
            userContent.AppendLine("[Current canvas Workflow (for reference, modify as needed)]");
            userContent.AppendLine(request.CurrentWorkflowJson);
            userContent.AppendLine();
        }
        userContent.Append(request.UserMessage);
        messages.Add(new ChatMessage(ChatRole.User, userContent.ToString()));

        return messages;
    }

    private static string GetStreamingUxInstructions(string locale)
    {
        var langRule = locale switch
        {
            "en" => "Write your design explanation and agent `instructions` in **English**.",
            "ja" => "Write your design explanation in **Japanese (日本語)**. Write agent `instructions` in Japanese, ending with 「日本語で回答してください。」",
            _ => "Write your design explanation in **Traditional Chinese (繁體中文)**. Write agent `instructions` in Traditional Chinese, ending with 「請使用繁體中文回答。」 If the user explicitly requests a specific language for an agent, write that agent's instructions in the requested language.",
        };

        return $"""

        ## Additional Rules (overrides General Rule 1, adds key reminders)

        ### Response Format
        The rules above say "Output ONLY a JSON object", but this overrides that. Respond as follows:
        1. Briefly explain your design rationale (2-4 sentences, one paragraph, no repetition). {langRule}
        2. Then output JSON (wrapped in ```json)
        3. Agent names must be in English
        4. Do not add anything after the JSON block

        ### Common Mistakes (pay special attention)
        - **Do NOT insert code nodes for format conversion**: If the previous agent already outputs a JSON array, do not add a code node to transform it again — connect directly to the next node
        - **Correct approach for "decompose into sub-questions and search each"**: Do NOT use parallel (sub-questions are unknown at planning time, violating Rule 12). Correct:
          1. Agent (decompose into JSON array)
          2. iteration (split: jsonArray) + body agent with search tools
          3. Agent (merge all results)
          4. Code (format table, zero tokens)
        - **parallel only for known concrete items**: e.g., "compare React vs Vue vs Angular" → three concrete names → parallel OK. "decompose then search" → items unknown → do NOT use parallel
        - **Every node must have a clear purpose**: If removing a node doesn't change the flow, don't add it
        """;
    }

    private static readonly JsonSerializerOptions PlanJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Regex TrailingCommaRegex = new(@",\s*([}\]])", RegexOptions.Compiled);
    private static readonly Regex JsonBlockRegex = new(@"```json\s*([\s\S]*?)```", RegexOptions.Compiled);

    /// <summary>
    /// 移除 JSON string 外部的 // 行註解和 /* */ 區塊註解（不動 string 內的 // 如 URL）。
    /// </summary>
    private static string RemoveJsonComments(string json)
    {
        var sb = new System.Text.StringBuilder(json.Length);
        bool inString = false;
        bool escaped = false;
        int i = 0;

        while (i < json.Length)
        {
            var c = json[i];
            if (escaped) { sb.Append(c); escaped = false; i++; continue; }
            if (c == '\\' && inString) { sb.Append(c); escaped = true; i++; continue; }
            if (c == '"') { inString = !inString; sb.Append(c); i++; continue; }

            if (!inString)
            {
                // 行尾 // 註解
                if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
                {
                    while (i < json.Length && json[i] != '\n') i++;
                    continue;
                }
                // 區塊 /* */ 註解
                if (c == '/' && i + 1 < json.Length && json[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/')) i++;
                    i += 2;
                    continue;
                }
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// 修復 LLM 截斷造成的未閉合 JSON — 補上缺少的 ", ] , }。
    /// </summary>
    private static string RepairTruncatedJson(string json)
    {
        var trimmed = json.TrimEnd();

        // 追蹤結構狀態
        bool inString = false;
        bool escaped = false;
        var stack = new Stack<char>();

        foreach (var c in trimmed)
        {
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }

            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            switch (c)
            {
                case '{': stack.Push('}'); break;
                case '[': stack.Push(']'); break;
                case '}' or ']' when stack.Count > 0: stack.Pop(); break;
            }
        }

        var sb = new System.Text.StringBuilder(trimmed);

        // 如果截斷在 string 中間，先補上 closing quote
        if (inString)
            sb.Append('"');

        // 移除尾部懸空的逗號或冒號
        while (sb.Length > 0 && sb[sb.Length - 1] is ',' or ':')
            sb.Length--;

        // 補上未閉合的 brackets
        while (stack.Count > 0)
            sb.Append(stack.Pop());

        return sb.ToString();
    }

    /// <summary>
    /// 修復 LLM 在 JSON string 值中輸出的字面換行符。
    /// LLM 常在 httpHeaders 等欄位直接輸出換行，導致 JSON 解析失敗。
    /// </summary>
    private static string FixUnescapedNewlinesInJsonStrings(string json)
    {
        var sb = new System.Text.StringBuilder(json.Length);
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < json.Length; i++)
        {
            var c = json[i];

            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                sb.Append(c);
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                sb.Append(c);
                continue;
            }

            // 在 JSON string 內部遇到字面換行符 → 替換為 \n
            if (inString && (c == '\n' || c == '\r'))
            {
                if (c == '\r' && i + 1 < json.Length && json[i + 1] == '\n')
                    i++; // 跳過 \r\n 的 \n
                sb.Append("\\n");
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
