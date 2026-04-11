# Studio 畫布操作指南

本文件說明 AgentCraftLab Workflow Studio 的介面配置與操作方式。

---

## 1. 介面總覽

Studio 頁面由四個主要區域組成：

| 區域 | 位置 | 說明 |
|------|------|------|
| Node Palette | 左側面板 | 節點選板，列出所有可用的節點類型，可拖曳至畫布 |
| Canvas 畫布 | 中央區域 | React Flow 畫布，用於放置節點、拉線連接、組裝 workflow |
| Properties Panel | 右側浮動面板 | 選取節點後顯示，可編輯該節點的所有屬性 |
| Chat Panel | 最右側面板 | 包含 Execute 與 AI Build 兩個分頁，可收折與拖曳調整寬度 |

畫布上方為 **Top Bar 工具列**，由左至右依序為：

- 檔案操作：Load / Save / Import
- 畫布工具：Auto Layout / Settings / Templates / Save as Template
- 產出：Code Generation / Export

畫布底部另有 **Console Panel**，顯示執行時的 log 輸出。

### Node Palette 節點分類

節點選板分為兩組：

**Nodes（核心節點）：**
Agent、Tool、RAG、Condition、Loop、Router、Human、Code、Iteration、Parallel、Autonomous

**Integrations（整合節點）：**
A2A Agent、HTTP Request

點擊左上角箭頭圖示可收折 Node Palette，僅顯示圖示列。

---

## 2. 基本操作

### 2.1 新增節點

從左側 Node Palette 將節點拖曳至畫布上即可新增。放開滑鼠時，節點會對齊至 20px 格線。

也可透過右鍵選單快速新增常用節點（Agent、Condition、Human、Code、Parallel、Iteration）。

### 2.2 連接節點

從節點底部的輸出端點（handle）拖曳連線至另一個節點的輸入端點。系統會自動驗證連線合法性：

- 不可自己連自己
- 不可連入 Start 節點
- 不可從 End 節點連出
- RAG 節點只能與 Agent 節點相連

連線預設使用 smoothstep 樣式，點擊連線可選取，選取後按 Delete 鍵直接刪除（不需確認）。

### 2.3 選取與刪除節點

- 點擊節點即可選取，選取後右側會顯示 Properties Panel
- 按 `Delete` 或 `Backspace` 鍵刪除選取的節點（會跳出確認對話框）
- 點擊畫布空白處取消選取

### 2.4 複製節點

選取節點後按 `Ctrl+D` 可複製該節點，複製的節點會出現在原節點附近。

### 2.5 Undo / Redo

- `Ctrl+Z`：復原上一步操作
- `Ctrl+Y`：重做上一步操作
- 最多可回溯 50 步

### 2.6 Auto Layout 自動排版

點擊工具列的 Auto Layout 按鈕，系統會自動重新排列所有節點，並以動畫方式調整視圖以適應畫面。

### 2.7 快速儲存

按 `Ctrl+S` 開啟儲存對話框。若已有儲存記錄，可直接覆蓋；首次儲存需輸入名稱。

### 2.8 右鍵選單

在畫布上按右鍵可開啟上下文選單：

- **在空白處按右鍵**：顯示快速新增節點選單
- **在節點上按右鍵**：顯示 Duplicate（複製）、Delete（刪除）、Auto Layout（自動排版）選項

按 `Escape` 鍵可關閉右鍵選單。

### 2.9 匯入 Workflow

點擊工具列的 Import 按鈕，選擇 `.json` 格式的 workflow 檔案即可匯入。匯入後會取代目前畫布上的所有內容。

---

## 3. Properties Panel 屬性面板

選取任一節點後，畫布右側會浮出 Properties Panel，可編輯該節點的屬性。不同類型的節點會顯示不同的設定欄位，常見項目包括：

