# AgentCraftLab.Autonomous.Flow

Flow 結構化執行模式 — LLM 規劃節點序列 → Engine 執行 → Crystallize 為固定 Workflow。

## 核心設計決策

### 雙模型架構
- **Plan**：`gpt-4.1`（`FlowAgentFactory.DefaultPlannerModel`），temperature=0，只呼叫一次
- **Execute**：`request.Model`（預設 gpt-4o-mini），多次呼叫，省成本

### IGoalExecutor 介面隔離
- `ReactExecutor` 和 `FlowExecutor` 都實作 `IGoalExecutor`（Engine 層）
- DI 切換：`AddAutonomousAgent()`（ReAct）vs `AddAutonomousFlowAgent()`（Flow）
- 消費端只認介面，不知道具體實作

### Parallel 分支隔離
- 用 `branch.Name` 作為 input（不傳完整使用者輸入），避免 gpt-4o-mini 不遵守隔離指令
- Branch name 必須是具體值（如 "AAPL", "English"），不能是佔位符
- 規劃時不知道項目就不要用 parallel（Rule 12）
- `SemaphoreSlim(MaxParallelBranches=3)` 防 429

### Condition 分支
- `HashSet<int> skipIndices` 記錄要跳過的節點 index
- 假設 Condition 後面接 `[TRUE]` 和 `[FALSE]`（gpt-4o 總是按此順序規劃）

### Loop 退出
- 新結果觸發退出條件時，保留上一輪的實質內容（不被確認訊息覆蓋）

## Prompt 優化關鍵規則

FlowPlannerPrompt 14 條規則中最常踩的坑：禁止重複搜尋、Parallel branch name 必須是具體值、Summarizer 不帶工具、多語言用 parallel 不用 iteration、禁止 iteration + search、格式化用 code 節點（零 token）。

**Rule 13（工具推理）**：每個 agent 評估是否需要即時資料（法規/股價/新聞/市場 → 必帶 search tool）。
**Rule 14（Synthesizer 強制）**：parallel 後面必須接 Synthesizer agent 彙整結果。

### FlowPlanValidator 確定性兜底
- 檢查 9：agent instructions 含即時資料關鍵字但 tools 為空 → warning
- 檢查 10：plan 最後一個 parallel 後面沒有 agent → warning「建議加 Synthesizer」
- `LikelyNeedsRealtimeData()` — 30 個中英關鍵字啟發式

### Flow Tuning Harness
48 scenarios（含工具推薦 4 案例）。`dotnet run --project AgentCraftLab.Flow.Tuning -- --test`。

## 依賴關係

`Autonomous` 和 `Autonomous.Flow` 互不引用，都只引用 `Engine`。透過 `IGoalExecutor` 介面解耦。
