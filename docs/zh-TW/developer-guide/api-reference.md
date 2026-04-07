# API 端點參考

AgentCraftLab 後端 API 運行於 `http://localhost:5200`，提供 AG-UI 協定串流端點與 REST CRUD 端點。

所有 REST 端點回傳 JSON。錯誤回應統一格式：`{ "code": "ERROR_CODE", "message": "..." }`。

---

## 1. AG-UI 端點

AG-UI 協定端點以 SSE（Server-Sent Events）串流回傳執行事件。

### POST /ag-ui

執行 Workflow。Credentials 從後端 `ICredentialStore` 讀取（DPAPI 加密），前端無需傳送 API Key。

**Request Body（RunAgentInput）：**
```json
{
  "threadId": "string",
  "runId": "string",
  "messages": [{ "role": "user", "content": "..." }],
  "forwardedProps": {
    "workflowJson": "string (JSON)",
    "fileId": "string (選填，上傳後取得)"
  }
}
```

**Response：** `text/event-stream`，串流 `ExecutionEvent` 事件。

### POST /ag-ui/goal

執行 Autonomous Agent（ReAct 或 Flow 模式，依啟動設定切換）。

**Request Body（RunAgentInput）：**
```json
{
  "threadId": "string",
  "runId": "string",
  "messages": [{ "role": "user", "content": "目標描述" }],
  "forwardedProps": {
    "provider": "openai",
    "model": "gpt-4o-mini",
    "tools": "web_search,calculator"
  }
}
```

**Response：** `text/event-stream`，串流執行事件。

### POST /ag-ui/human-input

提交 Human-in-the-loop 輸入，回應暫停中的 human 節點。

**Request Body：**
```json
{
  "threadId": "string",
  "runId": "string",
  "response": "使用者的回覆文字"
}
```

**Response：** `200 { "success": true }` 或 `404 { "error": "No pending human input for this session" }`

---

## 2. Workflow CRUD

基礎路徑：`/api/workflows`

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/api/workflows` | 建立 Workflow |
| GET | `/api/workflows` | 列出目前使用者的所有 Workflow |
| GET | `/api/workflows/{id}` | 取得單一 Workflow |
| PUT | `/api/workflows/{id}` | 更新 Workflow |
| DELETE | `/api/workflows/{id}` | 刪除 Workflow |
| PATCH | `/api/workflows/{id}/publish` | 設定發布狀態 |

**POST / PUT Request Body：**
```json
{
  "name": "string (必填)",
  "description": "string",
  "type": "string",
  "workflowJson": "string (JSON)"
}
```

**PATCH publish Request Body：**
```json
{
  "isPublished": true,
  "inputModes": ["text", "file"]
}
```

**Response：** `WorkflowDocument` 物件，含 `id`、`name`、`description`、`type`、`workflowJson`、`createdAt`、`updatedAt`。

---

## 3. 工具

### GET /api/tools

列出所有可用的內建工具（Tool Catalog）。

**Response：**
```json
[
  {
    "id": "web_search",
    "name": "Web Search",
    "description": "...",
    "category": "Search",
    "icon": "search"
  }
]
```

---

## 4. 發現與測試

### POST /api/mcp/discover

探索 MCP Server 提供的工具清單。

**Request：** `{ "url": "http://localhost:3001/mcp" }`

**Response：** `{ "healthy": true, "tools": [{ "name": "...", "description": "..." }] }`

### POST /api/a2a/discover

探索 A2A Agent 的 Agent Card。

**Request：** `{ "url": "http://...", "format": "auto|google|microsoft" }`

**Response：** `{ "healthy": true, "agent": { ... } }`

### POST /api/a2a/test

向 A2A Agent 發送測試訊息。

**Request：** `{ "url": "http://...", "message": "Hello", "format": "auto" }`

**Response：** `{ "success": true, "response": "..." }`

### POST /api/http-tools/test

測試 HTTP API 工具定義。

**Request：**
```json
{
  "name": "test",
  "url": "https://api.example.com/data",
  "method": "GET",
  "headers": "Authorization: Bearer xxx",
  "body": "",
  "input": ""
}
```

**Response：** `{ "success": true, "response": "..." }`

---

## 5. 知識庫

基礎路徑：`/api/knowledge-bases`

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/api/knowledge-bases` | 建立知識庫 |
| GET | `/api/knowledge-bases` | 列出使用者的知識庫 |
| GET | `/api/knowledge-bases/{id}` | 取得單一知識庫 |
| PUT | `/api/knowledge-bases/{id}` | 更新知識庫名稱/描述 |
| DELETE | `/api/knowledge-bases/{id}` | 刪除知識庫 |
| GET | `/api/knowledge-bases/{id}/files` | 列出知識庫檔案 |
| POST | `/api/knowledge-bases/{id}/files` | 上傳檔案並 Ingest |
| DELETE | `/api/knowledge-bases/{kbId}/files/{fileId}` | 刪除單一檔案 |