- **名稱 (Label)**：節點在畫布上顯示的名稱
- **Instructions**：Agent 的系統提示詞（支援展開全螢幕編輯）
- **模型 (Model)**：Agent 使用的 LLM 模型
- **工具 (Tools)**：掛載的內建工具、MCP Server、HTTP API
- **條件 / 路由規則**：Condition、Router 節點的判斷邏輯
- **Code 設定**：Code 節點的轉換模式與腳本

點擊畫布空白處可關閉屬性面板。

---

## 4. Chat Panel 聊天面板

Chat Panel 位於介面最右側，可透過拖曳左邊緣調整寬度（280px ~ 800px），或點擊收折按鈕隱藏。

### 4.1 Execute 分頁

用於執行目前畫布上的 workflow：

1. 在輸入框輸入訊息（作為 workflow 的輸入）
2. 可透過附件按鈕上傳檔案
3. 送出後，系統會透過 AG-UI 協定串流回應
4. 執行過程中若遇到 Human 節點，會顯示互動面板（文字輸入 / 選擇 / 審批三種模式）

### 4.2 AI Build 分頁

用自然語言描述需求，AI 會自動在畫布上建構 workflow：

1. 切換至 AI Build 分頁
2. 以自然語言描述你想要的 workflow（例如「建立一個客服機器人，先分類問題再交給專家回答」）
3. AI 會串流產生節點配置，畫布即時更新

AI Build 使用 partial update 優先策略（增量更新），僅在必要時執行 full rebuild。

---

## 5. Workflow Settings 工作流設定

點擊工具列的齒輪圖示開啟設定對話框，可配置：

<div v-pre>

### 變數 (Variables)

在 **Variables** tab 定義 Workflow 變數。在任何節點中透過 `{{prefix:name}}` 語法引用。

| 層 | 語法 | 說明 |
|---|------|------|
| 系統 | `{{sys:user_id}}` | 唯讀系統變數：`user_id`、`timestamp`、`execution_id`、`workflow_name`、`user_message` |
| Workflow | `{{var:counter}}` | 使用者定義變數，有名稱、型別和預設值 |
| 環境 | `{{env:API_URL}}` | 伺服器環境變數（需 `AGENTCRAFTLAB_` 前綴） |
| 節點輸出 | `{{node:Agent-1}}` | 前一節點的輸出（既有功能） |

**定義變數**：開啟 Workflow Settings → Variables tab → 新增變數。設定名稱、型別（`string` / `number` / `boolean` / `json`）和預設值。

**引用變數**：在任何指令或表達式欄位輸入 `{{` 觸發自動補全。

**Code 節點寫入變數**：JavaScript 用 `$variables.name` 讀取。寫回時回傳含 `__variables__` 和 `__output__` 的 JSON：
```javascript
const count = parseInt($variables.counter || '0') + 1;
return JSON.stringify({
  __variables__: { counter: String(count) },
  __output__: `已處理 ${count} 筆`
});
```

**環境變數**：在伺服器設定 `AGENTCRAFTLAB_API_URL=https://...`，即可用 `{{env:API_URL}}` 引用（前綴自動移除）。

</div>

### 中介層 (Middleware)

依序包裝 Agent 的 ChatClient，提供企業級安全與可觀察性：

#### GuardRails — 內容安全護欄

保護 Agent 不處理或產出違規內容。在 Middleware 設定面板中可配置：

- **Scan All Messages**：掃描所有對話訊息（不只最後一則），防止多輪攻擊
- **Scan Output**：掃描 LLM 回應，防止模型洩漏敏感內容
- **Injection Detection**：偵測 Prompt Injection 攻擊（如「忽略之前的指令」），9 種中英文 pattern
- **Blocked Terms**：封鎖關鍵字（逗號分隔），觸發時回傳拒絕訊息
- **Warn Terms**：警告關鍵字，記錄警告但允許通過
- **Regex Rules**：正則表達式規則（每行一條），用於複雜 pattern 匹配
- **Allowed Topics**：限制 Agent 只能討論指定主題（逗號分隔），偏離主題自動封鎖
- **Blocked Response**：自訂封鎖回應訊息

