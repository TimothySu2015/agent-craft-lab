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

            var plan = JsonSerializer.Deserialize<FlowPlan>(cleaned, PlanJsonOptions);
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

        // 加入串流 UX 指示 + 繁體中文要求
        var enhancedPrompt = basePrompt + StreamingUxInstructions;

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
            userContent.AppendLine("【目前畫布上的 Workflow（供參考，需要時可修改）】");
            userContent.AppendLine(request.CurrentWorkflowJson);
            userContent.AppendLine();
        }
        userContent.Append(request.UserMessage);
        messages.Add(new ChatMessage(ChatRole.User, userContent.ToString()));

        return messages;
    }

    private const string StreamingUxInstructions = """

        ## 額外規則（覆蓋上方 General Rules 第 1 條，並強化重點）

        ### 回覆格式
        上方說「Output ONLY a JSON object」，但這裡覆蓋該規則。請按以下格式回覆：
        1. 先用繁體中文簡短說明你的設計思路（2-4 句，一段即可，不要重複）
        2. 然後輸出 JSON（用 ```json 包裹）
        3. Agent 的 instructions 預設使用繁體中文，結尾加「請使用繁體中文回答。」。但若使用者要求某個 Agent 用其他語言輸出，則該 Agent 的 instructions 必須用目標語言撰寫（如日文 Agent 用日文寫 instructions，結尾加「日本語で回答してください。」）
        4. Agent 的 name 請使用英文
        5. JSON 結束後不需要再說明

        ### 常見錯誤提醒（最容易犯的，請特別注意）
        - **禁止插入純格式轉換的 code 節點**：如果前一個 agent 已經輸出 JSON 陣列，不要加一個 code 節點再轉換一次，直接接下一個節點
        - **子問題拆解 + 分別搜尋的正確做法**：如果使用者要求「拆解成子問題並分別搜尋」，不要用 parallel（因為子問題在規劃時未知，違反 Rule 12）。正確做法：
          1. Agent（拆解子問題為 JSON 陣列）
          2. iteration（splitMode: json-array）+ 帶搜尋工具的 body agent
          3. Agent（彙整所有結果）
          4. Code（格式化表格，零 token）
        - **parallel 只用於已知的具體項目**：如「比較 React vs Vue vs Angular」→ 三個具體名稱可以用 parallel。「拆解後搜尋」→ 項目未知，不能用 parallel
        - **每個節點都要有明確作用**：如果移除某個節點後流程不變，就不要加它
        """;

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