**POST 建立 Request：**
```json
{
  "name": "string (必填)",
  "description": "string",
  "embeddingModel": "text-embedding-3-small",
  "chunkSize": 512,
  "chunkOverlap": 50
}
```

**POST files：** `multipart/form-data`，欄位名 `file`。檔案上限 50MB。需已設定 OpenAI 或 Azure OpenAI credentials。

**POST files Response：** `text/event-stream`，SSE 串流 ingest 進度事件 `{ "type": "progress|complete|error", "text": "..." }`。

---

## 6. Skills

基礎路徑：`/api/skills`

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/skills` | 列出內建 + 自訂 Skills |
| POST | `/api/skills` | 建立自訂 Skill |
| PUT | `/api/skills/{id}` | 更新自訂 Skill |
| DELETE | `/api/skills/{id}` | 刪除自訂 Skill |

**GET Response：**
```json
{
  "builtin": [{ "id": "...", "name": "...", "description": "...", "instructions": "...", "category": "...", "icon": "...", "tools": [], "isBuiltin": true }],
  "custom": [{ "id": "...", "name": "...", ... }]
}
```

**POST / PUT Request：**
```json
{
  "name": "string (必填)",
  "description": "string",
  "category": "string",
  "icon": "string",
  "instructions": "string",
  "tools": ["tool_id_1", "tool_id_2"]
}
```

---

## 7. 範本

基礎路徑：`/api/templates`

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/templates` | 列出使用者的自訂範本 |
| POST | `/api/templates` | 建立範本 |
| PUT | `/api/templates/{id}` | 更新範本 |
| DELETE | `/api/templates/{id}` | 刪除範本 |

**POST / PUT Request：**
```json
{
  "name": "string (必填)",
  "description": "string",
  "category": "string",
  "icon": "string",
  "tags": ["tag1", "tag2"],
  "workflowJson": "string (JSON)"
}
```

---

## 8. API Keys

基礎路徑：`/api/keys`

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/api/keys` | 建立 API Key |
| GET | `/api/keys` | 列出使用者的 API Keys |
| DELETE | `/api/keys/{id}` | 撤銷 API Key |

**POST Request：**
```json
{
  "name": "string (必填)",
  "scopedWorkflowIds": "wf1,wf2 (選填，逗號分隔)",
  "expiresAt": "2026-12-31T00:00:00Z (選填)"
}
```

**POST Response（僅建立時回傳完整 rawKey）：**
```json
{
  "id": "string",
  "name": "string",
  "keyPrefix": "acl_xxxx...",
  "scopedWorkflowIds": "...",
  "expiresAt": "...",
  "createdAt": "...",
  "rawKey": "acl_xxxxxxxxxxxxxxxx (僅此次回傳)"
}
```

---

## 9. Credentials

基礎路徑：`/api/credentials`

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/api/credentials` | 儲存 Provider Credential（DPAPI 加密） |
| GET | `/api/credentials` | 列出 Credentials（不含明文 API Key） |
| PUT | `/api/credentials/{id}` | 更新 Credential |
| DELETE | `/api/credentials/{id}` | 刪除 Credential |
| GET | `/api/credentials/runtime-keys` | 取得解密 Credentials（僅 localhost） |

**POST / PUT Request：**
```json
{
  "provider": "openai (必填)",
  "name": "string",
  "apiKey": "sk-...",
  "endpoint": "string (Azure OpenAI 需要)",
  "model": "string"
}
```

**GET 列表 Response（安全版，不含明文 key）：**
```json
[{ "id": "...", "provider": "openai", "name": "...", "hasApiKey": true, "endpoint": "", "model": "", "createdAt": "...", "updatedAt": "..." }]
```

**GET runtime-keys：** 僅允許 localhost 存取，有速率限制（每分鐘 10 次）。回傳解密後的 credentials 供 Runtime 使用。

---

## 10. 上傳

### POST /api/upload

暫存檔案上傳，供 AG-UI 執行時附加檔案。

**Request：** `multipart/form-data`，欄位名 `file`。上限 32MB。

