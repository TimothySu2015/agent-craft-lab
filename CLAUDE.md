# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet build                                              # 編譯整個 solution

# ─── React 前端 + .NET API 後端 ───
# Terminal 1 — .NET API 後端（port 5200）
dotnet run --project AgentCraftLab.Api

# Terminal 2 — CopilotKit Runtime（port 4000）
cd AgentCraftLab.Web && node server.mjs

# Terminal 3 — React 開發伺服器（port 5173）
cd AgentCraftLab.Web && npm run dev:vite

# 開啟 http://localhost:5173

# MCP 測試 Server（用於 Workflow Studio 的 MCP 工具整合）
npx -y @modelcontextprotocol/server-everything streamableHttp
# → http://localhost:3001/mcp
```

`dotnet test` runs 1098 unit tests. `TreatWarningsAsErrors` is enabled on all projects.

**整合測試：** `dotnet run --project AgentCraftLab.Autonomous.Playground -- --test`（8 場景，需 Azure OpenAI 憑證於 `appsettings.json`）。

**API 金鑰管理：** 透過 React 前端 `/settings` 頁面設定，存入後端 `ICredentialStore`（DPAPI 加密）。後端執行時直接從 Store 讀取，前端不再傳送明文 API Key。

**Code Style：** `.editorconfig` 強制 file-scoped namespaces、大括號必填、using 排序等規則。因 `TreatWarningsAsErrors`，違反這些 style 規則會導致編譯失敗。

## 對談與開發規範

- **使用繁體中文對談**：所有回覆、說明、commit message 皆使用繁體中文
- **善用知識庫與網路搜尋**：每次開發前，可透過 NotebookLM（知識庫）查詢專案相關文件，並搭配網路搜尋取得最新技術資訊，以提升開發的準確性與品質

## 開發流程（強制遵守）

### 1. 先調研再動手（禁止未查詢就開發）

執行任何計畫或實作任務前，**必須**先透過以下至少一種方式調研：
- **NotebookLM 知識庫**（`mcp__notebooklm-mcp__notebook_query`）查詢專案相關文件
- **網路搜尋**（`WebSearch`）取得業界最新做法、最佳實踐、相關框架設計

調研目的：了解業界現有方案、對比優缺點、確認技術選型合理性。**禁止跳過調研直接實作。**

### 2. 程式碼 Review（禁止未 review 就 commit）

程式撰寫完成後，**必須**執行 `/clean-code-review` skill 對本次修改進行 Clean Code Review：
- 修復所有 Critical Issues 和 Warnings
- Suggestions 視情況決定是否修復（需說明理由）
- Review 通過（無 Critical/Warning）後才可 commit

**禁止未經 review 就 commit。**

### 3. 補齊單元測試

Review 修改完成後，**必須**檢查並補齊單元測試：
- 檢查本次新增/修改的 public API 是否都有對應測試
- 補上缺少的測試案例（正常路徑 + 邊界情況）
- 確認 `dotnet test` 全部通過後才可 commit

## 開源 Repo 同步流程

**私有 repo**（`F:/codes/AgentFrameworkProject`）→ **開源 repo**（`F:/codes/agent-craft-lab`）

### 開發流程

```bash
# 1. 私有 repo 開 branch 開發
git checkout -b feat/xxx
# ... 開發、測試、commit ...
git push origin feat/xxx

# 2. merge 回 main
git checkout main && git merge feat/xxx && git push

