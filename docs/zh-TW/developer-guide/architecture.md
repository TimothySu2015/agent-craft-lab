# AgentCraftLab 系統架構指南

本文件面向希望理解或擴展 AgentCraftLab 的開發者，涵蓋核心架構、執行流程與擴展機制。

---

## 1. Solution 總覽 -- Open Core 架構

AgentCraftLab 採用 Open Core 模式，核心引擎開源，商業功能獨立封裝。

| 專案 | 定位 |
|------|------|
| `AgentCraftLab.Api` | 純後端 API（AG-UI + REST，Minimal API 端點，port 5200） |
| `AgentCraftLab.Web` | React 前端（React Flow + CopilotKit + shadcn/ui，port 5173） |
| `AgentCraftLab.Search` | 獨立搜尋引擎類別庫（FTS5 + 向量 + RRF 混合搜尋） |
| `AgentCraftLab.Engine` | 開源核心引擎（SQLite + 單人模式，策略 + 節點 + 工具 + Middleware + Hooks） |
| `AgentCraftLab.Autonomous` | ReAct 迴圈 + Sub-agent 協作 + 12 meta-tools + 安全機制 |
| `AgentCraftLab.Autonomous.Flow` | Flow 結構化執行（LLM 規劃 -> 7 種節點 -> Crystallize） |
| `AgentCraftLab.Autonomous.Playground` | CLI 測試控制台（Spectre.Console） |
| `AgentCraftLab.Script` | JS 沙箱引擎（Jint，IScriptEngine 介面可替換 Roslyn/Python） |
| `AgentCraftLab.Ocr` | OCR 引擎（Tesseract，IOcrEngine 介面） |
| `AgentCraftLab.Commercial` | 商業層（MongoDB + OAuth，不開源） |
| `AgentCraftLab` | Blazor Web App（舊版 UI，Drawflow 畫布） |

**技術棧：** .NET 10 + LangVersion 13.0，使用 `Microsoft.Agents.AI` 系列 API（禁止 Semantic Kernel）。

**功能歸屬決策：** 新功能先問「單人自用是否需要？」-- 需要放 Engine；多人/計費/SSO 放 Commercial；搜尋/擷取/分塊放 Search。

---

## 2. Open Core 模式切換

系統透過偵測 `ConnectionStrings:MongoDB` 是否存在，在啟動時自動切換模式：

```
                  ConnectionStrings:MongoDB 存在？
                          |
               +----------+----------+
               |                     |
              否                    是
               |                     |
        開源模式（預設）         商業模式
        - SQLite               - MongoDB (Azure DocumentDB)
        - 無認證               - Google/GitHub OAuth
        - userId="local"       - 多使用者
        - Sqlite*Store         - Mongo*Store
```

所有 Store 介面（IWorkflowStore、ICredentialStore 等）都有 SQLite 和 MongoDB 兩套實作，DI 容器在啟動時根據設定註冊對應實作。

---

## 3. Workflow 執行三層架構

Workflow 執行是系統的核心路徑，分為三層：

```
WorkflowExecutionService.ExecuteAsync(request)        <-- 精簡編排器（~180 行）
  |
  +-> ParseAndValidatePayload                         <-- 驗證 JSON payload
  +-> Hook(OnInput)                                   <-- 輸入攔截
  +-> WorkflowPreprocessor.PrepareAsync                <-- 第二層：節點分類 + RAG + AgentContext
  |     |
  |     +-> 分類節點（executable / data / meta）
  |     +-> 解析 RAG 節點，執行 ingest
  |     +-> AgentContextBuilder 建構每個 agent 的 context
  |
  +-> WorkflowStrategyResolver.Resolve                 <-- 第三層：策略選擇
  +-> IWorkflowStrategy.ExecuteAsync                   <-- 策略執行
  +-> Hook(OnComplete / OnError)                       <-- 完成/錯誤回呼
  +-> yield IAsyncEnumerable<ExecutionEvent>            <-- 串流輸出
```

### 各層職責

