# Workflow 執行與服務發布

本文件涵蓋 AgentCraftLab 的 Workflow 執行方式、服務發布流程，以及相關設定。

---

## Part 1: 執行 Workflow

### 1.1 Execute Chat

在 Workflow Studio 中切換至 **Execute** 頁籤即可執行目前的 Workflow。輸入訊息後按下送出，系統會透過 AG-UI Protocol 以 SSE 串流方式即時回傳執行過程與結果。

執行流程如下：

1. 使用者在 Execute Chat 輸入訊息
2. 後端 `WorkflowExecutionService.ExecuteAsync()` 接收請求
3. 經過 Hook（OnInput）、前處理（WorkflowPreprocessor）、策略選擇後執行
4. 執行過程以 `IAsyncEnumerable<ExecutionEvent>` 串流回前端
5. 前端即時顯示各 Agent 的回應文字、工具呼叫結果等

### 1.2 Chat 附件上傳

Execute Chat 支援檔案附件功能，操作方式：

1. 點選聊天輸入框旁的附件按鈕
2. 選擇檔案（支援 PDF、DOCX、圖片等格式，上限 32MB）
3. 檔案上傳至 `POST /api/upload`，取得 `fileId`（暫存 1 小時）
4. 送出訊息時，`fileId` 隨請求一併傳送至後端
5. 後端依檔案類型決定處理方式：RAG ingest、ZIP 解壓、或作為多模態 DataContent 傳入 Agent

由於 CopilotKit 原生僅支援圖片上傳且實作未完成，系統採用獨立上傳管線繞過此限制。

### 1.3 五種執行策略

系統根據 Workflow 的節點組成自動選擇執行策略，使用者無需手動設定。

| 策略 | 說明 | 自動偵測條件 |
|------|------|-------------|
| **Single** | 單一 Agent 直接執行 | Workflow 僅含一個可執行節點 |
| **Sequential** | 依序執行多個 Agent | 多個 Agent，各僅一條 outgoing 連線 |
| **Concurrent** | 多個 Agent 同時並行執行 | 明確標記為並行的節點群組 |
| **Handoff** | Agent 間交接控制權 | 任一 Agent 有多條 outgoing 連線 |
| **Imperative** | 命令式流程控制 | 包含 condition、loop、human 等流程控制節點 |

自動偵測邏輯：`NodeTypeRegistry.HasAnyRequiringImperative()` 查詢是否有需要 Imperative 的節點類型（如 condition、loop、human、code、iteration、parallel 等）。若有則使用 Imperative；若任一 Agent 有多條 outgoing 則使用 Handoff；其餘依節點數量選擇 Single 或 Sequential。

### 1.4 Human Input 節點

Human 節點會在執行過程中暫停，等待使用者輸入後才繼續。支援三種互動模式：

- **text** -- 自由文字輸入，使用者可輸入任意回覆
- **choice** -- 選擇題，從預設選項中選擇一個
- **approval** -- 核准/拒絕，用於審批流程

當執行到 Human 節點時，系統發出 `WaitingForInput` 事件，前端顯示對應的輸入介面。使用者提交後發出 `UserInputReceived` 事件，流程繼續執行。

### 1.5 Execution Events

執行過程中，系統以事件流（Event Stream）方式回報進度。主要事件類型：

| 事件 | 說明 |
|------|------|
| `AgentStarted` | Agent 開始執行（含 Agent 名稱） |
| `TextChunk` | 串流文字片段（即時輸出） |
| `AgentCompleted` | Agent 執行完成（含完整回應） |
| `ToolCall` | 工具呼叫（含工具名稱與參數） |
| `ToolResult` | 工具回傳結果 |
| `WaitingForInput` | 等待使用者輸入（Human 節點暫停） |
| `UserInputReceived` | 使用者輸入已接收 |
| `RagProcessing` / `RagReady` | RAG 管線處理狀態 |
| `HookExecuted` / `HookBlocked` | Hook 執行結果 |
| `WorkflowCompleted` | 整個 Workflow 執行完成 |
| `Error` | 錯誤事件 |

這些事件經 `AgUiEventConverter` 轉換為 AG-UI Protocol 格式後，透過 SSE 推送至前端。

### 1.6 Autonomous 模式

Autonomous 節點提供 AI 自主執行能力，分為兩種模式：