#### PII Masking — 個資保護

自動偵測並遮罩個人識別資訊（PII），支援 GDPR/HIPAA/PCI-DSS 企業合規：

- **Protection Mode**：
  - *Irreversible*（不可逆）：PII 以 `***` 替換，LLM 永遠看不到原始資料
  - *Reversible*（可逆）：PII 以 `[EMAIL_1]`、`[PHONE_1]` 等 token 替換，LLM 回應後自動還原
- **Region Rules**：選擇要偵測的地區格式
  - Global（Email、IP、信用卡、IBAN、URL、加密貨幣地址）
  - Taiwan（身分證、電話、統編、健保卡、地址）
  - Japan（My Number、電話、護照、駕照）
  - Korea（住民登錄、電話、事業者登錄）
  - US（SSN、電話、護照、駕照）
  - UK（NHS、NINO、護照、郵遞區號）
- **Confidence Threshold**：信賴度門檻（0.0-1.0），低於此值的偵測結果將被忽略
- **Scan Output**：掃描 LLM 回應中的 PII
- **Custom Patterns**：自訂正則表達式規則

#### 其他中介層

- **RateLimit**：限制 LLM 呼叫頻率（Token Bucket 演算法，預設每秒 5 次）
- **Retry**：失敗自動重試（指數退避，最多 3 次），支援 HTTP 429/503 等暫態錯誤
- **Logging**：記錄每次 LLM 呼叫的輸入、輸出與耗時

### Hooks 事件鉤子

6 個插入點，可在 workflow 執行的特定階段插入自訂邏輯：

| Hook | 觸發時機 |
|------|----------|
| OnInput | 收到輸入時 |
| PreExecute | 執行前 |
| PreAgent | 每個 Agent 執行前 |
| PostAgent | 每個 Agent 執行後 |
| OnComplete | 執行完成時 |
| OnError | 發生錯誤時 |

每個 Hook 支援兩種類型：
- **code**：使用 TransformHelper 進行資料轉換
- **webhook**：發送 HTTP POST 至指定 URL

另外支援 **BlockPattern**，可用正規表達式攔截特定內容。

---

## 6. Code Generation 程式碼產生

點擊工具列的 Code 按鈕，可將目前的 workflow 轉換為可執行的程式碼。支援三種語言：

| 語言 | 說明 |
|------|------|
| C# | 使用 Microsoft.Agents.AI 框架，可直接整合至 .NET 專案 |
| Python | Python 版本的等效實作 |
| TypeScript | TypeScript/Node.js 版本的等效實作 |

產生的程式碼可複製至剪貼簿，直接用於專案開發。

---

## 7. Export 匯出

點擊工具列的 Export 按鈕，可選擇四種匯出模式：

| 模式 | 說明 |
|------|------|
| JSON | 匯出為 `.json` 檔案，可用於備份或再匯入 |
| Web API | 產生完整的 .NET Web API 部署包 |
| Teams Bot | 產生 Microsoft Teams Bot 部署包 |
| Console App | 產生 .NET Console 應用程式部署包 |

JSON 以外的三種模式會產生包含完整專案結構的壓縮檔，可直接建置與部署。

---

## 8. Save as Template 儲存為範本

點擊工具列的書籤圖示，輸入範本名稱後即可將目前的 workflow 儲存為自訂範本。儲存後的範本會出現在 Templates 對話框中，後續可快速載入複用。

自訂範本會同步至後端持久化儲存，若後端不可用則退回本地 fallback。

---

## 快捷鍵總覽

| 快捷鍵 | 功能 |
|--------|------|
| `Ctrl+S` | 開啟儲存對話框 |
| `Ctrl+Z` | 復原（最多 50 步） |
| `Ctrl+Y` | 重做 |
| `Ctrl+D` | 複製選取的節點 |
| `Delete` / `Backspace` | 刪除選取的節點或連線 |
| `Escape` | 關閉右鍵選單 |
