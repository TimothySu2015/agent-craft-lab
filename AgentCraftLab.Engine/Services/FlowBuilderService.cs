using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Strategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 透過 LLM 將自然語言描述轉換為 Workflow 規格（AI Build 模式）。
/// </summary>
public sealed class FlowBuilderService
{
    private readonly ILogger<FlowBuilderService> _logger;
    private readonly ConcurrentDictionary<string, object> _clientCache = new();
    private readonly string _systemPrompt;
    private readonly string[] _toolIds;

    public FlowBuilderService(
        ILogger<FlowBuilderService> logger,
        ToolRegistryService toolRegistry,
        SkillRegistryService skillRegistry)
    {
        _logger = logger;

        // 快取工具 ID 清單（供 JS 白名單注入）
        var tools = toolRegistry.GetAvailableTools();
        _toolIds = tools.Select(t => t.Id).ToArray();

        // 從 Registry 動態組建 System Prompt（Singleton 建構時執行一次）
        // NODE_SPECS 先注入 — 它本身含 PROVIDERS/TOOLS/SKILLS 巢狀 placeholder，
        // 下面幾個 Replace 會把展開後的 markdown 內的 placeholder 再替換一次。
        var providersSection = BuildProvidersSection();
        var toolsSection = BuildToolsSection(tools);
        var skillsSection = BuildSkillsSection(skillRegistry.GetAvailableSkills());
        _systemPrompt = PromptTemplate
            .Replace("{NODE_SPECS}", NodeSpecRegistry.BuildMarkdownSection())
            .Replace("{PROVIDERS_SECTION}", providersSection)
            .Replace("{TOOLS_SECTION}", toolsSection)
            .Replace("{SKILLS_SECTION}", skillsSection);
    }