| 層級 | 類別 | 職責 |
|------|------|------|
| 編排層 | `WorkflowExecutionService` | 組裝管線、錯誤處理、Hooks 呼叫 |
| 預處理層 | `WorkflowPreprocessor` | 節點分類、RAG 索引、AgentContext 建構 |
| 策略層 | `IWorkflowStrategy` | 具體執行邏輯（依拓撲決定執行順序） |

---

## 4. 五種執行策略與自動偵測

### 策略一覽

| 策略 | 說明 | 適用場景 |
|------|------|----------|
| Single | 單一 agent 直接執行 | 僅一個 agent 節點 |
| Sequential | 依拓撲排序逐一執行 | 線性 pipeline |
| Concurrent | 多 agent 並行執行 | 獨立 agent、無依賴 |
| Handoff | Agent 間交接控制權 | 任一 agent 有多條 outgoing edge |
| Imperative | 命令式逐步執行（支援分支/迴圈） | 包含 condition/loop/code 等控制流節點 |

### 自動偵測邏輯

```
NodeTypeRegistry.HasAnyRequiringImperative() == true ?
  |-- 是 --> Imperative 策略
  |-- 否 --> 任一 agent 有多條 outgoing edge ?
                |-- 是 --> Handoff 策略
                |-- 否 --> agent 數量 == 1 ?
                              |-- 是 --> Single 策略
                              |-- 否 --> Sequential 策略
```

`WorkflowStrategyResolver.Resolve()` 封裝此邏輯。擴展新策略：實作 `IWorkflowStrategy` 介面 + 在 Resolve 加入 case。

---

## 5. 節點系統

### NodeTypeRegistry -- 唯一真相來源

所有節點的元資料集中在 `NodeTypeRegistry`，每個節點型別定義三個旗標：

| 節點 | IsExecutable | RequiresImperative | IsAgentLike | 說明 |
|------|:---:|:---:|:---:|------|
| `agent` | Y | | Y | 本地 LLM Agent（ChatClientAgent + tools） |
| `a2a-agent` | Y | Y | Y | 遠端 A2A Agent（URL + format） |
| `autonomous` | Y | Y | Y | ReAct 迴圈（介面解耦） |
| `condition` | Y | Y | | 條件分支 |
| `loop` | Y | Y | | 迴圈 |
| `router` | Y | Y | | 多路由分類 |
| `human` | Y | Y | | 暫停等使用者輸入 |
| `code` | Y | Y | | 確定性轉換（9 種模式 + JS 沙箱） |
| `iteration` | Y | Y | | foreach 迴圈（SplitMode + MaxItems 50） |
| `parallel` | Y | Y | | fan-out/fan-in 並行 |
| `http-request` | Y | Y | | 確定性 HTTP 呼叫 |
| `start` / `end` | | | | Meta 節點（IsMeta） |
| `rag` / `tool` | | | | 資料節點（IsDataNode） |

### NodeExecutorRegistry

每個可執行節點型別對應一個 executor handler。執行引擎透過 `NodeExecutorRegistry` 查表分派。

### 新增節點步驟

1. `NodeTypes` 類別加入常數字串
2. `NodeTypeRegistry` 加入一行元資料定義
3. `NodeExecutorRegistry` 加入對應的 handler
4. （前端）JS `NODE_REGISTRY` 加入節點渲染定義

---

## 6. Agent 工具解析 -- 四層工具來源

`AgentContextBuilder.ResolveToolsAsync()` 負責合併所有工具來源：

```
+------------------------------------------------------+
|              AgentContextBuilder                      |
|                                                       |
|  Layer 1: Tool Catalog（內建工具）                     |
|    - ToolRegistryService 註冊的靜態工具               |
|    - web_search, file_read, calculator 等             |
|                                                       |
|  Layer 2: MCP Servers（動態外部工具）                  |
|    - 透過 MCP 協定連接外部 Tool Server                |
|    - 動態列舉可用工具                                 |
|                                                       |
|  Layer 3: A2A Agents（Agent-to-Agent 互叫）           |
|    - 遠端 Agent 作為工具暴露                          |
|    - 支援 Google / Microsoft 兩種格式                 |
|                                                       |
|  Layer 4: HTTP APIs + OCR + Script                    |
|    - http-request 節點的 API 呼叫                     |
|    - OCR（IOcrEngine，Tesseract）                     |
|    - Script（IScriptEngine，Jint JS 沙箱）            |
|                                                       |
|  --> 合併為統一的 AITool[] 交給 ChatClientAgent        |
+------------------------------------------------------+
```