# 3. 同步到開源（自動開分支 + 建 PR）
bash sync-to-opensource.sh "feat: 功能描述"
# → 開源 repo 會出現 sync/yyyymmdd-hhmmss 分支
# → 到 GitHub 建 PR merge 到 main
```

### 同步腳本排除清單

不會同步到開源的項目：
- `AgentCraftLab.Commercial/`（MongoDB + OAuth）
- `AgentCraftLab.CopilotKit/`（獨立 CopilotKit Runtime）
- `AgentCraftLab.Autonomous.Playground/`（CLI 測試主控台）
- `AgentCraftLab/`（Blazor 前端）
- `.claude/`、`nupkgs/`、`deploy*.zip`
- `docs/` 中非 user-guide / developer-guide 的文件

### 新增專案時

如果新增了開源專案，需更新：
1. `sync-to-opensource.sh` — 加到 `for proj in ...` 清單
2. `AgentCraftLab.slnx` — 加 `<Project>` 行

## Critical Constraints

- **禁止 Semantic Kernel**：所有程式碼純用 `Microsoft.Agents.AI` 系列 API，不使用 `Microsoft.SemanticKernel` 命名空間
- Microsoft.Agents.AI 已 GA（1.0.0）；.csproj 中以 `1.*` 版本語法自動拉取最新穩定版
- .NET 10 + LangVersion 13.0

## Solution 概覽

| 專案 | 定位 |
|------|------|
| `AgentCraftLab.Api` | 純後端 API（AG-UI + REST，Minimal API 端點） |
| `AgentCraftLab.Web` | React 前端（React Flow + CopilotKit + shadcn/ui） |
| `AgentCraftLab.Search` | 獨立搜尋引擎（FTS5 + 向量 + RRF 混合搜尋） |
| `AgentCraftLab.Engine` | 開源核心（SQLite + 單人模式，5 種策略 + 8 種節點 + 4 層工具 + Middleware + Hooks） |
| `AgentCraftLab.Autonomous` | ReAct 迴圈 + Sub-agent 協作 + 15 meta-tools + 安全機制 |
| `AgentCraftLab.Autonomous.Flow` | Flow 結構化執行（LLM 規劃 → 7 種節點 → Crystallize） |
| `AgentCraftLab.Script` | 多語言沙箱引擎（Jint JS + Roslyn C#，IScriptEngine / IScriptEngineFactory 介面） |
| `AgentCraftLab.Ocr` | OCR 引擎（Tesseract，IOcrEngine 介面，繁中/簡中/英/日/韓） |
| `AgentCraftLab.Cleaner` | 資料清洗引擎（Partition → Clean → Schema Mapper，7 種格式 + 多層 Agent） |
| `AgentCraftLab.MongoDB` | MongoDB 資料庫 Provider（替換 SQLite Store，可選啟用） |

**開發規則**：核心功能放 Engine；搜尋/擷取/分塊 → Search

## Architecture

### Workflow Execution — 三層架構

```
WorkflowExecutionService.ExecuteAsync(request)         ← 精簡編排器（~180 行）
  → ParseAndValidatePayload
  → Hook(OnInput)
  → WorkflowPreprocessor.PrepareAsync                  ← 節點分類 + RAG + AgentContext 建構
  → WorkflowStrategyResolver.Resolve                   ← 策略選擇
  → IWorkflowStrategy.ExecuteAsync
  → Hook(OnComplete / OnError)
  → IAsyncEnumerable<ExecutionEvent>
```

**5 種策略：** Single / Sequential / Concurrent / Handoff / Imperative

**自動偵測：** `NodeTypeRegistry.HasAnyRequiringImperative()` 查詢 → Imperative；任一 agent 多條 outgoing → Handoff；其他 → Sequential

### 節點類型 — NodeTypeRegistry（唯一真相來源）

| 節點 | IsExecutable | RequiresImperative | IsAgentLike | 說明 |
|------|---|---|---|------|
| `agent` | ✓ | | ✓ | 本地 LLM Agent（ChatClientAgent + tools） |
| `a2a-agent` | ✓ | ✓ | ✓ | 遠端 A2A Agent（URL + format auto/google/microsoft） |
| `autonomous` | ✓ | ✓ | ✓ | ReAct 迴圈（IAutonomousNodeExecutor 介面解耦） |
| `condition` | ✓ | ✓ | | 條件分支 |
| `loop` | ✓ | ✓ | | 迴圈 |
| `router` | ✓ | ✓ | | 多路由分類 |
| `human` | ✓ | ✓ | | 暫停等待使用者輸入（text/choice/approval 三模式） |
| `code` | ✓ | ✓ | | 確定性轉換（TransformHelper — 9 種模式 + JS 沙箱） |
| `iteration` | ✓ | ✓ | | foreach 迴圈（SplitMode + MaxItems 50） |
| `parallel` | ✓ | ✓ | | fan-out/fan-in 並行（Task.WhenAll + MergeStrategy） |
| `http-request` | ✓ | ✓ | | 確定性 HTTP 呼叫 |
| `start` / `end` | | | | Meta 節點（IsMeta） |
| `rag` | | | | 資料節點（IsDataNode） |

**新增節點**：(1) `NodeTypes` 加常數 (2) `NodeTypeRegistry` 加一行 (3) `NodeExecutorRegistry` 加 handler

### Agent 四層工具來源

`AgentContextBuilder.ResolveToolsAsync()` 合併：Tool Catalog（內建）+ MCP Servers + A2A Agents + HTTP APIs + OCR + Script

### Credentials — 後端加密儲存

```
Settings 頁面 → api.credentials.save() → POST /api/credentials（DPAPI 加密）
AG-UI 執行 → ResolveCredentialsAsync() → ICredentialStore.GetDecryptedCredentialsAsync()
前端不再傳送 API Key（localStorage 不存明文，saved flag 判斷）
```

### Chat 附件上傳 — 獨立上傳管線

```
📎 選檔 → POST /api/upload → { fileId }（暫存 1 小時）
       → CopilotKit properties.fileId（forwardedProps）
       → AgUiEndpoints: GetAndRemove(fileId) → request.Attachment
       → WorkflowPreprocessor（RAG ingest / ZIP 解壓 / 多模態 DataContent）
