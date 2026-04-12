using System.Text;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 單一節點規格（Markdown 段落組合用）— 與原 prompt 逐字對齊的結構：
/// <code>
/// ### {Type} — {ShortName}
/// {Description}
/// ```json
/// {ExampleJson}
/// ```
/// {Notes}
/// </code>
/// </summary>
/// <param name="Type">節點類型字串（對應 <see cref="NodeTypes"/>）</param>
/// <param name="ShortName">節點短名稱（顯示於 ### 標題後）</param>
/// <param name="Description">節點描述（顯示於標題下一行）</param>
/// <param name="ExampleJson">可拷貝的 JSON 範例</param>
/// <param name="Notes">例項後的補充說明（可含 {PROVIDERS_SECTION} 等外層 placeholder）</param>
public sealed record NodeSpec(
    string Type,
    string ShortName,
    string Description,
    string ExampleJson,
    string? Notes = null);

/// <summary>
/// LLM prompt 用的節點規格註冊表 — AI Build（<see cref="FlowBuilderService"/>）
/// 和 Autonomous Flow planner 共用的單一真相。新增節點型別時必須在此加一筆 NodeSpec，
/// 否則 <c>NodeSpecRegistryTests</c> 會 fail。
/// </summary>
/// <remarks>
/// NodeSpec 描述的是 <see cref="Models.Schema.NodeConfig"/> nested schema（Schema v2）。
/// LLM 產出的 JSON 直接對應前端 NodeData 型別，不再需要中間轉換。
/// </remarks>
public static class NodeSpecRegistry
{
    public static IReadOnlyList<NodeSpec> All { get; } =
    [
        new(
            Type: NodeTypes.Agent,
            ShortName: "LLM Agent 節點",
            Description: "最核心的節點，由 LLM 驅動，可掛載工具。",
            ExampleJson: """
                {
                  "type": "agent",
                  "name": "名稱（英文，如 Researcher）",
                  "instructions": "Agent 的系統指令（繁體中文）",
                  "model": { "provider": "openai", "model": "gpt-4o" },
                  "tools": [],
                  "skills": []
                }
                """,
            Notes: """
                - `model.provider` 與 `model.model` 可用組合（自動從 Providers.Catalog 產生）：
                {PROVIDERS_SECTION}
                - `tools`: 內建工具 ID 列表（ID 使用底線 `_`，不是連字號 `-`），可選（自動從 ToolRegistryService 產生）：
                {TOOLS_SECTION}
                - `skills`: Skill ID 列表，為 Agent 注入領域知識/方法論/人設，可選（自動從 SkillRegistryService 產生）：
                {SKILLS_SECTION}
                """),

        new(
            Type: NodeTypes.Condition,
            ShortName: "條件分支節點",
            Description: "根據前一個節點的輸出做判斷，有 2 個輸出口（output_1=True, output_2=False）。",
            ExampleJson: """
                {
                  "type": "condition",
                  "name": "CheckApproval",
                  "condition": { "kind": "contains", "value": "APPROVED" }
                }
                """,
            Notes: "- `condition.kind`: contains, regex, llmJudge, expression"),

        new(
            Type: NodeTypes.Loop,
            ShortName: "迴圈節點",
            Description: "反覆執行直到條件滿足，有 2 個輸出口（output_1=Body 繼續, output_2=Exit 跳出）。",
            ExampleJson: """
                {
                  "type": "loop",
                  "name": "ReviewLoop",
                  "condition": { "kind": "contains", "value": "APPROVED" },
                  "maxIterations": 3
                }
                """),

        new(
            Type: NodeTypes.Router,
            ShortName: "路由節點",
            Description: "將輸入分派到多個分支，有 N 個輸出口（output_1..output_N）。",
            ExampleJson: """
                {
                  "type": "router",
                  "name": "Router",
                  "routes": [
                    { "name": "billing", "keywords": ["billing", "charge"], "isDefault": false },
                    { "name": "technical", "keywords": ["bug", "error"], "isDefault": false },
                    { "name": "general", "keywords": [], "isDefault": true }
                  ]
                }
                """,
            Notes: """
                - `routes`: RouteConfig 陣列，每個有 name/keywords/isDefault
                - 最後一個路由建議設 `isDefault: true` 作為 fallback
                - 路由前通常需要一個分類 Agent
                """),

        new(
            Type: NodeTypes.Human,
            ShortName: "人工介入節點",
            Description: "暫停流程等待使用者輸入。有 2 個輸出口（output_1=approve, output_2=reject，僅 approval 模式）。",
            ExampleJson: """
                {
                  "type": "human",
                  "name": "HumanReview",
                  "prompt": "請審閱以上內容是否正確？",
                  "kind": "approval",
                  "timeoutSeconds": 0
                }
                """,
            Notes: """
                - `kind`: text（自由文字）, choice（選擇）, approval（核准/拒絕）
                - `choices`: 僅 choice 模式，字串陣列，如 ["選項A", "選項B", "選項C"]
                """),

        new(
            Type: NodeTypes.Code,
            ShortName: "程式碼轉換節點",
            Description: "確定性資料轉換，不需 LLM。",
            ExampleJson: """
                {
                  "type": "code",
                  "name": "Transform",
                  "kind": "template",
                  "expression": "摘要：{{input}}"
                }
                """,
            Notes: """
                - `kind`: template, regex, jsonPath, trim, split, upper, lower, truncate, script
                - template 模式用 `{{input}}` 佔位符
                - regex 模式加 `"replacement": "$1"`
                - script 模式加 `"language": "javaScript"` 或 `"cSharp"`
                """),

        new(
            Type: NodeTypes.Parallel,
            ShortName: "並行節點",
            Description: "多個分支同時執行（fan-out / fan-in）。分支數量動態設定，最後一個 output port 是 Done。",
            ExampleJson: """
                {
                  "type": "parallel",
                  "name": "MultiExpert",
                  "branches": [
                    { "name": "Legal", "goal": "", "tools": [] },
                    { "name": "Technical", "goal": "", "tools": [] },
                    { "name": "Financial", "goal": "", "tools": [] }
                  ],
                  "merge": "labeled"
                }
                """,
            Notes: """
                - `branches`: BranchConfig 陣列（name + goal + tools），2-8 個
                - `merge`: labeled（帶標題合併）、join（逐行合併）、json（JSON 物件）
                - output_1..N 分別連到各分支的節點鏈，output_(N+1) 是 Done port
                - 所有分支同時執行（Task.WhenAll），完成後合併結果從 Done port 輸出
                - Done port 後面應接一個 Synthesizer Agent 彙整結果
                """),

        new(
            Type: NodeTypes.Iteration,
            ShortName: "迭代節點",
            Description: "對輸入陣列中的每個元素依序執行子流程（foreach）。有 2 個輸出口（output_1=Body 每個元素, output_2=Done 完成後）。",
            ExampleJson: """
                {
                  "type": "iteration",
                  "name": "ProcessItems",
                  "split": "jsonArray",
                  "maxItems": 50
                }
                """,
            Notes: """
                - `split`: jsonArray（預設，自動解析 JSON 陣列）或 delimiter（用分隔符切割）
                - `delimiter`: 僅 delimiter 模式，預設 `\n`
                - `maxItems`: 防護上限，預設 50
                - output_1 連接的節點鏈會對**每個元素**執行一次
                - 所有結果彙整後從 output_2 輸出
                """),

        new(
            Type: NodeTypes.A2AAgent,
            ShortName: "遠端 A2A Agent 節點",
            Description: "呼叫遠端 Agent-to-Agent 協定的 Agent，不需本地 LLM。",
            ExampleJson: """
                {
                  "type": "a2a-agent",
                  "name": "RemoteAgent",
                  "url": "http://remote-server/a2a",
                  "format": "auto"
                }
                """,
            Notes: """
                - `url`: 遠端 A2A Agent 的 URL
                - `format`: auto（自動偵測）、google（Google A2A 格式）、microsoft（Microsoft A2A 格式）
                - 有 A2A Agent 節點時強制 Imperative 策略
                - 超時 300 秒
                """),

        new(
            Type: NodeTypes.HttpRequest,
            ShortName: "HTTP 請求節點",
            Description: "不經 LLM，直接呼叫 HTTP API。零 token 成本，確定性執行。",
            ExampleJson: """
                {
                  "type": "http-request",
                  "name": "PostToAPI",
                  "spec": {
                    "kind": "inline",
                    "url": "https://api.example.com/v1/data",
                    "method": "post",
                    "headers": [{ "name": "Authorization", "value": "Bearer xxx" }],
                    "body": { "content": {"query": "{input}"} },
                    "contentType": "application/json",
                    "auth": { "kind": "none" },
                    "retry": { "count": 0, "delayMs": 1000 },
                    "timeoutSeconds": 15,
                    "response": { "kind": "json" },
                    "responseMaxLength": 2000
                  }
                }
                """,
            Notes: """
                - `spec.kind`: `inline`（就地定義完整 HTTP 請求）
                - `spec.method`: get / post / put / patch / delete
                - `spec.headers`: `[{ "name": "Key", "value": "Value" }]` 陣列格式
                - `spec.body.content`: 請求 body（JSON 物件或字串），`{input}` 替換為前一節點輸出
                - `spec.auth.kind`: none / bearer（+ token）/ basic（+ userPass）/ apikey-header（+ keyName + value）
                - `spec.response.kind`: text / json / jsonPath（+ path）
                """),

        new(
            Type: NodeTypes.Rag,
            ShortName: "RAG 節點",
            Description: "連接到 Agent 後啟用向量檢索增強。不獨立執行，需連線到 Agent。",
            ExampleJson: """
                {
                  "type": "rag",
                  "name": "KnowledgeBase"
                }
                """),

        new(
            Type: NodeTypes.Autonomous,
            ShortName: "Autonomous Agent 節點",
            Description: "觸發 ReAct 自主推理迴圈，處理複雜開放式任務。",
            ExampleJson: """
                {
                  "type": "autonomous",
                  "name": "Investigator",
                  "instructions": "深入調查並回答使用者的問題",
                  "model": { "provider": "openai", "model": "gpt-4o" },
                  "maxIterations": 15
                }
                """,
            Notes: """
                - 使用時機：任務需要自主多步推理 + 動態工具選擇（例如「調查 X 並產出報告」）
                - 會自動注入所有可用工具（包括 MCP / A2A / HTTP API）
                - `maxIterations`: ReAct 迴圈上限，預設 25
                """)
    ];

    /// <summary>
    /// 組合所有 NodeSpec 為 LLM prompt 的 "## 節點類型規格" 段落。
    /// 輸出格式逐字對齊原 <c>FlowBuilderService.PromptTemplate</c> 中的節點描述段落。
    /// </summary>
    public static string BuildMarkdownSection()
    {
        var sb = new StringBuilder();
        foreach (var spec in All)
        {
            sb.AppendLine($"### {spec.Type} — {spec.ShortName}");
            sb.AppendLine(spec.Description);
            sb.AppendLine("```json");
            sb.AppendLine(spec.ExampleJson);
            sb.AppendLine("```");
            if (!string.IsNullOrWhiteSpace(spec.Notes))
            {
                sb.AppendLine(spec.Notes);
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