**ReAct 模式（完全自主）：** ReactExecutor 以 Reasoning-Acting 迴圈運作。AI 自行決定下一步行動、呼叫工具、觀察結果，直到任務完成。支援 12 種 meta-tools（建立/詢問/生成 sub-agent、共享狀態、請求使用者確認等）。採用雙模型架構 -- TaskPlanner 用強模型規劃，ReactExecutor 用弱模型執行。

**Flow 模式（結構化執行）：** LLM 先規劃執行計畫（FlowPlan），再依計畫逐步執行 7 種節點（agent / code / condition / iteration / parallel / loop / http-request）。執行完成後可透過 Crystallize 將結果轉為可重複使用的 Workflow。

三層漏斗關係：Engine Workflow（人類設計）> Flow（AI 規劃 + 結構化執行）> ReAct（完全自主）。

---

## Part 2: 發布服務

### 2.1 Publish Workflow

前往 `/published-services` 頁面管理 Workflow 的發布狀態。

**啟用/停用發布：** 每個 Workflow 可獨立啟用或停用發布。啟用後，該 Workflow 即可透過 API 呼叫或其他協定存取。

**Input Modes：** 發布時可設定接受的輸入格式：

- **text/plain** -- 純文字輸入（預設）
- **application/pdf**、**application/vnd.openxmlformats-officedocument.wordprocessingml.document** 等 -- 檔案輸入
- **application/json** -- 結構化 JSON 輸入

Input Modes 決定了外部呼叫者可以傳送哪些格式的資料給該 Workflow。

### 2.2 API Keys 管理

前往 `/api-keys` 頁面管理 API 金鑰。

**建立 API Key：**

1. 點選建立按鈕
2. 輸入名稱描述（方便辨識用途）
3. 可選擇 Scope 限定此 Key 僅能存取特定 Workflow
4. 系統產生金鑰，僅顯示一次，請妥善保存

**Scope 限定：** 建立 Key 時可指定僅允許存取哪些已發布的 Workflow，實現最小權限原則。未指定 Scope 則可存取所有已發布的 Workflow。

**撤銷 Key：** 在 API Keys 列表中可隨時撤銷不再使用的金鑰，撤銷後立即生效，該 Key 將無法再用於任何請求。

### 2.3 Service Tester

前往 `/service-tester` 頁面測試已發布的服務或外部端點。採用雙面板設計（設定面板 + 對話面板），支援 5 種協定：

| 協定 | 說明 |
|------|------|
| **AG-UI** | AgentCraftLab 原生的 Agent-UI 串流協定 |
| **A2A** | Google Agent-to-Agent 協定，用於跨服務 Agent 通訊 |
| **MCP** | Model Context Protocol，用於工具整合測試 |
| **HTTP** | 標準 REST API 呼叫 |
| **Teams** | Microsoft Teams Bot 協定 |

選擇已發布的 Workflow 或輸入外部端點 URL，即可在 Chat 面板中進行互動式測試。

### 2.4 Request Logs

前往 `/request-logs` 頁面查看執行記錄與分析。可檢視每次 API 呼叫的請求內容、回應結果、執行時間等資訊，用於除錯與效能分析。

---

## Part 3: 設定

### 3.1 Settings 頁面

前往 `/settings` 頁面進行個人設定，包含以下區塊：

**Profile：** 個人資料設定。

**語系：** 切換介面語言，目前支援英文（en）與繁體中文（zh-TW）。

**預設模型：** 設定 Agent 的預設 LLM 模型。支援的 Provider 包含 OpenAI、Azure OpenAI、Ollama、Azure Foundry、GitHub Copilot、Anthropic、AWS Bedrock。

### 3.2 Credentials 管理

在 Settings 頁面的 Credentials 區塊管理各 AI 服務的 API 金鑰：

1. 選擇 Provider（如 OpenAI、Azure OpenAI 等）
2. 輸入對應的 API Key、Endpoint 等認證資訊
3. 儲存後，認證資訊以 DPAPI 加密存入後端 `ICredentialStore`
4. 前端不儲存明文 Key，僅記錄「已儲存」狀態

Workflow 執行時，後端透過 `ResolveCredentialsAsync()` 從 `ICredentialStore` 讀取解密後的認證資訊，前端不再傳送 API Key。

### 3.3 Budget 設定

設定 Token 使用預算上限，避免意外的高額費用。可依模型或整體設定預算限制。