```

CopilotKit 僅支援圖片且實作未完成，故用獨立上傳繞過。`StableChatInput`（module-scope 定義）+ `chatInputFileRef` 確保 component identity 穩定，避免 CopilotChat 重建 Input。

### Autonomous Agent — ReAct 迴圈

ReactExecutor（~540 行）透過策略物件拆分：`IBudgetPolicy`、`IHumanInteractionHandler`、`IHistoryManager`、`IReflectionEngine`、`IToolDelegationStrategy`。

**雙模型架構：** TaskPlanner 用強模型（gpt-4o）規劃，ReactExecutor 用弱模型（gpt-4o-mini）執行。

**12+3 meta-tools**（MetaToolFactory）：create/ask/spawn/collect/stop/send/list sub-agents + shared state + ask_user + peer_review + challenge + search_tools + load_tools + create_tool

**create_tool 自製工具**：`ToolCreator` + `ToolCodeSanitizer` + `IToolCodeRunner`（DI 解耦）。Agent 執行中用 JS 自製工具（Jint 沙箱），三層驗證（安全掃描 → 語法測試 → 功能測試）。每 session 上限 10 個，ephemeral（不跨 session）。需註冊 `IToolCodeRunner` 橋接器。

**Spawn 並行**：持久 Agent（多輪對話）vs 臨時 Spawn（一次性並行，預設）。支援 Cascade Kill、Spawn Depth（巢狀）、Send to Running Spawn。

**安全機制**：P0 Risk 審批 → P1 Transparency → P2 Multi-Agent Reflexion → S1~S8 隔離保護。Sub-agent instructions 有 prompt injection 過濾 + 不可覆蓋安全前綴。

**Multi-Agent Reflexion**：`MultiAgentReflectionEngine` — 三角色評估面板（Factual/Logic/Completeness Auditor）平行審查 + 投票聚合（Contradiction 升級）。`AutoReflectionEngine` 根據複雜度自動選擇 Single 或 Panel。`ReflectionMode`：Single / Panel / Auto。可選 Judge 合成。自訂 `EvaluatorPersona`。

**Step-level PRM**：`IStepEvaluator` + `RuleBasedStepEvaluator`（Route A）— 每步 tool call 後確定性品質檢查。五條規則：空結果偵測、重複呼叫、結果膨脹、連續失敗（Block 級）、偏離目標。零 LLM 成本。未來 Route B 可加 `LlmStepEvaluator`（每 N 步 LLM 評分）。

**跨 Session 記憶**：Reflexion 反思 + 關鍵字匹配 + 經驗注入（IExecutionMemoryStore）+ 三層記憶：Episodic（執行經驗）/ Entity（實體事實，IEntityMemoryStore）/ Contextual（使用者模式，IContextualMemoryStore）

**Tool Search 按需載入 + ToolOrchestrator**：`ToolOrchestrator` 集中管理工具分類（唯一真相來源）。`MetaToolTier` 五層分類：Core（shared_state, list）永遠可用 → Discovery（search/load）永遠可用 → Delegation/Collaboration/Creation 按需載入。外部工具同理：SafeWhitelist 常駐，其他 MCP/A2A/HTTP 工具按需搜尋。簡單任務省 50%+ tokens。

**平行 Guardrails**：`ParallelGuardRailsEvaluator` — ReAct 迴圈 iteration > 1 時 guardrails input scan 與 LLM 呼叫平行執行，fail-fast 透過 CancellationToken 取消。`IncrementalGuardRailsScanner` 支援逐 chunk 增量 output 掃描。啟用：`ParallelGuardRails=true`。

**Checkpoint 持久化 + Resume**：`CheckpointManager` + `ICheckpointStore`（SQLite）— ReAct 迴圈每 N 步（預設 5）儲存完整狀態快照（messages, trackers, shared state, sub-agents）。`SerializableChatMessage` 處理 MEAI ChatMessage 多態序列化（TextContent / FunctionCallContent / FunctionResultContent）。`CheckpointSnapshot` 為完整 JSON round-trip 快照。`ReactExecutor.ResumeAsync()` 從 checkpoint 恢復執行（不重新生成 Plan、不重新查詢記憶）。啟用：`CheckpointEnabled=true`。

### Autonomous Flow — 結構化執行

透過 `IGoalExecutor` 介面與 ReAct 隔離。DI 切換：`AddAutonomousAgent()` vs `AddAutonomousFlowAgent()`。

**三層漏斗：** Engine Workflow（人類設計）→ Flow（AI 規劃+結構化執行+Crystallize）→ ReAct（完全自主）

**漏斗銜接：** ReactTraceConverter 將 spawn/collect 軌跡轉 FlowPlan JSON → ExecutionMemoryService 存入 → 下次 Flow 規劃時注入作為 Reference Plan

**7 種節點：** agent / code / condition / iteration / parallel / loop / http-request

**雙模型：** Plan 用 gpt-4.1（temperature=0），Execute 用 gpt-4o-mini

**Crystallize：** ExecutionTrace → Studio buildFromAiSpec JSON → 存檔 Data/flow-outputs/

**Flow 進階功能（Phase A+B）：**
- **F7 Prompt Cache**：`BuildAgentMessages` 用 `CacheableSystemPrompt` 分割 static/dynamic（Anthropic cache_control）
- **F3 Context Windowing**：agent 節點 input > 3000 chars 時 `LlmContextCompactor` 壓縮（ContextWindowingBudget=500）
- **F1 Checkpoint**：`FlowCheckpointSnapshot`（PlanJson + CompletedNodeIndex + PreviousResult + SkipIndices），每節點完成存 `ICheckpointStore`。`ResumeFromCheckpointAsync` 從斷點恢復（透過 `Options["resumeExecutionId"]` 觸發）
- **F4 Adaptive Replanning**：agent 節點失敗（空 output）→ `PlanAsync` 重規劃剩餘步驟（`MaxReplanAttempts=1`，`lastSuccessfulResult` 回退）
- **F6 Working Memory**：`FlowWorkingMemory`（`ConcurrentDictionary`），agent 節點透過 `flow_memory_write` meta-tool 寫入，下游節點 system prompt 自動注入 memory snapshot

### AG-UI 雙端點路由

```
Execute tab (Chat)
  agent="craftlab"       → POST /ag-ui      → WorkflowExecutionService（畫布 Workflow）
  agent="craftlab-goal"  → POST /ag-ui/goal → IGoalExecutor（ReAct / Flow 自主模式）
