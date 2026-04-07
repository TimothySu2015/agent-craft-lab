# 節點類型參考指南

本文件說明 AgentCraftLab Workflow Studio 中所有可用的節點類型，包含用途、主要設定欄位與適用場景。

## 總覽表

| 節點類型 | 分類 | 說明 |
|----------|------|------|
| `start` | Meta | 工作流程起點 |
| `end` | Meta | 工作流程終點 |
| `agent` | 可執行 / Agent | 本地 LLM Agent，呼叫語言模型處理任務 |
| `a2a-agent` | 可執行 / Agent | 遠端 A2A Agent，透過 Agent-to-Agent 協定呼叫外部服務 |
| `autonomous` | 可執行 / Agent | ReAct 迴圈自主 Agent，可建立 sub-agent 協作 |
| `condition` | 可執行 / 控制流程 | 條件分支，依表達式走不同路徑 |
| `loop` | 可執行 / 控制流程 | 迴圈，重複執行直到條件滿足或達最大次數 |
| `router` | 可執行 / 控制流程 | 多路由分類，由 LLM 判斷輸入應走哪條路徑 |
| `human` | 可執行 / 控制流程 | 暫停工作流程，等待使用者輸入 |
| `code` | 可執行 / 轉換 | 確定性文字轉換，不消耗 LLM token |
| `iteration` | 可執行 / 控制流程 | foreach 迴圈，將輸入拆分後逐項處理 |
| `parallel` | 可執行 / 控制流程 | 並行 fan-out/fan-in，多分支同時執行 |
| `http-request` | 可執行 / 整合 | 確定性 HTTP 呼叫外部 API |
| `rag` | 資料節點 | 掛載 RAG 知識來源（上傳檔案或知識庫） |

---

## Meta 節點

### start — 起點

**用途：** 標示工作流程的入口。使用者輸入的訊息從此節點開始傳遞。

**設定欄位：** 無需設定。

**適用場景：** 每個工作流程必須有且僅有一個 start 節點。

### end — 終點

**用途：** 標示工作流程的結束。最後一個 Agent 的輸出到達此節點後，整個工作流程完成。

**設定欄位：** 無需設定。

**適用場景：** 每個工作流程必須有至少一個 end 節點。在有分支的工作流程中可有多個 end 節點。

---

## Agent 節點

### agent — 本地 LLM Agent

**用途：** 呼叫語言模型執行任務。是工作流程中最常用的節點類型，可掛載工具、RAG 知識與 Middleware。

**主要設定欄位：**

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `provider` | LLM 供應商（openai / azure-openai / ollama / foundry / github-copilot / anthropic / aws-bedrock） | openai |
| `model` | 模型名稱 | gpt-4o |
| `instructions` | 系統指令，定義 Agent 的行為與角色 | 空 |
| `temperature` | 生成溫度（0~2），越高越有創意 | 未設定（使用模型預設） |
| `topP` | Top-P 取樣 | 未設定 |
| `maxOutputTokens` | 最大輸出 token 數 | 未設定 |
| `tools` | 掛載的內建工具 ID 清單 | 空 |
| `mcpServers` | 掛載的 MCP Server 名稱清單 | 空 |
| `httpApis` | 掛載的 HTTP API 名稱清單 | 空 |
| `outputFormat` | 輸出格式（text / json / json_schema） | text |
| `middleware` | 啟用的 Middleware（GuardRails / PII / RateLimit / Retry / Logging） | 空 |

**適用場景：** 文字摘要、翻譯、分析、程式碼生成、客服回覆等所有需要 LLM 推理的任務。

### a2a-agent — 遠端 A2A Agent

**用途：** 透過 Agent-to-Agent（A2A）協定呼叫遠端 Agent 服務。適用於跨服務、跨組織的 Agent 協作。

**主要設定欄位：**

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `a2aUrl` | 遠端 Agent 的 A2A 端點 URL | 空（必填） |
| `a2aFormat` | 協定格式（auto / google / microsoft） | auto |

**適用場景：** 呼叫已部署為 A2A 服務的外部 Agent，例如企業內部的專業 Agent、第三方 Agent 服務。`auto` 模式會依序嘗試兩種格式。

### autonomous — ReAct 迴圈自主 Agent

**用途：** 以 ReAct（Reasoning + Acting）迴圈運作的自主 Agent。可自行建立 sub-agent、分配任務、收集結果，適合複雜的多步推理任務。

