# CLAUDE.md — AgentCraftLab.Cleaner

資料清洗引擎 + Schema Mapper 類別庫，參考 Unstructured.io 架構。

## 設計原則

- **獨立類別庫**：零平台依賴，任何專案都能引用（Engine / Api / Autonomous）
- **介面驅動**：所有工具（Partitioner / Rule / Filter / LlmProvider / OcrProvider）都透過介面隔開
- **不處理 Embedding**：只負責「原始檔案 → 乾淨結構化元素 → 結構化 JSON」
- **Element-aware**：文件不是一坨文字，而是帶類型的 DocumentElement 序列

## 兩段管線

```
Stage 1 — 清洗管線（Partition → Clean）
Raw File → IPartitioner → IElementFilter → ICleaningRule → CleanedDocument

Stage 2 — 結構化擷取（Schema Mapper）
CleanedDocument[] + SchemaDefinition → ISchemaMapper(LLM) → JSON
```

## 內建 Partitioner（7 種格式）

| Partitioner | 格式 | 分類策略 |
|-------------|------|---------|
| `DocxPartitioner` | DOCX | Heading style / ListParagraph / NumberingProperties / Table |
| `PptxPartitioner` | PPTX | PlaceholderShape type + Bullet 偵測 + Drawing.Table |
| `HtmlPartitioner` | HTML | 標籤直接對應（h1→Title, table→Table, li→ListItem） |
| `PlainTextPartitioner` | TXT/MD/CSV/JSON/Code | Markdown heading / bullet / code fence / 副檔名判斷 |
| `XlsxPartitioner` | XLSX | 每個工作表 → Markdown Table（上限 10,000 列） |
| `PdfPartitioner` | PDF | 啟發式：全大寫/章節編號→Title、bullet→ListItem、頁碼→PageNumber |
| `ImagePartitioner` | PNG/JPG/TIFF/BMP/WebP | 透過 IOcrProvider 介面，不直接依賴 Ocr 層 |

## Schema Mapper

- **ISchemaMapper**：`CleanedDocument[] + SchemaDefinition → LLM → JSON`
- **ISchemaTemplateProvider**：檔案式模板管理，`Data/schema-templates/*.json`
- **ILlmProvider**：LLM 呼叫介面（不依賴 MEAI），外部透過 adapter 橋接
- **SchemaDefinition**：JSON Schema 標準格式 + 擷取指引
- **內建模板**：`software-requirements`（軟體需求規格書，13 個區塊）

新增模板只需在 `Data/schema-templates/` 放 JSON 檔案，零程式碼修改。

## 整合點

- **RagService**：`IDocumentCleaner?` 可選注入，擷取後、分塊前自動清洗
- **Agent Tool**：`CleanerToolRegistration.RegisterCleanerTools()` 註冊 `document_clean` 工具
- **OCR 橋接**：`OcrEngineAdapter` + `AddCraftCleanerOcr()` 橋接 IOcrEngine → IOcrProvider

## 擴充方式

- **新增 Partitioner**：實作 `IPartitioner` + `services.AddPartitioner<T>()`
- **新增清洗規則**：實作 `ICleaningRule` + `services.AddCleaningRule<T>()`
- **新增過濾器**：實作 `IElementFilter` + `services.AddElementFilter<T>()`
- **替換 OCR**：實作 `IOcrProvider` 或用 `AddCraftCleanerOcr()` 橋接
- **新增 Schema 模板**：在 `Data/schema-templates/` 放 JSON 檔案
- **替換 LLM**：實作 `ILlmProvider`

## DI 註冊

```csharp
services.AddCraftCleaner();    // Pipeline + 7 Partitioners + 7 Rules + 1 Filter
services.AddSchemaMapper();    // LlmSchemaMapper + FileSchemaTemplateProvider
```