```

**重要**：Execute tab 預設走 `/ag-ui`（畫布 Workflow 執行），不是 FlowExecutor。FlowExecutor 只在 `ExecutionMode=flow` 且走 `/ag-ui/goal` 時觸發。

### Engine 共用壓縮積木

```
Engine 積木（各自獨立，React + Flow + 畫布 Workflow 都可用）
├─ IContextCompactor          — LLM 摘要壓縮（LlmContextCompactor 實作）
├─ ToolResultTruncator        — 截斷超長 tool results（零 LLM 成本）
├─ MessageDeduplicator        — 去重 + 合併短訊息（零 LLM 成本）
├─ MessageSerializer          — ChatMessage[] ↔ string 序列化
├─ CompressionState           — 壓縮狀態追蹤（已截斷 tool IDs、累計 token 節省量）
├─ CacheableSystemPrompt      — Static/Dynamic prompt 分割（Anthropic cache_control）
└─ RecoveryChatClient         — L3 截斷恢復 / L4 Context Overflow 壓縮重試 / L5 Model 不可用
```

**設計原則**：Engine 提供積木，上層（ReactExecutor / FlowExecutor / WorkflowStrategy）根據狀況自由組合。不寫死管線順序。

**Memory 語義搜索**：`IExecutionMemoryStore.SemanticSearchAsync` — 有 CraftSearch 時用 FTS5（trigram + BM25），否則 fallback 到 Jaccard。`SqliteExecutionMemoryStore` 儲存時背景索引到 CraftSearch。

**設計文件**：`docs/zh-TW/claude-code-architecture-analysis.md`（ReAct 進化）、`docs/zh-TW/flow-evolution-plan.md`（Flow 進化）、`docs/zh-TW/engine-shared-infrastructure-plan.md`（Engine E1-E4）

### CraftSearch 搜尋引擎

獨立類別庫。核心：`ISearchEngine` + `IDocumentExtractor` + `ITextChunker` + `IReranker`。搜尋模式：FullText（FTS5）/ Vector（SIMD Cosine）/ Hybrid（RRF k=60）。Provider：SqliteSearchEngine（開源）/ InMemorySearchEngine（測試）/ PgVectorSearchEngine / QdrantSearchEngine。

**Advanced RAG 元件：**
- **Relevance Filtering**：`SearchQuery.MinScore` 過濾低分結果（預設 `DefaultRagMinScore = 0.005f`）
- **Reranker**：`IReranker` 介面 + 3 種實作 — `NoOpReranker`（預設）/ `CohereReranker`（Cohere API cross-encoder）/ `LlmReranker`（用現有 ChatClient 評分，位於 Engine 層）
- **Metadata Enrichment**：`ExtractionResult.Metadata` 由各 Extractor 自動填充（title/author/page_count 等），chunk 級 metadata 含 `chunk_index`/`total_chunks`
- **StructuralChunker**：按 Markdown/HTML heading + 段落邊界切割，太長委派 FixedSizeChunker，太短合併。DI：`AddCraftSearch(ChunkerType.Structural)`
- **Context Reorder**：Lost in the Middle 解法 — Rerank 後重新排列 chunks，最高分放頭尾（LLM 注意力最強），低分放中間
- **Context Compression**：Token Budget 自適應壓縮 — 超過預算時用 LLM 摘要壓縮，不超過就不壓縮

### RAG Pipeline

`RagService` + `RagChatClient`（DelegatingChatClient）。Ingest：多格式擷取 → 分塊 → embedding(1536維) → 索引（含 metadata）。Search：Hybrid 搜尋 → MinScore 過濾 → Rerank 重排序 → 注入 system message（含 metadata 來源標注）。indexName 慣例：`{userId}_rag_{guid}`（臨時）、`{userId}_kb_{id}`（知識庫）。

### CraftCleaner 資料清洗引擎

獨立類別庫 `AgentCraftLab.Cleaner`，參考 Unstructured.io 架構。管線：`IPartitioner → IElementFilter → ICleaningRule → CleanedDocument`。

**7 個 Partitioner**：DocxPartitioner / PptxPartitioner / HtmlPartitioner / PlainTextPartitioner / XlsxPartitioner / PdfPartitioner / ImagePartitioner（透過 IOcrProvider 介面）。

**7 條清洗規則**：unicode_normalize(30) → clean_non_ascii_control(50) → clean_whitespace(100) → clean_bullets(200) → clean_ordered_bullets(210) → clean_dashes(300) → group_broken_paragraphs(400)。

**Schema Mapper**：`CleanedDocument[] + SchemaDefinition → LLM → JSON`。兩種實作：
- `LlmSchemaMapper` — 單次 LLM（快速模式）
- `MultiLayerSchemaMapper` — Layer 2 大綱規劃 + Layer 3 逐項擷取（並行 + Search）+ Layer 4 LLM Challenge 驗證（可選）+ Merge

**LLM Challenge（Layer 4）**：第二個 LLM 驗證第一個的擷取結果。信心度三級：Accept(>0.8) / Flag(0.5-0.8) / Reject(<0.5)。

**共用工具**：`PartitionerHelper`（表格 + 元素建立）、`MetadataKeys`（常數）、`MimeTypeHelper`（副檔名→MIME）。

**擴充**：`services.AddCleaningRule<T>()` / `services.AddPartitioner<T>()` / 放 JSON 到 `Data/schema-templates/`。

### DocRefinery 文件精煉

精煉專案管理：`RefineryProject` / `RefineryFile` / `RefineryOutput`。前端頁面 `/doc-refinery`，4 Tab（Files / Preview / Output / Settings）。

**檔案管理**：上傳 → 清洗 → 索引持久化（`IndexName`）。每個檔案有 `IndexStatus`（Pending → Indexing → Indexed / Failed / Skipped）和 `IsIncluded`（checkbox 勾選）。收折式上傳進度（Channel pattern 即時 SSE）。Rate limit retry（batch=20 + 指數退避）。

**雙模式**：快速（單次 LLM）/ 精準（多層 Agent + Search + 可選 Challenge）。`ExtractionMode` + `EnableChallenge` 存在 RefineryProject。

**輸出版本化**：每次 Generate 產生新版本（v1, v2, v3...）。含 Token 統計（`TotalInputTokens` / `TotalOutputTokens`）、信心度、來源檔案清單、Challenge 結果。Markdown 渲染（react-markdown + remark-gfm）+ JSON 樹狀檢視（react-json-view-lite）。

**API**：16 端點（`/api/refinery/*`）+ Schema 模板（`/api/schema-templates`）。

### Middleware Pipeline

`AgentContextBuilder.ApplyMiddleware()` 依序包裝：GuardRails → PII → RateLimit → Retry → Logging。RAG 獨立掛載。

### GuardRails 內容安全（企業級）

`IGuardRailsPolicy` 介面解耦，可替換 ML 分類器、Azure Content Safety、NeMo Guardrails。

**DefaultGuardRailsPolicy**：關鍵字 + Regex 規則引擎。三級動作：Block / Warn / Log。CJK `Contains` 匹配（不靠空格斷詞）。Prompt Injection 偵測（9 種 pattern，opt-in）。Topic 限制（限定主題白名單）。

**掃描範圍**：預設掃描所有 User 訊息（不只最後一則）。可選掃描 LLM Output。串流模式 buffer 後掃描。

**前端設定**：MiddlewareConfigDialog → Scan All Messages / Scan Output / Injection Detection / Blocked Terms / Warn Terms / Regex Rules / Allowed Topics / Blocked Response。

**擴展**：實作 `IGuardRailsPolicy` 即可替換規則引擎。DI：`services.AddSingleton<IGuardRailsPolicy>(...)`。

### PII 保護（企業級）

`AgentCraftLab.Engine/Pii/` — 透過 `IPiiDetector` + `IPiiTokenVault` 介面解耦，可替換為 ONNX NER、Presidio、Azure AI。

**偵測**：`RegexPiiDetector` — 35 條規則 × 6 Locale（Global/TW/JP/KR/US/UK），含 7 種 Checksum 驗證（Luhn、mod97、台灣身分證/統編、JP My Number、KR RRN、UK NHS）+ Context-aware 加權 + 重疊解析。

**匿名化**：兩種模式 —
- **可逆**（有 vault）：`[EMAIL_1]`、`[PHONE_1]` 等型別 token，LLM 回應後自動還原
- **不可逆**（無 vault）：固定文字替換（`***`），向下相容

**雙向掃描**：Input anonymize + Output detokenize。串流 detokenize 有 buffer + 安全閥。

**審計**：`[PII] Direction=Input, Entities=[Global.Email:1, TW.Phone:1], Count=2`（絕不記錄 PII 原始值）。

**前端設定**：MiddlewareConfigDialog → Protection Mode / Locale 多選 / Confidence Threshold / Scan Output。

**擴展**：實作 `IPiiDetector` 即可替換偵測引擎；實作 `IPiiTokenVault` 可換 Redis/DB 持久化。DI：`services.AddPiiProtection()`。

### Workflow Hooks

6 個插入點：OnInput → PreExecute → PreAgent → PostAgent → OnComplete / OnError。兩種類型：code（TransformHelper）/ webhook（HTTP POST）。BlockPattern 支援 regex 攔截。

### AI Build

自然語言 → FlowBuilderService → LLM 串流 → buildFromAiSpec（partial update 優先 → full rebuild fallback）。Tool ID 白名單由 ToolRegistryService 動態注入。

## Extensibility（速查）

- **新策略**：實作 `IWorkflowStrategy` + `WorkflowStrategyResolver.Resolve()` 加 case
- **新內建工具**：`ToolImplementations.cs` 加方法 + `ToolRegistryService` 加 `Register()`（AI Build 自動同步，僅需更新 `.claude/skills/build-flow/node-specs.md`）
- **新節點類型**：(1) `NodeTypes` 加常數 (2) `NodeTypeRegistry` 加一行 (3) `NodeExecutorRegistry` 加 handler (4) JS `NODE_REGISTRY`
- **新 Middleware**：繼承 `DelegatingChatClient` + `ApplyMiddleware()` 加 case
- **新 Flow 節點**：`FlowNodeRunner` 加 case + `FlowPlannerPrompt` + `FlowPlanValidator.SupportedNodeTypes` + `WorkflowCrystallizer.StepToNode`
- **替換 Autonomous 策略物件**：實作對應介面 + DI `Replace` 註冊
- **替換實體記憶 Store**：實作 `IEntityMemoryStore` + DI 替換（預設 SqliteEntityMemoryStore）
- **替換情境記憶 Store**：實作 `IContextualMemoryStore` + DI 替換（預設 SqliteContextualMemoryStore）
- **替換步驟評估器**：實作 `IStepEvaluator` + DI 替換（預設 RuleBasedStepEvaluator，未來可換 LlmStepEvaluator）
- **替換反思引擎**：實作 `IReflectionEngine`（預設 AutoReflectionEngine → Single/Panel 自動選擇）
- **自訂 Evaluator Persona**：`ReflectionConfig.Personas` 自訂審查角色（預設 Factual/Logic/Completeness）
- **工具分類擴展**：`MetaToolFactory.TierMap` 新增 meta-tool 時指定 Tier（Core/Discovery/Delegation/Collaboration/Creation）
- **替換腳本引擎**：實作 `IScriptEngine` + DI 替換（Jint JS / Roslyn C# / Python）
- **新增腳本語言**：`ScriptEngineFactory.Register("language", engine)` + 前端 CodeForm SCRIPT_LANGUAGES 加選項
- **擴展沙箱 API**：實作 `ISandboxApi`（`GetMethods()` 回傳 delegate dictionary）
- **替換 OCR 引擎**：實作 `IOcrEngine` + DI 替換
- **替換 Reranker**：實作 `IReranker` + `services.AddReranker<T>()`（預設 NoOpReranker）
- **替換分塊策略**：實作 `ITextChunker` 或用 `AddCraftSearch(ChunkerType.Structural)`
- **新增外部工具模組**：參考 `AgentCraftLab.Ocr` / `AgentCraftLab.Script` 的 `AddXxx()` + `UseXxxTools()` pattern
- **新清洗規則**：實作 `ICleaningRule` + `services.AddCleaningRule<T>()`
- **新 Partitioner**：實作 `IPartitioner` + `services.AddPartitioner<T>()`
- **新 Schema 模板**：在 `Data/schema-templates/` 放 JSON 檔案（零程式碼）
- **替換 OCR Provider（Cleaner）**：實作 `IOcrProvider` 或用 `AddCraftCleanerOcr()` 橋接

## 外部參考資源

**模型 Context Window 對照表：** `ModelContextWindows.cs` 資料來源為 [Azure Foundry Models](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure?tabs=global-standard-aoai%2Cglobal-standard&pivots=azure-openai)

## Integration Test

```bash
# 非互動式整合測試（8 場景，需 Azure OpenAI 憑證）
dotnet run --project AgentCraftLab.Autonomous.Playground -- --test
```

| 場景 | 測試功能 |
|------|---------|
| 1 | 基礎 ReAct — GetDateTime 工具呼叫 |
| 2 | Step PRM — 空結果偵測 + 提示注入 |
| 3 | Multi-Agent Reflexion — Panel 模式 3 Evaluator |
| 4 | Checkpoint — Calculator 多步計算 |
| 5 | Spawn 平行 — AzureWebSearch 比較研究 |
| 6 | Tool Search — search_tools → load_tools → 按需載入 |
| 7 | create_tool — JS 沙箱自製工具 + 三層驗證 |
| 8 | Checkpoint Resume — Save + Resume 端到端 |

## Known Limitations

- **A2A Client auto 模式雙格式嘗試**：`format="auto"` 時先試 Google A2A 再試 Microsoft 格式，非超時錯誤會嘗試兩種。這是 auto-detection 的設計取捨 — 使用者知道對方格式時應直接指定 `google` 或 `microsoft` 避免額外延遲。
- **AsyncLocal 在 async iterator 中遺失**：`Activity.Current`（`AsyncLocal`）在 `yield return` 後變 null（dotnet/runtime#47802，上游未修復）。**已有穩定 workaround**：`TraceCollectorExporter` 用 Event-based 追蹤 + `session.id` tag 配對，完全不依賴 `AsyncLocal`。UI TraceWaterfall 不受影響。

## CopilotKit 前端遷移

### 已完成

| # | 項目 | 狀態 |
|---|---|---|
| 1 | Save as Template | ✅ 完成（後端持久化 + 離線 fallback） |
| 2 | Workflow Settings 面板 | ✅ 完成 |
| 3 | Middleware 設定 UI | ✅ 完成 |
| 4 | Code Generation | ✅ 完成 |
| 5 | Export 部署包 | ✅ 完成（4 種模式） |
| 6 | HumanInputBridge | ✅ 完成（AG-UI 模式） |
| 7 | KB 檔案上傳 + Ingest | ✅ 完成（SSE streaming 進度 + 檔案列表 + 單檔刪除） |
| 8 | Skill Manager 頁面 | ✅ 完成（含 Built-in Skill 詳情檢視） |
| 9 | Credentials 後端串接 | ✅ 完成（DPAPI 加密，前端不存明文） |
| 10 | API Keys 管理 | ✅ 完成（建立/列表/撤銷 + 使用說明） |
| 11 | 排程管理 | ✅ 完成（CRUD + Toggle + 執行記錄） |
| 12 | Service Tester | ✅ 完成（雙面板 + Chat + 5 種協定） |
| 13 | 自訂範本後端串接 | ✅ 完成（/api/templates + 離線 fallback） |
| 14 | i18n 翻譯 | ✅ 完成（en + zh-TW，common/studio/chat） |
| 15 | ErrorBoundary | ✅ 完成（全域錯誤邊界） |
| 16 | ExpandableTextarea | ✅ 完成（7 個表單欄位 + 行號 + language 標籤） |
| 17 | Chat 附件上傳 | ✅ 完成（獨立上傳 + fileId 引用，繞過 CopilotKit 限制） |
| 18 | TraceWaterfall | ✅ 完成（Event-based 甘特圖 + OTel 外部匯出） |

### 待做

| # | 項目 |
|---|---|
| 1 | Execute Chat 意圖偵測 |
| 2 | 首次使用引導流程（Onboarding Wizard） |

### 新功能（遷移後新增）

| 功能 | 說明 |
|------|------|
| Settings 個人設定 | `/settings` 頁面：Profile / 語系 / 預設模型 / Credentials / Budget / 進階 |
| OCR 工具 | `AgentCraftLab.Ocr`：Tesseract OCR，tessdata 目錄存在時自動啟用 |
| 多語言沙箱腳本 | `AgentCraftLab.Script`：Code 節點 script 模式，Jint JS + Roslyn C#（collectible ALC + AST 安全掃描）+ AI 腳本生成 + Test Run |
| Script Generator | `POST /api/script-generator`：LLM 生成符合沙箱的腳本（JS / C#，按 language 切換 prompt） |
| Prompt Refiner | `POST /api/prompt-refiner`：LLM + Prompt Engineering 指南優化 Agent Instructions |
| ExpandableTextarea | 可展開全螢幕編輯器：行號 gutter + Edit/Preview 雙模式 + language 標籤 + ✨ Optimize 按鈕 |
| MonacoCodeEditor | Code 節點 script 模式專用編輯器：Monaco Editor（VS Code 核心）+ 語法高亮 + 括號配對 + 全螢幕 Modal |
| TraceWaterfall | 執行追蹤瀑布圖：甘特圖 + Modal 詳情 + Tool Call 子行 + 多色彩 + 可拖拽高度 |
| CraftCleaner | `AgentCraftLab.Cleaner`：7 格式 Partition + 7 清洗規則 + Schema Mapper（單層/多層）+ Markdown Renderer |
| DocRefinery | `/doc-refinery` 頁面：精煉專案 + 檔案勾選 + 清洗預覽 + 雙模式 Generate + LLM Challenge + 信心度 + 版本管理 + Token 統計 |
| Chat 清除對話 | Execute tab ↺ 按鈕：清除對話訊息（CopilotKit remount）+ 重置執行狀態（coagent-store）+ 清除附件 |

### TraceWaterfall — 執行追蹤瀑布圖

ConsolePanel 的 Trace tab 顯示 Jaeger/Aspire 風格的瀑布圖，呈現每個節點的執行時間、tokens、model、tool call。

**雙軌架構**：
- **Event-based（UI 用）**：`TraceCollectorExporter.RecordEvent()` 從 `ExecutionEvent` 即時組裝 span，無 AsyncLocal 依賴
- **OTel（外部工具用）**：`EngineActivitySource` + `ActivitySource("AgentCraftLab.Engine")` 保留給 Aspire/Jaeger，透過 `session.id` tag 配對

**資料流**：`ExecutionEvent` → `RecordEvent()` → `GetSpans()` → STATE_SNAPSHOT `traceSpans` → 前端 `useTraceBuilder` → `TraceWaterfall`

**前端元件**：`TraceWaterfall.tsx`（共用）+ `useTraceBuilder.ts`（hook）+ ConsolePanel Log/Trace 雙 tab + 點擊開 Modal（完整內容 + Copy）+ 點擊 Tool Call 開 ToolCallModal

### Prompt Refiner — AI Prompt 優化

使用者在 Agent 節點展開 Instructions 編輯器後，點擊 ✨ Optimize 按鈕，透過 LLM 優化 prompt。

**架構**：`SkillPromptProvider`（通用 Skill Prompt 載入器）→ `PromptRefinerService`（LLM 呼叫）→ `POST /api/prompt-refiner`。

**指南管理**：`Data/skill-prompts/prompt-refiner/` 目錄，common.md + 模型專屬指南（claude.md / gpt.md / gemini.md）。新增模型只需放入 .md 檔案，零程式碼修改。

**匹配策略**：優先 provider 匹配（openai→gpt.md）→ provider 別名（azure-openai→gpt）→ fallback model 名稱 Contains。

**前端**：ExpandableTextarea ✨ Optimize 按鈕 → PromptRefinerDialog（Before/After 預覽 + Changes 清單）→ Apply 套用。

**設計文件**：`docs/zh-TW/`（繁中）、`docs/en/`（英文）、`docs/ja/`（日文）、`docs/copilotkit-*.md`、`docs/prototype/design-system/MASTER.md`、`.ai_docs/features/SPEC_*.md`