**主要設定欄位：** 透過 Agent 節點的基本欄位設定（provider、model、instructions），執行時由 ReactExecutor 接管。

**適用場景：** 需要多步推理、動態決策的複雜任務。例如：研究報告撰寫（自動拆解子任務）、多來源資料彙整、需要反覆驗證的分析工作。內建 12 種 meta-tools 支援 sub-agent 管理。

---

## 控制流程節點

### condition — 條件分支

**用途：** 根據條件表達式將流程導向不同路徑。支援 `output_1`（條件為真）和 `output_2`（條件為假）兩個輸出埠。

**主要設定欄位：**

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `conditionType` | 條件判斷方式 | 空 |
| `conditionExpression` | 條件表達式（套用在前一節點的輸出上） | 空 |

**適用場景：** 依據前一個 Agent 的輸出決定後續流程。例如：情感分析結果為正面走路徑 A、負面走路徑 B。

### loop — 迴圈

**用途：** 重複執行迴圈內的節點，直到條件滿足或達到最大迭代次數。

**主要設定欄位：**

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `maxIterations` | 最大迴圈次數 | 5 |
| `conditionExpression` | 終止條件表達式 | 空 |

**適用場景：** 需要反覆精煉的任務。例如：文章潤飾直到品質達標、翻譯校對直到無誤。

### router — 多路由分類

**用途：** 由 LLM 判斷輸入內容的類別，將流程導向對應的分支路徑。支援多個輸出埠。

**主要設定欄位：**

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `instructions` | 路由判斷的指令，描述各路徑的分類條件 | 空 |

**適用場景：** 智慧分流。例如：客服系統依問題類型（帳務 / 技術 / 一般）分派給不同 Agent 處理。

### human — 人工輸入

**用途：** 暫停工作流程執行，等待使用者提供輸入後再繼續。支援三種互動模式。

**主要設定欄位：**

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `prompt` | 顯示給使用者的提示訊息 | 空 |
| `inputType` | 輸入模式：`text`（自由文字）/ `choice`（選項）/ `approval`（核准/拒絕） | text |
| `choices` | 選項清單（逗號分隔），僅 choice 模式使用 | 空 |
| `timeoutSeconds` | 等待逾時秒數（0 = 無限等待） | 0 |

**適用場景：** 人機協作流程。例如：AI 產生草稿後請使用者確認、流程中需要人工審批、請使用者從選項中選擇方向。

### iteration — foreach 迴圈

**用途：** 將輸入拆分為多個項目，逐項送入子節點處理。類似程式語言的 foreach。

**主要設定欄位：**

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `splitMode` | 拆分方式：`json-array`（JSON 陣列）/ `delimiter`（分隔符號） | json-array |
| `iterationDelimiter` | 分隔符號（僅 delimiter 模式） | 換行符 |
| `maxItems` | 最大處理項目數 | 50 |

**適用場景：** 批次處理。例如：對一組產品名稱逐一產生行銷文案、對 JSON 陣列中的每筆資料逐一分析。

### parallel — 並行執行

**用途：** fan-out/fan-in 模式，多個分支同時並行執行，全部完成後合併結果。

**主要設定欄位：**

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `branches` | 分支名稱（逗號分隔） | Branch1,Branch2 |
| `mergeStrategy` | 結果合併策略：`labeled`（加標籤）/ `join`（串接）/ `json`（JSON 物件） | labeled |

**適用場景：** 需要同時處理多個獨立子任務。例如：同時翻譯成多種語言、同時從多個角度分析同一份資料。

---

## 轉換節點

### code — 確定性轉換

**用途：** 不呼叫 LLM，以確定性規則轉換文字。零 token 消耗，適合格式整理、資料擷取等前後處理。

**主要設定欄位：**

| 欄位 | 說明 |
|------|------|
| `transformType` | 轉換模式（見下方九種模式） |
| `template` | 模板字串（template / script 模式使用） |
| `pattern` | 正則表達式（regex-extract / regex-replace / json-path 使用） |
| `replacement` | 替換字串（regex-replace 使用） |
| `maxLength` | 截斷長度（trim 使用） |
| `delimiter` | 分隔符號（split-take 使用） |
| `splitIndex` | 取第幾段（split-take 使用） |
| `scriptLanguage` | 腳本語言：`javascript`（預設）或 `csharp`（script 模式使用） |

**九種轉換模式：**

