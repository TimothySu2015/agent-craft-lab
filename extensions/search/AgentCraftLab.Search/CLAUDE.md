# CLAUDE.md — AgentCraftLab.Search

獨立搜尋引擎類別庫，參考 Azure AI Search（全文 + 向量 + RRF 混合排序）。

## 設計原則

- **獨立類別庫**：零平台依賴，Engine 引用 Search，反之不行
- **不處理 Embedding**：`ISearchEngine` 只負責儲存和搜尋向量，embedding 由呼叫端（RagService）負責
- **Provider 模式**：`ISearchEngine` 介面 + 多種實作（SQLite / InMemory / 未來 Cosmos DB）
- **禁止 Semantic Kernel**：遵循 Solution 全域規則

## 搜尋模式

| 模式 | SQLite 實作 | 說明 |
|------|-------------|------|
| `FullText` | FTS5 trigram tokenizer + `rank` 評分 | 子字串搜尋，支援 CJK |
| `Vector` | BLOB + Cosine Similarity (SIMD) | 語意搜尋（兩步查詢：先排序再回查） |
| `Hybrid` | FTS5 + Vector + RRF(k=60) | 混合排序，品質最佳（預設） |

## 關鍵設計決策

- **CJK 全文搜尋**：FTS5 trigram tokenizer（字元 n-gram），不需外部斷詞器。trigram 不支援 `bm25()`，改用 FTS5 內建 `rank`。向量搜尋補償語意理解。
- **FTS5 external content 一致性**：IndexDocuments 先寫主表再 transaction { delete + insert FTS5 }；DeleteDocuments 先 delete FTS5（子查詢依賴 rowid）再刪主表。
- **向量兩步查詢**：第一步只載入 Id + EmbeddingBlob 做排序，top-K 後才回查 Content，節省記憶體。
- **indexName 慣例**：`{userId}_rag_{guid}`（臨時）/ `{userId}_kb_{id}`（知識庫）。`_rag_` 有 TTL 24h 自動清理，`_kb_` 不受 TTL 影響。
- **SqliteSearchEngine** 使用獨立 `SearchDbContext`（不汙染 Engine 的 AppDbContext），持久化在 `Data/craftsearch.db`。
- **`ISearchEngine` 必須 Singleton**：`RagService` 是 Singleton，會持有引用。