每個 agent 節點的 `tools` 欄位引用 tool ID 清單，ResolveToolsAsync 根據 ID 從四層來源中撈出對應工具實例。

---

## 7. Middleware Pipeline

Middleware 採用 `DelegatingChatClient`（裝飾者模式），由 `AgentContextBuilder.ApplyMiddleware()` 依序包裝：

```
外層                                              內層
  |                                                 |
  v                                                 v
GuardRails --> PII --> RateLimit --> Retry --> Logging --> ChatClient
```

RAG 獨立於此管線掛載（透過 `RagChatClient`）。

### 7.1 GuardRails — 企業級內容安全

透過 `IGuardRailsPolicy` 介面解耦，預設實作 `DefaultGuardRailsPolicy`，可替換為 ML 分類器、Azure Content Safety、NVIDIA NeMo Guardrails。

| 功能 | 說明 |
|------|------|
| 關鍵字 + Regex 規則 | `text.Contains()`（CJK 安全）+ `RegexOptions.Compiled` |
| 三級動作 | Block（封鎖回傳拒絕訊息）、Warn（警告但放行）、Log（靜默記錄） |
| Prompt Injection 偵測 | 9 種內建 pattern（中英文），opt-in 啟用 |
| Topic 限制 | 限定 Agent 只能討論白名單主題 |
| 全訊息掃描 | 預設掃描所有 User 訊息（不只最後一則），防止多輪攻擊 |
| Output 掃描 | 可選掃描 LLM 回應（串流模式 buffer 後掃描） |
| 審計日誌 | `[GUARD] Direction=Input, Action=Block, Rule="hack", Match="hack"` |
| 前端設定 | Blocked/Warn Terms、Regex Rules、Allowed Topics、Injection Detection、自訂封鎖回應 |

### 7.2 PII 保護 — 企業級個資偵測與匿名化

透過 `IPiiDetector` + `IPiiTokenVault` 介面解耦，可替換為 ONNX NER 模型、Microsoft Presidio、Azure AI Language。

| 功能 | 說明 |
|------|------|
| 35 條 Regex 規則 × 6 Locale | Global / TW / JP / KR / US / UK，涵蓋 GDPR/HIPAA/PCI-DSS |
| 7 種 Checksum 驗證 | Luhn（信用卡）、mod97（IBAN）、台灣身分證/統編、JP My Number、KR RRN、UK NHS |
| Context-aware 加權 | 掃描前後文關鍵字提升信賴度，減少誤判 |
| 可逆 Tokenization | `[EMAIL_1]`、`[PHONE_1]` 等型別 token，LLM 回應後自動還原 |
| 不可逆模式 | `***` 固定替換（向下相容） |
| 雙向掃描 | Input anonymize + Output detokenize |
| 審計日誌 | `[PII] Direction=Input, Entities=[Global.Email:1, TW.Phone:1], Count=2`（絕不記錄原始 PII） |
| 前端設定 | Mode（reversible/irreversible）、Locale 多選、Confidence Threshold、Scan Output |

### 7.3 RateLimit — Token Bucket 限流

使用 `System.Threading.RateLimiting.TokenBucketRateLimiter`（預設每秒 5 次）。Queue 容量 10、FIFO 排序。`AcquireAsync` 有 30 秒 timeout 保護。

### 7.4 Retry — 指數退避重試

預設最多 3 次重試，指數退避（500ms → 1s → 2s）。`IsTransient` 使用 `HttpRequestException.StatusCode` pattern matching（429/502/503/504）+ `TaskCanceledException` + `TimeoutException`。重試耗盡時記錄 `LogError`。串流模式僅在第一個 chunk 前的失敗才重試。

### 7.5 Logging — 結構化日誌

