# 工具系統與 RAG 知識庫

本文件說明 AgentCraftLab 的工具系統、RAG（Retrieval-Augmented Generation）知識庫功能，以及 Skill 系統。

---

## Part 1: 工具系統

### 1.1 Agent 四層工具來源

每個 Agent 節點可同時使用來自四個來源的工具，執行時由 `AgentContextBuilder.ResolveToolsAsync()` 合併：

| 層級 | 來源 | 說明 |
|------|------|------|
| 1 | Tool Catalog（內建工具） | 平台預設提供的搜尋、檔案、Email 等工具 |
| 2 | MCP Servers | 透過 Model Context Protocol 連接外部工具伺服器 |
| 3 | A2A Agents | 透過 Agent-to-Agent 協定呼叫遠端 Agent |
| 4 | HTTP APIs | 自訂 HTTP 端點，作為確定性工具呼叫 |

此外，OCR 與 JS 沙箱腳本引擎以擴充模組形式掛載，啟用後同樣合併進工具清單。

在 Workflow Studio 中，於 Agent 節點的設定面板即可勾選內建工具、輸入 MCP Server URL、A2A Agent URL 或 HTTP API 端點。

### 1.2 內建工具（Tool Catalog）

內建工具依分類如下：

**搜尋類（Search）**

| 工具 ID | 名稱 | 說明 | 需要憑證 |
|---------|------|------|----------|
| `azure_web_search` | Azure Web Search | 透過 Azure OpenAI Responses API 搜尋即時網路資訊 | azure-openai |
| `tavily_search` | Tavily Search | AI 專用搜尋引擎（免費 1000 次/月） | tavily |
| `tavily_extract` | Tavily Extract | 從 URL 提取純淨網頁內容，自動去除廣告 | tavily |
| `brave_search` | Brave Search | 隱私導向搜尋引擎（免費 2000 次/月） | brave |
| `serper_search` | Serper (Google) | Google Search API，支援 search/news/images/places | serper |
| `web_search` | Web Search (Free) | DuckDuckGo + Wikipedia，免費、無需 API Key | -- |
| `wikipedia` | Wikipedia | Wikipedia 百科全書搜尋（自動偵測中/英文） | -- |

**工具類（Utility）**

| 工具 ID | 名稱 | 說明 |
|---------|------|------|
| `get_datetime` | Date & Time | 取得目前日期、時間與時區 |
| `calculator` | Calculator | 計算數學表達式 |
| `uuid_generator` | UUID Generator | 產生唯一 UUID / GUID |
| `send_email` | Send Email | 透過 SMTP 發送電子郵件（需要 smtp 憑證） |

**網頁類（Web）**

| 工具 ID | 名稱 | 說明 |
|---------|------|------|
| `url_fetch` | URL Fetch | 抓取指定網頁的文字內容摘要 |

**資料類（Data）**

| 工具 ID | 名稱 | 說明 |
|---------|------|------|
| `json_parser` | JSON Parser | 解析 JSON 字串並提取指定欄位 |
| `csv_log_analyzer` | CSV Log Analyzer | 讀取目錄下 CSV 檔案，合併供 AI 分析 |
| `zip_extractor` | ZIP Extractor | 解壓縮 ZIP 檔案到暫存目錄 |
| `write_file` | Write File | 將文字寫入檔案（csv/json/txt/md/xml/yaml/html 等） |
| `write_csv` | Write CSV | 將 JSON 陣列資料寫入 CSV 檔案 |
| `list_directory` | List Directory | 列出目錄結構（tree 格式） |
| `read_file` | Read File | 讀取檔案指定行範圍（帶行號） |
| `search_code` | Search Code | 在 codebase 中搜尋匹配 regex pattern 的程式碼 |

需要憑證的工具，請先在 `/settings` 頁面的 Credentials 區段設定對應的 API Key。未設定憑證時，工具仍會出現在清單中，但執行時會回傳錯誤提示。

### 1.3 MCP Server 整合

MCP（Model Context Protocol）讓 Agent 透過標準協定連接外部工具伺服器。

**設定方式：**

1. 在 Workflow Studio 中選取 Agent 節點
2. 在節點設定面板找到「MCP Servers」區段
3. 輸入 MCP Server 的 URL（例如 `http://localhost:3001/mcp`）
4. 可新增多個 MCP Server URL

**連接流程：**

執行時，系統會對每個 MCP Server URL 發送 discovery 請求，取得該伺服器提供的工具清單，並自動合併到 Agent 的可用工具中。若連接失敗，系統會記錄警告但不中斷執行。

