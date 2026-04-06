# DocRefinery — 文件精煉

DocRefinery 是 AgentCraftLab 的文件資料清洗與結構化輸出功能。從多份不同格式的來源文件（PDF、DOCX、PPTX、XLSX、HTML、TXT、圖片），自動清洗並整合成一份標準的結構化規格文件。

---

## 1. 核心概念

```
多份來源文件 → 清洗（去雜訊）→ 結構化擷取（LLM）→ 規格文件
```

| 概念 | 說明 |
|------|------|
| **精煉專案** | 一個工作單位，包含多個來源檔案和多個輸出版本 |
| **清洗** | 將原始文件拆解為帶類型的元素（Title、NarrativeText、Table、ListItem…），去除頁首頁尾、正規化空白 |
| **Schema 模板** | 定義輸出文件的結構（如：軟體需求規格書有 13 個區塊） |
| **快速模式** | 一次 LLM 呼叫，適合小文件 |
| **精準模式** | 多層 Agent + 搜尋，適合大文件/多文件 |

---

## 2. 操作流程

### Step 1：建立專案

1. 點側邊欄 🏭 **文件精煉**
2. 點右上角 **建立**
3. 輸入專案名稱和描述

### Step 2：上傳檔案

1. 進入專案 → **檔案** tab
2. 拖拉檔案到上傳區（支援多檔同時上傳）
3. 每個檔案會即時顯示處理進度（清洗 → 索引）
4. 完成後可看到每個檔案的狀態圖示

**檔案狀態：**

| 圖示 | 狀態 | 說明 |
|------|------|------|
| ✅ | Indexed | 索引完成，可用於精準搜尋 |
| 🔄 | Indexing | 正在建立搜尋索引 |
| ⏳ | Pending | 等待索引 |
| ⚠️ | Failed | 索引失敗，可點 🔄 重試 |
| ⏭️ | Skipped | 快速模式不需要索引 |

### Step 3：預覽清洗結果

1. 切到 **清洗預覽** tab
2. 從下拉選一個檔案
3. 看到每個元素的類型標籤：

| 標籤顏色 | 元素類型 | 說明 |
|---------|---------|------|
| 藍色 | Title | 標題 |
| 灰色 | NarrativeText | 正文段落 |
| 綠色 | Table | 表格 |
| 黃色 | ListItem | 清單項目 |
| 紫色 | CodeSnippet | 程式碼 |
| 粉色 | Image | 圖片 |

### Step 3.5：選擇納入的檔案

每個檔案旁有 **checkbox**，可勾選/取消：
- ☑ 勾選 = 納入 Generate 的來源（預設全勾）
- ☐ 取消 = 排除，檔案變半透明 + 刪除線
- 不需要刪除檔案，隨時可重新勾選

### Step 4：設定 Schema 與模式

1. 切到 **設定** tab
2. 選擇 **Schema 模板**（如「軟體需求規格書」）
3. 選擇 **LLM Provider** 和 **Model**
4. 選擇 **擷取模式**：

| 模式 | 說明 | 適合 |
|------|------|------|
| **快速** | 一次 LLM 呼叫，把所有文件內容 + Schema 一起丟給 LLM | 小文件（< 10 頁）、少量文件 |
| **精準** | 多層 Agent + 搜尋引擎輔助 | 大文件（> 10 頁）、多文件、需要高精準度 |

5. 精準模式下可開啟 **LLM Challenge 驗證**（見下方說明）
6. 點 **Save** 儲存設定
7. 點 **Generate Structured Output** 開始產生

### Step 5：檢視輸出

1. 切到 **輸出** tab
2. 頂部顯示：
   - **版本選擇器**（v1, v2, v3...）
   - **信心度 badge**（綠 ≥80% / 黃 50-80% / 紅 <50%，啟用 Challenge 時才顯示）
   - **Markdown / JSON** 雙檢視切換
3. **來源檔案** — 顯示此版本使用了哪些檔案
4. **缺少欄位**（黃色）= LLM 找不到對應資料
5. **待確認問題**（橘色）= LLM 標記需確認的項目
6. **驗證質疑**（紫色）= LLM Challenge 的結果，按區塊分組，含原始值 vs 建議值對比
7. **Markdown 檢視** — 完整渲染（標題層級、表格框線、清單、程式碼高亮）
8. **JSON 檢視** — 可展開/收折的樹狀結構，語法高亮
9. 可 **複製** 或 **下載**（.md / .json）

### Step 6：迭代更新

- 補充新檔案或調整勾選 → 回 Settings tab → 再次 Generate
- 每次產生新版本（v1, v2, v3...），不覆蓋舊版
- Output tab 可用下拉切換版本，對比不同檔案組合的輸出結果

---

## 3. 精準模式架構

精準模式使用四層 Agent 架構：