| 模式 | 說明 |
|------|------|
| `template` | 模板替換，`{{input}}` 代入前一節點輸出 |
| `regex-extract` | 正則擷取匹配的內容 |
| `regex-replace` | 正則搜尋與替換 |
| `json-path` | 從 JSON 中擷取指定路徑的值 |
| `trim` | 截斷至指定長度 |
| `split-take` | 按分隔符號拆分，取指定索引的段落 |
| `upper` | 轉大寫 |
| `lower` | 轉小寫 |
| `script` | 執行沙箱腳本（JavaScript 或 C#，需啟用 AgentCraftLab.Script） |

**Script 模式雙語言支援：**

| 語言 | 引擎 | 特色 |
|------|------|------|
| JavaScript | Jint 沙箱 | 用 `input` 變數讀取輸入，設定 `result` 變數輸出 |
| C# | Roslyn 動態編譯 | 參數 `input` 為字串，用 `return` 回傳結果。可用 LINQ、JsonSerializer、Regex |

兩種語言都在安全沙箱中執行：禁止 File/Network/Process 操作，有 timeout 和記憶體限制。

**Script Studio（全螢幕編輯器）：**

側邊面板顯示程式碼唯讀預覽，點擊打開 Script Studio 全螢幕 Modal：

- **上方** — AI 生成：輸入自然語言描述，LLM 自動生成腳本程式碼 + 測試資料
- **中間** — Monaco Editor（VS Code 核心）：語法高亮、括號配對、自動縮排、minimap
- **下方** — Test Run：填入測試輸入，即時執行並檢視結果
- **Format 按鈕** — 自動格式化程式碼（Shift+Alt+F）
- 點擊「套用」將程式碼帶回節點設定

**適用場景：** Agent 輸出的後處理（擷取 JSON 欄位、格式化模板、正則清洗）、節點間的資料轉換、複雜 LINQ 查詢與資料處理（C#）。

---

## 整合節點

### http-request — HTTP 呼叫

**用途：** 發送確定性的 HTTP 請求至外部 API，不經過 LLM。適合呼叫已知格式的 REST API。

**主要設定欄位：**

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `httpApiId` | 引用的 HTTP API 定義 ID | 空（必填） |
| `httpArgsTemplate` | JSON 參數模板，`{input}` 會被替換為前一節點輸出 | `{}` |

HTTP API 定義（在工作流程層級設定）包含：URL、Method（GET/POST/PUT/DELETE）、Headers、BodyTemplate。

**適用場景：** 呼叫第三方 REST API、Webhook 通知、從外部系統取得資料。

---

## 資料節點

資料節點本身不執行邏輯，而是為 Agent 節點提供額外能力。透過連線將資料節點連接到 Agent 節點即可生效。

### rag — RAG 知識來源

**用途：** 為 Agent 掛載檢索增強生成（RAG）的知識來源。Agent 回答時會先搜尋相關文件片段，注入上下文後再生成回應。

**主要設定欄位（RagConfig）：**

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `dataSource` | 資料來源類型 | upload |
| `chunkSize` | 分塊大小（字元數） | 1000 |
| `chunkOverlap` | 分塊重疊區域 | 100 |
| `topK` | 檢索時取前 K 個相關片段 | 5 |
| `embeddingModel` | 嵌入模型 | text-embedding-3-small |

也可透過 `knowledgeBaseIds` 連接已建立的知識庫。

**適用場景：** 讓 Agent 基於特定文件回答問題。例如：上傳產品手冊後提供客服問答、基於公司內部文件產生報告。

### tool — 工具

**用途：** 為 Agent 掛載內建工具。Agent 在推理過程中可自行決定是否呼叫這些工具。

**主要設定欄位：** 透過 UI 從工具目錄選擇要掛載的工具。

工具來源包含四層：Tool Catalog（內建工具）、MCP Servers、A2A Agents、HTTP APIs。

**適用場景：** 讓 Agent 具備搜尋網路、查詢天氣、計算數學等外部能力。例如：掛載 Web Search 工具讓 Agent 能搜尋最新資訊。

---

## 策略自動偵測

工作流程的執行策略會根據節點組合自動選擇：

1. 若包含任何需要 Imperative 的節點（condition / loop / router / human / code / iteration / parallel / http-request / a2a-agent / autonomous）→ 使用 **Imperative** 策略
2. 若任一 Agent 有多條 outgoing 連線 → 使用 **Handoff** 策略
3. 其他情況 → 使用 **Sequential** 策略

也可在工作流程設定中手動指定策略（auto / sequential / concurrent / handoff / imperative）。