記錄輸入（截斷至 100 字元）+ 耗時。非串流/串流都有 try-catch 記錄例外（含耗時）。

### 7.6 RAG — 檢索增強

獨立掛載（不在 ApplyMiddleware 管線中）。支援臨時索引 + 多知識庫並行搜尋（`Task.WhenAll`）。搜尋失敗 graceful degradation（不阻斷，記錄 Warning）。

### 設計亮點

- **介面解耦**：GuardRails（`IGuardRailsPolicy`）和 PII（`IPiiDetector` + `IPiiTokenVault`）均透過介面抽象，可在不修改 Middleware 的情況下替換為 ML/雲端服務
- **DI 智慧重用**：`ApplyMiddleware` 優先使用 DI singleton，僅在前端指定自訂規則時才建立新實例
- **雙建構子**：每個強化過的 Middleware 都有新版（DI 介面注入）+ 舊版（config dictionary）建構子，完全向下相容
- **防禦性程式設計**：RateLimit 有 timeout 保護、Retry 有 StatusCode pattern matching、Logging 記錄例外、RAG graceful degradation
- **企業合規**：PII 審計日誌符合 GDPR Art.30（絕不記錄原始 PII）；GuardRails 支援 Prompt Injection 偵測和 Topic 限制

**擴展方式：** 繼承 `DelegatingChatClient` 實作新 Middleware，在 `ApplyMiddleware()` 加入對應 case。或實作 `IGuardRailsPolicy` / `IPiiDetector` 替換偵測引擎。

---

## 8. Workflow Hooks

Hooks 提供 6 個生命週期插入點，在 workflow 執行的不同階段觸發：

```
使用者輸入
    |
    v
 OnInput ---------> 可攔截/轉換輸入
    |
 PreExecute ------> workflow 開始前
    |
    +-- 節點迴圈 --+
    |              |
    | PreAgent     | --> 每個 agent 執行前
    | PostAgent    | --> 每個 agent 執行後
    |              |
    +--------------+
    |
 OnComplete ------> 成功完成
 OnError ---------> 執行失敗
```

### Hook 類型

| 類型 | 機制 |
|------|------|
| `code` | 透過 TransformHelper 執行（支援 9 種轉換模式） |
| `webhook` | HTTP POST 到指定 URL |

`BlockPattern` 支援 regex 攔截 -- 若輸入匹配 pattern，直接拒絕執行。

---

## 9. Credentials 後端加密儲存

API 金鑰全程在後端處理，前端不接觸明文：

```
React /settings 頁面
    |
    | POST /api/credentials { provider, apiKey }
    v
ICredentialStore.SaveAsync()
    |
    | DPAPI 加密（Windows Data Protection API）
    v
SQLite / MongoDB 儲存（密文）

--- 執行時 ---

WorkflowExecutionService
    |
    | ResolveCredentialsAsync()
    v
ICredentialStore.GetDecryptedCredentialsAsync()
    |
    | DPAPI 解密
    v
明文 API Key --> 注入 ChatClient
```

前端以 `saved` flag 判斷金鑰是否已設定，`localStorage` 不存放任何明文。

---

## 10. Chat 附件上傳管線

CopilotKit 原生僅支援圖片上傳且實作未完成，因此採用獨立上傳管線：

```
使用者選擇檔案
    |
    | POST /api/upload（multipart/form-data）
    v
後端暫存（1 小時 TTL）--> 回傳 { fileId }
    |
    | CopilotKit forwardedProps.fileId
    v
AG-UI 端點收到 fileId
    |
    | GetAndRemove(fileId) --> 取出檔案、移除暫存
    v
WorkflowPreprocessor 處理
    |
    +-- 文件類型 --> RAG ingest（擷取 + 分塊 + 索引）
    +-- ZIP 檔案 --> 解壓 + 批次處理
    +-- 圖片/其他 --> 多模態 DataContent（直接注入 LLM）
```

前端使用 `StableChatInput`（module-scope 定義）+ `chatInputFileRef` 確保 component identity 穩定，避免 CopilotChat 重建 Input 元件。

---

## 11. Autonomous Agent -- ReAct + Flow 雙模式