```
Layer 2（大綱規劃）：
  LLM 分析文件摘要 → 判斷 Schema 哪些區塊有資料 → 規劃搜尋關鍵字

Layer 3（逐項擷取，並行）：
  每個區塊獨立一個 LLM 呼叫 →
  先用搜尋引擎找相關段落 → 再讓 LLM 只擷取該區塊的 JSON

Layer 4（LLM Challenge 驗證，可選，並行）：
  第二個 LLM 驗證 Layer 3 的擷取結果 →
  找出不一致、矛盾、可疑的欄位 → 給出信心度分數

Merge（純程式）：
  合併所有區塊 + Challenge 結果 → 完整規格文件
```

**優勢：**
- 每次 LLM 只專注一個主題，精準度高
- 搜尋引擎輔助，不怕文件太長
- 區塊間並行執行，不會比快速模式慢太多
- LLM Challenge 找出矛盾與錯誤，每個欄位都有信心度

### LLM Challenge 驗證（Layer 4）

在設定 tab 精準模式下開啟「LLM Challenge 驗證」後：

1. Layer 3 擷取完成後，每個區塊會被第二個 LLM 重新驗證
2. 驗證結果依信心度分為三級：

| 信心度 | 動作 | 說明 |
|--------|------|------|
| ≥ 80% | ✅ Accept | 兩個 LLM 一致，直接採用 |
| 50-80% | ⚠️ Flag | 有疑慮，標記待確認 |
| < 50% | ❌ Reject | 明確不一致 |

3. 輸出 tab 會顯示：
   - 整體信心度 badge
   - 按區塊分組的驗證質疑（可展開/收折）
   - 原始值 vs 建議值的紅/綠對比

4. Token 用量約為不開 Challenge 的 1.5 倍（每區塊多一次驗證 LLM 呼叫）

---

## 4. 支援的檔案格式

| 格式 | 副檔名 | 清洗能力 |
|------|--------|---------|
| Word | .docx | Heading style → Title，List → ListItem，Table 結構化 |
| PowerPoint | .pptx | Slide Shape type 分類，Drawing.Table |
| Excel | .xlsx | 每個工作表 → Markdown Table |
| PDF | .pdf | 啟發式分類（標題、清單、頁尾偵測） |
| HTML | .html | 標籤直接對應（h1→Title, table→Table） |
| 純文字 | .txt, .md, .csv | Markdown heading, bullet, code fence |
| 圖片 | .png, .jpg, .tiff, .bmp | OCR 辨識（需安裝 Tesseract） |

---

## 5. 內建 Schema 模板

### 軟體需求規格書

13 個區塊：

| 區塊 | 說明 |
|------|------|
| document | 文件 metadata（標題、版本、日期、來源） |
| project_overview | 專案概述（名稱、目標、範圍、限制） |
| stakeholders | 利害關係人 |
| functional_requirements | 功能需求（含驗收條件、MoSCoW 優先級） |
| non_functional_requirements | 非功能需求（效能、安全、可用性） |
| data_model | 資料模型（Entity + Fields） |
| api_endpoints | API 端點規格 |
| ui_screens | UI 畫面清單 |
| timeline | 時程規劃 + 里程碑 |
| budget | 預算拆解 |
| risks | 風險評估 |
| glossary | 專有名詞 |
| open_questions | 待確認問題（LLM 自動填充） |

**自訂模板：** 在 `Data/schema-templates/` 目錄放入 JSON 檔案即可，零程式碼新增。

---

## 6. Progress Log 與 Token 統計

Generate 時前端會顯示即時執行日誌：

```
Layer 2: Planning extraction for 13 sections...
Layer 2: Found 8/13 sections with data
Layer 3: Extracting project_overview (3 queries)...
Layer 3: project_overview done (1,200 tokens)
Layer 3: Extracting functional_requirements (5 queries)...
Layer 3: functional_requirements done (2,100 tokens)
Layer 3: Completed 8 sections
Merging results...
✅ Generated v2 (Precise) | 25.3s | 5,050 in + 3,200 out = 8,250 tokens
```

最後一行顯示總時間 + 輸入/輸出 token 用量。

---

## 7. API 端點

| Method | Path | 說明 |
|--------|------|------|
| POST | `/api/refinery` | 建立專案 |
| GET | `/api/refinery` | 列出專案 |
| GET | `/api/refinery/{id}` | 取得專案 |
| PUT | `/api/refinery/{id}` | 更新專案 |
| DELETE | `/api/refinery/{id}` | 軟刪除 |
| GET | `/api/refinery/{id}/files` | 列出檔案 |
| POST | `/api/refinery/{id}/files` | 上傳+清洗（SSE） |
| DELETE | `/api/refinery/{id}/files/{fileId}` | 刪除檔案 |
| GET | `/api/refinery/{id}/files/{fileId}/preview` | 清洗預覽 |
| POST | `/api/refinery/{id}/files/{fileId}/reindex` | 重試索引（SSE） |
| POST | `/api/refinery/{id}/generate` | 產出結構化文件（SSE） |
| GET | `/api/refinery/{id}/outputs` | 列出版本 |
| GET | `/api/refinery/{id}/outputs/latest` | 最新版本 |
| GET | `/api/refinery/{id}/outputs/{version}` | 指定版本 |
| GET | `/api/schema-templates` | 列出 Schema 模板 |
| GET | `/api/schema-templates/{id}` | 取得模板詳情 |