**Response：**
```json
{
  "fileId": "upload-xxxxxxxx",
  "fileName": "document.pdf",
  "size": 12345
}
```

暫存檔案 1 小時後自動清除。前端取得 `fileId` 後放入 `forwardedProps.fileId`，AG-UI 執行時自動注入。

---

## 11. 分析

### GET /api/analytics/summary

取得使用量摘要。

**Query 參數：** `from`（DateTime，預設過去 24 小時）、`userId`（選填）。

**Response：** 摘要統計物件。

### GET /api/analytics/logs

查詢請求日誌。

**Query 參數：** `from`、`to`（DateTime）、`protocol`（string）、`limit`（int，預設 100）。

**Response：** 日誌記錄陣列。

---

## 12. 排程（商業模式）

需啟用商業模式（設定 `ConnectionStrings:MongoDB`）。

基礎路徑：`/api/schedules`

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/schedules` | 列出使用者的排程 |
| GET | `/api/schedules/{id}` | 取得單一排程 |
| POST | `/api/schedules` | 建立或更新排程 |
| PATCH | `/api/schedules/{id}/toggle` | 啟用/停用排程 |
| DELETE | `/api/schedules/{id}` | 刪除排程 |
| GET | `/api/schedules/{id}/logs` | 查詢執行記錄 |

**POST Request：**
```json
{
  "id": "string (選填，有則更新)",
  "workflowId": "string (必填，需已發布)",
  "cronExpression": "0 9 * * * (必填)",
  "timeZone": "UTC",
  "enabled": true,
  "defaultInput": "string"
}
```

**GET logs Query 參數：** `limit`（int，預設 20）。

---

## 13. AI Build

### POST /api/flow-builder

自然語言描述轉 Workflow JSON。SSE 串流回傳生成結果。

**Request Body：**
```json
{
  "message": "string (必填，自然語言描述)",
  "provider": "openai",
  "model": "gpt-4o",
  "apiKey": "string (選填，優先從 CredentialStore 讀取)",
  "endpoint": "string (選填)",
  "currentPayload": "string (目前的 workflow JSON，用於增量修改)",
  "history": [{ "role": "user|assistant", "content": "..." }],
  "mode": "legacy (選填，預設使用 Flow Planner 強化版)"
}
```

**Response：** `text/event-stream`，串流 JSON 字串片段。最後一個事件為 metadata：
```json
{ "type": "__metadata", "durationMs": 1234, "estimatedTokens": 500, "model": "gpt-4o", "estimatedCost": "$0.01" }
```
結尾發送 `[DONE]`。

---

## 14. Script Generator

### POST /api/script-generator

LLM 生成符合沙箱的腳本（JavaScript 或 C#），同時生成測試資料。

**Request Body：**
```json
{
  "prompt": "string (必填，腳本需求描述)",
  "provider": "openai",
  "model": "gpt-4o-mini",
  "apiKey": "string (選填，優先從後端 ICredentialStore 讀取)",
  "endpoint": "string (選填)",
  "language": "javascript | csharp (選填，預設 javascript)"
}
```

**Response：**
```json
{
  "code": "const data = JSON.parse(input); ...",
  "testInput": "[{\"Name\":\"Alice\",\"Score\":95}]"
}
```

根據 `language` 參數自動切換 system prompt（JS 規則 vs C# 規則）。`testInput` 為 LLM 自動生成的測試資料樣本。

### POST /api/script-test

在沙箱中測試腳本（JavaScript 或 C#）。

**Request Body：**
```json
{
  "code": "string (必填，腳本程式碼)",
  "input": "string (模擬的 input 變數值)",
  "language": "javascript | csharp (選填，預設 javascript)"
}
```

**Response：**
```json
{
  "success": true,
  "output": "執行結果",
  "error": null,
  "consoleOutput": "console.log 輸出",
  "elapsedMs": 12.5
}
```

---

## 15. 診斷

### GET /api/traces/latest

取得最新的執行 Trace。

**Response：** `{ "runId": "...", "path": "...", "entries": ["jsonl line 1", "..."] }`

### GET /api/traces/{runId}

取得指定 runId 的執行 Trace。

**Response：** 同上。Trace 檔案存放於 `Data/traces/{runId}.jsonl`。

### GET /info

API 伺服器資訊。

**Response：** `{ "name": "AgentCraftLab API", "protocol": "AG-UI", "version": "1.0.0", "mode": "react|flow", "endpoints": [...] }`