### 三層執行漏斗

```
+----------------------------------------------------------+
|  Engine Workflow（人類設計）                               |
|  - 開發者手動拖拉節點、定義流程                            |
|  - 完全確定性                                             |
+----------------------------------------------------------+
                        |
                        v
+----------------------------------------------------------+
|  Flow（AI 規劃 + 結構化執行 + Crystallize）                |
|  - IGoalExecutor 介面                                     |
|  - LLM 生成 FlowPlan --> 7 種節點結構化執行                |
|  - 完成後 Crystallize 為可編輯 Workflow                    |
+----------------------------------------------------------+
                        |
                        v
+----------------------------------------------------------+
|  ReAct（完全自主）                                         |
|  - ReactExecutor（~540 行）                                |
|  - 觀察 -> 思考 -> 行動 迴圈                               |
|  - 12 meta-tools + Sub-agent 協作                         |
+----------------------------------------------------------+
```

### ReAct 模式核心

**策略物件拆分：**

| 介面 | 職責 |
|------|------|
| `IBudgetPolicy` | Token/步數預算控制 |
| `IHumanInteractionHandler` | 人機互動（ask_user） |
| `IHistoryManager` | 對話歷史管理 |
| `IReflectionEngine` | 自我反思（Reflexion） |
| `IToolDelegationStrategy` | 工具選擇與委派 |

**雙模型架構：** TaskPlanner 用強模型（gpt-4o）規劃，ReactExecutor 用弱模型（gpt-4o-mini）執行，降低成本。

**12 meta-tools（MetaToolFactory）：**
- Sub-agent 管理：create / ask / spawn / collect / stop / send / list
- 共享狀態：shared_state
- 人機互動：ask_user
- 品質控制：peer_review / challenge

**安全等級：** P0 Risk 審批 -> P1 Transparency -> P2 Self-Reflection -> S1~S8 隔離保護

### Flow 模式核心

透過 `IGoalExecutor` 介面與 ReAct 隔離。DI 切換：

```csharp
// ReAct 模式
services.AddAutonomousAgent();

// Flow 模式
services.AddAutonomousFlowAgent();
```

**漏斗銜接：** ReactTraceConverter 將 spawn/collect 軌跡轉為 FlowPlan JSON，存入 ExecutionMemoryService，下次 Flow 規劃時作為 Reference Plan 注入。

**Crystallize：** 執行完成後，將 ExecutionTrace 轉為 Studio buildFromAiSpec JSON，存入 `Data/flow-outputs/`，可直接載入 Workflow Studio 編輯。

---

## 12. CraftSearch 搜尋引擎

`AgentCraftLab.Search` 是獨立類別庫，不依賴 Engine 或 Autonomous。

### 核心介面

```
ISearchEngine          --> 搜尋入口（query + options）
IDocumentExtractor     --> 文件內容擷取（PDF/DOCX/HTML/TXT...）
ITextChunker           --> 文字分塊（固定大小 + 重疊）
```

### 三種搜尋模式

```
+------------------+     +------------------+     +------------------+
|   FullText       |     |   Vector         |     |   Hybrid         |
|   (FTS5)         |     |   (SIMD Cosine)  |     |   (RRF k=60)    |
|                  |     |                  |     |                  |
|  SQLite FTS5     |     |  1536 維向量     |     |  FullText 排名   |
|  分詞 + BM25     |     |  餘弦相似度      |     |  + Vector 排名   |
|                  |     |  SIMD 加速       |     |  RRF 融合        |
+------------------+     +------------------+     +------------------+
```

**RRF（Reciprocal Rank Fusion）：** 以 k=60 將全文和向量兩種排名融合，兼顧關鍵字精確匹配和語意相似度。

**Provider 實作：**
- `SqliteSearchEngine` -- 生產環境（開源）
- `InMemorySearchEngine` -- 單元測試

---

## 13. RAG Pipeline

RAG 功能建構在 CraftSearch 之上，提供完整的擷取增強生成管線。

### Ingest 流程