    /// <summary>
    /// 串流呼叫 LLM，將使用者自然語言轉換為 Workflow JSON 規格。
    /// </summary>
    public async IAsyncEnumerable<string> GenerateFlowAsync(
        FlowBuildRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Credential.ApiKey) && !Providers.IsKeyOptional(request.Provider))
        {
            yield return $"\n\n❌ 尚未設定 API Key，請先在設定中填入 {request.Provider} 的金鑰。";
            yield break;
        }

        var chatClient = CreateChatClient(request);
        var messages = BuildMessages(request);

        _logger.LogInformation("[FlowBuilder] 開始生成 workflow，使用模型 {Model}", request.Model);

        var responseLength = 0;
        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
        {
            if (update.Text is { Length: > 0 } text)
            {
                responseLength += text.Length;
                yield return text;
            }
        }

        _logger.LogInformation("[FlowBuilder] 生成完成，回應長度 {Length}", responseLength);
    }

    private IChatClient CreateChatClient(FlowBuildRequest request)
    {
        var provider = request.Provider ?? Providers.OpenAI;
        var (apiKey, endpoint) = AgentContextBuilder.NormalizeCredential(
            provider, request.Credential.ApiKey, request.Credential.Endpoint ?? "");
        var cacheKey = $"{provider}:{endpoint}";
        var baseClient = _clientCache.GetOrAdd(cacheKey, _ =>
        {
            var timeout = TimeSpan.FromMinutes(Timeouts.LlmNetworkTimeoutMinutes);
            return AgentContextBuilder.CreateLlmClient(provider, apiKey, endpoint, timeout);
        });

        return AgentContextBuilder.GetChatClientFromBase(baseClient, request.Model);
    }

    private List<ChatMessage> BuildMessages(FlowBuildRequest request)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, _systemPrompt)
        };

        // 加入對話歷史
        foreach (var entry in request.History)
        {
            var role = entry.Role == "assistant" ? ChatRole.Assistant : ChatRole.User;
            messages.Add(new ChatMessage(role, entry.Text));
        }

        // 目前畫布狀態（execution payload 格式，與輸出格式相近）
        var userContent = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.CurrentWorkflowJson))
        {
            userContent.AppendLine("【目前畫布上的 Workflow（execution payload 格式）】");
            userContent.AppendLine("```json");
            userContent.AppendLine(request.CurrentWorkflowJson);
            userContent.AppendLine("```");
            userContent.AppendLine("請在此基礎上修改，輸出完整的新 Workflow JSON（你的輸出格式使用 index-based connections，不是 id-based）。");
            userContent.AppendLine();
        }

        userContent.Append(request.UserMessage);
        messages.Add(new ChatMessage(ChatRole.User, userContent.ToString()));

        return messages;
    }

    // ─── System Prompt（動態組建） ───

    /// <summary>
    /// 從 Providers.Catalog 產生 provider/model 列表文字。
    /// </summary>
    private static string BuildProvidersSection()
    {
        var sb = new StringBuilder();
        foreach (var (id, (_, models)) in Providers.Catalog)
        {
            var suffix = id == Providers.OpenAI ? "（預設）" : "";
            sb.AppendLine($"  - `{id}`{suffix}: {string.Join(", ", models)}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 從 ToolRegistryService 產生工具列表文字。
    /// </summary>
    private static string BuildToolsSection(IReadOnlyList<ToolDefinition> tools)
    {
        var sb = new StringBuilder();
        foreach (var t in tools)
        {
            sb.AppendLine($"  - `{t.Id}` — {t.Description}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 從 SkillRegistryService 產生 skill 列表文字。
    /// </summary>
    private static string BuildSkillsSection(IReadOnlyList<SkillDefinition> skills)
    {
        var sb = new StringBuilder();
        foreach (var s in skills)
        {
            var toolsSuffix = s.Tools is { Count: > 0 }
                ? $"（自帶 {string.Join(" + ", s.Tools)}）"
                : "";
            sb.AppendLine($"  - `{s.Id}` — {s.DisplayName}{toolsSuffix}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 取得所有合法的工具 ID（供 JS 端白名單使用）。
    /// </summary>
    public IReadOnlyList<string> GetToolIds() => _toolIds;

    private const string PromptTemplate = """
你是 AgentCraftLab 的 Workflow 設計助手。使用者會用自然語言描述他們想要的 Agent 工作流程，你要幫他們設計並產出 Workflow 規格 JSON。

## 你的任務

1. 理解使用者描述的需求
2. 設計合適的 Agent Workflow（選擇正確的節點類型與連線方式）
3. 在回覆中先用自然語言說明你的設計思路
4. 最後輸出一個 JSON 區塊（用 ```json 包裹），格式符合下方規格

## 節點類型規格

{NODE_SPECS}

## 輸出 JSON 格式（Schema v2）

節點欄位直接放在 node 物件上（不要用 `"data": {...}` 包裹）。

```json
{
  "nodes": [
    { "type": "agent", "name": "Researcher", "instructions": "...", "tools": ["web_search"] },
    { "type": "agent", "name": "Writer", "instructions": "..." }
  ],
  "connections": [
    { "from": 0, "to": 1 },
    { "from": 1, "to": 2, "fromOutput": "output_2" }
  ]
}
```

### 連線規則
- `from` 和 `to` 是 nodes 陣列的 index（從 0 開始）
- `fromOutput` 預設為 "output_1"，多輸出口節點需指定：
  - condition: output_1=True 分支, output_2=False 分支
  - loop: output_1=Body（繼續迴圈）, output_2=Exit（跳出迴圈）
  - router: output_1/output_2/output_3 對應 routes 中的三個目標
  - human (approval): output_1=Approve, output_2=Reject
- Start 和 End 節點由系統自動產生，不需要在 nodes 中包含
- 系統會自動將 Start 連到第一個節點、最後一個節點連到 End
- 不需要輸出 `workflowSettings` — 系統會自動設定

## 範例

### 範例 1：順序流水線
使用者：「幫我建一個研究→撰稿→編輯的流程」
```json
{
  "nodes": [
    { "type": "agent", "name": "Researcher", "instructions": "你是一位研究員。針對使用者的主題產出重點摘要。請使用繁體中文回答。" },
    { "type": "agent", "name": "Drafter", "instructions": "你是一位撰稿人。根據研究摘要撰寫結構完整的文章草稿。請使用繁體中文回答。" },
    { "type": "agent", "name": "Editor", "instructions": "你是一位編輯。潤飾文字，確保內容清晰、文法正確，輸出最終版本。請使用繁體中文回答。" }
  ],
  "connections": [
    { "from": 0, "to": 1 },
    { "from": 1, "to": 2 }
  ]
}
```

### 範例 2：帶審閱迴圈
使用者：「寫一篇文章，然後審稿，不通過就重寫，最多 3 次」
```json
{
  "nodes": [
    { "type": "agent", "name": "Writer", "instructions": "你是一位撰稿人。根據主題撰寫內容。請使用繁體中文回答。" },
    { "type": "loop", "name": "ReviewLoop", "condition": { "kind": "contains", "value": "APPROVED" }, "maxIterations": 3 },
    { "type": "agent", "name": "Reviewer", "instructions": "你是一位審稿人。審閱草稿，通過請回覆 APPROVED，否則提出修改建議。請使用繁體中文回答。" },
    { "type": "agent", "name": "Publisher", "instructions": "你是一位發佈者。將已核准的內容排版並發佈。請使用繁體中文回答。" }
  ],
  "connections": [
    { "from": 0, "to": 1 },
    { "from": 1, "to": 2, "fromOutput": "output_1" },
    { "from": 1, "to": 3, "fromOutput": "output_2" }
  ]
}
```

### 範例 3：客服分流
使用者：「做一個客服系統，分成帳務、技術、一般三類」
```json
{
  "nodes": [
    { "type": "agent", "name": "Triage", "instructions": "你是一位分流客服。理解客戶需求並分派給適當專員。請使用繁體中文回答。" },
    { "type": "router", "name": "Router", "routes": [{ "name": "billing", "keywords": ["帳務", "收費"], "isDefault": false }, { "name": "technical", "keywords": ["技術", "bug"], "isDefault": false }, { "name": "general", "keywords": [], "isDefault": true }] },
    { "type": "agent", "name": "Billing", "instructions": "你負責處理帳務相關的問題。請使用繁體中文回答。" },
    { "type": "agent", "name": "Technical", "instructions": "你負責處理技術支援相關的問題。請使用繁體中文回答。" },
    { "type": "agent", "name": "General", "instructions": "你負責處理一般性的諮詢問題。請使用繁體中文回答。" }
  ],
  "connections": [
    { "from": 0, "to": 1 },
    { "from": 1, "to": 2, "fromOutput": "output_1" },
    { "from": 1, "to": 3, "fromOutput": "output_2" },
    { "from": 1, "to": 4, "fromOutput": "output_3" }
  ]
}
```

## 重要規則

1. **回覆語言**：使用繁體中文說明設計思路
2. **Agent 指令**：預設用繁體中文撰寫，結尾加「請使用繁體中文回答。」。但若使用者明確要求某個 Agent 用其他語言輸出（如日文、英文），則該 Agent 的 instructions 必須用目標語言撰寫，結尾加上該語言的對應指示（如「日本語で回答してください。」「Please respond in English.」）
3. **Agent 名稱**：使用英文命名（如 Researcher, Writer, Reviewer）
4. **JSON 必須有效**：確保 JSON 可被正確解析
5. **連線邏輯正確**：from/to index 不能超出 nodes 陣列範圍
6. **設計簡潔**：不要過度設計，用最少的節點完成需求
7. **漸進修改**：如果使用者提供了目前畫布狀態（execution payload 格式），在其基礎上修改。注意：畫布 payload 中的 connections 使用 node id（如 "3", "4"），但你的輸出必須使用 nodes 陣列的 **index**（從 0 開始）。你需要理解現有 workflow 的結構，產出修改後的**完整** nodes 與 connections。
8. **每次回覆都必須包含完整 JSON**：無論是新建還是修改，回覆末尾都必須有一個 ```json 區塊，包含完整的 `{ "nodes": [...], "connections": [...] }`。絕對不能只描述修改而不輸出 JSON。
9. **一個 JSON 區塊**：回覆中只包含一個 ```json 區塊，放在回覆最末尾

## 設計原則

### 工具自動推薦
為每個 Agent 評估：這個任務是否需要即時/最新資料？
- **需要**（法規/財報/股價/新聞/市場/趨勢/競品/即時數據分析）→ 必須配置搜尋工具（如 `web_search` 或 `azure_web_search`）。沒有工具的 agent 只能用過時的訓練資料，會給使用者錯誤資訊。
- **不需要**（純推理/寫作/翻譯/格式化/創作）→ 不配工具。

### Parallel 必須有 Synthesizer
使用 parallel 節點後，必須在最後接一個 Synthesizer Agent 彙整所有分支結果。Synthesizer 不需要搜尋工具（只處理已有資料）。

### Parallel 範例（含工具 + Synthesizer）
使用者：「建立法律、技術、財務三專家平行分析」
```json
{
  "nodes": [
    { "type": "parallel", "name": "ExpertAnalysis",
      "branches": [
        { "name": "法律", "goal": "你是法律專家，分析輸入內容的法律風險與合規建議。請使用繁體中文回答。", "tools": ["web_search"] },
        { "name": "技術", "goal": "你是技術專家，分析輸入內容的技術可行性與挑戰。請使用繁體中文回答。", "tools": ["web_search"] },
        { "name": "財務", "goal": "你是財務專家，分析輸入內容的財務風險與成本效益。請使用繁體中文回答。", "tools": ["web_search"] }
      ],
      "merge": "labeled"
    },
    { "type": "agent", "name": "Synthesizer", "instructions": "你是一位資深分析師。綜合法律、技術、財務三方專家的分析結果，產出一份完整的評估報告，包含各面向重點摘要、風險矩陣和行動建議。請使用繁體中文回答。" }
  ],
  "connections": [
    { "from": 0, "to": 1 }
  ]
}
```
""";
}