**測試用 MCP Server：**

```bash
npx -y @modelcontextprotocol/server-everything streamableHttp
# 啟動後可透過 http://localhost:3001/mcp 連接
```

### 1.4 A2A Agent 整合

A2A（Agent-to-Agent）協定可讓本地 Agent 呼叫遠端 Agent 作為工具。

**設定方式：**

1. 在 Agent 節點設定面板找到「A2A Agents」區段
2. 輸入遠端 Agent 的 URL
3. 選擇格式：`auto`（自動偵測）、`google`（Google A2A 格式）、`microsoft`（Microsoft 格式）

**連接流程：**

系統會先對 URL 發送 discovery 請求取得 Agent Card（包含名稱、描述、能力等資訊），再將該遠端 Agent 包裝為一個 AITool，供本地 Agent 呼叫。

另外也可以使用獨立的 `a2a-agent` 節點類型，在 workflow 中直接作為一個遠端 Agent 節點參與執行。

### 1.5 HTTP API 工具

HTTP API 工具讓 Agent 能呼叫自訂的 HTTP 端點，適合連接內部系統或第三方 REST API。

**設定方式：**

在 Agent 節點設定中加入 HTTP API 端點資訊，系統會將其包裝為可供 Agent 呼叫的工具。

另有獨立的 `http-request` 節點類型，可在 workflow 中作為確定性的 HTTP 呼叫步驟，不經過 LLM 決策。

### 1.6 OCR 工具

AgentCraftLab 整合了 Tesseract OCR 引擎，以擴充模組形式提供（`AgentCraftLab.Ocr`）。

**啟用條件：** 系統偵測到 `tessdata` 目錄存在時自動啟用。

**支援語言：** 繁體中文、簡體中文、英文、日文、韓文。

啟用後，OCR 工具會自動合併進 Agent 的工具清單，Agent 可用它來識別圖片中的文字。

### 1.7 JS 沙箱腳本

Code 節點支援 script 模式，使用 Jint 引擎在 JS 沙箱中執行 JavaScript 腳本。

**用途：** 確定性的資料轉換，不消耗 LLM token。例如 JSON 欄位重組、格式化、篩選等。

**功能特色：**

- 沙箱隔離，不會影響主系統
- 可透過 `ISandboxApi` 擴展沙箱內可用的 API
- 支援 AI 腳本生成：透過 `POST /api/script-generator` 端點，由 LLM 根據描述自動生成沙箱相容的 JS 腳本
- 支援 Test Run：在部署前先測試腳本輸出

引擎介面為 `IScriptEngine`，可透過 DI 替換為 Roslyn 或 Python 引擎。

---

## Part 2: RAG 與知識庫

### 2.1 RAG 概念說明

RAG（Retrieval-Augmented Generation）是一種結合「搜尋」與「生成」的技術模式。執行流程為：

1. **Ingest（攝取）：** 將文件擷取文字 → 分塊（chunking） → 向量化（embedding） → 寫入搜尋索引
2. **Search（搜尋）：** 使用者輸入 → 向量化 → 在索引中搜尋相關片段 → Rerank 重排序
3. **Augment（增強）：** 將搜尋結果（含來源 metadata）注入 LLM 的 system message，提供上下文
4. **Generate（生成）：** LLM 根據上下文與問題生成回答

AgentCraftLab 的 RAG 管線由 `RagService` 負責 Ingest，`RagChatClient`（DelegatingChatClient）負責搜尋與注入。搜尋引擎由獨立的 `AgentCraftLab.Search` 類別庫提供。

### 2.2 知識庫管理

知識庫是 RAG 的核心，適合需要持久保存、多次使用的文件集合。透過 `/knowledge-bases` 頁面管理。

**建立知識庫：**

1. 進入知識庫管理頁面
2. 點選「建立知識庫」
3. 填寫名稱、描述
4. 設定索引參數（建立後不可修改）：
   - **Embedding Model：** 向量化模型（text-embedding-3-small / large / ada-002）
   - **Chunk Strategy：** 分塊策略
     - **固定大小（Fixed Size）：** 按字元數切割加重疊，適用所有文件
     - **結構感知（Structural）：** 按 Markdown/HTML heading 和段落邊界切割，適合結構化文件
   - **Chunk Size：** 分塊大小（字元數，預設 512）
   - **Chunk Overlap：** 分塊重疊區域（字元數，預設 50）
5. 建立後，系統會建立對應的搜尋索引（命名慣例：`{userId}_kb_{id}`）

> **注意：** Embedding 模型、分塊策略、Chunk Size 與 Overlap 在建立後無法修改。如需變更，請刪除知識庫重新建立。

