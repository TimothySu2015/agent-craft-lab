# AgentCraftLab -- 快速入門指南

本指南將帶你在幾分鐘內啟動 AgentCraftLab，建立並執行你的第一個多 Agent 工作流。

---

## 1. 系統需求

| 項目 | 最低版本 |
|------|----------|
| .NET SDK | 10.0 Preview |
| Node.js | 20 LTS 以上 |
| npm | 隨 Node.js 安裝即可 |
| 作業系統 | Windows 10+、macOS、Linux |

> AgentCraftLab 預設使用 **SQLite**，無需額外安裝資料庫。

---

## 2. 快速啟動

### 2.1 取得原始碼

```bash
git clone https://github.com/your-org/AgentCraftLab.git
cd AgentCraftLab
```

### 2.2 安裝前端相依套件

```bash
cd AgentCraftLab.Web
npm install
cd ..
```

### 2.3 啟動三個服務

AgentCraftLab 採用前後端分離架構，需要同時啟動三個 Terminal：

**Terminal 1 -- .NET API 後端（port 5200）**

```bash
dotnet run --project AgentCraftLab.Api
```

等待出現 `Now listening on: http://localhost:5200` 後，開啟下一個 Terminal。

**Terminal 2 -- CopilotKit Runtime（port 4000）**

```bash
cd AgentCraftLab.Web
node server.mjs
```

**Terminal 3 -- React 開發伺服器（port 5173）**

```bash
cd AgentCraftLab.Web
npm run dev:vite
```

### 2.4 開啟瀏覽器

前往 **http://localhost:5173** -- 你應該會看到 Workflow Studio 介面。

> 無需登入，系統以 `local` 使用者身份運行。

---

## 3. 設定 API Credentials

在執行任何包含 LLM Agent 的工作流之前，你需要先設定至少一組 AI 模型的 API Key。

1. 點擊左側導覽列的 **Settings**（或直接前往 `/settings`）。
2. 找到 **Credentials** 區塊。
3. 輸入你的 API Key，例如：
   - **OpenAI API Key** -- 用於 GPT-4o、GPT-4o-mini 等模型
   - **Azure OpenAI** -- 需額外填入 Endpoint 和 Deployment Name
   - **Anthropic API Key** -- 用於 Claude 系列模型
   - **Google AI API Key** -- 用於 Gemini 系列模型
4. 點擊 **Save** 儲存。

所有 API Key 皆透過 DPAPI 加密儲存在後端，前端不會保留明文。

---

## 4. 建立你的第一個 Workflow

### 方法一：從範本建立

1. 在 Workflow Studio 頁面，點擊 **Templates**。
2. 選擇 **Basic** 分類下的 **Simple Chat** 範本。
3. 範本會自動載入到畫布上，包含一個 `start` 節點、一個 `agent` 節點和一個 `end` 節點。
4. 點選 `agent` 節點，在右側面板中確認模型設定（例如 `gpt-4o-mini`）。

### 方法二：用 AI Build 自然語言建立

1. 在 Workflow Studio 頁面，開啟 **AI Build** 面板。
2. 用自然語言描述你想要的工作流，例如：

   ```
   建立一個翻譯 Agent，接收使用者輸入的中文，翻譯成英文和日文，最後合併結果回傳。
   ```

3. AI 會自動產生對應的節點與連線，載入到畫布上。
4. 你可以手動微調節點設定後再執行。

---

## 5. 測試執行

1. 切換到 **Execute** 頁籤（畫布右側的聊天面板）。
2. 在輸入框中輸入訊息，例如 `你好，請自我介紹`。
3. 按下送出，觀察 Agent 的回應串流輸出。
4. 如果工作流包含多個節點，你可以在執行過程中看到各節點的執行狀態。

> 若出現 API Key 相關錯誤，請回到 Settings 頁面確認 Credentials 已正確設定。

---

## 6. 核心概念速覽

| 概念 | 說明 |
|------|------|
| **節點（Node）** | 工作流的基本單元。`agent` 節點呼叫 LLM，`code` 節點做資料轉換，`condition` 節點做條件分支等。 |
| **連線（Edge）** | 定義節點之間的執行順序與資料流向。 |
| **工具（Tool）** | Agent 可使用的外部能力，包含內建工具、MCP Server、A2A Agent、HTTP API 等四層來源。 |
| **策略（Strategy）** | 系統根據節點類型自動選擇執行策略：Sequential、Handoff、Imperative 等。 |
| **Middleware** | 可掛載 GuardRails、PII 過濾、Rate Limit 等中介層。 |

---

## 7. 常用頁面

| 頁面 | 路徑 | 用途 |
|------|------|------|
| Workflow Studio | `/` | 視覺化設計與執行工作流 |
| Settings | `/settings` | API Credentials、語系、預設模型 |
| Skills | `/skills` | 管理 Agent 技能 |
| Service Tester | `/tester` | 測試 MCP / A2A / HTTP 等外部服務 |
| Schedules | `/schedules` | 排程管理 |

---

## 8. 下一步

- **進階節點類型**：嘗試在工作流中加入 `condition`（條件分支）、`iteration`（迴圈）、`parallel`（並行）等節點。
- **外部工具整合**：在 Agent 節點中掛載 MCP Server 或 HTTP API，擴展 Agent 的能力。
- **知識庫（RAG）**：上傳文件建立知識庫，讓 Agent 具備領域知識。
- **Autonomous Agent**：使用 `autonomous` 節點，讓 AI 自主規劃與執行複雜任務。
- **Export 部署**：完成的工作流可匯出為獨立部署包。

如有問題，請參閱 `docs/` 目錄下的其他設計文件，或查看專案的 `CLAUDE.md` 取得完整的架構說明。
