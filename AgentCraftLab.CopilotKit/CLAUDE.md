# AgentCraftLab.CopilotKit — AG-UI Protocol Bridge

CopilotKit React 前端透過 AG-UI Protocol 連接 AgentCraftLab Engine，支援 Workflow / Autonomous (ReAct) / Flow 三種執行模式。

## 啟動方式（3 個終端）

```bash
# Terminal 1 — .NET AG-UI 後端（port 5200）
dotnet run --project AgentCraftLab.CopilotKit

# Terminal 2 — CopilotKit Runtime 橋接（port 4000）
cd AgentCraftLab.CopilotKit/client-app && npm run runtime

# Terminal 3 — React 開發伺服器（port 5173）
cd AgentCraftLab.CopilotKit/client-app && npm run dev
```

## 架構

```
瀏覽器 (5173) → CopilotKit React → Vite proxy → CopilotKit Runtime (Node.js, 4000)
  → HttpAgent → AG-UI SSE → .NET 後端 (5200)
    /ag-ui       → WorkflowExecutionService（Workflow）
    /ag-ui/goal  → IGoalExecutor（Autonomous ReAct / Flow）
  → ExecutionEvent → AgUiEventConverter → AG-UI SSE → CopilotKit UI
```

## 關鍵設計決策

- **為何需要 Node.js 中間層**：CopilotKit `<CopilotKit runtimeUrl>` 使用自有協定（非直接 AG-UI），需 Runtime 轉譯。Runtime 內部用 `HttpAgent` 連接 .NET AG-UI 端點。
- **為何 server.mjs 用 http.createServer**：Express `app.use` 會 strip 路徑前綴，導致 Hono 內部路由 404。原生 `http.createServer` 保留完整路徑。
- **forwardedProps**：CopilotKit `properties` 放在 AG-UI 的 `forwardedProps`（非 `state`）。.NET 端點從此讀取 workflowJson 和 credentials。
- **Active Message 追蹤**：AgUiEventConverter 用 `_hasActiveMessage` 確保 TEXT_MESSAGE_CONTENT 前有 START，ToolCall/ReasoningStep 前先 END。
- **兩個 Agent**：`craftlab`（/ag-ui，Workflow）和 `craftlab-goal`（/ag-ui/goal，Autonomous）。

## 切換 ReAct / Flow

```json
// appsettings.json
{ "ExecutionMode": "flow" }   // 或 "react"（預設）
```

## 開發注意

- .NET 後端運行中無法 rebuild（檔案鎖定），需先 kill process
- CopilotKit Runtime 改動後需手動重啟
- `client-app/node_modules` 不進 git