**智慧預設推薦：**

切換 Chunk Strategy 時，系統自動推薦對應的 Chunk Size 和 Overlap：
- 固定大小（Fixed Size） → 512 / 50
- 結構感知（Structural） → 1024 / 100

**上傳檔案：**

1. 選擇已建立的知識庫
2. 上傳檔案（支援 PDF / DOCX / PPTX / HTML / TXT / MD / CSV / JSON 等格式）
3. 系統以 SSE（Server-Sent Events）串流方式即時回報處理進度：
   - Extracting text...（擷取文字，自動填充 metadata：標題、作者、頁數等）
   - Chunking text...（依設定的策略分塊）
   - Generating embeddings...（向量化，維度依模型動態決定）
   - Ingested X chunks（完成）

**URL 爬取：**

知識庫詳情面板提供 URL 輸入框，可直接從網頁擷取內容加入知識庫：

1. 輸入網頁 URL
2. 系統自動抓取網頁 → 擷取文字（使用 HtmlExtractor） → 依 KB 設定分塊 → embedding → 索引
3. 支援 SSE 串流進度回報
4. 爬取的內容以 `{domain}_{path}.html` 為檔名存入知識庫

**同名檔案替換：**

上傳與既有檔案同名的檔案時，系統自動刪除舊檔的 chunks 再重新 ingest。使用者不需要手動刪除舊檔再重傳。檔名比對為 case-insensitive。

**上傳後 Chunk 預覽：**

上傳完成後，進度區域會顯示前 3 個 chunk 的內容預覽，讓使用者立即確認分塊品質是否符合預期。預覽 10 秒後自動消失。

**管理檔案：**

- 知識庫詳情面板顯示已上傳檔案清單、chunk 數量、建立時間
- 面板標題列顯示索引設定摘要（embedding model / chunk strategy / chunk size）
- 支援刪除單一檔案（含確認對話框），系統會同步清除該檔案對應的所有 chunk 與向量資料

**知識庫統計：**

詳情面板 header 顯示檔案類型分佈（例如：PDF: 3 · DOCX: 1 · HTML: 2），方便快速掌握知識庫的內容組成。

**搜尋測試（Retrieval Test）：**

在知識庫詳情面板底部提供搜尋測試功能，可在上線前驗證搜尋品質：

1. 輸入問題，點擊搜尋
2. 顯示召回的 chunks，包含：來源檔名、Section 編號、相關度分數
3. 點擊結果可展開查看完整 chunk 內容
4. 可調整測試參數（TopK、搜尋模式、最低分數門檻）

### 2.3 在 Workflow 中使用知識庫

1. 在 Workflow Studio 拖入一個 `rag` 節點
2. 在 rag 節點設定中，選擇要使用的知識庫
3. 將 rag 節點連接到 Agent 節點

**RAG 節點設定：**

- **知識庫選擇：** 選擇已建立的知識庫（Embedding Model 顯示為唯讀，由知識庫決定）
- **搜尋品質滑桿：** 精確 ↔ 涵蓋 三段式切換
  - **精確：** TopK=3, MinScore=0.01（少量高品質結果）
  - **平衡：** TopK=5, MinScore=0.005（預設）
  - **涵蓋：** TopK=10, MinScore=0.001（多量結果，不遺漏）
- **進階搜尋設定（收合）：** TopK、搜尋模式（Hybrid/Vector/FullText）、最低分數門檻
- **查詢擴展：** 預設啟用，自動產生查詢變體以提升召回率。可手動關閉。
- **檔案過濾：** 依檔案名稱過濾搜尋結果（例如輸入「.pdf」或「report」）
- **上下文壓縮：** 預設關閉。啟用後，當搜尋結果總 token 數超過 Token 預算時，使用 LLM 摘要壓縮上下文
- **Token 預算：** 預設 1500，僅在啟用上下文壓縮時顯示

執行時，`RagChatClient` 會搜尋知識庫索引中的相關內容，經過 Rerank 重排序後，注入到 Agent 的上下文中（含來源 metadata 標注）。

**引用來源追蹤：**

執行時，RAG 搜尋到的引用來源會透過 STATE_SNAPSHOT 傳到前端。ConsolePanel 新增「Sources」tab，顯示每個引用的來源檔名、Section 編號、相關度分數。點擊可展開查看完整 chunk 內容。

### 2.4 搜尋模式

搜尋引擎（`AgentCraftLab.Search`）支援三種搜尋模式：