```
檔案上傳
    |
    v
IDocumentExtractor.ExtractAsync()     --> 擷取文字（多格式支援）
    |
    v
ITextChunker.ChunkAsync()            --> 分塊（固定大小 + 重疊視窗）
    |
    v
Embedding（1536 維）                  --> 向量化
    |
    v
ISearchEngine.IndexAsync()           --> 建立索引
```

### 查詢流程

```
使用者問題
    |
    v
RagService.SearchAsync()             --> Hybrid 搜尋（FTS5 + Vector + RRF）
    |
    v
RagChatClient（DelegatingChatClient） --> 將搜尋結果注入 system message
    |
    v
LLM 回應（基於檢索到的上下文）
```

### indexName 慣例

| 格式 | 用途 |
|------|------|
| `{userId}_rag_{guid}` | 臨時索引（單次上傳） |
| `{userId}_kb_{id}` | 知識庫索引（持久化） |

---

## 14. CopilotKit 前端架構

### 系統全景

```
+-------------------+     +-------------------+     +-------------------+
|   React 前端      |     |  CopilotKit       |     |  .NET API 後端    |
|   (port 5173)     |     |  Runtime          |     |  (port 5200)      |
|                   |     |  (port 4000)      |     |                   |
|  React Flow       | --> |  Node.js          | --> |  Minimal API      |
|  CopilotKit SDK   |     |  server.mjs       |     |  AG-UI 端點       |
|  shadcn/ui        |     |  AG-UI 協定轉接   |     |  WorkflowEngine   |
|  i18n (en/zh-TW)  |     |                   |     |  CraftSearch      |
+-------------------+     +-------------------+     +-------------------+
      Vite dev                  中間層                   後端服務
```

### AG-UI 協定

CopilotKit Runtime 作為中間層，將前端的 CopilotKit 格式轉為 AG-UI（Agent-UI）協定與 .NET 後端溝通。後端透過 `IAsyncEnumerable<ExecutionEvent>` 串流事件回前端。

### 前端主要模組

| 模組 | 說明 |
|------|------|
| Workflow Studio | React Flow 畫布，拖拉建構 workflow |
| Chat Panel | CopilotChat 整合，支援附件上傳 |
| Settings | 個人設定、Credentials、預設模型 |
| Skill Manager | 技能管理（內建 + 自訂） |
| Service Tester | 雙面板 + Chat，5 種協定測試 |
| KB Manager | 知識庫檔案上傳 + SSE 進度串流 |

### 關鍵設計決策

- **獨立上傳管線：** CopilotKit 原生上傳限制多，改用 `POST /api/upload` 獨立上傳
- **ErrorBoundary：** 全域錯誤邊界，React 元件崩潰不會導致白屏
- **i18n：** 支援 en + zh-TW，分為 common / studio / chat 三個 namespace
- **StableChatInput：** module-scope 定義，避免 CopilotChat 重建導致輸入框狀態遺失

---

## 附錄：擴展速查表

| 擴展項目 | 步驟 |
|----------|------|
| 新執行策略 | 實作 `IWorkflowStrategy` + `WorkflowStrategyResolver.Resolve()` 加 case |
| 新節點類型 | `NodeTypes` 常數 + `NodeTypeRegistry` 元資料 + `NodeExecutorRegistry` handler + JS `NODE_REGISTRY` |
| 新內建工具 | `ToolImplementations.cs` 加方法 + `ToolRegistryService.Register()` |
| 新 Middleware | 繼承 `DelegatingChatClient` + `ApplyMiddleware()` 加 case |
| 新 Flow 節點 | `FlowNodeRunner` case + `FlowPlannerPrompt` + `FlowPlanValidator` + `WorkflowCrystallizer` |
| 替換腳本引擎 | 實作 `IScriptEngine` + DI 替換（Jint -> Roslyn -> Python） |
| 替換 OCR 引擎 | 實作 `IOcrEngine` + DI 替換 |
| 新工具模組 | 參考 `AgentCraftLab.Ocr` / `AgentCraftLab.Script` 的 `AddXxx()` + `UseXxxTools()` pattern |
| 替換 Autonomous 策略 | 實作對應介面（IBudgetPolicy 等）+ DI `Replace` 註冊 |
