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
        var providersSection = BuildProvidersSection();
        var toolsSection = BuildToolsSection(tools);
        var skillsSection = BuildSkillsSection(skillRegistry.GetAvailableSkills());
        _systemPrompt = PromptTemplate
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

### agent — LLM Agent 節點
最核心的節點，由 LLM 驅動，可掛載工具。
```json
{
  "type": "agent",
  "name": "名稱（英文，如 Researcher）",
  "data": {
    "instructions": "Agent 的系統指令（繁體中文）",
    "model": "gpt-4o",
    "provider": "openai",
    "tools": [],
    "skills": [],
    "middleware": ""
  }
}
```
- `provider` 與 `model` 可用組合（自動從 Providers.Catalog 產生）：
{PROVIDERS_SECTION}
- `tools`: 內建工具 ID 列表（ID 使用底線 `_`，不是連字號 `-`），可選（自動從 ToolRegistryService 產生）：
{TOOLS_SECTION}
- `middleware`: 逗號分隔，可選 guardrails,pii,ratelimit,retry,logging
- `skills`: Skill ID 列表，為 Agent 注入領域知識/方法論/人設，可選（自動從 SkillRegistryService 產生）：
{SKILLS_SECTION}

### condition — 條件分支節點
根據前一個節點的輸出做判斷，有 2 個輸出口（output_1=True, output_2=False）。
```json
{
  "type": "condition",
  "name": "CheckApproval",
  "data": {
    "conditionType": "contains",
    "conditionExpression": "APPROVED"
  }
}
```
- `conditionType`: contains, regex, json-path

### loop — 迴圈節點
反覆執行直到條件滿足，有 2 個輸出口（output_1=Body 繼續, output_2=Exit 跳出）。
```json
{
  "type": "loop",
  "name": "ReviewLoop",
  "data": {
    "conditionType": "contains",
    "conditionExpression": "APPROVED",
    "maxIterations": 3
  }
}
```

### router — 路由節點
將輸入分派到多個分支，有 3 個輸出口（output_1, output_2, output_3）。
```json
{
  "type": "router",
  "name": "Router",
  "data": {
    "conditionExpression": "Classify the input into: billing, technical, or general",
    "routes": "billing,technical,general"
  }
}
```

### human — 人工介入節點
暫停流程等待使用者輸入。有 2 個輸出口（output_1=approve, output_2=reject，僅 approval 模式）。
```json
{
  "type": "human",
  "name": "HumanReview",
  "data": {
    "prompt": "請審閱以上內容是否正確？",
    "inputType": "approval",
    "timeoutSeconds": 0
  }
}
```
- `inputType`: text（自由文字）, choice（選擇）, approval（核准/拒絕）
- `choices`: 僅 choice 模式，逗號分隔選項，如 "選項A,選項B,選項C"

### code — 程式碼轉換節點
確定性資料轉換，不需 LLM。
```json
{
  "type": "code",
  "name": "Transform",
  "data": {
    "transformType": "template",
    "template": "摘要：{{input}}"
  }
}
```
- `transformType`: template, regex-extract, regex-replace, json-path, trim, split, upper, lower
- template 模式用 `{{input}}` 佔位符

### parallel — 並行節點
多個分支同時執行（fan-out / fan-in）。分支數量動態設定，最後一個 output port 是 Done。
```json
{
  "type": "parallel",
  "name": "MultiExpert",
  "data": {
    "branches": "Legal,Technical,Financial",
    "mergeStrategy": "labeled"
  }
}
```
- `branches`: 逗號分隔的分支名稱（2-8 個）
- `mergeStrategy`: labeled（帶標題合併）、join（逐行合併）、json（JSON 物件）
- output_1..N 分別連到各分支的節點鏈，output_(N+1) 是 Done port
- 所有分支同時執行（Task.WhenAll），完成後合併結果從 Done port 輸出

### iteration — 迭代節點
對輸入陣列中的每個元素依序執行子流程（foreach）。有 2 個輸出口（output_1=Body 每個元素, output_2=Done 完成後）。
```json
{
  "type": "iteration",
  "name": "ProcessItems",
  "data": {
    "splitMode": "json-array",
    "maxItems": 50
  }
}
```
- `splitMode`: json-array（預設，自動解析 JSON 陣列）或 delimiter（用分隔符切割）
- `iterationDelimiter`: 僅 delimiter 模式，預設 `\n`
- `maxItems`: 防護上限，預設 50
- output_1 連接的節點鏈會對**每個元素**執行一次
- 所有結果彙整後從 output_2 輸出

### a2a-agent — 遠端 A2A Agent 節點
呼叫遠端 Agent-to-Agent 協定的 Agent，不需本地 LLM。
```json
{
  "type": "a2a-agent",
  "name": "RemoteAgent",
  "data": {
    "a2aUrl": "http://remote-server/a2a",
    "a2aFormat": "auto"
  }
}
```
- `a2aUrl`: 遠端 A2A Agent 的 URL
- `a2aFormat`: auto（自動偵測）、google（Google A2A 格式）、microsoft（Microsoft A2A 格式）
- 有 A2A Agent 節點時強制 Imperative 策略
- 超時 300 秒