| 模式 | 說明 | 適用場景 |
|------|------|----------|
| **FullText** | 使用 SQLite FTS5 全文檢索（trigram，支援 CJK） | 精確關鍵字匹配、已知術語搜尋 |
| **Vector** | 使用 SIMD 加速的 Cosine Similarity 向量搜尋 | 語意相似度搜尋、模糊概念匹配 |
| **Hybrid**（預設） | 結合 FullText + Vector，透過 RRF（k=60）融合排序 | 大多數場景的最佳選擇 |

### 2.5 Advanced RAG 元件

AgentCraftLab 實作了 Advanced RAG 架構的關鍵元件：

| 元件 | 說明 |
|------|------|
| **Relevance Filtering** | `MinScore` 門檻過濾低分結果，避免將不相關內容注入 LLM |
| **Reranker** | `IReranker` 介面，支援 NoOp（預設）/ Cohere API / LLM 重排序 |
| **Metadata Enrichment** | 各格式 Extractor 自動擷取文件 metadata（標題/作者/頁數），注入搜尋結果 |
| **Structural Chunker** | 按 Markdown/HTML heading + 段落邊界切割，保留文件結構語意 |
| **查詢擴展（Query Expansion）** | `QueryExpander` 透過 LLM 生成 2 個查詢變體，平行搜尋後合併去重結果。可提升召回率 30% 以上。預設啟用，可在 RAG 節點設定中關閉。 |
| **檔案名稱過濾（File Name Filter）** | `FileNameFilter` 依檔案名稱子字串過濾搜尋結果（不區分大小寫）。例如：輸入「.pdf」僅搜尋 PDF 檔案，輸入「report」僅搜尋檔名含「report」的檔案。 |
| **上下文重排序（Context Reorder）** | Lost in the Middle 解法 — Rerank 後重新排列 chunks，最高分放頭尾（LLM 注意力最強），低分放中間，提升 LLM 對關鍵資訊的記憶 |
| **上下文壓縮（Context Compression）** | Token 預算自適應壓縮 — 搜尋結果超過設定的 token 預算時，用 LLM 摘要壓縮；不超過就不壓縮，避免不必要的延遲 |

Hybrid 搜尋 + Rerank + MinScore 過濾的組合，確保 LLM 收到的上下文既相關又精確。

---

## Part 3: Skill 系統

Skill 是一組預定義的「指令 + 工具」組合，讓 Agent 具備特定領域的能力。

### 3.1 內建 Skill

系統預設提供多種內建 Skill，分為五大類：

| 分類 | 範例 | 說明 |
|------|------|------|
| **領域知識（Domain Knowledge）** | 程式碼審查、法律合約審查 | 注入專業領域的審查指引與判斷標準 |
| **方法論（Methodology）** | 結構化推理 | 注入思考框架（如 Chain-of-Thought） |
| **輸出格式（Output Format）** | -- | 規範 Agent 的輸出結構 |
| **角色設定（Persona）** | -- | 為 Agent 設定特定角色人格 |
| **工具預設（Tool Preset）** | -- | 預設勾選特定工具組合 |

每個 Skill 包含：
- **Instructions：** 注入 Agent 的系統提示詞
- **Tools：** 自動帶入的工具清單（會檢查憑證可用性，跳過未設定憑證的工具）

在 Workflow Studio 中，Agent 節點或整個 Flow 都可以掛載 Skill。

### 3.2 自訂 Skill

透過 `/skills` 頁面（Skill Manager）管理自訂 Skill：

**建立自訂 Skill：**

1. 進入 Skill Manager 頁面
2. 點選「新增 Skill」
3. 填寫：
   - **名稱**
   - **描述**
   - **分類**
   - **圖示**
   - **Instructions：** 注入 Agent 的專業指令
   - **工具清單：** 選擇要自動帶入的內建工具

**管理操作：**

- 編輯：修改已建立的 Skill 設定
- 刪除：移除不再需要的 Skill
- 檢視內建 Skill：可查看內建 Skill 的詳細指令內容

自訂 Skill 的資料存放在 `ISkillStore`（開源模式使用 SQLite，商業模式使用 MongoDB）。

---

## 快速參考

| 功能 | 頁面路徑 | 說明 |
|------|----------|------|
| API Key 設定 | `/settings` | 設定各 Provider 與工具所需的憑證 |
| 知識庫管理 | `/knowledge-bases` | 建立知識庫、上傳檔案、管理文件 |
| Skill 管理 | `/skills` | 檢視內建 Skill、建立自訂 Skill |
| 服務測試 | Service Tester | 測試 MCP / A2A 等外部服務連線 |