### http-request — HTTP 請求節點
不經 LLM，直接呼叫 HTTP API。零 token 成本，確定性執行。
```json
{
  "type": "http-request",
  "name": "PostToAPI",
  "data": {
    "httpUrl": "https://api.example.com/v1/data",
    "httpMethod": "POST",
    "httpContentType": "application/json",
    "httpHeaders": "Authorization: Bearer xxx",
    "httpBodyTemplate": "{\"content\": \"{input}\"}",
    "httpArgsTemplate": "{}"
  }
}
```
- `httpUrl`: API 端點 URL（支援 `{param}` 佔位符）
- `httpMethod`: GET / POST / PUT / PATCH / DELETE
- `httpContentType`: application/json / text/plain / text/csv / text/xml / application/x-www-form-urlencoded / multipart/form-data
- `httpHeaders`: Key: Value 格式，多個用 `\n` 分隔（如 `"Authorization: Bearer xxx\\nContent-Type: application/json"`）。**必須用 `\\n` 跳脫字元，禁止在 JSON string 中放真正的換行**
- `httpBodyTemplate`: 請求 body 模板，`{input}` 替換為前一節點輸出
- `httpArgsTemplate`: JSON 參數模板
- 可選：`httpAuthMode`（none/bearer/basic/apikey-header/apikey-query）、`httpResponseFormat`（text/json/jsonpath）、`httpRetryCount`（0-5）、`httpTimeoutSeconds`（預設 15）

### rag — RAG 節點
連接到 Agent 後啟用向量檢索增強。不獨立執行，需連線到 Agent。
```json
{
  "type": "rag",
  "name": "KnowledgeBase",
  "data": {}
}
```

## 輸出 JSON 格式

```json
{
  "nodes": [
    { "type": "agent", "name": "Researcher", "data": { "instructions": "..." } },
    { "type": "agent", "name": "Writer", "data": { "instructions": "..." } }
  ],
  "connections": [
    { "from": 0, "to": 1 },
    { "from": 1, "to": 2, "fromOutput": "output_2" }
  ],
  "workflowSettings": {
    "type": "auto",
    "maxTurns": 10
  }
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

### workflowSettings.type 選擇
- `auto` — 自動偵測（推薦，大多數情況使用此值）
- `sequential` — 強制依序執行
- `concurrent` — 所有 agent 同時執行
- `handoff` — Router agent 委派

## 範例

### 範例 1：順序流水線
使用者：「幫我建一個研究→撰稿→編輯的流程」
```json
{
  "nodes": [
    { "type": "agent", "name": "Researcher", "data": { "instructions": "你是一位研究員。針對使用者的主題產出重點摘要。請使用繁體中文回答。" } },
    { "type": "agent", "name": "Drafter", "data": { "instructions": "你是一位撰稿人。根據研究摘要撰寫結構完整的文章草稿。請使用繁體中文回答。" } },
    { "type": "agent", "name": "Editor", "data": { "instructions": "你是一位編輯。潤飾文字，確保內容清晰、文法正確，輸出最終版本。請使用繁體中文回答。" } }
  ],
  "connections": [
    { "from": 0, "to": 1 },
    { "from": 1, "to": 2 }
  ],
  "workflowSettings": { "type": "auto", "maxTurns": 10 }
}
```

### 範例 2：帶審閱迴圈
使用者：「寫一篇文章，然後審稿，不通過就重寫，最多 3 次」
```json
{
  "nodes": [
    { "type": "agent", "name": "Writer", "data": { "instructions": "你是一位撰稿人。根據主題撰寫內容。請使用繁體中文回答。" } },
    { "type": "loop", "name": "ReviewLoop", "data": { "conditionType": "contains", "conditionExpression": "APPROVED", "maxIterations": 3 } },
    { "type": "agent", "name": "Reviewer", "data": { "instructions": "你是一位審稿人。審閱草稿，通過請回覆 APPROVED，否則提出修改建議。請使用繁體中文回答。" } },
    { "type": "agent", "name": "Publisher", "data": { "instructions": "你是一位發佈者。將已核准的內容排版並發佈。請使用繁體中文回答。" } }
  ],
  "connections": [
    { "from": 0, "to": 1 },
    { "from": 1, "to": 2, "fromOutput": "output_1" },
    { "from": 1, "to": 3, "fromOutput": "output_2" }
  ],
  "workflowSettings": { "type": "auto", "maxTurns": 10 }
}
```

### 範例 3：客服分流
使用者：「做一個客服系統，分成帳務、技術、一般三類」
```json
{
  "nodes": [
    { "type": "agent", "name": "Triage", "data": { "instructions": "你是一位分流客服。理解客戶需求並分派給適當專員。請使用繁體中文回答。" } },
    { "type": "router", "name": "Router", "data": { "conditionExpression": "Classify the input into: billing, technical, or general", "routes": "billing,technical,general" } },
    { "type": "agent", "name": "Billing", "data": { "instructions": "你負責處理帳務相關的問題。請使用繁體中文回答。" } },
    { "type": "agent", "name": "Technical", "data": { "instructions": "你負責處理技術支援相關的問題。請使用繁體中文回答。" } },
    { "type": "agent", "name": "General", "data": { "instructions": "你負責處理一般性的諮詢問題。請使用繁體中文回答。" } }
  ],
  "connections": [
    { "from": 0, "to": 1 },
    { "from": 1, "to": 2, "fromOutput": "output_1" },
    { "from": 1, "to": 3, "fromOutput": "output_2" },
    { "from": 1, "to": 4, "fromOutput": "output_3" }
  ],
  "workflowSettings": { "type": "auto", "maxTurns": 10 }
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
8. **每次回覆都必須包含完整 JSON**：無論是新建還是修改，回覆末尾都必須有一個 ```json 區塊，包含完整的 `{ "nodes": [...], "connections": [...], "workflowSettings": {...} }`。絕對不能只描述修改而不輸出 JSON。
9. **一個 JSON 區塊**：回覆中只包含一個 ```json 區塊，放在回覆最末尾
""";
}
